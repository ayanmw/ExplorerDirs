using System.Runtime.InteropServices;
using System.Threading;

[ComVisible(true), Guid("9CC22886-DC8E-11D2-B1D0-00C04F8EEB3E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProbeFolderFilter
{
    [PreserveSig] int ShouldShow(nint shellFolder, nint folderPidl, nint itemPidl);
    [PreserveSig] int GetEnumFlags(nint shellFolder, nint folderPidl, ref nint hwnd, out uint flags);
}

// Isolated capability test only; never enables filtering in the product.
internal static class FolderFilterCapabilityProbe
{
    private static readonly Guid FilterSiteId = new("C0A651F5-B48B-11D2-B5ED-006097C686F6");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");

    internal static int Run(nint browser, string fixture, nint parent, nint foreground)
    {
        nint filterSite = 0, shellView = 0, folderView = 0, filterUnknown = 0;
        ProbeFilter? filter = null;
        bool installed = false;
        try
        {
            var siteHr = Marshal.QueryInterface(browser, in FilterSiteId, out filterSite);
            var shellHr = GetCurrentView(browser, in ShellViewId, out shellView);
            var folderHr = GetCurrentView(browser, in FolderViewId, out folderView);
            Console.WriteLine($"IExplorerBrowser: IFolderFilterSite={siteHr:X8}; IShellView={shellHr:X8}; IFolderView={folderHr:X8}");
            Marshal.ThrowExceptionForHR(siteHr);
            Marshal.ThrowExceptionForHR(shellHr);
            Marshal.ThrowExceptionForHR(folderHr);

            var initialCount = ReadItemCount(folderView);
            if (initialCount != 3) throw new Exception($"Expected 3 fixture items before filter, found {initialCount}.");

            filter = new ProbeFilter();
            filterUnknown = Marshal.GetIUnknownForObject(filter);
            var filterId = new Guid("9CC22886-DC8E-11D2-B1D0-00C04F8EEB3E");
            var filterQueryHr = Marshal.QueryInterface(filterUnknown, in filterId, out var filterInterface);
            if (filterInterface != 0) Marshal.Release(filterInterface);
            Console.WriteLine($"ProbeFilter: IFolderFilter={filterQueryHr:X8}");
            var setFilter = GetMethod<SetFilter>(filterSite, 3);
            var installHr = setFilter(filterSite, filterUnknown);
            Console.WriteLine($"SetFilter(install)={installHr:X8}");
            Marshal.ThrowExceptionForHR(installHr);
            installed = true;

            Marshal.ThrowExceptionForHR(Refresh(shellView));
            var filteredCount = WaitForFilter(folderView, filter, TimeSpan.FromSeconds(3));
            Console.WriteLine($"filter-installed=True; enum-calls={filter.EnumCalls}; should-show-calls={filter.ShouldShowCalls}; items={initialCount}->{filteredCount}");

            Marshal.ThrowExceptionForHR(setFilter(filterSite, 0));
            installed = false;
            Marshal.ThrowExceptionForHR(Refresh(shellView));
            var restoredCount = WaitForCount(folderView, 3, TimeSpan.FromSeconds(3));
            Console.WriteLine($"filter-removed=True; items-restored={restoredCount == 3}; items={restoredCount}; fixture={Path.GetFileName(fixture)}");

            var passed = filter.ShouldShowCalls == initialCount && filteredCount == 0 && restoredCount == initialCount;
            Console.WriteLine($"parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            return passed && !IsWindowVisible(parent) && GetForegroundWindow() == foreground ? 0 : 1;
        }
        finally
        {
            if (installed && filterSite != 0 && filter is not null)
            {
                Marshal.ThrowExceptionForHR(GetMethod<SetFilter>(filterSite, 3)(filterSite, 0));
                if (shellView != 0) _ = Refresh(shellView);
            }
            if (filterUnknown != 0) Marshal.Release(filterUnknown);
            if (folderView != 0) Marshal.Release(folderView);
            if (shellView != 0) Marshal.Release(shellView);
            if (filterSite != 0) Marshal.Release(filterSite);
            GC.KeepAlive(filter);
        }
    }

    private static int GetCurrentView(nint browser, in Guid iid, out nint view)
    {
        var interfaceId = iid;
        return GetMethod<GetCurrentViewDelegate>(browser, 17)(browser, ref interfaceId, out view);
    }

    private static int ReadItemCount(nint view) =>
        GetMethod<ItemCount>(view, 7)(view, 2, out var count) < 0 ? -1 : count;

    private static int WaitForCount(nint view, int expected, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        int count;
        do
        {
            Application.DoEvents();
            count = ReadItemCount(view);
            if (count == expected) return count;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < until);
        return count;
    }

    private static int WaitForFilter(nint view, ProbeFilter filter, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        int count;
        do
        {
            Application.DoEvents();
            count = ReadItemCount(view);
            if (count == 0 && filter.ShouldShowCalls >= 3) return count;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < until);
        return count;
    }

    private static int Refresh(nint shellView) => GetMethod<RefreshDelegate>(shellView, 8)(shellView);

    private static T GetMethod<T>(nint instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentViewDelegate(nint self, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefreshDelegate(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFilter(nint self, nint filter);

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ProbeFilter : IProbeFolderFilter
    {
        private int _enumCalls;
        private int _shouldShowCalls;
        internal int EnumCalls => Volatile.Read(ref _enumCalls);
        internal int ShouldShowCalls => Volatile.Read(ref _shouldShowCalls);

        public int GetEnumFlags(nint shellFolder, nint folderPidl, ref nint hwnd, out uint flags)
        {
            Interlocked.Increment(ref _enumCalls);
            flags = 0x60; // SHCONTF_FOLDERS | SHCONTF_NONFOLDERS
            return 0;
        }

        public int ShouldShow(nint shellFolder, nint folderPidl, nint itemPidl)
        {
            Interlocked.Increment(ref _shouldShowCalls);
            return 1; // S_FALSE: suppress this item in this isolated probe.
        }
    }
}
