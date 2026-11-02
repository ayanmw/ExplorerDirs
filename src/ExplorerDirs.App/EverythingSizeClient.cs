using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ExplorerDirs.App;

internal enum EverythingSizeStatus { Available, NotRunning, IndexUnavailable, Timeout, InvalidReply, Failed }
internal sealed record IndexedSizeItem(string Path, bool IsDirectory, ulong? Bytes);
internal sealed record IndexedSizePage(EverythingSizeStatus Status, IReadOnlyList<IndexedSizeItem> Items,
    uint Total = 0, uint Offset = 0, string? Detail = null);

// Uses the published Everything SDK IPC QUERY2W protocol. All windows are message-only;
// no Everything search window is opened and no external settings are changed.
internal static class EverythingSizeClient
{
    internal const int PageSize = 2048;
    private const uint ReplyId = 0x4544535A;
    private const uint RequestedFields = 0x4 | 0x10; // full path and logical size
    private static readonly SemaphoreSlim QueryGate = new(2, 2);

    internal static async Task<IndexedSizePage> QueryChildrenAsync(string directory, uint offset, CancellationToken cancellationToken)
    {
        await QueryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await StartQueryAsync(directory, offset, cancellationToken, exact: false).ConfigureAwait(false); }
        finally { QueryGate.Release(); }
    }

    internal static async Task<IndexedSizePage> QueryDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        await QueryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await StartQueryAsync(directory, 0, cancellationToken, exact: true).ConfigureAwait(false); }
        finally { QueryGate.Release(); }
    }

    private static Task<IndexedSizePage> StartQueryAsync(string directory, uint offset, CancellationToken cancellationToken, bool exact)
    {
        if (!Path.IsPathFullyQualified(directory) || directory.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Everything 查询需要绝对目录路径。", nameof(directory));
        var completion = new TaskCompletionSource<IndexedSizePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(Query(directory, offset, cancellationToken, exact)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception error) { completion.TrySetResult(new(EverythingSizeStatus.Failed, [], Detail: error.Message)); }
        }) { IsBackground = true, Name = "Explorer Dirs Everything query" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static IndexedSizePage Query(string directory, uint offset, CancellationToken cancellationToken, bool exact)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var server = FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION", null);
        if (server == 0) return new(EverythingSizeStatus.NotRunning, []);
        if (SendMessageTimeoutW(server, 0x400, 401, 0, 2, 400, out var loaded) == 0)
            return new(EverythingSizeStatus.Timeout, []);
        if (loaded == 0) return new(EverythingSizeStatus.IndexUnavailable, [], Detail: "Everything 索引尚未就绪。");
        if (SendMessageTimeoutW(server, 0x400, 411, 2, 2, 400, out var indexed) == 0)
            return new(EverythingSizeStatus.Timeout, []);
        if (indexed == 0) return new(EverythingSizeStatus.IndexUnavailable, [], Detail: "Everything 未提供文件夹大小索引。");

        IndexedSizePage? reply = null;
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            using var source = new HwndSource(new HwndSourceParameters("ExplorerDirs Everything IPC")
                { ParentWindow = (nint)(-3), WindowStyle = 0, Width = 0, Height = 0 });
            source.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message != 0x4A || lParam == 0 || (wParam != 0 && wParam != server)) return 0;
                var data = Marshal.PtrToStructure<CopyData>(lParam);
                if (data.Tag != ReplyId) return 0;
                handled = true;
                if (data.Length < 20 || data.Length > 32 * 1024 * 1024 || data.Data == 0)
                    reply = new(EverythingSizeStatus.InvalidReply, []);
                else
                {
                    var bytes = new byte[data.Length]; Marshal.Copy(data.Data, bytes, 0, bytes.Length);
                    reply = ParseReply(bytes);
                }
                frame.Continue = false;
                return 1;
            });
            var expression = exact ? "^" + System.Text.RegularExpressions.Regex.Escape(Path.TrimEndingDirectorySeparator(directory)) + "$"
                : $"parent:\"{directory}\"";
            var search = Encoding.Unicode.GetBytes(expression + "\0");
            var query = new byte[28 + search.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(query.AsSpan(0), unchecked((uint)source.Handle));
            BinaryPrimitives.WriteUInt32LittleEndian(query.AsSpan(4), ReplyId);
            BinaryPrimitives.WriteUInt32LittleEndian(query.AsSpan(8), exact ? 12u : 0u); // MATCHPATH | REGEX for an exact absolute identity
            BinaryPrimitives.WriteUInt32LittleEndian(query.AsSpan(12), offset);
            BinaryPrimitives.WriteUInt32LittleEndian(query.AsSpan(16), exact ? 1u : PageSize);
            BinaryPrimitives.WriteUInt32LittleEndian(query.AsSpan(20), RequestedFields);
            BinaryPrimitives.WriteUInt32LittleEndian(query.AsSpan(24), 1); // name ascending
            search.CopyTo(query, 28);
            var pin = GCHandle.Alloc(query, GCHandleType.Pinned);
            try
            {
                var copyData = new CopyData { Tag = 18, Length = query.Length, Data = pin.AddrOfPinnedObject() };
                if (SendQuery(server, 0x4A, source.Handle, ref copyData, 2, 500, out var accepted) == 0)
                    return new(EverythingSizeStatus.Timeout, []);
                if (accepted == 0) return new(EverythingSizeStatus.Failed, [], Detail: "Everything 拒绝查询。");
            }
            finally { pin.Free(); }
            var started = Environment.TickCount64;
            var timer = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) =>
            {
                if (cancellationToken.IsCancellationRequested || Environment.TickCount64 - started >= 1500)
                    frame.Continue = false;
            };
            timer.Start();
            try { if (reply is null) Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            cancellationToken.ThrowIfCancellationRequested();
            if (reply is not null && reply.Offset != offset) return new(EverythingSizeStatus.InvalidReply, []);
            // Confirm direct-parent identity independently of Everything search syntax.
            if (reply is not null && reply.Items.Any(item => !FavoritesStore.SamePath(
                exact ? item.Path : Path.GetDirectoryName(item.Path) ?? "", directory)))
                return new(EverythingSizeStatus.InvalidReply, []);
            return reply ?? new(EverythingSizeStatus.Timeout, []);
        }
        finally { dispatcher.InvokeShutdown(); }
    }

    internal static IndexedSizePage ParseReply(ReadOnlySpan<byte> bytes)
    {
        try
        {
            if (bytes.Length < 20) return new(EverythingSizeStatus.InvalidReply, []);

            var total = ReadUInt(bytes, 0); var count = ReadUInt(bytes, 4); var offset = ReadUInt(bytes, 8);
            if (ReadUInt(bytes, 12) != RequestedFields || count > PageSize || offset > total || count > total - offset ||
                20L + count * 8L > bytes.Length) return new(EverythingSizeStatus.InvalidReply, []);
            var items = new List<IndexedSizeItem>((int)count);
            for (var index = 0; index < count; index++)
            {
                var itemOffset = checked(20 + (int)index * 8);
                var flags = ReadUInt(bytes, itemOffset); var dataOffset = checked((int)ReadUInt(bytes, itemOffset + 4));
                if (dataOffset < 20L + count * 8L || dataOffset > bytes.Length - 4)
                    return new(EverythingSizeStatus.InvalidReply, []);
                var length = ReadUInt(bytes, dataOffset);
                var textLength = checked((int)length * 2);
                if ((long)dataOffset + 4 + textLength + 2 + 8 > bytes.Length)
                    return new(EverythingSizeStatus.InvalidReply, []);
                if (bytes[dataOffset + 4 + textLength] != 0 || bytes[dataOffset + 5 + textLength] != 0)
                    return new(EverythingSizeStatus.InvalidReply, []);
                var path = new UnicodeEncoding(false, false, true).GetString(bytes.Slice(dataOffset + 4, textLength));
                if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) return new(EverythingSizeStatus.InvalidReply, []);
                var size = BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(dataOffset + 6 + textLength, 8));
                items.Add(new(path, (flags & 1) != 0, size < 0 ? null : (ulong)size));
            }
            return new(EverythingSizeStatus.Available, items, total, offset);
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or DecoderFallbackException)
        { return new(EverythingSizeStatus.InvalidReply, []); }
    }

    private static uint ReadUInt(ReadOnlySpan<byte> bytes, int position) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4));

    [StructLayout(LayoutKind.Sequential)] private struct CopyData { internal nuint Tag; internal int Length; internal nint Data; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string className, string? windowName);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeoutW(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern nint SendQuery(nint window, uint message, nint wParam, ref CopyData data, uint flags, uint timeout, out nuint result);
}
