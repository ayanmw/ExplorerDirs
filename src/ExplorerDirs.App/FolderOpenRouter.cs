using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ExplorerDirs.App;

internal enum FolderForwardResult { Accepted, Rejected, Unavailable, Uncertain }

// Routes directory arguments and activation requests to the process that owns a profile.
// The pipe never lists or probes a target directory; the Shell does that later.
internal sealed class FolderOpenRouter : IDisposable
{
    private const int MaximumPathBytes = 128 * 1024;
    private const int RequestIdHandshake = -3;
    private const byte RequestIdHandshakeAccepted = 2;
    private const int CompletedRequestCapacity = 256;
    // The launcher's second forwarding attempt waits two seconds; release a
    // connected client that stops sending before that attempt expires.
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _pipeName;
    private readonly FileStream _ownership;
    private readonly Func<string, Task<bool>> _openDirectory;
    private readonly Func<string, Task<bool>>? _openQdr;
    private readonly Func<string, string, Task<bool>>? _openSelection;
    private readonly Func<Task<bool>>? _activateWindow;
    private readonly Dictionary<Guid, bool> _completedRequests = [];
    private readonly Queue<Guid> _completedRequestOrder = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serveTask;
    private int _disposed;

    private FolderOpenRouter(string pipeName, FileStream ownership,
        Func<string, Task<bool>> openDirectory, Func<Task<bool>>? activateWindow,
        Func<string, Task<bool>>? openQdr, Func<string, string, Task<bool>>? openSelection,
        NamedPipeServerStream firstServer)
    {
        _pipeName = pipeName;
        _ownership = ownership;
        _openDirectory = openDirectory;
        _openQdr = openQdr;
        _openSelection = openSelection;
        _activateWindow = activateWindow;
        _serveTask = ServeAsync(firstServer);
    }

