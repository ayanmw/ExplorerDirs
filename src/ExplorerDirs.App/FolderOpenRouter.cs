using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ExplorerDirs.App;

internal enum FolderForwardResult { Accepted, Rejected, Unavailable, Uncertain }

// Routes one directory argument to an existing process using the same profile.
// The pipe never lists or probes the target directory; the Shell does that later.
internal sealed class FolderOpenRouter : IDisposable
{
    private const int MaximumPathBytes = 128 * 1024;
    // The launcher's second forwarding attempt waits two seconds; release a
    // connected client that stops sending before that attempt expires.
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _pipeName;
    private readonly FileStream _ownership;
    private readonly Func<string, Task<bool>> _openDirectory;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serveTask;
    private int _disposed;

    private FolderOpenRouter(string pipeName, FileStream ownership,
        Func<string, Task<bool>> openDirectory, NamedPipeServerStream firstServer)
    {
        _pipeName = pipeName;
        _ownership = ownership;
        _openDirectory = openDirectory;
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

    public static FolderOpenRouter? TryStart(string profilePath, Func<string, Task<bool>> openDirectory)
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
            return new FolderOpenRouter(pipeName, ownership, openDirectory, firstServer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ownership?.Dispose();
            return null;
        }
    }

    public static async Task<FolderForwardResult> TryForwardAsync(string profilePath, string path,
        TimeSpan timeout)
    {
        byte[] payload;
        try { payload = Utf8.GetBytes(path); }
        catch (EncoderFallbackException) { return FolderForwardResult.Rejected; }
        if (payload.Length is < 1 or > MaximumPathBytes)
            return FolderForwardResult.Rejected;

        using var client = new NamedPipeClientStream(".", PipeName(profilePath), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = new CancellationTokenSource(timeout);
        try { await client.ConnectAsync(deadline.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or TimeoutException or
            UnauthorizedAccessException or OperationCanceledException)
        {
            return FolderForwardResult.Unavailable;
        }

        try
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await client.WriteAsync(header, deadline.Token).ConfigureAwait(false);
            await client.WriteAsync(payload, deadline.Token).ConfigureAwait(false);
            await client.FlushAsync(deadline.Token).ConfigureAwait(false);
            var response = new byte[1];
            await client.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(false);
            return response[0] switch
            {
                1 => FolderForwardResult.Accepted,
                0 => FolderForwardResult.Rejected,
                _ => FolderForwardResult.Uncertain
            };
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return FolderForwardResult.Uncertain;
        }
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
        var header = new byte[4];
        await server.ReadExactlyAsync(header, readDeadline.Token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        var accepted = false;
        if (length is > 0 and <= MaximumPathBytes)
        {
            var payload = new byte[length];
            readDeadline.CancelAfter(RequestReadTimeout);
            await server.ReadExactlyAsync(payload, readDeadline.Token).ConfigureAwait(false);
            try
            {
                var path = Utf8.GetString(payload);
                if (Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path)
                    accepted = await _openDirectory(path).ConfigureAwait(false);
            }
            catch (Exception) { /* Invalid request or failed UI dispatch: reject it. */ }
        }
        await server.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, stop).ConfigureAwait(false);
        await server.FlushAsync(stop).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _ = _serveTask.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
