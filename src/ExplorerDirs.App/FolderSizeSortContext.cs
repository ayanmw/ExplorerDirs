using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// A tab owns one context across directory visits. Locations remain caller-owned
// PIDLs; the DLL reference outlives the native lease, including owner shutdown.
internal sealed class FolderSizeSortContext : IDisposable
{
    private readonly object _gate = new();
    private readonly PrepareLocation _prepare;
    private readonly SetPolicy _set;
    private readonly ReleaseContext _release;
    private nint _module;
    private nint _lease;

    private FolderSizeSortContext(nint module, nint lease,
        PrepareLocation prepare, SetPolicy set, ReleaseContext release)
    {
        _module = module;
        _lease = lease;
        _prepare = prepare;
        _set = set;
        _release = release;
    }

    internal static bool TryCreate(string adapterPath, nint source, bool mixed, int direction,
        out FolderSizeSortContext? context, out nint location)
    {
        var policy = EncodePolicy(mixed, direction);
        context = null;
        location = 0;
        var module = NativeLibrary.Load(adapterPath);
        nint lease = 0;
        ReleaseContext? release = null;
        try
        {
            // Capability detection keeps old adapters usable during the staged migration.
            if (!NativeLibrary.TryGetExport(module, "CreateFolderSizeSortContext", out var createAddress) ||
                !NativeLibrary.TryGetExport(module, "PrepareFolderSizeSortLocation", out var prepareAddress) ||
                !NativeLibrary.TryGetExport(module, "SetFolderSizeSortPolicy", out var setAddress) ||
                !NativeLibrary.TryGetExport(module, "ReleaseFolderSizeSortContext", out var releaseAddress)) return false;
            release = Marshal.GetDelegateForFunctionPointer<ReleaseContext>(releaseAddress);
            var create = Marshal.GetDelegateForFunctionPointer<CreateContext>(createAddress);
            Marshal.ThrowExceptionForHR(create(source, policy, out location, out lease));
            if (lease == 0 || location == 0) throw new InvalidOperationException("Native sort context was incomplete.");
            context = new FolderSizeSortContext(module, lease,
                Marshal.GetDelegateForFunctionPointer<PrepareLocation>(prepareAddress),
                Marshal.GetDelegateForFunctionPointer<SetPolicy>(setAddress), release);
            module = 0;
            lease = 0;
            return true;
        }
        finally
        {
            if (lease != 0) release!(lease);
            if (module != 0)
            {
                if (location != 0) { Marshal.FreeCoTaskMem(location); location = 0; }
                NativeLibrary.Free(module);
            }
        }
    }

    internal nint Prepare(nint source)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_lease == 0, this);
            var result = _prepare(source, _lease, out var location);
            if (result < 0 || location == 0)
            {
                if (location != 0) Marshal.FreeCoTaskMem(location);
                Marshal.ThrowExceptionForHR(result);
                throw new InvalidOperationException("Native sort location was empty.");
            }
            return location;
        }
    }

    internal void Update(bool mixed, int direction)
    {
        var policy = EncodePolicy(mixed, direction);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_lease == 0, this);
            Marshal.ThrowExceptionForHR(_set(_lease, policy));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_lease == 0) return;
            _release(_lease);
            _lease = 0;
            NativeLibrary.Free(_module);
            _module = 0;
        }
    }

    private static uint EncodePolicy(bool mixed, int direction) => direction is 1 or -1
        ? (mixed ? 1u : 0u) | (direction < 0 ? 2u : 0u)
        : throw new ArgumentOutOfRangeException(nameof(direction));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateContext(nint source, uint policy, out nint location, out nint lease);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PrepareLocation(nint source, nint lease, out nint location);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetPolicy(nint lease, uint policy);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ReleaseContext(nint lease);
}