    public static string PipeName(string profilePath)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User?.Value ?? Environment.UserName;
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var key = Path.GetFullPath(profilePath).ToUpperInvariant() + "|" + user + "|" + elevated;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "ExplorerDirs.Folder.v1." + Convert.ToHexString(hash[..16]);
    }

    public static FolderOpenRouter? TryStart(string profilePath, Func<string, Task<bool>> openDirectory,
        Func<Task<bool>>? activateWindow = null, Func<string, Task<bool>>? openQdr = null,
        Func<string, string, Task<bool>>? openSelection = null)
    {
        var pipeName = PipeName(profilePath);
        FileStream? ownership = null;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(profilePath))!;
            Directory.CreateDirectory(directory);
            ownership = new FileStream(Path.Combine(directory, pipeName + ".lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var firstServer = NewServer(pipeName, first: true);
            return new FolderOpenRouter(pipeName, ownership, openDirectory, activateWindow, openQdr,
                openSelection, firstServer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ownership?.Dispose();
            return null;
        }
    }

    public static Task<FolderForwardResult> TryForwardAsync(string profilePath, string path,
        TimeSpan timeout, Guid? requestId = null)
    {
        byte[] payload;
        try { payload = Utf8.GetBytes(path); }
        catch (EncoderFallbackException) { return Task.FromResult(FolderForwardResult.Rejected); }
        if (payload.Length is < 1 or > MaximumPathBytes)
            return Task.FromResult(FolderForwardResult.Rejected);

        return SendRequestAsync(profilePath, payload, timeout, requestId: requestId ?? Guid.NewGuid());
    }

    public static Task<FolderForwardResult> TryForwardQdrAsync(string profilePath, string path,
        TimeSpan timeout, Guid? requestId = null)
    {
        byte[] payload;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Path.GetExtension(fullPath).Equals(".qdr", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(FolderForwardResult.Rejected);
            payload = Utf8.GetBytes(fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
            PathTooLongException or EncoderFallbackException)
        {
            return Task.FromResult(FolderForwardResult.Rejected);
        }
        if (payload.Length is < 1 or > MaximumPathBytes)
            return Task.FromResult(FolderForwardResult.Rejected);

        return SendRequestAsync(profilePath, payload, timeout, qdr: true, requestId: requestId ?? Guid.NewGuid());
    }

    public static Task<FolderForwardResult> TryForwardSelectionAsync(string profilePath, string directory,
        string selectedPath, TimeSpan timeout, Guid? requestId = null)
    {
        byte[] directoryPayload;
        byte[] selectedPayload;
        try
        {
            directory = Path.GetFullPath(directory);
            selectedPath = Path.GetFullPath(selectedPath);
            var selectedParent = Path.GetDirectoryName(selectedPath);
            if (selectedParent is null || !SameDirectory(directory, selectedParent))
                return Task.FromResult(FolderForwardResult.Rejected);
            directoryPayload = Utf8.GetBytes(directory);
            selectedPayload = Utf8.GetBytes(selectedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
            PathTooLongException or EncoderFallbackException)
        {
            return Task.FromResult(FolderForwardResult.Rejected);
        }
        if (directoryPayload.Length is < 1 or > MaximumPathBytes ||
            selectedPayload.Length is < 1 or > MaximumPathBytes)
            return Task.FromResult(FolderForwardResult.Rejected);

        return SendRequestAsync(profilePath, directoryPayload, timeout, selectionPayload: selectedPayload,
            requestId: requestId ?? Guid.NewGuid());
    }

    public static Task<FolderForwardResult> TryActivateAsync(string profilePath, TimeSpan timeout,
        Guid? requestId = null) => SendRequestAsync(profilePath, null, timeout, requestId: requestId ?? Guid.NewGuid());

    private static async Task<FolderForwardResult> SendRequestAsync(string profilePath, byte[]? payload,
        TimeSpan timeout, bool qdr = false, byte[]? selectionPayload = null, Guid? requestId = null)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var client = new NamedPipeClientStream(".", PipeName(profilePath), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await client.ConnectAsync(deadline.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or TimeoutException or
            UnauthorizedAccessException or OperationCanceledException)
        {
            return FolderForwardResult.Unavailable;
        }

        try
        {
            var marker = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(marker, RequestIdHandshake);
            await client.WriteAsync(marker, deadline.Token).ConfigureAwait(false);
            await client.FlushAsync(deadline.Token).ConfigureAwait(false);
            var handshake = new byte[1];
            await client.ReadExactlyAsync(handshake, deadline.Token).ConfigureAwait(false);
            if (handshake[0] == 0)
            {
                // A legacy receiver rejects this marker before dispatching any request.
                client.Dispose();
                return await SendLegacyRequestAsync(profilePath, payload, qdr, selectionPayload, deadline.Token).ConfigureAwait(false);
            }
            if (handshake[0] != RequestIdHandshakeAccepted)
                return FolderForwardResult.Uncertain;

            await client.WriteAsync((requestId ?? Guid.NewGuid()).ToByteArray(), deadline.Token).ConfigureAwait(false);
            await WriteRequestAsync(client, payload, qdr, selectionPayload, deadline.Token).ConfigureAwait(false);
            return await ReadRequestResultAsync(client, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return FolderForwardResult.Uncertain;
        }
    }

    private static async Task<FolderForwardResult> SendLegacyRequestAsync(string profilePath,
        byte[]? payload, bool qdr, byte[]? selectionPayload, CancellationToken deadline)
    {
        using var client = new NamedPipeClientStream(".", PipeName(profilePath), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await client.ConnectAsync(deadline).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or TimeoutException or
            UnauthorizedAccessException or OperationCanceledException)
        {
            return FolderForwardResult.Unavailable;
        }
        try
        {
            await WriteRequestAsync(client, payload, qdr, selectionPayload, deadline).ConfigureAwait(false);
            return await ReadRequestResultAsync(client, deadline).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return FolderForwardResult.Uncertain;
        }
    }

    private static async Task WriteRequestAsync(NamedPipeClientStream client, byte[]? payload,
        bool qdr, byte[]? selectionPayload, CancellationToken token)
    {
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header,
            selectionPayload is not null ? -4 : payload is null ? -1 : qdr ? -2 : payload.Length);
        await client.WriteAsync(header, token).ConfigureAwait(false);
        if (payload is not null)
        {
            if (qdr || selectionPayload is not null)
            {
                BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
                await client.WriteAsync(header, token).ConfigureAwait(false);
            }
            await client.WriteAsync(payload, token).ConfigureAwait(false);
        }
        if (selectionPayload is not null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(header, selectionPayload.Length);
            await client.WriteAsync(header, token).ConfigureAwait(false);
            await client.WriteAsync(selectionPayload, token).ConfigureAwait(false);
        }
        await client.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task<FolderForwardResult> ReadRequestResultAsync(NamedPipeClientStream client,
        CancellationToken token)
    {
        var response = new byte[1];
        await client.ReadExactlyAsync(response, token).ConfigureAwait(false);
        return response[0] switch
        {
            1 => FolderForwardResult.Accepted,
            0 => FolderForwardResult.Rejected,
            _ => FolderForwardResult.Uncertain
        };
    }

    private static NamedPipeServerStream NewServer(string pipeName, bool first) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly |
            (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    private async Task ServeAsync(NamedPipeServerStream server)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using (server)
                    {
                        await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                        await HandleClientAsync(server, _stop.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (OperationCanceledException) { /* An incomplete client request timed out. */ }
                catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { break; }
                catch (IOException) { /* A disconnected client must not stop later requests. */ }
                if (_stop.IsCancellationRequested) break;
                try { server = NewServer(_pipeName, first: false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { break; }
            }
        }
        finally { _ownership.Dispose(); }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken stop)
    {
        using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        readDeadline.CancelAfter(RequestReadTimeout);
        var header = new byte[sizeof(int)];
        await server.ReadExactlyAsync(header, readDeadline.Token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Guid? requestId = null;
        if (length == RequestIdHandshake)
        {
            await server.WriteAsync(new[] { RequestIdHandshakeAccepted }, stop).ConfigureAwait(false);
            await server.FlushAsync(stop).ConfigureAwait(false);
            var idBytes = new byte[16];
            readDeadline.CancelAfter(RequestReadTimeout);
            await server.ReadExactlyAsync(idBytes, readDeadline.Token).ConfigureAwait(false);
            requestId = new Guid(idBytes);
            await server.ReadExactlyAsync(header, readDeadline.Token).ConfigureAwait(false);
            length = BinaryPrimitives.ReadInt32LittleEndian(header);
        }

        var accepted = false;
        if (length == -1)
        {
            if (requestId is { } activationId && _completedRequests.TryGetValue(activationId, out var previous))
                accepted = previous;
            else
            {
                if (_activateWindow is not null)
                {
                    try { accepted = await _activateWindow().WaitAsync(stop).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
                    catch (Exception) { /* A failed activation must not masquerade as accepted. */ }
                }
                if (requestId is { } completedActivationId) RememberResult(completedActivationId, accepted);
            }
        }
        else if (length is > 0 and <= MaximumPathBytes || length is -2 or -4)
        {
            var pathLength = length;
            if (length is -2 or -4)
            {
                await server.ReadExactlyAsync(header, readDeadline.Token).ConfigureAwait(false);
                pathLength = BinaryPrimitives.ReadInt32LittleEndian(header);
            }
            if (pathLength is < 1 or > MaximumPathBytes) goto Respond;
            var payload = new byte[pathLength];
            readDeadline.CancelAfter(RequestReadTimeout);
            await server.ReadExactlyAsync(payload, readDeadline.Token).ConfigureAwait(false);
            try
            {
                var path = Utf8.GetString(payload);
                if (!IsNormalizedAbsolutePath(path)) goto Respond;
                if (length == -2 && !Path.GetExtension(path).Equals(".qdr", StringComparison.OrdinalIgnoreCase))
                    goto Respond;
                string? selectedPath = null;
                if (length == -4)
                {
                    await server.ReadExactlyAsync(header, readDeadline.Token).ConfigureAwait(false);
                    var selectedLength = BinaryPrimitives.ReadInt32LittleEndian(header);
                    if (selectedLength is < 1 or > MaximumPathBytes) goto Respond;
                    var selectedPayload = new byte[selectedLength];
                    await server.ReadExactlyAsync(selectedPayload, readDeadline.Token).ConfigureAwait(false);
                    selectedPath = Utf8.GetString(selectedPayload);
                    var selectedParent = Path.GetDirectoryName(selectedPath);
                    if (!IsNormalizedAbsolutePath(selectedPath) || selectedParent is null ||
                        !SameDirectory(path, selectedParent)) goto Respond;
                }
                if (requestId is { } directoryRequestId && _completedRequests.TryGetValue(directoryRequestId, out var previous))
                    accepted = previous;
                else
                {
                    try
                    {
                        if (length == -2)
                        {
                            if (_openQdr is not null)
                                accepted = await _openQdr(path).WaitAsync(stop).ConfigureAwait(false);
                        }
                        else if (selectedPath is not null)
                        {
                            if (_openSelection is not null)
                                accepted = await _openSelection(path, selectedPath).WaitAsync(stop).ConfigureAwait(false);
                        }
                        else accepted = await _openDirectory(path).WaitAsync(stop).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
                    catch (Exception) { /* Cache the rejection so an ambiguous retry cannot repeat a side effect. */ }
                    if (requestId is { } completedRequestId) RememberResult(completedRequestId, accepted);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
            catch (Exception) { /* Invalid request or failed UI dispatch: reject it. */ }
        }
    Respond:
        await server.WriteAsync(new[] { accepted ? (byte)1 : (byte)0 }, stop).ConfigureAwait(false);
        await server.FlushAsync(stop).ConfigureAwait(false);
    }

    private static bool IsNormalizedAbsolutePath(string path)
    {
        try { return Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return false; }
    }

    private static bool SameDirectory(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private void RememberResult(Guid requestId, bool accepted)
    {
        _completedRequests[requestId] = accepted;
        _completedRequestOrder.Enqueue(requestId);
        while (_completedRequestOrder.Count > CompletedRequestCapacity)
            _completedRequests.Remove(_completedRequestOrder.Dequeue());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _ = _serveTask.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
