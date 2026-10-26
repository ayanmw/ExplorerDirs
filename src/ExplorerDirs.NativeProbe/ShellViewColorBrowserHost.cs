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
        ShellViewNavigationHistory? navigation = null, ShellViewStateStore? stateStore = null,
        Guid? stateId = null, nint menuFrameWindow = 0,
        ShellMenuFrameCoordinator? menuFrameCoordinator = null)
        : base(parent, state, stateStore, stateId, navigation, menuFrameWindow, menuFrameCoordinator)
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
    private static readonly CoordinatorCommandObserver CoordinatorCommandObserverProcedure = ObserveCoordinatorCommand;
    private static uint _coordinatorObservedCommandId;

    internal static void Run()
    {
        VerifyPersistedSessionStream();

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

        VerifyBaseHostFrameStateRouting();
        VerifyOleInPlaceFrameSurface();
        VerifyNativeSharedMenuGroups();
        VerifyNativeMenuRollback();
        VerifyNativeSharedMenuInstallation();
        VerifySharedMenuFrameCoordination();
        VerifyStaleViewMenuCommandOwnerIsIgnored();
        VerifyBaseHostMenuMergeRouting();
        VerifyBaseHostControlRouting();
        VerifyBaseHostAcceleratorRouting();
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
        CheckIndependentPaneNavigationHistories();
        CheckNavigationExceptionComBoundary();
    }

    private static void VerifyPersistedSessionStream()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExplorerDirsColorHostState-" + Guid.NewGuid().ToString("N"));
        var store = new ShellViewStateStore(directory);
        var sessionId = Guid.NewGuid();
        var initial = new byte[] { 1, 3, 5 };
        var updated = new byte[] { 8, 6, 4, 2 };
        store.Save(sessionId, initial);
        try
        {
            var host = new ShellViewColorBrowserHost(0, [0],
                new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)),
                stateStore: store, stateId: sessionId);
            try
            {
                Check(host.GetViewStateStream(0, out var readPointer) == 0 && readPointer != 0,
                    "the color host opens the saved stream for its session key");
                try
                {
                    Check(ReadStream(readPointer).SequenceEqual(initial) && host.Reads == 1,
                        "a recreated color host restores its session's stored Shell stream");
                }
                finally { Marshal.Release(readPointer); }

                Check(host.GetViewStateStream(1, out var writePointer) == 0 && writePointer != 0,
                    "the color host provides a write stream for its session");
                try { WriteStream(writePointer, updated); }
                finally { Marshal.Release(writePointer); }
            }
            finally { host.Dispose(); }

            Check(store.TryLoad(sessionId, out var saved) && saved.SequenceEqual(updated),
                "disposing the color host persists its Shell-written stream under the session key");

            var restoredHost = new ShellViewColorBrowserHost(0, [9],
                new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)),
                stateStore: new ShellViewStateStore(directory), stateId: sessionId);
            try
            {
                Check(restoredHost.GetViewStateStream(0, out var pointer) == 0 && pointer != 0,
                    "a new color host can reopen the persisted session stream");
                try
                {
                    Check(ReadStream(pointer).SequenceEqual(updated),
                        "a new color host restores the latest persisted Shell-written bytes");
                }
                finally { Marshal.Release(pointer); }
            }
            finally { restoredHost.Dispose(); }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static byte[] ReadStream(nint pointer)
    {
        var stream = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(pointer);
        try
        {
            var size = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                stream.Stat(out var stat, 1);
                var bytes = new byte[checked((int)stat.cbSize)];
                stream.Seek(0, 0, 0);
                stream.Read(bytes, bytes.Length, size);
                Check(Marshal.ReadInt32(size) == bytes.Length, "the full session stream is readable");
                return bytes;
            }
            finally { Marshal.FreeCoTaskMem(size); }
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    private static void WriteStream(nint pointer, byte[] bytes)
    {
        var stream = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(pointer);
        try
        {
            var written = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                stream.Write(bytes, bytes.Length, written);
                Check(Marshal.ReadInt32(written) == bytes.Length, "the Shell-written session stream is complete");
            }
            finally { Marshal.FreeCoTaskMem(written); }
        }
        finally { Marshal.ReleaseComObject(stream); }
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
        var changes = new List<string?>();
        host.StatusTextChanged += changes.Add;
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        var message = Marshal.StringToHGlobalUni("Loading folder contents");
        try
        {
            var setStatusText = GetComMethod<SetStatusTextCallback>(browser, 8);
            var setHr = setStatusText(browser, message);
            Check(setHr == 0 && host.StatusText == "Loading folder contents",
                "the generic StreamViewHost captures Shell status text through SetStatusTextSB");
            Check(changes.SequenceEqual(["Loading folder contents"]),
                "the generic StreamViewHost publishes Shell status text changes to its isolated host");
            var clearHr = setStatusText(browser, 0);
            Check(clearHr == 0 && host.StatusText is null,
                "a null SetStatusTextSB argument clears the generic host status text");
            Check(changes.SequenceEqual(new string?[] { "Loading folder contents", null }),
                "clearing Shell status text publishes a null status update");
        }
        finally
        {
            Marshal.FreeHGlobal(message);
            Marshal.Release(browser);
        }
    }

    private static void VerifyBaseHostAcceleratorRouting()
    {
        using var host = new StreamViewHost(0, []);
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        var message = Marshal.AllocHGlobal(48);
        try
        {
            var translate = GetComMethod<TranslateAcceleratorCallback>(browser, 10);
            ushort receivedId = 0;
            nint receivedMessage = 0;
            host.AcceleratorHandler = (nativeMessage, id) =>
            {
                receivedMessage = nativeMessage;
                receivedId = id;
                return id == 0x54;
            };
            Check(translate(browser, message, 0x54) == 0 && receivedMessage == message && receivedId == 0x54,
                "the generic StreamViewHost lets its parent consume an application accelerator");
            Check(translate(browser, message, 0x55) == 1 && receivedId == 0x55,
                "an application accelerator declined by the parent remains available to Shell");
            host.AcceleratorHandler = (_, _) => throw new InvalidOperationException("fixture failure");
            Check(translate(browser, message, 0x54) == 1,
                "an accelerator handler exception falls through to Shell without crossing the COM boundary");
            host.AcceleratorHandler = null;
            Check(translate(browser, message, 0x54) == 1,
                "the generic StreamViewHost preserves Shell handling when no parent accelerator route exists");
            host.Dispose();
            Check(translate(browser, message, 0x54) == 1,
                "disposing the generic StreamViewHost disconnects its parent accelerator route");
        }
        finally
        {
            Marshal.FreeHGlobal(message);
            Marshal.Release(browser);
        }
    }

    private static void VerifyBaseHostControlRouting()
    {
        using var host = new StreamViewHost(0, []);
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        try
        {
            var getControlWindow = GetComMethod<GetControlWindowCallback>(browser, 13);
            var sendControlMessage = GetComMethod<SendControlMessageCallback>(browser, 14);
            Check(getControlWindow(browser, 1, out var missingWindow) == unchecked((int)0x80004005) &&
                  missingWindow == 0 && sendControlMessage(browser, 1, 0x500, 2, 3, out var missingResult) == unchecked((int)0x80004001) &&
                  missingResult == 0,
                "the generic StreamViewHost reports unsupported control windows and messages explicitly");

            uint receivedWindowId = 0;
            host.ControlWindowProvider = id => { receivedWindowId = id; return id == 2 ? 0x1234 : 0; };
            Check(getControlWindow(browser, 2, out var controlWindow) == 0 &&
                  controlWindow == 0x1234 && receivedWindowId == 2 &&
                  getControlWindow(browser, 3, out var absentWindow) == unchecked((int)0x80004005) && absentWindow == 0,
                "the generic StreamViewHost exposes only control windows supplied by its parent");

            (uint Id, uint Message, nuint WParam, nint LParam) received = default;
            host.ControlMessageHandler = (id, message, wParam, lParam) =>
            {
                received = (id, message, wParam, lParam);
                return (0, 0xBEEF);
            };
            Check(sendControlMessage(browser, 2, 0x501, 7, 9, out var messageResult) == 0 &&
                  messageResult == 0xBEEF && received == (2, 0x501, 7, 9),
                "the generic StreamViewHost forwards control message parameters and result to its parent");
            host.ControlMessageHandler = (_, _, _, _) => (unchecked((int)0x80070005), 0xCAFE);
            Check(sendControlMessage(browser, 2, 0x502, 0, 0, out var failedResult) == unchecked((int)0x80070005) &&
                  failedResult == 0xCAFE,
                "the generic StreamViewHost preserves the parent's control-message HRESULT and result");
            host.ControlMessageHandler = (_, _, _, _) => throw new InvalidOperationException("fixture failure");
            Check(sendControlMessage(browser, 2, 0x503, 0, 0, out var exceptionResult) == unchecked((int)0x80004005) &&
                  exceptionResult == 0,
                "a parent control-message exception stays inside the COM boundary");
            host.Dispose();
            Check(getControlWindow(browser, 2, out var disposedWindow) == unchecked((int)0x80004005) &&
                  disposedWindow == 0 && sendControlMessage(browser, 2, 0x504, 0, 0, out var disposedResult) == unchecked((int)0x80004001) &&
                  disposedResult == 0,
                "disposing the generic StreamViewHost disconnects parent control routes");
        }
        finally { Marshal.Release(browser); }
    }

    private static void VerifyBaseHostMenuMergeRouting()
    {
        using var host = new StreamViewHost(0, []);
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        try
        {
            var insertMenus = GetComMethod<InsertMenusCallback>(browser, 5);
            var setMenu = GetComMethod<SetMenuCallback>(browser, 6);
            var removeMenus = GetComMethod<RemoveMenusCallback>(browser, 7);
            Check(host.InsertMenusCallCount == 0 && host.SetMenuCallCount == 0 && host.RemoveMenusCallCount == 0,
                "a new isolated Shell host starts with an empty menu lifecycle trace");
            Check(insertMenus(browser, 0x111, 0x222) == 0 && setMenu(browser, 0x111, 0x333, 0x444) == 0 &&
                  removeMenus(browser, 0x111) == 0,
                "the generic StreamViewHost accepts Shell menu lifecycle calls when no parent menu is configured");
            Check(host.InsertMenusCallCount == 1 && host.SetMenuCallCount == 1 && host.RemoveMenusCallCount == 1,
                "the isolated Shell host records each shared menu lifecycle callback");

            (nint Menu, nint Widths) inserted = default;
            host.InsertMenusHandler = (menu, widths) => { inserted = (menu, widths); return 0; };
            Check(insertMenus(browser, 0x123, 0x456) == 0 && inserted == (0x123, 0x456),
                "the generic StreamViewHost forwards shared menu and group-width pointers");

            (nint Menu, nint OleMenu, nint Active) installed = default;
            host.SetMenuHandler = (menu, oleMenu, active) =>
            {
                installed = (menu, oleMenu, active);
                return 1;
            };
            Check(setMenu(browser, 0x234, 0x567, 0x89A) == 1 && installed == (0x234, 0x567, 0x89A),
                "the generic StreamViewHost forwards shared menu, OLE menu, and active-window handles");

            nint removedMenu = 0;
            host.RemoveMenusHandler = menu => { removedMenu = menu; return unchecked((int)0x80070005); };
            Check(removeMenus(browser, 0x345) == unchecked((int)0x80070005) && removedMenu == 0x345,
                "the generic StreamViewHost preserves the parent's RemoveMenus HRESULT");
            host.InsertMenusHandler = (_, _) => throw new InvalidOperationException("fixture failure");
            Check(insertMenus(browser, 0x111, 0x222) == unchecked((int)0x80004005),
                "a parent menu merge exception stays inside the COM boundary");
            host.Dispose();
            Check(insertMenus(browser, 0x111, 0x222) == 0 && setMenu(browser, 0x111, 0x333, 0x444) == 0 &&
                  removeMenus(browser, 0x111) == 0,
                "disposing the generic StreamViewHost disconnects parent menu routes");
        }
        finally { Marshal.Release(browser); }
    }

    private static void VerifyOleInPlaceFrameSurface()
    {
        var frameWindow = CreateWindowExW(0, "STATIC", "isolated OLE frame border smoke", 0,
            0, 0, 320, 200, 0, 0, 0, 0);
        Check(frameWindow != 0 && !IsWindowVisible(frameWindow),
            "the OLE frame border smoke uses a hidden parent window");
        var host = new StreamViewHost(frameWindow, []);
        var frame = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeOleInPlaceFrame));
        try
        {
            var getWindow = GetComMethod<GetWindowCallback>(frame, 3);
            var border = GetComMethod<BorderCallback>(frame, 5);
            var requestBorderSpace = GetComMethod<BorderCallback>(frame, 6);
            var setBorderSpace = GetComMethod<BorderCallback>(frame, 7);
            var setActiveObject = GetComMethod<SetActiveObjectCallback>(frame, 8);
            var insertMenus = GetComMethod<InsertMenusCallback>(frame, 9);
            var setMenu = GetComMethod<SetMenuCallback>(frame, 10);
            var removeMenus = GetComMethod<RemoveMenusCallback>(frame, 11);
            var setStatusText = GetComMethod<SetStatusTextCallback>(frame, 12);
            var enableModeless = GetComMethod<EnableModelessCallback>(frame, 13);
            var translateAccelerator = GetComMethod<TranslateAcceleratorCallback>(frame, 14);
            Check(getWindow(frame, out var window) == 0 && window == frameWindow,
                "the isolated OLE frame exposes its parent HWND through IOleWindow");
            using (var windowlessHost = new StreamViewHost(0, []))
            {
                var windowlessFrame = Marshal.GetComInterfaceForObject(windowlessHost, typeof(IStreamProbeOleInPlaceFrame));
                try
                {
                    var getWindowless = GetComMethod<GetWindowCallback>(windowlessFrame, 3);
                    Check(getWindowless(windowlessFrame, out var missingWindow) == unchecked((int)0x80004005) &&
                          missingWindow == 0,
                        "the isolated OLE frame reports E_FAIL when it has no parent HWND");
                }
                finally { Marshal.Release(windowlessFrame); }
            }
            var retiredWindow = CreateWindowExW(0, "STATIC", "retired OLE frame parent", 0,
                0, 0, 120, 80, 0, 0, 0, 0);
            Check(retiredWindow != 0 && DestroyWindow(retiredWindow) && !IsWindow(retiredWindow),
                "the stale-parent fixture is destroyed before the COM query");
            using (var staleHost = new StreamViewHost(retiredWindow, []))
            {
                var staleFrame = Marshal.GetComInterfaceForObject(staleHost, typeof(IStreamProbeOleInPlaceFrame));
                try
                {
                    var getStale = GetComMethod<GetWindowCallback>(staleFrame, 3);
                    Check(getStale(staleFrame, out var staleOutput) == unchecked((int)0x80004005) &&
                          staleOutput == 0,
                        "the isolated OLE frame reports E_FAIL when its parent HWND has been destroyed");
                }
                finally { Marshal.Release(staleFrame); }
            }
            var borderPointer = Marshal.AllocHGlobal(Marshal.SizeOf<StreamProbeRect>());
            try
            {
                Check(GetClientRect(frameWindow, out var expectedBorder),
                    "the hidden OLE frame client rectangle is readable");
                var borderResult = border(frame, borderPointer);
                var actualBorder = Marshal.PtrToStructure<StreamProbeRect>(borderPointer);
                Check(borderResult == 0 && actualBorder.Left == expectedBorder.Left &&
                      actualBorder.Top == expectedBorder.Top && actualBorder.Right == expectedBorder.Right &&
                      actualBorder.Bottom == expectedBorder.Bottom,
                    $"IOleInPlaceFrame.GetBorder returns the parent client area (hr=0x{borderResult:X8}, rect={actualBorder.Left},{actualBorder.Top},{actualBorder.Right},{actualBorder.Bottom})");
                Check(border(frame, 0) == unchecked((int)0x80070057),
                    "IOleInPlaceFrame.GetBorder rejects a null output pointer");
            }
            finally { Marshal.FreeHGlobal(borderPointer); }
            var borderWidths = Marshal.AllocHGlobal(Marshal.SizeOf<StreamProbeRect>());
            try
            {
                Marshal.StructureToPtr(default(StreamProbeRect), borderWidths, false);
                Check(requestBorderSpace(frame, borderWidths) == 0 &&
                      setBorderSpace(frame, borderWidths) == 0 && setBorderSpace(frame, 0) == 0,
                    "the isolated OLE frame accepts requests that allocate no toolbar border space");
                Marshal.StructureToPtr(new StreamProbeRect { Left = 1 }, borderWidths, false);
                Check(requestBorderSpace(frame, borderWidths) == unchecked((int)0x800401A1) &&
                      setBorderSpace(frame, borderWidths) == unchecked((int)0x800401A1),
                    "the isolated OLE frame reports the documented no-tool-space HRESULT for unsupported toolbars");
            }
            finally { Marshal.FreeHGlobal(borderWidths); }
            Check(requestBorderSpace(frame, 0) == unchecked((int)0x80070057),
                "the isolated OLE frame rejects a null border-space request");
            (nint Object, nint Name) activeObject = default;
            var activeObjectUnknown = Marshal.GetIUnknownForObject(new ActiveObjectFixture());
            try
            {
                host.ActiveObjectHandler = (value, name) => { activeObject = (value, name); return 5; };
                Check(setActiveObject(frame, activeObjectUnknown, 0xDEF) == 5 &&
                      activeObject == (activeObjectUnknown, 0xDEF),
                    "IOleInPlaceFrame.SetActiveObject forwards and retains the active COM object");
                host.ActiveObjectHandler = (_, _) => throw new InvalidOperationException("fixture failure");
                Check(setActiveObject(frame, activeObjectUnknown, 0xDEF) == unchecked((int)0x80004005),
                    "a parent active-object callback exception stays inside the COM boundary");
            }
            finally { Marshal.Release(activeObjectUnknown); }
            host.InsertMenusHandler = (_, _) => 0;
            host.SetMenuHandler = (_, _, _) => 0;
            Check(insertMenus(frame, 0x123, 0x456) == 0 && setMenu(frame, 0x234, 0x567, 0x89A) == 0 &&
                  removeMenus(frame, 0x345) == 0,
                "the isolated OLE frame accepts shared-menu lifecycle calls when no frame menu is configured");
            host.SetMenuHandler = null;

            (nint Menu, nint Widths) inserted = default;
            host.InsertMenusHandler = (menu, widths) => { inserted = (menu, widths); return 1; };
            Check(insertMenus(frame, 0x123, 0x456) == 1 && inserted == (0x123, 0x456),
                "IOleInPlaceFrame.InsertMenus reuses the isolated Shell host menu-group route");
            (nint Menu, nint OleMenu, nint Active) installed = default;
            host.SetMenuHandler = (menu, oleMenu, active) => { installed = (menu, oleMenu, active); return 2; };
            Check(setMenu(frame, 0x234, 0x567, 0x89A) == 2 && installed == (0x234, 0x567, 0x89A),
                "IOleInPlaceFrame.SetMenu reuses the Shell composite-menu route");
            nint removed = 0;
            host.RemoveMenusHandler = menu => { removed = menu; return 3; };
            Check(removeMenus(frame, 0x345) == 3 && removed == 0x345,
                "IOleInPlaceFrame.RemoveMenus reuses the Shell menu cleanup route");

            var text = Marshal.StringToHGlobalUni("frame status");
            try { Check(setStatusText(frame, text) == 0 && host.StatusText == "frame status",
                "IOleInPlaceFrame.SetStatusText publishes frame status through the common host"); }
            finally { Marshal.FreeHGlobal(text); }

            bool? modeless = null;
            host.EnableModelessHandler = enabled => { modeless = enabled; return 4; };
            Check(enableModeless(frame, 1) == 4 && modeless == true,
                "IOleInPlaceFrame.EnableModeless reuses the frame modeless-state route");
            bool? accelerated = null;
            host.AcceleratorHandler = (message, id) => { accelerated = message == 0xABC && id == 7; return accelerated.Value; };
            Check(translateAccelerator(frame, 0xABC, 7) == 0 && accelerated == true,
                "IOleInPlaceFrame.TranslateAccelerator reuses the host accelerator route");

            host.Dispose();
            Check(enableModeless(frame, 1) == 0 &&
                  translateAccelerator(frame, 0xABC, 7) == 1,
                "disposing the isolated host disconnects its OLE frame callbacks");
        }
        finally
        {
            Marshal.Release(frame);
            host.Dispose();
            Check(DestroyWindow(frameWindow) && !IsWindow(frameWindow),
                "the hidden OLE frame border parent is destroyed after the smoke");
        }
    }

    private static void VerifyNativeSharedMenuInstallation()
    {
        var oleResult = OleInitialize(0);
        if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
        nint frameWindow = 0, menu = 0, popup = 0, previousMenu = 0, descriptor = 0, browser = 0, frame = 0, activeObject = 0;
        StreamViewHost? host = null;
        try
        {
            frameWindow = CreateWindowExW(0, "STATIC", "isolated OLE menu install smoke", 0,
                0, 0, 320, 200, 0, 0, 0, 0);
            Check(frameWindow != 0 && !IsWindowVisible(frameWindow),
                "the OLE menu installation smoke uses a hidden top-level frame");
            menu = CreateMenu();
            popup = CreatePopupMenu();
            Check(menu != 0 && popup != 0 && AppendMenuW(menu, 0x10, popup, "File"),
                "a native shared menu fixture is created without showing a window");
            previousMenu = CreateMenu();
            Check(previousMenu != 0 && AppendMenuW(previousMenu, 0, 1, "existing frame menu") &&
                  SetMenu(frameWindow, previousMenu),
                "a pre-existing frame menu is installed before the Shell composite");

            var widths = Marshal.AllocHGlobal(6 * sizeof(int));
            try
            {
                for (var i = 0; i < 6; i++) Marshal.WriteInt32(widths, i * sizeof(int), 0);
                Marshal.WriteInt32(widths, 0, 1);
                descriptor = OleCreateMenuDescriptor(menu, widths);
            }
            finally { Marshal.FreeHGlobal(widths); }
            Check(descriptor != 0, "OLE creates a descriptor for the shared menu fixture");

            host = new StreamViewHost(frameWindow, []);
            browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            frame = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeOleInPlaceFrame));
            var activeObjectFixture = new ActiveObjectInterfaceFixture(frameWindow);
            activeObject = Marshal.GetComInterfaceForObject(activeObjectFixture, typeof(IOleInPlaceActiveObjectProbe));
            var setActiveObject = GetComMethod<SetActiveObjectCallback>(frame, 8);
            Check(setActiveObject(frame, activeObject, 0) == 0,
                "IOleInPlaceFrame.SetActiveObject accepts and retains a real IOleInPlaceActiveObject interface");
            var setMenu = GetComMethod<SetMenuCallback>(browser, 6);
            var removeMenus = GetComMethod<RemoveMenusCallback>(browser, 7);
            var installResult = setMenu(browser, menu, descriptor, frameWindow);
            Check(installResult == 0 && GetMenu(frameWindow) == menu,
                "IShellBrowser.SetMenuSB installs the shared HMENU and active-object dispatcher on the hidden frame");
            var removeResult = removeMenus(browser, menu);
            Check(removeResult == 0 && GetMenu(frameWindow) == previousMenu,
                "IShellBrowser.RemoveMenusSB unhooks OLE dispatch and restores the previous frame menu");
            Check(setMenu(browser, menu, descriptor, frameWindow) == 0 && GetMenu(frameWindow) == menu,
                "the same host can reinstall the composite menu after a completed deactivation");
            host.Dispose();
            host = null;
            Check(GetMenu(frameWindow) == previousMenu,
                "disposing an active menu host unhooks OLE and restores the prior frame menu");
            OleDestroyMenuDescriptor(descriptor);
            descriptor = 0;
            Console.WriteLine("PASS the caller retains and can destroy its OLE menu descriptor after host uninstallation");
        }
        finally
        {
            if (browser != 0) Marshal.Release(browser);
            if (frame != 0) Marshal.Release(frame);
            host?.Dispose();
            if (activeObject != 0) Marshal.Release(activeObject);
            if (descriptor != 0) OleDestroyMenuDescriptor(descriptor);
            if (frameWindow != 0 && GetMenu(frameWindow) != 0) SetMenu(frameWindow, 0);
            if (frameWindow != 0 && IsWindow(frameWindow)) DestroyWindow(frameWindow);
            // Destroying the menu bar also destroys its attached popup.
            if (menu != 0) DestroyMenu(menu);
            else if (popup != 0) DestroyMenu(popup);
            if (previousMenu != 0) DestroyMenu(previousMenu);
            OleUninitialize();
        }
    }

    private static void VerifySharedMenuFrameCoordination()
    {
        var oleResult = OleInitialize(0);
        if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
        nint frameWindow = 0, paneOne = 0, paneTwo = 0, baseMenu = 0, menuOne = 0, menuTwo = 0;
        nint descriptorOne = 0, descriptorTwo = 0, activeOne = 0, activeTwo = 0;
        StreamViewHost? hostOne = null, hostTwo = null;
        try
        {
            frameWindow = CreateWindowExW(0, "STATIC", "shared frame menu coordinator smoke", 0,
                0, 0, 320, 200, 0, 0, 0, 0);
            Check(frameWindow != 0 && !IsWindowVisible(frameWindow),
                "the shared menu coordinator uses a hidden top-level frame");
            paneOne = CreateWindowExW(0, "STATIC", "pane one", 0x40000000, 0, 0, 100, 80, frameWindow, 0, 0, 0);
            paneTwo = CreateWindowExW(0, "STATIC", "pane two", 0x40000000, 100, 0, 100, 80, frameWindow, 0, 0, 0);
            baseMenu = CreateMenu();
            menuOne = CreateMenu();
            menuTwo = CreateMenu();
            Check(paneOne != 0 && paneTwo != 0 && baseMenu != 0 && menuOne != 0 && menuTwo != 0 &&
                  AppendMenuW(baseMenu, 0, 1, "base") && AppendMenuW(menuOne, 0, 2, "pane one") &&
                  AppendMenuW(menuTwo, 0, 3, "pane two") && SetMenu(frameWindow, baseMenu),
                "two child panes and three independent native menu bars are created");

            descriptorOne = CreateOleDescriptor(menuOne);
            descriptorTwo = CreateOleDescriptor(menuTwo);
            using var coordinator = new ShellMenuFrameCoordinator(frameWindow);
            hostOne = new StreamViewHost(paneOne, [], menuFrameWindow: frameWindow,
                menuFrameCoordinator: coordinator);
            hostTwo = new StreamViewHost(paneTwo, [], menuFrameWindow: frameWindow,
                menuFrameCoordinator: coordinator);
            activeOne = Marshal.GetComInterfaceForObject(new ActiveObjectInterfaceFixture(paneOne),
                typeof(IOleInPlaceActiveObjectProbe));
            activeTwo = Marshal.GetComInterfaceForObject(new ActiveObjectInterfaceFixture(paneTwo),
                typeof(IOleInPlaceActiveObjectProbe));
            Check(hostOne.SetActiveObject(activeOne, 0) == 0 && hostTwo.SetActiveObject(activeTwo, 0) == 0,
                "each pane host retains its own active OLE object");

            Check(hostOne.SetMenuSB(menuOne, descriptorOne, paneOne) == 0 && GetMenu(frameWindow) == menuOne,
                "the first pane installs its menu on the shared top-level frame");
            Check(hostTwo.SetMenuSB(menuTwo, descriptorTwo, paneTwo) == 0 && GetMenu(frameWindow) == menuTwo,
                "activating the second pane transfers the frame menu and OLE descriptor");
            Check(hostOne.SetMenuSB(new nint(0xBAD), descriptorOne, paneOne) == unchecked((int)0x80070057) &&
                  GetMenu(frameWindow) == menuTwo,
                "an invalid incoming menu is rejected without disturbing the current frame owner");
            Check(hostOne.RemoveMenusSB(menuOne) == 0 && GetMenu(frameWindow) == menuTwo,
                "a late RemoveMenusSB from the inactive first pane leaves the second pane menu installed");
            hostOne.Dispose();
            hostOne = null;
            Check(GetMenu(frameWindow) == menuTwo,
                "disposing an inactive pane does not uninstall the active pane menu");
            Check(hostTwo.RemoveMenusSB(menuTwo) == 0 && GetMenu(frameWindow) == baseMenu,
                "deactivating the active pane restores the frame's pre-existing menu");
            Check(((IStreamProbeBrowser)hostTwo).GetWindow(out var browserWindow) == 0 && browserWindow == paneTwo &&
                  ((IStreamProbeOleInPlaceFrame)hostTwo).GetWindow(out var oleFrameWindow) == 0 &&
                  oleFrameWindow == frameWindow,
                "IShellBrowser retains its pane HWND while IOleInPlaceFrame exposes the shared top-level HWND");
            Console.WriteLine("PASS shared frame menu transfer, stale-owner removal, and restoration");
        }
        finally
        {
            hostOne?.Dispose();
            hostTwo?.Dispose();
            if (activeOne != 0) Marshal.Release(activeOne);
            if (activeTwo != 0) Marshal.Release(activeTwo);
            if (frameWindow != 0 && IsWindow(frameWindow) && GetMenu(frameWindow) != 0) SetMenu(frameWindow, 0);
            if (descriptorOne != 0) OleDestroyMenuDescriptor(descriptorOne);
            if (descriptorTwo != 0) OleDestroyMenuDescriptor(descriptorTwo);
            if (frameWindow != 0 && IsWindow(frameWindow)) DestroyWindow(frameWindow);
            if (baseMenu != 0) DestroyMenu(baseMenu);
            if (menuOne != 0) DestroyMenu(menuOne);
            if (menuTwo != 0) DestroyMenu(menuTwo);
            OleUninitialize();
        }
    }

    private static nint CreateOleDescriptor(nint menu)
    {
        var widths = Marshal.AllocHGlobal(6 * sizeof(int));
        try
        {
            for (var index = 0; index < 6; index++) Marshal.WriteInt32(widths, index * sizeof(int), 0);
            Marshal.WriteInt32(widths, 0, 1);
            var descriptor = OleCreateMenuDescriptor(menu, widths);
            if (descriptor == 0) throw new InvalidOperationException("OLE could not create a menu descriptor fixture.");
            return descriptor;
        }
        finally { Marshal.FreeHGlobal(widths); }
    }

    private static void VerifyStaleViewMenuCommandOwnerIsIgnored()
    {
        var oleResult = OleInitialize(0);
        if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
        nint frameWindow = 0, stalePane = 0, activePane = 0, baseMenu = 0, staleMenu = 0, activeMenu = 0;
        nint staleDescriptor = 0, activeDescriptor = 0, staleActiveObject = 0, activeActiveObject = 0;
        StreamViewHost? staleHost = null, activeHost = null;
        try
        {
            frameWindow = CreateWindowExW(0, "STATIC", "stale queued view command owner smoke", 0,
                0, 0, 320, 200, 0, 0, 0, 0);
            stalePane = CreateWindowExW(0, "STATIC", "stale pane", 0x40000000, 0, 0, 100, 80, frameWindow, 0, 0, 0);
            activePane = CreateWindowExW(0, "STATIC", "active pane", 0x40000000, 100, 0, 100, 80, frameWindow, 0, 0, 0);
            baseMenu = CreateMenu();
            staleMenu = CreateViewCommandMenu(0x7001);
            activeMenu = CreateViewCommandMenu(0x7001);
            Check(frameWindow != 0 && stalePane != 0 && activePane != 0 && baseMenu != 0 &&
                  staleMenu != 0 && activeMenu != 0 && SetMenu(frameWindow, baseMenu),
                "two pane menus with the same Shell View command are created on a hidden frame");

            staleDescriptor = CreateOleDescriptor(staleMenu);
            activeDescriptor = CreateOleDescriptor(activeMenu);
            using var coordinator = new ShellMenuFrameCoordinator(frameWindow);
            staleHost = new StreamViewHost(stalePane, [], menuFrameWindow: frameWindow,
                menuFrameCoordinator: coordinator);
            activeHost = new StreamViewHost(activePane, [], menuFrameWindow: frameWindow,
                menuFrameCoordinator: coordinator);
            staleHost.View = frameWindow; // The inactive-owner gate must run before the view pointer is dereferenced.
            staleActiveObject = Marshal.GetComInterfaceForObject(new ActiveObjectInterfaceFixture(stalePane),
                typeof(IOleInPlaceActiveObjectProbe));
            activeActiveObject = Marshal.GetComInterfaceForObject(new ActiveObjectInterfaceFixture(activePane),
                typeof(IOleInPlaceActiveObjectProbe));
            Check(staleHost.SetActiveObject(staleActiveObject, 0) == 0 &&
                  activeHost.SetActiveObject(activeActiveObject, 0) == 0 &&
                  staleHost.SetMenuSB(staleMenu, staleDescriptor, stalePane) == 0 &&
                  activeHost.SetMenuSB(activeMenu, activeDescriptor, activePane) == 0,
                "activating the second pane transfers command ownership after the first pane menu was active");

            coordinator.EnqueuePendingShellMenuCommand(staleHost, 0x7001, (nint)0x1111);
            coordinator.EnqueuePendingShellMenuCommand(activeHost, 0x7002, (nint)0x2222);
            Check(coordinator.TryDequeuePendingShellMenuCommand(out var firstPending) &&
                  ReferenceEquals(firstPending.Owner, staleHost) && firstPending.CommandId == 0x7001 &&
                  firstPending.SelectedMenu == (nint)0x1111 &&
                  coordinator.TryDequeuePendingShellMenuCommand(out var secondPending) &&
                  ReferenceEquals(secondPending.Owner, activeHost) && secondPending.CommandId == 0x7002 &&
                  secondPending.SelectedMenu == (nint)0x2222 &&
                  !coordinator.TryDequeuePendingShellMenuCommand(out _),
                "deferred Shell menu commands preserve FIFO owner, command ID, and selected menu source");

            var traceCountBeforeStaleCommand = staleHost.MenuTrace.Count;
            Check(ShellViewColorSpike.TryInvokeMergedViewMenuCommand(staleHost, 0x7001,
                      isStillActive: () => coordinator.IsCurrentOwner(staleHost)) &&
                  staleHost.MenuTrace.Count == traceCountBeforeStaleCommand,
                "a queued View menu command for the retired pane is consumed without dispatching to its view");
            Console.WriteLine("PASS a stale queued Shell View command cannot dispatch through its retired pane owner");
        }
        finally
        {
            staleHost?.Dispose();
            activeHost?.Dispose();
            if (staleActiveObject != 0) Marshal.Release(staleActiveObject);
            if (activeActiveObject != 0) Marshal.Release(activeActiveObject);
            if (frameWindow != 0 && IsWindow(frameWindow) && GetMenu(frameWindow) != 0) SetMenu(frameWindow, 0);
            if (staleDescriptor != 0) OleDestroyMenuDescriptor(staleDescriptor);
            if (activeDescriptor != 0) OleDestroyMenuDescriptor(activeDescriptor);
            if (frameWindow != 0 && IsWindow(frameWindow)) DestroyWindow(frameWindow);
            if (baseMenu != 0) DestroyMenu(baseMenu);
            if (staleMenu != 0) DestroyMenu(staleMenu);
            if (activeMenu != 0) DestroyMenu(activeMenu);
            OleUninitialize();
        }
    }

    internal static void VerifyCoordinatorCommandMessagePath()
    {
        var oleResult = OleInitialize(0);
        if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
        nint frameWindow = 0, paneWindow = 0, baseMenu = 0, compositeMenu = 0, fileMenu = 0, viewMenu = 0;
        nint fileChild = 0, descriptor = 0, activeObject = 0;
        var groups = new nint[6];
        StreamViewHost? host = null;
        var observerInstalled = false;
        try
        {
            frameWindow = CreateWindowExW(0, "STATIC", "merged menu message path smoke", 0,
                0, 0, 320, 200, 0, 0, 0, 0);
            paneWindow = CreateWindowExW(0, "STATIC", "synthetic shell pane", 0x40000000,
                0, 0, 160, 100, frameWindow, 0, 0, 0);
            baseMenu = CreateMenu();
            compositeMenu = CreateMenu();
            Check(frameWindow != 0 && paneWindow != 0 && baseMenu != 0 && compositeMenu != 0 &&
                  SetMenu(frameWindow, baseMenu),
                "the coordinator message path uses a hidden ordinary frame and pane");

            for (var index = 0; index < groups.Length; index++)
            {
                groups[index] = CreatePopupMenu();
                if (groups[index] == 0 || !AppendMenuW(compositeMenu, 0x10, groups[index], $"Group {index}"))
                    throw new InvalidOperationException("Could not create the synthetic composite Shell menu groups.");
            }
            Check(GetMenuItemCount(compositeMenu) == 6,
                "the synthetic composite Shell menu has six OLE menu groups");
            fileMenu = groups[0];
            viewMenu = groups[2];
            fileChild = CreatePopupMenu();
            Check(fileChild != 0 && AppendMenuW(fileChild, 0, (nint)0x7123, "New Item") &&
                  AppendMenuW(fileMenu, 0x10, fileChild, "New") &&
                  AppendMenuW(viewMenu, 0, (nint)0x7123, "Details") &&
                  AppendMenuW(groups[3], 0, (nint)0x7123, "Object command"),
                "File, View, and Object groups contain the same command ID under distinct menu handles");
            descriptor = CreateOleDescriptor(compositeMenu);

            using var coordinator = new ShellMenuFrameCoordinator(frameWindow);
            host = new StreamViewHost(paneWindow, [], menuFrameWindow: frameWindow,
                menuFrameCoordinator: coordinator);
            activeObject = Marshal.GetComInterfaceForObject(new ActiveObjectInterfaceFixture(paneWindow),
                typeof(IOleInPlaceActiveObjectProbe));
            Check(host.SetActiveObject(activeObject, 0) == 0 &&
                  host.SetMenuSB(compositeMenu, descriptor, paneWindow) == 0,
                "the hidden frame installs the real coordinator subclass and merged menu descriptor");
            host.View = activeObject;
            Check(SetWindowSubclass(paneWindow, CoordinatorCommandObserverProcedure, 0xD17, 0),
                "the fake pane observes deferred Shell WM_COMMAND delivery");
            observerInstalled = true;
            var traceCountBeforeCommands = host.MenuTrace.Count;

            _coordinatorObservedCommandId = 0;
            SendMessageW(frameWindow, 0x0211, 0, 0); // WM_ENTERMENULOOP.
            SendMessageW(frameWindow, 0x011F, 0x00100000, compositeMenu); // File group popup.
            SendMessageW(frameWindow, 0x011F, 0x7123, fileChild); // File leaf command selected.
            SendMessageW(frameWindow, 0x0111, 0x7123, 0); // WM_COMMAND.
            SendMessageW(frameWindow, 0x011F, 0x00100002, compositeMenu); // View group popup.
            SendMessageW(frameWindow, 0x011F, 0x7123, viewMenu); // View leaf command selected.
            SendMessageW(frameWindow, 0x0111, 0x7123, 0);
            DispatchOnePostedCoordinatorMessage(frameWindow);
            Check(_coordinatorObservedCommandId == 0x7123 && host.MenuTrace.Count == traceCountBeforeCommands + 1 &&
                  host.MenuTrace[^1].Contains("group=0", StringComparison.Ordinal),
                "the first queued duplicate ID keeps its File source after a later View selection");
            DispatchOnePostedCoordinatorMessage(frameWindow);
            Check(_coordinatorObservedCommandId == 0x7123 && host.MenuTrace.Count == traceCountBeforeCommands + 2 &&
                  host.MenuTrace[^1].Contains("group=2", StringComparison.Ordinal),
                "the next queued duplicate ID retains FIFO order and its View source");

            _coordinatorObservedCommandId = 0;
            SendMessageW(frameWindow, 0x0126, 0, fileChild); // WM_MENUCOMMAND, File item position 0.
            DispatchOnePostedCoordinatorMessage(frameWindow);
            Check(_coordinatorObservedCommandId == 0x7123 && host.MenuTrace.LastOrDefault()?.Contains("group=0", StringComparison.Ordinal) == true,
                "WM_MENUCOMMAND resolves the File command by position and retains its menu source");

            _coordinatorObservedCommandId = 0;
            SendMessageW(frameWindow, 0x0126, 0, viewMenu); // WM_MENUCOMMAND, View item position 0.
            DispatchOnePostedCoordinatorMessage(frameWindow);
            Check(_coordinatorObservedCommandId == 0x7123 && host.MenuTrace.LastOrDefault()?.Contains("group=2", StringComparison.Ordinal) == true,
                "WM_MENUCOMMAND disambiguates the same ID by its View menu handle");

            var traceCount = host.MenuTrace.Count;
            _coordinatorObservedCommandId = 0;
            SendMessageW(frameWindow, 0x0126, 0, groups[3]); // WM_MENUCOMMAND from the unsupported Object group.
            Check(!PeekMessageW(out _, frameWindow, 0x83A1, 0x83A1, 1) &&
                  _coordinatorObservedCommandId == 0 && host.MenuTrace.Count == traceCount,
                "WM_MENUCOMMAND from the Object group is consumed without dispatching to the pane");

            SendMessageW(frameWindow, 0x0212, 0, 0); // WM_EXITMENULOOP.
            Console.WriteLine("PASS coordinator WM_MENUSELECT/WM_COMMAND and WM_MENUCOMMAND posted dispatch on hidden Win32 windows");
        }
        finally
        {
            host?.Dispose();
            if (activeObject != 0) Marshal.Release(activeObject);
            if (observerInstalled && paneWindow != 0 && IsWindow(paneWindow))
                _ = RemoveWindowSubclass(paneWindow, CoordinatorCommandObserverProcedure, 0xD17);
            if (frameWindow != 0 && IsWindow(frameWindow) && GetMenu(frameWindow) != 0) SetMenu(frameWindow, 0);
            if (descriptor != 0) OleDestroyMenuDescriptor(descriptor);
            if (paneWindow != 0 && IsWindow(paneWindow)) DestroyWindow(paneWindow);
            if (frameWindow != 0 && IsWindow(frameWindow)) DestroyWindow(frameWindow);
            if (baseMenu != 0) DestroyMenu(baseMenu);
            if (compositeMenu != 0) DestroyMenu(compositeMenu);
            if (fileChild != 0) DestroyMenu(fileChild);
            foreach (var group in groups) if (group != 0) DestroyMenu(group);
            OleUninitialize();
        }
    }

    private static void DispatchOnePostedCoordinatorMessage(nint frameWindow)
    {
        if (!PeekMessageW(out var message, frameWindow, 0x83A1, 0x83A1, 1))
            throw new InvalidOperationException("The coordinator did not post its deferred Shell command message.");
        _ = DispatchMessageW(ref message);
    }

    private static nint ObserveCoordinatorCommand(nint window, uint message, nuint wParam, nint lParam,
        nuint subclassId, nint referenceData)
    {
        if (message == 0x0111)
        {
            _coordinatorObservedCommandId = (uint)(wParam & 0xFFFF);
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static nint CreateViewCommandMenu(uint commandId)
    {
        var menu = CreateMenu();
        var file = CreatePopupMenu();
        var edit = CreatePopupMenu();
        var view = CreatePopupMenu();
        if (menu == 0 || file == 0 || edit == 0 || view == 0 ||
            !AppendMenuW(view, 0, (nint)commandId, "Details") ||
            !AppendMenuW(menu, 0x10, file, "File") || !AppendMenuW(menu, 0x10, edit, "Edit") ||
            !AppendMenuW(menu, 0x10, view, "View"))
        {
            if (menu != 0) DestroyMenu(menu);
            else
            {
                if (file != 0) DestroyMenu(file);
                if (edit != 0) DestroyMenu(edit);
                if (view != 0) DestroyMenu(view);
            }
            throw new InvalidOperationException("Could not create a Shell View command menu fixture.");
        }
        return menu;
    }

    private static void VerifyNativeSharedMenuGroups()
    {
        var frameWindow = CreateWindowExW(0, "STATIC", "isolated native menu groups", 0,
            0, 0, 320, 200, 0, 0, 0, 0);
        using var host = new StreamViewHost(frameWindow, []);
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        var menu = CreateMenu();
        var foreignPopup = CreatePopupMenu();
        var widths = Marshal.AllocHGlobal(6 * sizeof(int));
        try
        {
            Check(menu != 0 && foreignPopup != 0 && AppendMenuW(menu, 0x10, foreignPopup, "Shell object menu"),
                "a native shared-menu root with a foreign popup is created for the insertion smoke");
            for (var i = 0; i < 6; i++) Marshal.WriteInt32(widths, i * sizeof(int), 0);
            var insertMenus = GetComMethod<InsertMenusCallback>(browser, 5);
            var removeMenus = GetComMethod<RemoveMenusCallback>(browser, 7);
            Check(insertMenus(browser, menu, widths) == 0 && GetMenuItemCount(menu) == 7,
                "IShellBrowser.InsertMenusSB inserts the six standard host popup groups");
            var labels = new[] { "File", "Edit", "View", "Favorites", "Tools", "Help" };
            for (var i = 0; i < labels.Length; i++)
            {
                var text = new System.Text.StringBuilder(64);
                Check(GetMenuStringW(menu, (uint)i, text, text.Capacity, 0x400) > 0 && text.ToString() == labels[i],
                    $"the shared menu group {i} is {labels[i]}");
            }
            Check(Enumerable.Range(0, 6).Select(i => GetMenuItemCommandId(menu, i))
                    .SequenceEqual(new uint[] { 0x8000, 0x8040, 0x8080, 0x8170, 0x80C0, 0x8100 }),
                "the standard Shell popup menu identifiers are assigned in documented order");
            Check(Enumerable.Range(0, 6).Select(i => Marshal.ReadInt32(widths, i * sizeof(int)))
                    .SequenceEqual(new[] { 2, 0, 3, 0, 1, 0 }),
                "OLEMENUGROUPWIDTHS reports host counts for File, Container, and Window groups");
            Check(removeMenus(browser, menu) == 0 && GetMenuItemCount(menu) == 1 &&
                  GetSubMenu(menu, 0) == foreignPopup,
                "IShellBrowser.RemoveMenusSB leaves the foreign popup untouched while removing host groups");
            Check(insertMenus(browser, menu, widths) == 0 && GetMenuItemCount(menu) == 7 &&
                  removeMenus(browser, menu) == 0 && GetMenuItemCount(menu) == 1 && GetSubMenu(menu, 0) == foreignPopup,
                "the same shared menu can be merged and removed again");
        }
        finally
        {
            Marshal.FreeHGlobal(widths);
            if (menu != 0) DestroyMenu(menu);
            else if (foreignPopup != 0) DestroyMenu(foreignPopup);
            Marshal.Release(browser);
            if (frameWindow != 0) DestroyWindow(frameWindow);
        }
    }

    private static void VerifyNativeMenuRollback()
    {
        for (var inserted = 0; inserted <= 6; inserted++)
        {
            var menu = CreateMenu();
            var popups = Enumerable.Range(0, 6).Select(_ => CreatePopupMenu()).ToArray();
            var foreignPopup = CreatePopupMenu();
            try
            {
                Check(menu != 0 && popups.All(popup => popup != 0) && foreignPopup != 0,
                    $"native popup fixtures are created for rollback after {inserted} inserted groups");
                for (var i = 0; i < inserted; i++)
                    Check(AppendMenuW(menu, 0x10, popups[i], $"owned {i}"),
                        $"owned menu group {i} is appended before rollback");
                Check(AppendMenuW(menu, 0x10, foreignPopup, "foreign"),
                    "an unrelated popup follows the partially inserted host groups");

                StreamViewHost.RollbackInsertedMenuGroups(menu, popups, inserted);

                Check(GetMenuItemCount(menu) == 1 && GetSubMenu(menu, 0) == foreignPopup &&
                      popups.Take(inserted).All(popup => !IsMenu(popup)) &&
                      popups.Skip(inserted).All(IsMenu) && IsMenu(foreignPopup),
                    $"rollback after {inserted} groups removes only inserted popups and preserves external state");
            }
            finally
            {
                if (menu != 0) DestroyMenu(menu);
                foreach (var popup in popups) if (popup != 0 && IsMenu(popup)) DestroyMenu(popup);
                if (foreignPopup != 0 && IsMenu(foreignPopup)) DestroyMenu(foreignPopup);
            }
        }
    }

    private static void VerifyBaseHostFrameStateRouting()
    {
        using var host = new StreamViewHost(0, []);
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        try
        {
            var enableModeless = GetComMethod<EnableModelessCallback>(browser, 9);
            var contextSensitiveHelp = GetComMethod<EnableModelessCallback>(browser, 4);
            var viewWindowActive = GetComMethod<ViewWindowActiveCallback>(browser, 16);
            var setToolbarItems = GetComMethod<SetToolbarItemsCallback>(browser, 17);
            Check(enableModeless(browser, 0) == 0 && contextSensitiveHelp(browser, 0) == unchecked((int)0x80004001) &&
                  viewWindowActive(browser, 0xABC) == 0 &&
                  setToolbarItems(browser, 0x111, 2, 3) == 0,
                "the generic StreamViewHost accepts frame callbacks when no parent UI is configured");

            bool? modeless = null;
            host.EnableModelessHandler = enabled =>
            {
                modeless = enabled;
                return enabled ? 0 : unchecked((int)0x80070005);
            };
            Check(enableModeless(browser, 1) == 0 && modeless == true &&
                  enableModeless(browser, 0) == unchecked((int)0x80070005) && modeless == false,
                "the generic StreamViewHost forwards modeless state and preserves the parent's HRESULT");

            bool? contextHelp = null;
            host.ContextSensitiveHelpHandler = entering =>
            {
                contextHelp = entering;
                return entering ? 1 : unchecked((int)0x80070005);
            };
            Check(contextSensitiveHelp(browser, 1) == 1 && contextHelp == true &&
                  contextSensitiveHelp(browser, 0) == unchecked((int)0x80070005) && contextHelp == false,
                "the generic StreamViewHost forwards context-help state and preserves the parent's HRESULT");
            host.ContextSensitiveHelpHandler = _ => throw new InvalidOperationException("fixture failure");
            Check(contextSensitiveHelp(browser, 1) == unchecked((int)0x80004005),
                "a parent context-help callback exception stays inside the COM boundary");

            nint activeView = 0;
            host.ViewWindowActiveHandler = view =>
            {
                activeView = view;
                return 1;
            };
            Check(viewWindowActive(browser, 0xDEF) == 1 && activeView == 0xDEF,
                "the generic StreamViewHost forwards the activated Shell view and preserves the parent's HRESULT");
            host.ViewWindowActiveHandler = _ => throw new InvalidOperationException("fixture failure");
            Check(viewWindowActive(browser, 0xDEF) == unchecked((int)0x80004005),
                "a parent view-activation callback exception stays inside the COM boundary");

            (nint Buttons, uint Count, uint Flags) toolbar = default;
            host.ToolbarItemsHandler = (buttons, count, flags) =>
            {
                toolbar = (buttons, count, flags);
                return 1;
            };
            Check(setToolbarItems(browser, 0x222, 4, 5) == 1 && toolbar == (0x222, 4, 5),
                "the generic StreamViewHost forwards Shell toolbar button data and flags");
            host.ToolbarItemsHandler = (_, _, _) => throw new InvalidOperationException("fixture failure");
            Check(setToolbarItems(browser, 0x222, 4, 5) == unchecked((int)0x80004005),
                "a parent toolbar callback exception stays inside the COM boundary");
            host.Dispose();
            Check(enableModeless(browser, 1) == 0 &&
                  contextSensitiveHelp(browser, 1) == unchecked((int)0x80004001) &&
                  viewWindowActive(browser, 0xDEF) == 0 &&
                  setToolbarItems(browser, 0x222, 4, 5) == 0,
                "disposing the generic StreamViewHost disconnects frame-state routes");
        }
        finally { Marshal.Release(browser); }
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

    private static void CheckIndependentPaneNavigationHistories()
    {
        using var pool = new ShellViewNavigationHistorySmoke.PidlPool();
        var firstRoot = pool.Create(101);
        var firstChild = pool.Create(102);
        var firstFailed = pool.Create(104);
        var secondRoot = pool.Create(201);
        var secondChild = pool.Create(202);
        var secondBranch = pool.Create(203);
        var firstHistory = new ShellViewNavigationHistory(firstRoot, pool.Clone, pool.Free,
            pool.Equal, pool.Parent, pool.Combine,
            (target, _) => pool.Read(target) == 104 ? unchecked((int)0x80004005) : 0);
        var secondHistory = new ShellViewNavigationHistory(secondRoot, pool.Clone, pool.Free,
            pool.Equal, pool.Parent, pool.Combine, (_, _) => 0);
        using var firstHost = new ShellViewColorBrowserHost(0, [],
            new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)), firstHistory);
        using var secondHost = new ShellViewColorBrowserHost(0, [],
            new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)), secondHistory);
        var firstBrowser = Marshal.GetComInterfaceForObject(firstHost, typeof(IStreamProbeBrowser));
        var secondBrowser = Marshal.GetComInterfaceForObject(secondHost, typeof(IStreamProbeBrowser));
        try
        {
            var firstBrowse = GetComMethod<BrowseObjectCallback>(firstBrowser, 11);
            var secondBrowse = GetComMethod<BrowseObjectCallback>(secondBrowser, 11);
            Check(firstBrowse(firstBrowser, firstChild, 0) == 0 && firstHistory.Index == 1 &&
                  pool.Read(firstHistory.CurrentPidl) == 102 && secondHistory.Index == 0 &&
                  pool.Read(secondHistory.CurrentPidl) == 201,
                "the first Shell pane records navigation without changing its sibling history");
            Check(secondBrowse(secondBrowser, secondChild, 0) == 0 && secondHistory.Index == 1 &&
                  pool.Read(secondHistory.CurrentPidl) == 202 && firstHistory.Index == 1 &&
                  pool.Read(firstHistory.CurrentPidl) == 102,
                "interleaved navigation keeps each Shell pane at its own location");
            Check(firstBrowse(firstBrowser, 0, 0x4000) == 0 && firstHistory.Index == 0 &&
                  pool.Read(firstHistory.CurrentPidl) == 101 && secondHistory.Index == 1 &&
                  pool.Read(secondHistory.CurrentPidl) == 202,
                "Back in one Shell pane does not move the sibling pane");
            Check(secondBrowse(secondBrowser, secondBranch, 0) == 0 &&
                  secondHistory.Index == 2 && pool.Read(secondHistory.CurrentPidl) == 203 &&
                  firstHistory.Index == 0 && pool.Read(firstHistory.CurrentPidl) == 101,
                "a history branch in one Shell pane does not replace the sibling travel log");
            Check(firstBrowse(firstBrowser, firstFailed, 0) == unchecked((int)0x80004005) &&
                  firstHistory.Index == 0 && pool.Read(firstHistory.CurrentPidl) == 101 &&
                  secondHistory.Index == 2 && pool.Read(secondHistory.CurrentPidl) == 203,
                "a failed navigation preserves both pane locations and independent history indices");
        }
        finally
        {
            Marshal.Release(secondBrowser);
            Marshal.Release(firstBrowser);
        }
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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int TranslateAcceleratorCallback(nint self, nint message, ushort id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetControlWindowCallback(nint self, uint id, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SendControlMessageCallback(nint self, uint id, uint message, nuint wParam, nint lParam, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int InsertMenusCallback(nint self, nint menu, nint widths);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetMenuCallback(nint self, nint menu, nint oleMenu, nint active);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int RemoveMenusCallback(nint self, nint menu);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnableModelessCallback(nint self, int enable);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ViewWindowActiveCallback(nint self, nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetToolbarItemsCallback(nint self, nint buttons, uint count, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetWindowCallback(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BorderCallback(nint self, nint border);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetActiveObjectCallback(nint self, nint activeObject, nint name);

    [ComVisible(true), Guid("00000117-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleInPlaceActiveObjectProbe
    {
        [PreserveSig] int GetWindow(out nint window);
        [PreserveSig] int ContextSensitiveHelp(int enter);
        [PreserveSig] int TranslateAccelerator(nint message);
        [PreserveSig] int OnFrameWindowActivate(int activate);
        [PreserveSig] int OnDocWindowActivate(int activate);
        [PreserveSig] int ResizeBorder(nint border, nint uiWindow, int frameWindow);
        [PreserveSig] int EnableModeless(int enable);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ActiveObjectInterfaceFixture(nint window) : IOleInPlaceActiveObjectProbe
    {
        public int GetWindow(out nint result) { result = window; return 0; }
        public int ContextSensitiveHelp(int enter) => 0;
        public int TranslateAccelerator(nint message) => 1;
        public int OnFrameWindowActivate(int activate) => 0;
        public int OnDocWindowActivate(int activate) => 0;
        public int ResizeBorder(nint border, nint uiWindow, int frameWindow) => 0;
        public int EnableModeless(int enable) => 0;
    }

    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern nint OleCreateMenuDescriptor(nint menu, nint widths);
    [DllImport("ole32.dll")] private static extern void OleDestroyMenuDescriptor(nint descriptor);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(nint window, out StreamProbeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint CreateMenu();
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern nint GetSubMenu(nint menu, int position);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(nint menu, uint item, System.Text.StringBuilder text, int capacity, uint flags);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public nint SubMenu, CheckedBitmap, UncheckedBitmap, ItemData, Text;
        public uint TextLength;
        public nint ItemBitmap;
    }
    private static uint GetMenuItemCommandId(nint menu, int position)
    {
        var item = new NativeMenuItemInfo { Size = (uint)Marshal.SizeOf<NativeMenuItemInfo>(), Mask = 0x2 };
        if (!GetMenuItemInfoW(menu, (uint)position, true, ref item))
            throw new InvalidOperationException("GetMenuItemInfoW could not read a native menu item.");
        return item.Id;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMenuItemInfoW(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition,
        ref NativeMenuItemInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(nint menu, uint flags, nint idOrSubmenu, string text);
    [DllImport("user32.dll")] private static extern bool IsMenu(nint menu);
    [DllImport("user32.dll")] private static extern nint GetMenu(nint window);
    [DllImport("user32.dll")] private static extern bool SetMenu(nint window, nint menu);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out NativeMessage message, nint window,
        uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window,
        CoordinatorCommandObserver callback, nuint subclassId, nint referenceData);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window,
        CoordinatorCommandObserver callback, nuint subclassId);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PointX, PointY;
        public uint Private;
    }
    private delegate nint CoordinatorCommandObserver(nint window, uint message, nuint wParam, nint lParam,
        nuint subclassId, nint referenceData);

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ActiveObjectFixture { }

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
