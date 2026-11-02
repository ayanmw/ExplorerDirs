using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal sealed class FolderSizeNativeBridge : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ConfigureProvider(nint query, nint invalidate);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryProvider([MarshalAs(UnmanagedType.LPWStr)] string path, nint cancelled,
        out ulong bytes, out ulong files, out ulong folders, out ulong unreadable, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void InvalidateProvider([MarshalAs(UnmanagedType.LPWStr)] string path);

    private readonly ConfigureProvider _configure;
    private readonly QueryProvider _queryCallback;
    private readonly InvalidateProvider _invalidateCallback;
    private readonly Func<string, uint, CancellationToken, Task<IndexedSizePage>> _queryChildren;
    private readonly Func<string, CancellationToken, Task<IndexedSizePage>> _queryExact;
    private readonly object _cacheGate = new();
    private readonly object _configurationGate = new();
    private readonly Dictionary<string, (Task<IndexedSizePage> Task, long Started)> _pages = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _generation = new();
    private FolderSizeSource _source;
    private bool _disposed;

    internal FolderSizeNativeBridge(nint module, FolderSizeSource source,
        Func<string, uint, CancellationToken, Task<IndexedSizePage>>? queryChildren = null,
        Func<string, CancellationToken, Task<IndexedSizePage>>? queryExact = null)
    {
        _configure = Marshal.GetDelegateForFunctionPointer<ConfigureProvider>(NativeLibrary.GetExport(module, "ConfigureFolderSizeProvider"));
        _queryChildren = queryChildren ?? EverythingSizeClient.QueryChildrenAsync;
        _queryExact = queryExact ?? EverythingSizeClient.QueryDirectoryAsync;
        _queryCallback = Query;
        _invalidateCallback = Invalidate;
        SetSource(source);
    }

    internal void SetSource(FolderSizeSource source)
    {
        lock (_configurationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancellationTokenSource previous;
            lock (_cacheGate)
            {
                previous = _generation; _generation = new(); _source = source; _pages.Clear();
            }
            previous.Cancel();
            try
            {
                // Native registration waits for callbacks already in progress. Keep both delegates
                // strongly referenced until unregister/reconfigure has returned.
                Marshal.ThrowExceptionForHR(_configure(Marshal.GetFunctionPointerForDelegate(_queryCallback),
                    Marshal.GetFunctionPointerForDelegate(_invalidateCallback)));
            }
            finally { previous.Dispose(); }
        }
    }

    private Task<IndexedSizePage> ParentPage(string parent)
    {
        lock (_cacheGate)
        {
            if (_pages.TryGetValue(parent, out var cached) && Environment.TickCount64 - cached.Started < 2000) return cached.Task;
            _pages.Remove(parent);
            // Four first pages retain at most 8,192 rows. Later-page targets use a precise query,
            // rather than retaining an unbounded second copy of Everything's index.
            if (_pages.Count >= 4) _pages.Remove(_pages.MinBy(item => item.Value.Started).Key);
            var task = _queryChildren(parent, 0, CancellationToken.None);
            _pages[parent] = (task, Environment.TickCount64);
            return task;
        }
    }

    private int Query(string path, nint cancelled, out ulong bytes, out ulong files, out ulong folders, out ulong unreadable, out uint flags)
    {
        bytes = files = folders = unreadable = 0; flags = 0;
        System.Threading.Timer? nativeCancellation = null;
        CancellationTokenSource? cancellation = null;
        try
        {
            FolderSizeSource source;
            lock (_cacheGate)
            {
                if (_disposed) return unchecked((int)0x800704C7);
                source = _source;
                if (source == FolderSizeSource.BuiltIn) return unchecked((int)0x80004001); // E_NOTIMPL: native scanner
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(_generation.Token);
            }
            if (cancelled != 0)
            {
                if (Marshal.ReadInt32(cancelled) != 0) return unchecked((int)0x800704C7);
                nativeCancellation = new System.Threading.Timer(_ =>
                {
                    if (Marshal.ReadInt32(cancelled) != 0) cancellation.Cancel();
                }, null, 0, 25);
            }
            var token = cancellation.Token;
            var parent = Path.GetDirectoryName(path);
            IndexedSizeItem? match = null;
            if (parent is not null)
            {
                var page = ParentPage(parent).WaitAsync(token).GetAwaiter().GetResult();
                if (page.Status == EverythingSizeStatus.Available)
                {
                    match = page.Items.FirstOrDefault(item => item.IsDirectory && FavoritesStore.SamePath(item.Path, path));
                    if (match is null && page.Total > page.Items.Count)
                    {
                        var exact = _queryExact(path, token).GetAwaiter().GetResult();
                        if (exact.Status == EverythingSizeStatus.Available)
                            match = exact.Items.FirstOrDefault(item => item.IsDirectory && FavoritesStore.SamePath(item.Path, path));
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            if (match?.Bytes is { } value) { bytes = value; flags = 1; return 0; }
            // Unknown is a successfully cached unavailable state, not a fabricated 0 or a retry loop.
            return source == FolderSizeSource.Auto ? unchecked((int)0x80004001) : 0;
        }
        catch (OperationCanceledException) { return unchecked((int)0x800704C7); }
        catch (Exception) { return unchecked((int)0x80004005); }
        finally
        {
            // Wait for timer callbacks before native may release the cancellation flag's owner.
            if (nativeCancellation is not null) nativeCancellation.DisposeAsync().AsTask().GetAwaiter().GetResult();
            cancellation?.Dispose();
        }
    }

    private void Invalidate(string changed)
    {
        try
        {
            lock (_cacheGate)
            {
                foreach (var key in _pages.Keys.Where(parent => Within(changed, parent) || Within(parent, changed)).ToArray())
                    _pages.Remove(key);
            }
        }
        catch (Exception) { /* Native invalidation callbacks must never propagate managed exceptions. */ }
    }

    private static bool Within(string path, string root) => FavoritesStore.SamePath(path, root) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (_configurationGate)
        {
            lock (_cacheGate)
            {
                if (_disposed) return;
                _disposed = true; _generation.Cancel(); _pages.Clear();
            }
            Marshal.ThrowExceptionForHR(_configure(0, 0));
            _generation.Dispose();
            GC.KeepAlive(_queryCallback); GC.KeepAlive(_invalidateCallback);
        }
    }
}
