using System.Runtime.InteropServices;
using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

// Adds the same dialog-browser callback surface used by the product Shell site
// to the isolated color host, so filtering and color index mapping share a view.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class ShellViewColorBrowserHost : StreamViewHost,
    IComDialogBrowser, IComDialogBrowser2, IComDialogBrowser3
{
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private readonly ShellBrowserSite _callbacks = new() { QuickFilterActive = true };
    private readonly ShellFolderFilter _filter;
    private int _includeObjectCalls;

    internal ShellViewColorBrowserHost(nint parent, byte[] state, ShellFolderFilter filter,
        ShellViewNavigationHistory? navigation = null) : base(parent, state, navigation: navigation)
    {
        _filter = filter;
    }

    internal int IncludeObjectCalls => Volatile.Read(ref _includeObjectCalls);
    internal long SelectionVersion => _callbacks.SelectionVersion;
    internal int NavigationIndex => NavigationHistory?.Index ?? 0;
    internal nint CurrentPidl => NavigationHistory?.CurrentPidl ?? 0;

    public int OnDefaultCommand(nint shellView) => _callbacks.OnDefaultCommand(shellView);
    public int OnStateChange(nint shellView, uint change) => _callbacks.OnStateChange(shellView, change);

    public int IncludeObject(nint shellView, nint childPidl)
    {
        Interlocked.Increment(ref _includeObjectCalls);
        nint folderView = 0, shellFolder = 0;
        try
        {
            var folderViewId = FolderViewId;
            if (Marshal.QueryInterface(shellView, in folderViewId, out folderView) < 0) return 0;
            var shellFolderId = ShellFolderId;
            if (GetMethod<GetFolder>(folderView, 5)(folderView, ref shellFolderId, out shellFolder) < 0 ||
                shellFolder == 0) return 0;
            return _filter.ShouldShow(shellFolder, 0, childPidl);
        }
        catch
        {
            return 0; // Metadata failures must leave the Shell item visible.
        }
        finally
        {
            if (shellFolder != 0) Marshal.Release(shellFolder);
            if (folderView != 0) Marshal.Release(folderView);
        }
    }

    public int Notify(nint shellView, uint notification) => _callbacks.Notify(shellView, notification);
    public int GetDefaultMenuText(nint shellView, nint text, int capacity) =>
        _callbacks.GetDefaultMenuText(shellView, text, capacity);
    public int GetViewFlags(out uint flags) => _callbacks.GetViewFlags(out flags);
    public int OnColumnClicked(nint shellView, int column) => _callbacks.OnColumnClicked(shellView, column);
    public int GetCurrentFilter(nint text, int capacity) => _callbacks.GetCurrentFilter(text, capacity);
    public int OnPreViewCreated(nint shellView) => _callbacks.OnPreViewCreated(shellView);

    private static T GetMethod<T>(nint instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * nint.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetFolder(nint self, ref Guid interfaceId, out nint folder);
}

public static class ShellViewColorBrowserHostSmoke
{
    internal static void Run()
    {
        using var host = new ShellViewColorBrowserHost(0, [],
            new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)));
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        if (browser == 0) throw new InvalidOperationException("Color host does not expose IShellBrowser.");
        try
        {
            VerifyQueryInterface<IComDialogBrowser>(browser);
            VerifyQueryInterface<IComDialogBrowser2>(browser);
            VerifyQueryInterface<IComDialogBrowser3>(browser);
            VerifyQueryServiceCallbacks(host);
            var cdb3Id = typeof(IComDialogBrowser3).GUID;
            var cdb3Hr = Marshal.QueryInterface(browser, in cdb3Id, out var cdb3);
            if (cdb3Hr < 0 || cdb3 == 0)
                throw new InvalidOperationException($"ICommDlgBrowser3 QueryInterface failed: {cdb3Hr:X8}.");
            try { VerifyCdb3VtableCallbacks(cdb3, host); }
            finally { Marshal.Release(cdb3); }
            var cdb2Id = typeof(IComDialogBrowser2).GUID;
            var cdb2Hr = Marshal.QueryInterface(browser, in cdb2Id, out var cdb2);
            if (cdb2Hr < 0 || cdb2 == 0)
                throw new InvalidOperationException($"ICommDlgBrowser2 QueryInterface failed: {cdb2Hr:X8}.");
            try { VerifyCdb2VtableCallbacks(cdb2, host); }
            finally { Marshal.Release(cdb2); }
        }
        finally { Marshal.Release(browser); }
        Check(host.GetViewFlags(out var viewFlags) == 0 && viewFlags == 0,
            "active filtering does not set CDB2 NOINCLUDEITEM on the color host");
        Check(host.IncludeObject(0, 0) == 0 && host.IncludeObjectCalls == 3,
            "missing Shell item metadata keeps the item visible through the color host callback");

        VerifyBaseHostStatusText();
        VerifyBaseHostBrowseObject();
        CheckIncludeObject("notes.txt", expected: 0,
            "an item matching the configured pattern remains visible through IncludeObject");
        CheckIncludeObject("notes.bin", expected: 1,
            "an item outside the configured pattern is filtered through IncludeObject");
        CheckIncludeObject("notes.bin", expected: 0,
            "a failed GetFolder callback keeps the Shell item visible", getFolderResult: unchecked((int)0x80004005));
        CheckIncludeObject("notes.bin", expected: 0,
            "a failed GetAttributesOf callback keeps the Shell item visible", getAttributesResult: unchecked((int)0x80004005));
        CheckIncludeObject("notes.bin", expected: 0,
            "a failed GetDisplayNameOf callback keeps the Shell item visible", getDisplayNameResult: unchecked((int)0x80004005));

        CheckNavigationCallback();
        CheckNavigationExceptionComBoundary();
    }

    private static void VerifyCdb3VtableCallbacks(nint unknown, ShellViewColorBrowserHost host)
    {
        var onDefaultCommand = GetComMethod<BrowserViewCallback>(unknown, 3);
        var onStateChange = GetComMethod<BrowserStateChangeCallback>(unknown, 4);
        var includeObject = GetComMethod<BrowserIncludeObjectCallback>(unknown, 5);
        var notify = GetComMethod<BrowserNotifyCallback>(unknown, 6);
        var getDefaultMenuText = GetComMethod<BrowserMenuTextCallback>(unknown, 7);
        var getViewFlags = GetComMethod<BrowserViewFlagsCallback>(unknown, 8);
        var onColumnClicked = GetComMethod<BrowserColumnClickedCallback>(unknown, 9);
        var getCurrentFilter = GetComMethod<BrowserFilterCallback>(unknown, 10);
        var onPreViewCreated = GetComMethod<BrowserViewCallback>(unknown, 11);

        var defaultResult = onDefaultCommand(unknown, 0);
        var stateResult = onStateChange(unknown, 0, 2); // CDBOSC_SELCHANGE.
        var includeResult = includeObject(unknown, 0, 0);
        var notifyResult = notify(unknown, 0, 0);
        var menuText = Marshal.AllocHGlobal(16);
        var filterText = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt16(menuText, 0, 0);
            Marshal.WriteInt16(filterText, 0, 0x7FFF);
            var menuTextResult = getDefaultMenuText(unknown, 0, menuText, 8);
            var flagsResult = getViewFlags(unknown, out var viewFlags);
            var columnResult = onColumnClicked(unknown, 0, 0);
            var nullFilterResult = getCurrentFilter(unknown, 0, 8);
            var emptyFilterResult = getCurrentFilter(unknown, filterText, 0);
            var invalidFilterPreservedBuffer = Marshal.ReadInt16(filterText) == 0x7FFF;
            var filterResult = getCurrentFilter(unknown, filterText, 8);
            var previewResult = onPreViewCreated(unknown, 0);
            Check(defaultResult == 1 && stateResult == 0 && host.SelectionVersion == 1 &&
                  includeResult == 0 && host.IncludeObjectCalls == 1 && notifyResult == 0 &&
                  menuTextResult == 1 && flagsResult == 0 && viewFlags == 0 && columnResult == 0 &&
                  nullFilterResult == unchecked((int)0x80070057) &&
                  emptyFilterResult == unchecked((int)0x80070057) && invalidFilterPreservedBuffer &&
                  filterResult == 0 && Marshal.ReadInt16(filterText) == 0 && previewResult == 0,
                "the complete ICommDlgBrowser3 vtable routes callbacks and preserves HRESULT/output contracts");
        }
        finally
        {
            Marshal.FreeHGlobal(filterText);
            Marshal.FreeHGlobal(menuText);
        }
    }

    private static void VerifyCdb2VtableCallbacks(nint unknown, ShellViewColorBrowserHost host)
    {
        var onDefaultCommand = GetComMethod<BrowserViewCallback>(unknown, 3);
        var onStateChange = GetComMethod<BrowserStateChangeCallback>(unknown, 4);
        var includeObject = GetComMethod<BrowserIncludeObjectCallback>(unknown, 5);
        var notify = GetComMethod<BrowserNotifyCallback>(unknown, 6);
        var getDefaultMenuText = GetComMethod<BrowserMenuTextCallback>(unknown, 7);
        var getViewFlags = GetComMethod<BrowserViewFlagsCallback>(unknown, 8);
        var text = Marshal.AllocHGlobal(16);
        try
        {
            var defaultResult = onDefaultCommand(unknown, 0);
            var stateResult = onStateChange(unknown, 0, 2);
            var includeResult = includeObject(unknown, 0, 0);
            var notifyResult = notify(unknown, 0, 0);
            var menuTextResult = getDefaultMenuText(unknown, 0, text, 8);
            var flagsResult = getViewFlags(unknown, out var viewFlags);
            Check(defaultResult == 1 && stateResult == 0 && host.SelectionVersion == 2 &&
                  includeResult == 0 && host.IncludeObjectCalls == 2 && notifyResult == 0 &&
                  menuTextResult == 1 && flagsResult == 0 && viewFlags == 0,
                "the ICommDlgBrowser2 vtable preserves inherited callback HRESULT and output contracts");
        }
        finally { Marshal.FreeHGlobal(text); }
    }

    private static T GetComMethod<T>(nint unknown, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(unknown);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * nint.Size));
    }

    private static void VerifyQueryServiceCallbacks(ShellViewColorBrowserHost host)
    {
        var provider = Marshal.GetComInterfaceForObject(host, typeof(IShellServiceProvider));
        try
        {
            var queryService = GetComMethod<QueryServiceCallback>(provider, 3);
            var serviceId = ShellBrowserSite.BrowserFrameServiceId;
            var cdb2Id = typeof(IComDialogBrowser2).GUID;
            var cdb3Id = typeof(IComDialogBrowser3).GUID;
            var cdb2Hr = queryService(provider, ref serviceId, ref cdb2Id, out var cdb2);
            var cdb3Hr = queryService(provider, ref serviceId, ref cdb3Id, out var cdb3);
            Check(cdb2Hr == 0 && cdb2 != 0 && cdb3Hr == 0 && cdb3 != 0,
                "SID_SShellBrowser QueryService exposes the color host CDB2/CDB3 callbacks");
            if (cdb2 != 0) Marshal.Release(cdb2);
            if (cdb3 != 0) Marshal.Release(cdb3);
        }
        finally { Marshal.Release(provider); }
    }

    private static void VerifyBaseHostBrowseObject()
    {
        using var pool = new ShellViewNavigationHistorySmoke.PidlPool();
        var root = pool.Create(1);
        var child = pool.Create(2);
        var failedTarget = pool.Create(4);
        var history = new ShellViewNavigationHistory(root, pool.Clone, pool.Free,
            pool.Equal, pool.Parent, pool.Combine,
            (target, _) => pool.Read(target) == 4 ? unchecked((int)0x80004005) : 0);
        var host = new StreamViewHost(0, [], navigation: history);
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        try
        {
            var browseObject = GetComMethod<BrowseObjectCallback>(browser, 11);
            var successHr = browseObject(browser, child, 0);
            Check(successHr == 0 && history.Index == 1 && pool.Read(history.CurrentPidl) == 2,
                "the generic StreamViewHost routes BrowseObject through its retained Shell navigation history");
            var failedHr = browseObject(browser, failedTarget, 0);
            Check(failedHr == unchecked((int)0x80004005) && history.Index == 1 &&
                  pool.Read(history.CurrentPidl) == 2,
                "the generic StreamViewHost returns navigation failure without changing its current history");
        }
        finally
        {
            Marshal.Release(browser);
            host.Dispose();
        }
        Check(pool.FreedCount >= 2,
            "disposing the generic StreamViewHost releases its retained navigation PIDLs");
    }

    private static void VerifyBaseHostStatusText()
    {
        using var host = new StreamViewHost(0, []);
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        var message = Marshal.StringToHGlobalUni("Loading folder contents");
        try
        {
            var setStatusText = GetComMethod<SetStatusTextCallback>(browser, 8);
            var setHr = setStatusText(browser, message);
            Check(setHr == 0 && host.StatusText == "Loading folder contents",
                "the generic StreamViewHost captures Shell status text through SetStatusTextSB");
            var clearHr = setStatusText(browser, 0);
            Check(clearHr == 0 && host.StatusText is null,
                "a null SetStatusTextSB argument clears the generic host status text");
        }
        finally
        {
            Marshal.FreeHGlobal(message);
            Marshal.Release(browser);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowserViewCallback(nint self, nint shellView);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowserStateChangeCallback(nint self, nint shellView, uint change);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowserIncludeObjectCallback(nint self, nint shellView, nint childPidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowserNotifyCallback(nint self, nint shellView, uint notification);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowserMenuTextCallback(nint self, nint shellView, nint text, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowserViewFlagsCallback(nint self, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowserColumnClickedCallback(nint self, nint shellView, int column);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowserFilterCallback(nint self, nint text, int capacity);

    private static void CheckNavigationCallback()
    {
        using var pool = new ShellViewNavigationHistorySmoke.PidlPool();
        var root = pool.Create(1);
        var child = pool.Create(2);
        var relativeChild = pool.Create(7);
        var failedTarget = pool.Create(4);
        var history = new ShellViewNavigationHistory(root, pool.Clone, pool.Free,
            pool.Equal, pool.Parent, pool.Combine,
            (target, _) => pool.Read(target) == 4 ? unchecked((int)0x80004005) : 0);
        var host = new ShellViewColorBrowserHost(0, [],
            new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)), history);
        try
        {
            Check(host.BrowseObject(child, 0) == 0 && pool.Read(host.CurrentPidl) == 2,
                "the color host routes Shell BrowseObject through its navigation history");
            Check(host.BrowseObject(0, 0x2000) == 0 && host.NavigationIndex == 2 &&
                  pool.Read(host.CurrentPidl) == 1,
                "the color host records a successful parent navigation");
            Check(host.BrowseObject(0, 0x4000) == 0 && pool.Read(host.CurrentPidl) == 2,
                "the color host routes back navigation through its retained history");
            Check(host.BrowseObject(relativeChild, 0x1000) == 0 && pool.Read(host.CurrentPidl) == 27,
                "the color host resolves relative PIDLs against its current absolute location");
            var currentIndex = host.NavigationIndex;
            var currentPidl = pool.Read(host.CurrentPidl);
            Check(host.BrowseObject(failedTarget, 0) < 0 && host.NavigationIndex == currentIndex &&
                  pool.Read(host.CurrentPidl) == currentPidl,
                "a failed color-host navigation keeps the current location and history");
        }
        finally { host.Dispose(); }

        Check(pool.FreedCount > 0,
            "disposing the color host releases the PIDLs owned by its navigation history");
    }

    private static void CheckNavigationExceptionComBoundary()
    {
        using var pool = new ShellViewNavigationHistorySmoke.PidlPool();
        var root = pool.Create(1);
        var target = pool.Create(2);
        var history = new ShellViewNavigationHistory(root, pool.Clone, pool.Free,
            pool.Equal, pool.Parent, pool.Combine,
            (_, _) => throw new COMException("fixture navigation failure", unchecked((int)0x80070005)));
        using var host = new ShellViewColorBrowserHost(0, [],
            new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)), history);
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        try
        {
            var vtable = Marshal.ReadIntPtr(browser);
            var browseObject = Marshal.GetDelegateForFunctionPointer<BrowseObjectCallback>(
                Marshal.ReadIntPtr(vtable, 11 * nint.Size)); // IShellBrowser::BrowseObject.
            var hr = browseObject(browser, target, 0);
            Check(hr == unchecked((int)0x80070005) && history.Index == 0 && history.Count == 1 &&
                  pool.Read(history.CurrentPidl) == 1 && pool.FreedCount == 1,
                "the COM BrowseObject boundary converts navigation exceptions to HRESULT and preserves history");
        }
        finally { Marshal.Release(browser); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowseObjectCallback(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryServiceCallback(nint self, ref Guid service, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetStatusTextCallback(nint self, nint text);

    private static void CheckIncludeObject(string displayName, int expected, string message,
        int getFolderResult = 0, int getAttributesResult = 0, int getDisplayNameResult = 0)
    {
        var folder = new FakeShellFolder(displayName, getAttributesResult, getDisplayNameResult);
        var view = new FakeShellView(folder, getFolderResult);
        var viewPointer = Marshal.GetComInterfaceForObject(view, typeof(IFolderViewFilterProbe));
        try
        {
            using var host = new ShellViewColorBrowserHost(0, [],
                new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)));
            var browserPointer = Marshal.GetComInterfaceForObject(host, typeof(IComDialogBrowser3));
            int result;
            try
            {
                var includeObject = GetComMethod<BrowserIncludeObjectCallback>(browserPointer, 5);
                result = includeObject(browserPointer, viewPointer, (nint)1);
            }
            finally { Marshal.Release(browserPointer); }
            Check(result == expected, $"{message} (HRESULT 0x{result:X8})");
        }
        finally { Marshal.Release(viewPointer); }
        GC.KeepAlive(folder);
        GC.KeepAlive(view);
    }

    private static void VerifyQueryInterface<T>(nint unknown) where T : class
    {
        var interfaceId = typeof(T).GUID;
        var hr = Marshal.QueryInterface(unknown, in interfaceId, out var pointer);
        if (hr < 0 || pointer == 0)
            throw new InvalidOperationException($"IShellBrowser QueryInterface for {typeof(T).Name} failed: {hr:X8}.");
        Marshal.Release(pointer);
        Console.WriteLine($"PASS IShellBrowser can query {typeof(T).Name} from the color host");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Color host callback failed: {message}.");
        Console.WriteLine($"PASS {message}");
    }

    [ComVisible(true), Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFolderViewFilterProbe
    {
        [PreserveSig] int GetCurrentViewMode(out uint mode);
        [PreserveSig] int SetCurrentViewMode(uint mode);
        [PreserveSig] int GetFolder(ref Guid interfaceId, out nint folder);
    }

    [ComVisible(true), Guid("000214E6-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellFolderFilterProbe
    {
        [PreserveSig] int ParseDisplayName(nint window, nint bindContext, nint name, nint eaten, nint item, nint attributes);
        [PreserveSig] int EnumObjects(nint window, uint flags, out nint enumerator);
        [PreserveSig] int BindToObject(nint item, nint bindContext, ref Guid interfaceId, out nint result);
        [PreserveSig] int BindToStorage(nint item, nint bindContext, ref Guid interfaceId, out nint result);
        [PreserveSig] int CompareIds(nint lParam, nint first, nint second);
        [PreserveSig] int CreateViewObject(nint window, ref Guid interfaceId, out nint result);
        [PreserveSig] int GetAttributesOf(uint count, nint childPidls, nint attributes);
        [PreserveSig] int GetUIObjectOf(nint window, uint count, nint childPidls, ref Guid interfaceId,
            nint reserved, out nint result);
        [PreserveSig] int GetDisplayNameOf(nint childPidl, uint flags, nint stringResult);
        [PreserveSig] int SetNameOf(nint window, nint childPidl, nint name, uint flags, out nint result);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class FakeShellView(IShellFolderFilterProbe folder, int getFolderResult) : IFolderViewFilterProbe
    {
        public int GetCurrentViewMode(out uint mode) { mode = 0; return 0; }
        public int SetCurrentViewMode(uint mode) => unchecked((int)0x80004001);
        public int GetFolder(ref Guid interfaceId, out nint result)
        {
            if (getFolderResult < 0) { result = 0; return getFolderResult; }
            var expected = typeof(IShellFolderFilterProbe).GUID;
            if (interfaceId != expected) { result = 0; return unchecked((int)0x80004002); }
            result = Marshal.GetComInterfaceForObject(folder, typeof(IShellFolderFilterProbe));
            return result == 0 ? unchecked((int)0x80004005) : 0;
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class FakeShellFolder(string displayName, int getAttributesResult, int getDisplayNameResult) : IShellFolderFilterProbe
    {
        public int ParseDisplayName(nint window, nint bindContext, nint name, nint eaten, nint item, nint attributes) =>
            unchecked((int)0x80004001);
        public int EnumObjects(nint window, uint flags, out nint enumerator)
        { enumerator = 0; return unchecked((int)0x80004001); }
        public int BindToObject(nint item, nint bindContext, ref Guid interfaceId, out nint result)
        { result = 0; return unchecked((int)0x80004001); }
        public int BindToStorage(nint item, nint bindContext, ref Guid interfaceId, out nint result)
        { result = 0; return unchecked((int)0x80004001); }
        public int CompareIds(nint lParam, nint first, nint second) => unchecked((int)0x80004001);
        public int CreateViewObject(nint window, ref Guid interfaceId, out nint result)
        { result = 0; return unchecked((int)0x80004001); }
        public int GetAttributesOf(uint count, nint childPidls, nint attributes)
        { Marshal.WriteInt32(attributes, 0); return getAttributesResult; }
        public int GetUIObjectOf(nint window, uint count, nint childPidls, ref Guid interfaceId,
            nint reserved, out nint result)
        { result = 0; return unchecked((int)0x80004001); }
        public int GetDisplayNameOf(nint childPidl, uint flags, nint stringResult)
        {
            if (getDisplayNameResult < 0) return getDisplayNameResult;
            Marshal.WriteInt32(stringResult, 2); // STRRET_CSTR.
            var bytes = System.Text.Encoding.ASCII.GetBytes(displayName + '\0');
            Marshal.Copy(bytes, 0, stringResult + sizeof(uint), bytes.Length);
            return 0;
        }
        public int SetNameOf(nint window, nint childPidl, nint name, uint flags, out nint result)
        { result = 0; return unchecked((int)0x80004001); }
    }
}
