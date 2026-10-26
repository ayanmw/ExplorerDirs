using System.Runtime.InteropServices;
using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;

// A hidden IShellView consumes the exported stream through IShellBrowser.
// No application decoder/setters, visible window, input, activation or registry API.
internal static class QdrNativeViewProbe
{
    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden view-state stream probe",
            0x80000000, 0, 0, 900, 600, 0, 0, 0, 0);
        if (parent == 0) throw new Exception("Hidden parent creation failed.");
        nint pidl = 0, folder = 0;
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        string? fixturePath = null;
        var result = 0;
        try
        {
            fixturePath = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixturePath);
            File.WriteAllText(Path.Combine(fixturePath, "one.txt"), "one");
            File.WriteAllText(Path.Combine(fixturePath, "two.bin"), "two");
            Check(SHParseDisplayName(fixturePath, 0, out pidl, 0, out _));
            var folderId = new Guid("000214E6-0000-0000-C000-000000000046");
            Check(SHBindToObject(0, pidl, 0, in folderId, out folder));
            var format = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
            var name = new PropertyKeyState(format, 10);
            var type = new PropertyKeyState(format, 4);
            var columns = new DisplayColumnState[]
            { new(name, 271), new(new(format, 12), 137), new(type, 193) };
            foreach (var grouped in new[] { false, true })
            foreach (var style in ViewStyle.All)
            {
                var size = style.IconSize > 0 ? style.IconSize : style.Mode switch { 6 => 48, 8 => 32, _ => 16 };
                var expected = new ViewState(style.Mode, size, [new(name, -1)],
                    grouped ? type : null, false, columns);
                var text = QdrViewWriter.Write(expected)!;
                var streamBytes = Convert.FromHexString(text.Split('|')[0].Replace("%", ""));
                RunView(parent, folder, streamBytes, expected);
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Hidden/foreground boundary changed.");
            }
        }
        catch (Exception ex) { Console.WriteLine("qdr-native-stream failure: " + ex.Message); result = 1; }
        finally
        {
            if (folder != 0) Marshal.Release(folder);
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (fixturePath is not null)
            {
                var fullFixturePath = Path.GetFullPath(fixturePath);
                var fixturePrefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (fullFixturePath.StartsWith(fixturePrefix, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(fullFixturePath))
                {
                    try { Directory.Delete(fullFixturePath, recursive: true); }
                    catch (Exception ex)
                    {
                        Console.WriteLine("qdr-native-stream failure: fixture cleanup failed: " + ex.GetType().Name);
                        result = 1;
                    }
                }
            }
            if (!DestroyWindow(parent) || IsWindow(parent))
            {
                Console.WriteLine("qdr-native-stream failure: hidden parent cleanup failed.");
                result = 1;
            }
        }
        if (GetForegroundWindow() != foreground)
        {
            Console.WriteLine("qdr-native-stream failure: foreground changed during cleanup.");
            result = 1;
        }
        if (result == 0)
            Console.WriteLine("qdr-native-stream: 16 cases passed; parent-visible=False; foreground-unchanged=True");
        return result;
    }

    private static void RunView(nint parent, nint folder, byte[] bytes, ViewState expected)
    {
        var viewId = new Guid("000214E3-0000-0000-C000-000000000046");
        nint view = 0, hostPointer = 0, folderView = 0, viewWindow = 0;
        var created = false;
        var host = new StreamViewHost(parent, bytes);
        try
        {
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref viewId, out view));
            host.View = view;
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var rect = new StreamProbeRect { Right = 900, Bottom = 600 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref rect, out viewWindow));
            created = true;
            var until = Environment.TickCount64 + 1000;
            while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(10); }
            var fvId = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
            Check(Marshal.QueryInterface(view, in fvId, out folderView));
            Check(At<GetMode>(folderView, 36)(folderView, out var mode, out var size));
            Check(At<GetSortCount>(folderView, 26)(folderView, out var count));
            if (count is < 0 or > 16) throw new Exception("Unexpected sort count.");
            var sorts = new StreamProbeSort[count];
            Check(At<GetSorts>(folderView, 28)(folderView, sorts, count));
            var groupResult = At<GetGroup>(folderView, 18)(folderView, out var group, out var ascending);
            if (groupResult != 1) Check(groupResult);
            Check(ShellColumnManager.Capture(folderView, out var columns));
            var sortMatches = sorts.Select(s => new SortColumnState(new(s.Key.Id, s.Key.Property), s.Direction))
                .SequenceEqual(expected.SortColumns);
            var groupMatches = expected.GroupKey is null ? groupResult == 1 :
                groupResult == 0 && new PropertyKeyState(group.Id, group.Property) == expected.GroupKey &&
                (ascending != 0) == expected.GroupAscending;
            var columnMatches = columns is not null && columns.SequenceEqual(expected.DisplayColumns!);
            Console.WriteLine($"qdr-native-stream: requested={expected.Mode}/{expected.IconSize}; grouped={expected.GroupKey is not null}; bytes={bytes.Length}; " +
                $"stream-reads={host.Reads}; mode={mode}/{size}; sort={sortMatches}; group={groupMatches}; columns={columnMatches}; " +
                $"child-visible={IsWindowVisible(viewWindow)}");
            if (IsWindowVisible(viewWindow) || host.Reads == 0 || mode != expected.Mode || size != expected.IconSize ||
                !sortMatches || !groupMatches || !columnMatches)
            {
                // Diagnose the native persistence contract from its own output.
                // No application decoder participates in the restore check above.
                Check(At<SetMode>(folderView, 35)(folderView, expected.Mode, expected.IconSize));
                Check(At<GetMode>(folderView, 36)(folderView, out var setMode, out var setSize));
                Check(At<Simple>(view, 13)(view));
                var nativeBytes = host.ReadSavedBytes();
                Console.WriteLine($"qdr-native-stream persistence: applied={setMode}/{setSize}; writes={host.Writes}; saved-bytes={nativeBytes.Length}");
                if (nativeBytes.Length > 0)
                {
                    var encoded = string.Concat(nativeBytes.Select(b => "%" + b.ToString("X2"))) +
                        $"|CODEMODE1|{QdrViewSettings.ComputeChecksum(nativeBytes)}|{nativeBytes.Length}";
                    var known = QdrViewSettings.TryRead(encoded, out var stored);
                    Console.WriteLine($"qdr-native-stream native-saved-known={known}; saved-mode={stored.Mode}/{stored.IconSize}");
                    Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "stream-diagnostics"));
                    File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "stream-diagnostics", "native-view.bin"), nativeBytes);
                    File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "stream-diagnostics", "exported-view.bin"), bytes);
                }
                throw new Exception("Native Shell did not restore the exported stream exactly.");
            }
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            try
            {
                if (created)
                {
                    Check(At<Simple>(view, 10)(view));
                    if (IsWindow(viewWindow)) throw new Exception("Native view cleanup left a live window.");
                }
            }
            finally
            {
                host.View = 0;
                if (view != 0) Marshal.Release(view);
                if (hostPointer != 0) Marshal.Release(hostPointer);
                host.Dispose();
                GC.KeepAlive(host);
            }
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    [StructLayout(LayoutKind.Sequential)] private struct StreamProbeKey { public Guid Id; public uint Property; }
    [StructLayout(LayoutKind.Sequential)] private struct StreamProbeSort { public StreamProbeKey Key; public int Direction; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings,
        nint browser, ref StreamProbeRect rect, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Simple(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetMode(nint self, out uint mode, out int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetMode(nint self, uint mode, int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortCount(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSorts(nint self, [Out] StreamProbeSort[] sorts, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetGroup(nint self, out StreamProbeKey key, out int ascending);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}

[StructLayout(LayoutKind.Sequential)] internal struct StreamProbeSettings { public uint Mode, Flags; }
[StructLayout(LayoutKind.Sequential)] internal struct StreamProbeRect { public int Left, Top, Right, Bottom; }

// Kept inheritable so opt-in probes can add Shell callbacks without changing the shared host behavior.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public class StreamViewHost : IStreamProbeBrowser, IStreamProbeOleInPlaceFrame, IShellServiceProvider, IDisposable
{
    internal const uint ProbeApplicationCommandId = 0x7FF0;
    private readonly nint _parent;
    private readonly nint _menuFrameWindow;
    private readonly ShellMenuFrameCoordinator? _menuFrameCoordinator;
    private readonly byte[] _bytes;
    private readonly ShellViewStateStore? _stateStore;
    private readonly Guid? _stateId;
    private nint _activeObject;
    private nint _installedSharedMenu;
    private nint _installedActiveWindow;
    private nint _previousFrameMenu;
    private nint _insertedMenuRoot;
    private nint _customMenuRoot;
    private nint _customMenuPopup;
    private readonly nint[] _insertedMenuPopups = new nint[6];
    private ShellViewNavigationHistory? _navigation;
    private readonly List<ShellViewDropTargetRegistration> _dropTargetRegistrations = [];
    private int _insertMenusCallCount;
    private int _setMenuCallCount;
    private int _removeMenusCallCount;
    internal nint View;
    internal ShellBrowserSite? BrowserSite;
    internal string? StatusText { get; private set; }
    internal event Action<string?>? StatusTextChanged;
    internal Func<nint, ushort, bool>? AcceleratorHandler { get; set; }
    internal Func<bool, int>? ContextSensitiveHelpHandler { get; set; }
    internal Func<uint, nint>? ControlWindowProvider { get; set; }
    internal Func<uint, uint, nuint, nint, (int HResult, nint Result)>? ControlMessageHandler { get; set; }
    internal Func<nint, nint, int>? InsertMenusHandler { get; set; }
    internal Func<nint, nint, nint, int>? SetMenuHandler { get; set; }
    internal Func<nint, int>? RemoveMenusHandler { get; set; }
    internal int InsertMenusCallCount => Volatile.Read(ref _insertMenusCallCount);
    internal int SetMenuCallCount => Volatile.Read(ref _setMenuCallCount);
    internal int RemoveMenusCallCount => Volatile.Read(ref _removeMenusCallCount);
    internal int ApplicationMenuCommandCount { get; private set; }
    internal nint BrowserParentWindow => _parent;
    internal void RegisterDropTarget(nint viewWindow, nint dropTarget)
    {
        if (_dropTargetRegistrations.Any(registration => registration.Window == viewWindow))
            throw new InvalidOperationException("This Shell view window already has a registered drop target.");
        _dropTargetRegistrations.Add(new ShellViewDropTargetRegistration(viewWindow, dropTarget));
    }
    internal void RevokeDropTarget(nint viewWindow)
    {
        var registration = _dropTargetRegistrations.FirstOrDefault(item => item.Window == viewWindow);
        if (registration is null) return;
        registration.Dispose();
        _dropTargetRegistrations.Remove(registration);
    }
    internal void RevokeDropTarget()
    {
        while (_dropTargetRegistrations.Count > 0)
            RevokeDropTarget(_dropTargetRegistrations[^1].Window);
    }
    internal nint MenuFrameWindow => _menuFrameWindow;
    internal bool OwnsAttachedCustomMenuGroup => _customMenuRoot != 0 && _customMenuPopup != 0 && HasSubMenu(_customMenuRoot, _customMenuPopup);
    internal List<string> MenuTrace { get; } = [];
    internal Func<bool, int>? EnableModelessHandler { get; set; }
    internal Func<nint, int>? ViewWindowActiveHandler { get; set; }
    internal Func<nint, uint, uint, int>? ToolbarItemsHandler { get; set; }
    internal Func<nint, nint, int>? ActiveObjectHandler { get; set; }
    internal ShellViewNavigationHistory? NavigationHistory
    {
        get => _navigation;
        set => _navigation = value;
    }
    internal int Reads;
    internal int Writes;
    private nint _savedStream;
    internal byte[] ReadSavedBytes()
    {
        if (_savedStream == 0) return [];
        var stream = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(_savedStream);
        var count = Marshal.AllocCoTaskMem(4);
        try
        {
            stream.Stat(out var stat, 1);
            if (stat.cbSize is < 0 or > 65536) throw new Exception("Native saved stream exceeds probe limit.");
            var data = new byte[(int)stat.cbSize];
            stream.Seek(0, 0, 0);
            Marshal.WriteInt32(count, 0);
            stream.Read(data, data.Length, count);
            if (Marshal.ReadInt32(count) != data.Length) throw new Exception("Incomplete native saved stream.");
            return data;
        }
        finally { Marshal.FreeCoTaskMem(count); Marshal.ReleaseComObject(stream); }
    }
    public void Dispose()
    {
        try
        {
            RevokeDropTarget();
            if (_savedStream != 0 && _stateStore is not null && _stateId is Guid stateId)
                _stateStore.Save(stateId, ReadSavedBytes());
        }
        finally
        {
            try
            {
                if (_savedStream != 0)
                {
                    Marshal.Release(_savedStream);
                    _savedStream = 0;
                }
            }
            finally
            {
                UninstallSharedMenu();
                RemoveCustomMenuGroup(_customMenuRoot);
                RemoveOwnedMenuGroups(_insertedMenuRoot);
                if (_activeObject != 0)
                {
                    Marshal.Release(_activeObject);
                    _activeObject = 0;
                }
                var navigation = _navigation;
                _navigation = null;
                AcceleratorHandler = null;
                ContextSensitiveHelpHandler = null;
                ControlWindowProvider = null;
                ControlMessageHandler = null;
                InsertMenusHandler = null;
                SetMenuHandler = null;
                RemoveMenusHandler = null;
                EnableModelessHandler = null;
                ViewWindowActiveHandler = null;
                ToolbarItemsHandler = null;
                ActiveObjectHandler = null;
                StatusTextChanged = null;
                navigation?.Dispose();
            }
        }
    }
    public int QueryService(ref Guid service, ref Guid iid, out nint pointer)
    {
        pointer = 0;
        if (service == ShellBrowserSite.BrowserFrameServiceId)
        {
            if (BrowserSite is not null)
                return BrowserSite.QueryService(ref service, ref iid, out pointer);

            // Standalone IShellView hosts expose their own CDB callbacks through
            // SID_SShellBrowser, as DefView asks IServiceProvider rather than
            // relying on QueryInterface against the IShellBrowser pointer.
            var unknown = Marshal.GetIUnknownForObject(this);
            try { return Marshal.QueryInterface(unknown, in iid, out pointer); }
            finally { Marshal.Release(unknown); }
        }
        var browserId = typeof(IStreamProbeBrowser).GUID;
        if (iid == browserId && (service == browserId ||
            service == new Guid("4c96be40-915c-11cf-99d3-00aa004ae837")))
        {
            pointer = Marshal.GetComInterfaceForObject(this, typeof(IStreamProbeBrowser));
            return 0;
        }
        return unchecked((int)0x80004002);
    }
    // Imported view bytes can be shared by callers; snapshot them so later tab/source edits cannot mutate this host's fallback state.
    internal StreamViewHost(nint parent, byte[] bytes, ShellViewStateStore? stateStore = null,
        Guid? stateId = null, ShellViewNavigationHistory? navigation = null,
        nint menuFrameWindow = 0, ShellMenuFrameCoordinator? menuFrameCoordinator = null)
    {
        if ((stateStore is null) != (stateId is null))
            throw new ArgumentException("A view-state store and session ID must be supplied together.");
        _parent = parent;
        _menuFrameWindow = menuFrameWindow == 0 ? parent : menuFrameWindow;
        _menuFrameCoordinator = menuFrameCoordinator;
        _bytes = (byte[])bytes.Clone();
        _stateStore = stateStore;
        _stateId = stateId;
        _navigation = navigation;
    }
    public int GetWindow(out nint window)
    {
        window = 0;
        if (_parent == 0 || !IsWindow(_parent)) return unchecked((int)0x80004005);
        window = _parent;
        return 0;
    }
    int IStreamProbeOleInPlaceFrame.GetWindow(out nint window)
    {
        window = 0;
        if (_menuFrameWindow == 0 || !IsWindow(_menuFrameWindow)) return unchecked((int)0x80004005);
        window = _menuFrameWindow;
        return 0;
    }
    internal nint ActiveObjectPointer => _activeObject;
    public int ContextSensitiveHelp(int enter)
    {
        if (ContextSensitiveHelpHandler is not { } handler) return unchecked((int)0x80004001);
        try { return handler(enter != 0); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell context help: {ex.GetType().Name}");
            return unchecked((int)0x80004005);
        }
    }
    public int InsertMenusSB(nint menu, nint widths)
    {
        Interlocked.Increment(ref _insertMenusCallCount);
        int result;
        if (InsertMenusHandler is not { } handler)
            result = _parent == 0 ? 0 : InsertNativeMenuGroups(menu, widths);
        else try { result = handler(menu, widths); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell menu insertion: {ex.GetType().Name}");
            result = unchecked((int)0x80004005);
        }
        MenuTrace.Add($"insert menu=0x{menu:X} hr=0x{result:X8} valid={IsMenu(menu)} count={(IsMenu(menu) ? GetMenuItemCount(menu) : -1)}");
        return result;
    }
    public int SetMenuSB(nint menu, nint oleMenu, nint active)
    {
        Interlocked.Increment(ref _setMenuCallCount);
        int result;
        if (SetMenuHandler is not { } handler)
        {
            result = menu == 0
                ? RemoveCustomMenuGroup(_customMenuRoot)
                : _parent == 0 ? 0 : EnsureCustomMenuGroup(menu);
            if (result >= 0 && menu == 0) result = InstallSharedMenu(menu, oleMenu, active);
            if (result >= 0 && menu != 0) result = InstallSharedMenu(menu, oleMenu, active);
        }
        else try { result = handler(menu, oleMenu, active); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell menu activation: {ex.GetType().Name}");
            result = unchecked((int)0x80004005);
        }
        var installed = GetMenu(_menuFrameWindow);
        MenuTrace.Add($"set menu=0x{menu:X} active=0x{active:X} hr=0x{result:X8} frame=0x{_menuFrameWindow:X} installed=0x{installed:X} valid={IsMenu(installed)} count={(IsMenu(installed) ? GetMenuItemCount(installed) : -1)}");
        return result;
    }
    public int RemoveMenusSB(nint menu)
    {
        Interlocked.Increment(ref _removeMenusCallCount);
        int result;
        if (RemoveMenusHandler is not { } handler)
        {
            result = _menuFrameCoordinator is not null
                ? _menuFrameCoordinator.RemoveMenu(this, menu)
                : menu == _installedSharedMenu ? UninstallSharedMenu() : 0;
            // Shell may pass a different composite handle here than the one it
            // supplied to SetMenuSB; remove only popup handles this host owns.
            if (result >= 0) result = RemoveOwnedMenuGroups(_insertedMenuRoot);
            if (result >= 0) result = RemoveCustomMenuGroup(_customMenuRoot);
        }
        else try { result = handler(menu); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell menu removal: {ex.GetType().Name}");
            result = unchecked((int)0x80004005);
        }
        MenuTrace.Add($"remove menu=0x{menu:X} hr=0x{result:X8} frame=0x{_menuFrameWindow:X} installed=0x{GetMenu(_menuFrameWindow):X}");
        return result;
    }
    public int SetStatusTextSB(nint text)
    {
        StatusText = text == 0 ? null : Marshal.PtrToStringUni(text);
        try { StatusTextChanged?.Invoke(StatusText); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Shell status notification: {ex.GetType().Name}"); }
        return 0;
    }
    public int EnableModelessSB(int enable)
    {
        if (EnableModelessHandler is not { } handler) return 0;
        try { return handler(enable != 0); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell modeless state: {ex.GetType().Name}");
            return unchecked((int)0x80004005);
        }
    }
    public int TranslateAcceleratorSB(nint msg, ushort id)
    {
        if (AcceleratorHandler is not { } handler) return 1; // S_FALSE leaves the key with Shell.
        try { return handler(msg, id) ? 0 : 1; }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell accelerator notification: {ex.GetType().Name}");
            return 1;
        }
    }
    public virtual int BrowseObject(nint pidl, uint flags) =>
        _navigation?.BrowseObject(pidl, flags) ?? unchecked((int)0x80004001);
    public int GetViewStateStream(uint mode, out nint stream)
    {
        stream = 0;
        var access = mode & 3;
        if (access == 3) return unchecked((int)0x80070057);
        var read = access is 0 or 2;
        var write = access is 1 or 2;
        if (read) Reads++;
        if (write) Writes++;
        // Read-write callers need the latest snapshot as their initial stream contents, then may replace it in place.
        var bytes = read ? GetReadStateBytes() : [];
        stream = SHCreateMemStream(bytes, (uint)bytes.Length);
        if (write && stream != 0)
        {
            if (_savedStream != 0) Marshal.Release(_savedStream);
            _savedStream = stream;
            Marshal.AddRef(stream);
        }
        return stream == 0 ? unchecked((int)0x8007000E) : 0;
    }
    private byte[] GetReadStateBytes()
    {
        if (_savedStream != 0) return ReadSavedBytes();
        if (_stateStore is not null && _stateId is Guid stateId && _stateStore.TryLoad(stateId, out var persisted))
            return persisted;
        return _bytes;
    }
    public int GetControlWindow(uint id, out nint window)
    {
        window = 0;
        if (ControlWindowProvider is not { } provider) return unchecked((int)0x80004005);
        try
        {
            window = provider(id);
            if (window != 0) return 0;
            return unchecked((int)0x80004005);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell control-window lookup: {ex.GetType().Name}");
            return unchecked((int)0x80004005);
        }
    }
    public int SendControlMsg(uint id, uint message, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (ControlMessageHandler is not { } handler) return unchecked((int)0x80004001);
        try
        {
            var response = handler(id, message, wParam, lParam);
            result = response.Result;
            return response.HResult;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell control-message routing: {ex.GetType().Name}");
            return unchecked((int)0x80004005);
        }
    }
    public int QueryActiveShellView(out nint view)
    { view = View; if (view != 0) Marshal.AddRef(view); return view == 0 ? unchecked((int)0x80004005) : 0; }
    public int OnViewWindowActive(nint view)
    {
        if (ViewWindowActiveHandler is not { } handler) return 0;
        try { return handler(view); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell view activation: {ex.GetType().Name}");
            return unchecked((int)0x80004005);
        }
    }
    public int SetToolbarItems(nint buttons, uint count, uint flags)
    {
        if (ToolbarItemsHandler is not { } handler) return 0;
        try { return handler(buttons, count, flags); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell toolbar items: {ex.GetType().Name}");
            return unchecked((int)0x80004005);
        }
    }
    public int GetBorder(nint border)
    {
        if (border == 0) return unchecked((int)0x80070057);
        if (_parent == 0 || !IsWindow(_parent)) return unchecked((int)0x80004005);
        if (!GetClientRect(_parent, out var client)) return Marshal.GetHRForLastWin32Error();
        Marshal.StructureToPtr(new StreamProbeRect
        {
            Left = client.Left,
            Top = client.Top,
            Right = client.Right,
            Bottom = client.Bottom
        }, border, false);
        return 0;
    }
    public int RequestBorderSpace(nint border)
    {
        if (border == 0) return unchecked((int)0x80070057);
        return HasRequestedBorderSpace(border) ? unchecked((int)0x800401A1) : 0;
    }
    public int SetBorderSpace(nint border) =>
        border == 0 || !HasRequestedBorderSpace(border) ? 0 : unchecked((int)0x800401A1);
    private static bool HasRequestedBorderSpace(nint border)
    {
        var widths = Marshal.PtrToStructure<StreamProbeRect>(border);
        return widths.Left != 0 || widths.Top != 0 || widths.Right != 0 || widths.Bottom != 0;
    }
    public int SetActiveObject(nint activeObject, nint name)
    {
        var result = 0;
        try
        {
            if (ActiveObjectHandler is { } handler) result = handler(activeObject, name);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell active object: {ex.GetType().Name}");
            return unchecked((int)0x80004005);
        }
        if (result < 0) return result;
        if (activeObject != 0) Marshal.AddRef(activeObject);
        var previous = _activeObject;
        _activeObject = activeObject;
        if (previous != 0) Marshal.Release(previous);
        return result;
    }
    public int InsertMenus(nint sharedMenu, nint groupWidths) => InsertMenusSB(sharedMenu, groupWidths);
    public int SetMenu(nint sharedMenu, nint oleMenu, nint activeWindow) => SetMenuSB(sharedMenu, oleMenu, activeWindow);
    public int RemoveMenus(nint sharedMenu) => RemoveMenusSB(sharedMenu);
    public int SetStatusText(nint text) => SetStatusTextSB(text);
    public int EnableModeless(int enable) => EnableModelessSB(enable);
    public int TranslateAccelerator(nint message, ushort id) => TranslateAcceleratorSB(message, id);

    private int InstallSharedMenu(nint menu, nint oleMenu, nint active)
    {
        if (_parent == 0) return 0;
        if (_menuFrameCoordinator is not null)
            return _menuFrameCoordinator.SetMenu(this, menu, oleMenu, active);
        if (menu == 0) return UninstallSharedMenu();
        if (!IsWindow(_menuFrameWindow) || !IsMenu(menu)) return unchecked((int)0x80070057);
        if (_installedSharedMenu != 0)
        {
            var removeResult = UninstallSharedMenu();
            if (removeResult < 0) return removeResult;
        }

        var previousMenu = GetMenu(_menuFrameWindow);
        if (!SetMenu(_menuFrameWindow, menu)) return Marshal.GetHRForLastWin32Error();
        DrawMenuBar(_menuFrameWindow);
        var frame = Marshal.GetComInterfaceForObject(this, typeof(IStreamProbeOleInPlaceFrame));
        int result;
        try { result = OleSetMenuDescriptor(oleMenu, _menuFrameWindow, active, frame, _activeObject); }
        finally { Marshal.Release(frame); }
        if (result < 0)
        {
            SetMenu(_menuFrameWindow, previousMenu);
            DrawMenuBar(_menuFrameWindow);
            return result;
        }
        _installedSharedMenu = menu;
        _installedActiveWindow = active;
        _previousFrameMenu = previousMenu;
        return result;
    }

    private int UninstallSharedMenu()
    {
        if (_menuFrameCoordinator is not null) return _menuFrameCoordinator.Deactivate(this);
        if (_menuFrameWindow == 0 || _installedSharedMenu == 0) return 0;
        var frame = Marshal.GetComInterfaceForObject(this, typeof(IStreamProbeOleInPlaceFrame));
        int result;
        try { result = OleSetMenuDescriptor(0, _menuFrameWindow, _installedActiveWindow, frame, _activeObject); }
        finally { Marshal.Release(frame); }
        if (result < 0) return result;
        if (GetMenu(_menuFrameWindow) == _installedSharedMenu)
        {
            if (!SetMenu(_menuFrameWindow, _previousFrameMenu)) return Marshal.GetHRForLastWin32Error();
            DrawMenuBar(_menuFrameWindow);
        }
        _installedSharedMenu = 0;
        _installedActiveWindow = 0;
        _previousFrameMenu = 0;
        return result;
    }

    private int InsertNativeMenuGroups(nint menu, nint widths)
    {
        if (_parent == 0 || !IsWindow(_parent)) return 0;
        if (menu == 0 || widths == 0 || !IsMenu(menu) || _insertedMenuRoot != 0)
            return unchecked((int)0x80070057);

        string[] labels = ["File", "Edit", "View", "Favorites", "Tools", "Help"];
        uint[] ids = [0x8000, 0x8040, 0x8080, 0x8170, 0x80C0, 0x8100];
        var inserted = 0;
        for (var i = 0; i < labels.Length; i++)
        {
            var popup = CreatePopupMenu();
            var text = Marshal.StringToHGlobalUni(labels[i]);
            bool success;
            try
            {
                var item = new MenuItemInfo
                {
                    Size = (uint)Marshal.SizeOf<MenuItemInfo>(),
                    Mask = 0x47, // MIIM_STATE|MIIM_ID|MIIM_SUBMENU|MIIM_STRING.
                    Id = ids[i],
                    SubMenu = popup,
                    Text = text
                };
                success = popup != 0 && InsertMenuItemW(menu, (uint)i, true, ref item);
            }
            finally { Marshal.FreeHGlobal(text); }
            if (!success)
            {
                var error = Marshal.GetLastWin32Error();
                if (popup != 0) DestroyMenu(popup);
                RollbackInsertedMenuGroups(menu, _insertedMenuPopups, inserted);
                return error == 0 ? unchecked((int)0x80004005) : unchecked((int)(0x80070000 | (error & 0xFFFF)));
            }
            _insertedMenuPopups[i] = popup;
            inserted++;
        }

        _insertedMenuRoot = menu;
        for (var i = 0; i < 6; i++) Marshal.WriteInt32(widths, i * sizeof(int), 0);
        Marshal.WriteInt32(widths, 0, 2);
        Marshal.WriteInt32(widths, 2 * sizeof(int), 3);
        Marshal.WriteInt32(widths, 4 * sizeof(int), 1);
        return 0;
    }

    // The Shell builds its six standard groups after InsertMenusSB. Add the app-owned
    // group at SetMenuSB time so it participates in the exact composite HMENU the frame displays.
    private int EnsureCustomMenuGroup(nint menu)
    {
        if (menu == 0 || !IsMenu(menu)) return unchecked((int)0x80070057);
        if (_customMenuRoot == menu && _customMenuPopup != 0) return 0;
        if (_customMenuRoot != 0)
        {
            var removeResult = RemoveCustomMenuGroup(_customMenuRoot);
            if (removeResult < 0) return removeResult;
        }

        var popup = CreatePopupMenu();
        var label = Marshal.StringToHGlobalUni("Explorer Dirs");
        var marker = Marshal.StringToHGlobalUni("Probe active-pane command");
        try
        {
            var item = new MenuItemInfo
            {
                Size = (uint)Marshal.SizeOf<MenuItemInfo>(),
                Mask = 0x47, // MIIM_STATE|MIIM_ID|MIIM_SUBMENU|MIIM_STRING.
                State = 0,
                Id = ProbeApplicationCommandId,
                SubMenu = popup,
                Text = marker
            };
            if (popup == 0) return unchecked((int)0x8007000E);
            if (!InsertMenuItemW(popup, 0, true, ref item))
            {
                var error = Marshal.GetHRForLastWin32Error();
                DestroyMenu(popup);
                return error == 0 ? unchecked((int)0x80004005) : error;
            }
            var rootItem = item;
            rootItem.State = 0;
            rootItem.Id = 0x7FEF;
            rootItem.SubMenu = popup;
            rootItem.Text = label;
            if (!InsertMenuItemW(menu, (uint)GetMenuItemCount(menu), true, ref rootItem))
            {
                var error = Marshal.GetHRForLastWin32Error();
                DestroyMenu(popup);
                return error == 0 ? unchecked((int)0x80004005) : error;
            }
            _customMenuRoot = menu;
            _customMenuPopup = popup;
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(marker);
            Marshal.FreeHGlobal(label);
        }
    }

    private int RemoveCustomMenuGroup(nint menu)
    {
        if (_customMenuRoot == 0 || menu != _customMenuRoot) return 0;
        var root = _customMenuRoot;
        var popupHandle = _customMenuPopup;
        var found = -1;
        if (IsMenu(menu))
        {
            for (var position = GetMenuItemCount(menu) - 1; position >= 0; position--)
            {
                if (GetSubMenu(menu, position) != _customMenuPopup) continue;
                found = position;
                if (!RemoveMenu(menu, (uint)position, 0x400))
                {
                    var error = Marshal.GetHRForLastWin32Error();
                    return error == 0 ? unchecked((int)0x80004005) : error;
                }
                break;
            }
        }
        if (_customMenuPopup != 0) DestroyMenu(_customMenuPopup);
        _customMenuRoot = _customMenuPopup = 0;
        MenuTrace.Add($"custom-remove root=0x{root:X} popup=0x{popupHandle:X} found={found} still-present={HasSubMenu(root, popupHandle)}");
        return 0;
    }

    internal int RetireCustomMenuGroup() => RemoveCustomMenuGroup(_customMenuRoot);
    internal void OnApplicationMenuCommand() => ApplicationMenuCommandCount++;

    private static bool HasSubMenu(nint root, nint popup)
    {
        if (!IsMenu(root)) return false;
        for (var position = 0; position < GetMenuItemCount(root); position++)
            if (GetSubMenu(root, position) == popup) return true;
        return false;
    }

    internal static void RollbackInsertedMenuGroups(nint menu, nint[] popups, int inserted)
    {
        // Removing by position shifts later entries left, so unwind in reverse insertion order.
        for (var i = inserted - 1; i >= 0; i--)
        {
            RemoveMenu(menu, (uint)i, 0x400);
            DestroyMenu(popups[i]);
            popups[i] = 0;
        }
    }

    private int RemoveOwnedMenuGroups(nint menu)
    {
        if (_insertedMenuRoot == 0 || menu != _insertedMenuRoot) return 0;
        if (menu != 0 && IsMenu(menu))
        {
            for (var i = 0; i < _insertedMenuPopups.Length; i++)
            {
                var popup = _insertedMenuPopups[i];
                if (popup == 0) continue;
                var count = GetMenuItemCount(menu);
                for (var position = count - 1; position >= 0; position--)
                {
                    if (GetSubMenu(menu, position) != popup) continue;
                    RemoveMenu(menu, (uint)position, 0x400);
                    break;
                }
                DestroyMenu(popup);
                _insertedMenuPopups[i] = 0;
            }
        }
        _insertedMenuRoot = 0;
        return 0;
    }

    [DllImport("shlwapi.dll")] private static extern nint SHCreateMemStream(byte[] bytes, uint count);
    [DllImport("ole32.dll")] private static extern int OleSetMenuDescriptor(nint descriptor, nint frameWindow,
        nint activeWindow, nint frame, nint activeObject);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsMenu(nint menu);
    [DllImport("user32.dll")] private static extern nint GetMenu(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetMenu(nint window, nint menu);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint window, out StreamProbeRect rect);
    [DllImport("user32.dll")] private static extern bool DrawMenuBar(nint window);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public nint SubMenu, CheckedBitmap, UncheckedBitmap, ItemData, Text;
        public uint TextLength;
        public nint ItemBitmap;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool InsertMenuItemW(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition,
        ref MenuItemInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveMenu(nint menu, uint position, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern nint GetSubMenu(nint menu, int position);
}

// Multiple DefViews share one real frame menu. A stale RemoveMenusSB from an
// inactive pane must not unregister the menu dispatcher owned by the active pane.
internal sealed class ShellMenuFrameCoordinator : IDisposable
{
    private const uint WmCommand = 0x0111;
    private const uint WmRunShellMenuCommand = 0x8000 + 0x3A1;
    private static readonly SubclassProcedure FrameSubclassProcedure = FrameWindowSubclass;
    private readonly nint _frameWindow;
    private readonly GCHandle _selfHandle;
    private readonly nuint _subclassId;
    private bool _disposed;
    private nint _baseMenu;
    private nint _activeMenu;
    private nint _activeWindow;
    private nint _oleMenu;
    private StreamViewHost? _owner;
    private uint _pendingShellMenuCommandId;
    private StreamViewHost? _pendingShellMenuCommandOwner;

    internal ShellMenuFrameCoordinator(nint frameWindow)
    {
        if (frameWindow == 0 || !IsWindow(frameWindow))
            throw new ArgumentException("A valid top-level menu frame window is required.", nameof(frameWindow));
        _frameWindow = frameWindow;
        _baseMenu = GetMenu(frameWindow);
        _selfHandle = GCHandle.Alloc(this);
        _subclassId = unchecked((nuint)GCHandle.ToIntPtr(_selfHandle));
        if (!SetWindowSubclass(frameWindow, FrameSubclassProcedure, _subclassId, GCHandle.ToIntPtr(_selfHandle)))
        {
            _selfHandle.Free();
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not install the isolated Shell menu command router.");
        }
    }

    internal nint BaseMenu => _baseMenu;
    internal bool IsCurrentOwner(StreamViewHost owner) => !_disposed && ReferenceEquals(_owner, owner);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_owner is not null) _ = Deactivate(_owner);
        }
        finally
        {
            if (IsWindow(_frameWindow)) _ = RemoveWindowSubclass(_frameWindow, FrameSubclassProcedure, _subclassId);
            if (_selfHandle.IsAllocated) _selfHandle.Free();
        }
    }

    private static nint FrameWindowSubclass(nint window, uint message, nuint wParam, nint lParam,
        nuint subclassId, nint referenceData)
    {
        try
        {
            if (message == WmRunShellMenuCommand && referenceData != 0)
            {
                var coordinator = (ShellMenuFrameCoordinator?)GCHandle.FromIntPtr(referenceData).Target;
                var owner = coordinator?._pendingShellMenuCommandOwner;
                var commandId = coordinator?._pendingShellMenuCommandId ?? 0;
                if (coordinator is not null)
                {
                    coordinator._pendingShellMenuCommandId = 0;
                    coordinator._pendingShellMenuCommandOwner = null;
                }
                if (owner is not null && commandId != 0 &&
                    !ShellViewColorSpike.TryInvokeMergedViewMenuCommand(owner, commandId))
                    _ = ShellViewColorSpike.TryInvokeMergedEditMenuCommand(owner, commandId,
                        isStillActive: () => coordinator?.IsCurrentOwner(owner) == true);
                return 0;
            }
            if (message == WmCommand && referenceData != 0)
            {
                var coordinator = (ShellMenuFrameCoordinator?)GCHandle.FromIntPtr(referenceData).Target;
                var commandId = (uint)(wParam & 0xFFFF);
                if (lParam == 0 && commandId == StreamViewHost.ProbeApplicationCommandId)
                {
                    coordinator?._owner?.OnApplicationMenuCommand();
                    return 0;
                }
                if (lParam == 0 && commandId <= 0x7FFF && coordinator?._owner is { } owner &&
                    (ShellViewColorSpike.TryInvokeMergedViewMenuCommand(owner, commandId, execute: false) ||
                     ShellViewColorSpike.TryInvokeMergedEditMenuCommand(owner, commandId, execute: false)))
                {
                    coordinator._pendingShellMenuCommandId = commandId;
                    coordinator._pendingShellMenuCommandOwner = owner;
                    if (PostMessageW(window, WmRunShellMenuCommand, 0, 0)) return 0;
                    coordinator._pendingShellMenuCommandId = 0;
                    coordinator._pendingShellMenuCommandOwner = null;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Shell app menu command dispatch: {ex.GetType().Name}");
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    internal int SetMenu(StreamViewHost owner, nint menu, nint oleMenu, nint activeWindow)
    {
        if (menu == 0) return Deactivate(owner);
        if (!IsWindow(_frameWindow) || !IsMenu(menu)) return unchecked((int)0x80070057);
        var priorOwner = _owner;
        var priorMenu = _activeMenu;
        var priorWindow = _activeWindow;
        var priorOleMenu = _oleMenu;
        if (priorOwner is not null)
        {
            var unhookResult = Unhook(priorOwner, priorWindow);
            if (unhookResult < 0) return unhookResult;
            var removeCustomMenuResult = priorOwner.RetireCustomMenuGroup();
            if (removeCustomMenuResult < 0) return removeCustomMenuResult;
        }

        if (!SetMenu(_frameWindow, menu))
        {
            var error = Marshal.GetHRForLastWin32Error();
            RestorePrior(priorOwner, priorMenu, priorWindow, priorOleMenu);
            return error;
        }
        DrawMenuBar(_frameWindow);
        var frame = Marshal.GetComInterfaceForObject(owner, typeof(IStreamProbeOleInPlaceFrame));
        int result;
        try { result = OleSetMenuDescriptor(oleMenu, _frameWindow, activeWindow, frame, owner.ActiveObjectPointer); }
        finally { Marshal.Release(frame); }
        if (result < 0)
        {
            RestorePrior(priorOwner, priorMenu, priorWindow, priorOleMenu);
            return result;
        }

        _owner = owner;
        _activeMenu = menu;
        _activeWindow = activeWindow;
        _oleMenu = oleMenu;
        return result;
    }

    internal int RemoveMenu(StreamViewHost owner, nint menu)
    {
        if (!ReferenceEquals(_owner, owner) || menu != _activeMenu) return 0;
        return Deactivate(owner);
    }

    internal int Deactivate(StreamViewHost owner)
    {
        if (!ReferenceEquals(_owner, owner)) return 0;
        var result = Unhook(owner, _activeWindow);
        if (result < 0) return result;
        result = owner.RetireCustomMenuGroup();
        if (result < 0) return result;
        if (GetMenu(_frameWindow) == _activeMenu)
        {
            if (!SetMenu(_frameWindow, _baseMenu)) return Marshal.GetHRForLastWin32Error();
            DrawMenuBar(_frameWindow);
        }
        _owner = null;
        _activeMenu = _activeWindow = _oleMenu = 0;
        return 0;
    }

    private int Unhook(StreamViewHost owner, nint activeWindow)
    {
        var frame = Marshal.GetComInterfaceForObject(owner, typeof(IStreamProbeOleInPlaceFrame));
        try { return OleSetMenuDescriptor(0, _frameWindow, activeWindow, frame, owner.ActiveObjectPointer); }
        finally { Marshal.Release(frame); }
    }

    private void RestorePrior(StreamViewHost? owner, nint menu, nint activeWindow, nint oleMenu)
    {
        var restoreMenu = owner is null ? _baseMenu : menu;
        if (GetMenu(_frameWindow) != restoreMenu)
        {
            SetMenu(_frameWindow, restoreMenu);
            DrawMenuBar(_frameWindow);
        }
        if (owner is null)
        {
            _owner = null;
            _activeMenu = _activeWindow = _oleMenu = 0;
            return;
        }

        var frame = Marshal.GetComInterfaceForObject(owner, typeof(IStreamProbeOleInPlaceFrame));
        int result;
        try { result = OleSetMenuDescriptor(oleMenu, _frameWindow, activeWindow, frame, owner.ActiveObjectPointer); }
        finally { Marshal.Release(frame); }
        if (result >= 0)
        {
            _owner = owner;
            _activeMenu = menu;
            _activeWindow = activeWindow;
            _oleMenu = oleMenu;
        }
        else
        {
            _owner = null;
            _activeMenu = _activeWindow = _oleMenu = 0;
        }
    }

    [DllImport("ole32.dll")] private static extern int OleSetMenuDescriptor(nint descriptor,
        nint frameWindow, nint activeWindow, nint frame, nint activeObject);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint window,
        SubclassProcedure callback, nuint subclassId, nint referenceData);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool RemoveWindowSubclass(nint window,
        SubclassProcedure callback, nuint subclassId);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message,
        nuint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProcedure(nint window,
        uint message, nuint wParam, nint lParam, nuint subclassId, nint referenceData);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsMenu(nint menu);
    [DllImport("user32.dll")] private static extern nint GetMenu(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetMenu(nint window, nint menu);
    [DllImport("user32.dll")] private static extern bool DrawMenuBar(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
}

// Exact IShellBrowser layout, including its IOleWindow methods (slots 3/4).
[ComVisible(true), Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStreamProbeBrowser
{
    [PreserveSig] int GetWindow(out nint window);
    [PreserveSig] int ContextSensitiveHelp(int enter);
    [PreserveSig] int InsertMenusSB(nint menu, nint widths);
    [PreserveSig] int SetMenuSB(nint menu, nint oleMenu, nint active);
    [PreserveSig] int RemoveMenusSB(nint menu);
    [PreserveSig] int SetStatusTextSB(nint text);
    [PreserveSig] int EnableModelessSB(int enable);
    [PreserveSig] int TranslateAcceleratorSB(nint message, ushort id);
    [PreserveSig] int BrowseObject(nint pidl, uint flags);
    [PreserveSig] int GetViewStateStream(uint mode, out nint stream);
    [PreserveSig] int GetControlWindow(uint id, out nint window);
    [PreserveSig] int SendControlMsg(uint id, uint message, nuint wParam, nint lParam, out nint result);
    [PreserveSig] int QueryActiveShellView(out nint view);
    [PreserveSig] int OnViewWindowActive(nint view);
    [PreserveSig] int SetToolbarItems(nint buttons, uint count, uint flags);
}

[ComVisible(true), Guid("00000116-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStreamProbeOleInPlaceFrame
{
    [PreserveSig] int GetWindow(out nint window);
    [PreserveSig] int ContextSensitiveHelp(int enter);
    [PreserveSig] int GetBorder(nint border);
    [PreserveSig] int RequestBorderSpace(nint border);
    [PreserveSig] int SetBorderSpace(nint border);
    [PreserveSig] int SetActiveObject(nint activeObject, nint name);
    [PreserveSig] int InsertMenus(nint sharedMenu, nint groupWidths);
    [PreserveSig] int SetMenu(nint sharedMenu, nint oleMenu, nint activeWindow);
    [PreserveSig] int RemoveMenus(nint sharedMenu);
    [PreserveSig] int SetStatusText(nint text);
    [PreserveSig] int EnableModeless(int enable);
    [PreserveSig] int TranslateAccelerator(nint message, ushort id);
}
