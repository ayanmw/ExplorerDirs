using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ExplorerDirs.App;

// Hosts the Windows Shell folder view. Its hit testing, view modes, sorting,
// grouping, file operations and context menu all belong to the Shell view.
internal sealed class ExplorerBrowserHost : HwndHost, ExplorerBrowserHost.IExplorerBrowserEvents
{
    private static readonly Guid BrowserClassId = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid ShellItemArrayId = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
    private IExplorerBrowser? _browser;
    private IntPtr _hostWindow;
    private IntPtr _viewWindow;
    private readonly List<IntPtr> _subclassedWindows = new();
    private readonly SubclassProc _subclassProc;
    private bool _menuPrepared;
    private readonly Dictionary<ushort, Action> _menuActions = new();
    private uint _cookie;
    private bool _advised;

    public string CurrentPath { get; private set; }
    public event EventHandler<string>? Navigated;
    public event EventHandler<string>? NavigationFailed;
    public event EventHandler<string>? MenuError;
    public event EventHandler? Activated;
    public Func<string?, IReadOnlyList<MenuAction>>? CustomCommands { get; set; }
    public Func<IReadOnlyList<MenuAction>>? LayoutCommands { get; set; }

    public sealed record MenuAction(string Label, Action Action);

    public ExplorerBrowserHost(string initialPath)
    {
        CurrentPath = initialPath;
        _subclassProc = ViewWindowProc;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostWindow = CreateWindowEx(0, "STATIC", "", 0x50000000,
            0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight),
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hostWindow == IntPtr.Zero)
            throw new InvalidOperationException("无法创建 Shell 视图宿主窗口。");

        try
        {
            var browserType = Type.GetTypeFromCLSID(BrowserClassId, true)!;
            _browser = (IExplorerBrowser)Activator.CreateInstance(browserType)!;
            var rect = ClientRect();
            var settings = new FolderSettings { ViewMode = 4, Flags = 0 }; // FVM_DETAILS
            Check(_browser.Initialize(_hostWindow, ref rect, ref settings));
            Check(_browser.SetOptions(0x40)); // EBO_NOBORDER, no extra Explorer frames
            Check(_browser.SetPropertyBag("ExplorerDirs.FolderView"));
            Check(_browser.Advise(this, out _cookie));
            _advised = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_browser is null) return;
                try { Navigate(CurrentPath); }
                catch (Exception ex) { NavigationFailed?.Invoke(this, ex.Message); }
            }));
        }
        catch
        {
            DisposeBrowser();
            DestroyWindow(_hostWindow);
            _hostWindow = IntPtr.Zero;
            throw;
        }

        return new HandleRef(this, _hostWindow);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DisposeBrowser();
        DestroyWindow(hwnd.Handle);
        _hostWindow = IntPtr.Zero;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_browser is not null)
        {
            var rect = ClientRect();
            _browser.SetRect(IntPtr.Zero, ref rect);
        }
    }

    public void Navigate(string path)
    {
        path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        if (_browser is null)
        {
            CurrentPath = path;
            return;
        }

        Check(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _));
        try { Check(_browser.BrowseToIDList(pidl, 0)); }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    public void Refresh()
    {
        if (_browser is null) return;
        var iid = ShellViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) >= 0)
        {
            try { ((IShellView)viewObject).Refresh(); }
            finally { Marshal.ReleaseComObject(viewObject); }
        }
    }

    private Rect ClientRect()
    {
        var source = PresentationSource.FromVisual(this);
        var point = new System.Windows.Point(ActualWidth, ActualHeight);
        if (source?.CompositionTarget is { } target)
            point = target.TransformToDevice.Transform(point);
        return new Rect
        {
            Right = Math.Max(1, (int)Math.Ceiling(point.X)),
            Bottom = Math.Max(1, (int)Math.Ceiling(point.Y))
        };
    }

    private void DisposeBrowser()
    {
        if (_browser is null) return;
        DetachViewWindow();
        if (_advised) { _browser.Unadvise(_cookie); _advised = false; }
        _browser.Destroy();
        Marshal.ReleaseComObject(_browser);
        _browser = null;
    }

    int IExplorerBrowserEvents.OnNavigationPending(IntPtr pidl) => 0;
    int IExplorerBrowserEvents.OnViewCreated(IntPtr view)
    {
        try
        {
            var shellView = (IShellView)Marshal.GetObjectForIUnknown(view);
            try
            {
                if (shellView.GetWindow(out var hwnd) >= 0 && hwnd != IntPtr.Zero)
                    AttachViewWindow(hwnd);
            }
            finally { Marshal.ReleaseComObject(shellView); }
        }
        catch (Exception ex) { MenuError?.Invoke(this, ex.Message); }
        return 0;
    }
    int IExplorerBrowserEvents.OnNavigationComplete(IntPtr pidl)
    {
        var hr = SHGetNameFromIDList(pidl, 0x80058000, out var pathPtr);
        if (hr >= 0)
        {
            try
            {
                var path = Marshal.PtrToStringUni(pathPtr);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    CurrentPath = path;
                    try { AttachCurrentViewWindow(); }
                    catch (Exception ex) { MenuError?.Invoke(this, ex.Message); }
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (CurrentPath == path) Navigated?.Invoke(this, path);
                    }));
                }
            }
            finally { Marshal.FreeCoTaskMem(pathPtr); }
        }
        return 0;
    }

    private void AttachCurrentViewWindow()
    {
        if (_browser is null) return;
        var iid = ShellViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return;
        try
        {
            if (((IShellView)viewObject).GetWindow(out var hwnd) >= 0 && hwnd != IntPtr.Zero)
                AttachViewWindow(hwnd);
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private void AttachViewWindow(IntPtr hwnd)
    {
        if (hwnd == _viewWindow) return;
        DetachViewWindow();
        _viewWindow = hwnd;
        var current = hwnd;
        for (var depth = 0; current != IntPtr.Zero && depth < 12; current = GetParent(current), depth++)
        {
            SubclassWindow(current);
            if (current == _hostWindow) break;
        }
        EnumChildWindows(hwnd, (child, _) =>
        {
            SubclassWindow(child);
            return true;
        }, IntPtr.Zero);
        if (_subclassedWindows.Count == 0)
            MenuError?.Invoke(this, "Shell 菜单消息钩子未能安装。");
    }

    private void SubclassWindow(IntPtr hwnd)
    {
        if (_subclassedWindows.Contains(hwnd)) return;
        if (SetWindowSubclass(hwnd, _subclassProc, 1, IntPtr.Zero))
            _subclassedWindows.Add(hwnd);
    }

    private void DetachViewWindow()
    {
        foreach (var hwnd in _subclassedWindows)
            RemoveWindowSubclass(hwnd, _subclassProc, 1);
        _subclassedWindows.Clear();
        _viewWindow = IntPtr.Zero;
        _menuActions.Clear();
    }

    private IntPtr ViewWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, IntPtr data)
    {
        try
        {
            switch (message)
            {
                case 0x0007: // WM_SETFOCUS
                case 0x0201: // WM_LBUTTONDOWN
                case 0x0204: // WM_RBUTTONDOWN
                case 0x0207: // WM_MBUTTONDOWN
                    Activated?.Invoke(this, EventArgs.Empty);
                    break;
                case 0x0211: // WM_ENTERMENULOOP
                    _menuPrepared = false;
                    _menuActions.Clear();
                    break;
                case 0x0117: // WM_INITMENUPOPUP
                    if (!_menuPrepared)
                    {
                        _menuPrepared = true;
                        InjectCommands(wParam);
                    }
                    break;
                case 0x0111: // WM_COMMAND
                    var commandId = unchecked((ushort)(wParam.ToInt64() & 0xffff));
                    if (_menuActions.TryGetValue(commandId, out var action))
                    {
                        _menuActions.Clear();
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                        {
                            try { action(); }
                            catch (Exception ex) { MenuError?.Invoke(this, ex.Message); }
                        }));
                        return IntPtr.Zero;
                    }
                    break;
                case 0x0212: // WM_EXITMENULOOP
                    _menuPrepared = false;
                    break;
                case 0x0082: // WM_NCDESTROY
                    if (hwnd == _viewWindow) _viewWindow = IntPtr.Zero;
                    break;
            }
        }
        catch (Exception ex) { MenuError?.Invoke(this, ex.Message); }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void InjectCommands(IntPtr menu)
    {
        if (menu == IntPtr.Zero || CustomCommands is null) return;
        var selectedPath = GetSelectedPath();
        var commands = CustomCommands(selectedPath);
        var layouts = LayoutCommands?.Invoke() ?? [];
        if (commands.Count == 0 && layouts.Count == 0) return;

        var id = (ushort)0xE000;
        var entries = new List<(ushort Id, string Label)>();
        foreach (var command in commands)
        {
            _menuActions[id] = command.Action;
            entries.Add((id++, command.Label));
        }
        if (layouts.Count > 0)
        {
            var submenu = CreatePopupMenu();
            if (submenu != IntPtr.Zero)
            {
                foreach (var command in layouts)
                {
                    _menuActions[id] = command.Action;
                    AppendMenu(submenu, 0, id++, command.Label);
                }
                for (var index = entries.Count - 1; index >= 0; index--)
                    InsertMenu(menu, 0, 0x400, entries[index].Id, entries[index].Label);
                InsertMenu(menu, (uint)entries.Count, 0x400 | 0x10, (nuint)submenu, "布局");
                InsertMenu(menu, (uint)(entries.Count + 1), 0x400 | 0x800, 0, null);
                return;
            }
        }
        for (var index = entries.Count - 1; index >= 0; index--)
            InsertMenu(menu, 0, 0x400, entries[index].Id, entries[index].Label);
        InsertMenu(menu, (uint)entries.Count, 0x400 | 0x800, 0, null);
    }

    private string? GetSelectedPath()
    {
        if (_browser is null) return null;
        var folderViewId = FolderViewId;
        if (_browser.GetCurrentView(ref folderViewId, out var viewObject) < 0) return null;
        try
        {
            var folderView = (IFolderView)viewObject;
            var arrayId = ShellItemArrayId;
            if (folderView.Items(1, ref arrayId, out var arrayObject) < 0) return null;
            try
            {
                var array = (IShellItemArray)arrayObject;
                if (array.GetCount(out var count) < 0 || count != 1 || array.GetItemAt(0, out var item) < 0)
                    return null;
                try
                {
                    if (item.GetDisplayName(0x80058000, out var pathPtr) < 0) return null;
                    try { return Marshal.PtrToStringUni(pathPtr); }
                    finally { Marshal.FreeCoTaskMem(pathPtr); }
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            finally { Marshal.ReleaseComObject(arrayObject); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }
    int IExplorerBrowserEvents.OnNavigationFailed(IntPtr pidl)
    {
        NavigationFailed?.Invoke(this, "无法打开目录。");
        return 0;
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FolderSettings { public uint ViewMode, Flags; }

    [ComImport, Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerBrowser
    {
        [PreserveSig] int Initialize(IntPtr hwndParent, ref Rect rect, ref FolderSettings settings);
        [PreserveSig] int Destroy();
        [PreserveSig] int SetRect(IntPtr hdwp, ref Rect rect);
        [PreserveSig] int SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int SetFolderSettings(ref FolderSettings settings);
        [PreserveSig] int Advise([MarshalAs(UnmanagedType.Interface)] IExplorerBrowserEvents events, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOptions(uint flags);
        [PreserveSig] int GetOptions(out uint flags);
        [PreserveSig] int BrowseToIDList(IntPtr pidl, uint flags);
        [PreserveSig] int BrowseToObject(IntPtr value, uint flags);
        [PreserveSig] int FillFromObject(IntPtr value, uint flags);
        [PreserveSig] int RemoveAll();
        [PreserveSig] int GetCurrentView(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object view);
    }

    [ComVisible(true), Guid("361BBDC7-E6EE-4E13-BE58-58E2240C810F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerBrowserEvents
    {
        [PreserveSig] int OnNavigationPending(IntPtr pidl);
        [PreserveSig] int OnViewCreated(IntPtr shellView);
        [PreserveSig] int OnNavigationComplete(IntPtr pidl);
        [PreserveSig] int OnNavigationFailed(IntPtr pidl);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
        [PreserveSig] int ContextSensitiveHelp(bool enter);
        [PreserveSig] int TranslateAccelerator(IntPtr message);
        [PreserveSig] int EnableModeless(bool enable);
        [PreserveSig] int UIActivate(uint state);
        [PreserveSig] int Refresh();
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint mode);
        [PreserveSig] int SetCurrentViewMode(uint mode);
        [PreserveSig] int GetFolder(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object folder);
        [PreserveSig] int Item(int index, out IntPtr pidl);
        [PreserveSig] int ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object array);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr value);
        [PreserveSig] int GetPropertyStore(uint flags, ref Guid iid, out IntPtr store);
        [PreserveSig] int GetPropertyDescriptionList(IntPtr key, ref Guid iid, out IntPtr list);
        [PreserveSig] int GetAttributes(uint flags, uint mask, out uint attributes);
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemAt(uint index, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr value);
        [PreserveSig] int GetParent(out IntPtr parent);
        [PreserveSig] int GetDisplayName(uint kind, out IntPtr name);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, IntPtr data);
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, IntPtr data);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InsertMenu(IntPtr menu, uint position, uint flags, nuint id, string? text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributes, out uint receivedAttributes);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetNameFromIDList(IntPtr pidl, uint displayNameType, out IntPtr name);
}
