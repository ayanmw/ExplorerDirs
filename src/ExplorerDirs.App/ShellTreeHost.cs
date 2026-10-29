using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ExplorerDirs.App;

// Independent HWND lifetime: toggling this host never recreates the file browser.
internal sealed class ShellTreeHost : HwndHost
{
    private NativeShellTree? _tree;
    private nint _parent;
    private string? _root;
    private string? _directory;
    private bool _addressTreeMode;
    private IDisposable? _synchronization;
    private readonly DispatcherTimer _retry = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(100) };
    private int _attempts;
    private int _settledTicks;
    private long _revision;
    private long _generation;
    private bool _insideSynchronization;
    private readonly SubclassProc _subclass;
    public event Action<string>? DirectorySelected;
    public event Action<string>? FolderMiddleClicked;
    public event Action? Activated;
    public event Action? EscapeRequested;
    public event Action<string>? Error;
    public Func<ExplorerShortcut, bool>? ShortcutHandler { get; set; }

    public ShellTreeHost()
    {
        _subclass = TreeWindowProc;
        Focusable = true;
        _retry.Tick += (_, _) => Synchronize();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) StartSynchronization();
            else CancelSynchronization();
        };
    }

    internal bool ContainsNativeWindow(nint hwnd) => hwnd != 0 && _tree is { WindowHandle: not 0 } tree &&
        (hwnd == tree.WindowHandle || IsChild(tree.WindowHandle, hwnd));

    internal void RefreshTheme()
    {
        if (_parent != 0) NativeShellTheme.Apply(_parent);
    }

    public void SetDirectory(string directory)
    {
        _addressTreeMode = false;
        _directory = directory;
        StartSynchronization();
    }

    internal void SetSelectedFolderLocation(string identity)
    {
        _addressTreeMode = !Path.IsPathFullyQualified(identity);
        _directory = identity;
        if (_addressTreeMode && _tree is not null)
        {
            _root = "shell:Desktop";
            _tree.SetRoot(_root);
        }
        StartSynchronization();
    }

    internal void SetVirtualLocation()
    {
        _addressTreeMode = false;
        _directory = null;
        _root = null;
        CancelSynchronization();
        _tree?.SetRoot("shell:Desktop");
    }

    internal void SetAddressTreeLocation(string? identity)
    {
        _addressTreeMode = true;
        _directory = identity;
        CancelSynchronization();
        if (_tree is null) return;
        _root = "shell:Desktop";
        _tree.SetRoot(_root);
        if (IsVisible && identity is not null) StartSynchronization();
    }

    private void StartSynchronization()
    {
        CancelSynchronization();
        if (_tree is null || !IsVisible || _directory is null) return;
        _attempts = _settledTicks = 0;
        _synchronization = _tree.BeginSynchronization();
        _retry.Start();
    }

    internal void CancelSynchronization()
    {
        _retry.Stop();
        _generation++;
        _synchronization?.Dispose();
        _synchronization = null;
    }

    private void Synchronize()
    {
        if (_tree is null || !IsVisible || _directory is null) { CancelSynchronization(); return; }
        if (_insideSynchronization) return; // A COM message pump can reenter Dispatcher ticks.
        _insideSynchronization = true;
        var generation = _generation;
        var tree = _tree;
        var directory = _directory;
        bool Current() => generation == _generation && ReferenceEquals(tree, _tree) && IsVisible;
        try
        {
            if (++_attempts > 40)
            {
                CancelSynchronization();
                Error?.Invoke("目录树定位超时；文件视图仍可继续使用。");
                return;
            }
            var root = _addressTreeMode ? "shell:Desktop" : Path.GetPathRoot(directory);
            if (string.IsNullOrEmpty(root)) { CancelSynchronization(); return; }
            if (!string.Equals(root, _root, StringComparison.OrdinalIgnoreCase))
            {
                _root = null;
                tree.SetRoot(root, Current);
                if (!Current()) return;
                _root = root;
            }
            var path = _addressTreeMode ? tree.SelectedFolderIdentity : tree.SelectedDirectory;
            if (!Current()) return;
            var selected = string.Equals(path, directory, StringComparison.OrdinalIgnoreCase)
                || (_addressTreeMode ? tree.SelectLocation(directory, Current) : tree.SelectDirectory(directory, Current));
            if (!Current()) return;
            // Allow queued native expansion callbacks to settle. Never pump or sleep
            // in a COM call, and keep programmatic notifications suppressed meanwhile.
            _settledTicks = selected && _revision == _tree.SelectionRevision ? _settledTicks + 1 : 0;
            _revision = _tree.SelectionRevision;
            if (_settledTicks >= 2) CancelSynchronization();
        }
        catch (Exception ex)
        {
            if (!Current()) return;
            CancelSynchronization();
            Error?.Invoke($"目录树暂不可用（{ex.GetType().Name}）；文件视图仍可继续使用。");
        }
        finally { _insideSynchronization = false; }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _parent = CreateWindowExW(0, "STATIC", "", 0x50000000, 0, 0, 1, 1,
            hwndParent.Handle, 0, 0, 0);
        if (_parent == 0) throw new InvalidOperationException("无法创建目录树宿主窗口。");
        try
        {
            NativeShellTheme.Apply(_parent);
            _tree = new NativeShellTree(_parent, 1, 1);
            RefreshTheme();
            if (_addressTreeMode)
            {
                _root = "shell:Desktop";
                _tree.SetRoot(_root);
            }
            if (!SetWindowSubclass(_tree.WindowHandle, _subclass, 1, 0))
                throw new InvalidOperationException("无法连接目录树选择通知。");
            _tree.UserInput += () => { CancelSynchronization(); Activated?.Invoke(); };
            _tree.FolderMiddleClicked += path =>
            {
                var generation = _generation;
                var tree = _tree;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (generation == _generation && ReferenceEquals(tree, _tree) && IsVisible)
                        FolderMiddleClicked?.Invoke(path);
                }));
            };
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(StartSynchronization));
        }
        catch (Exception ex)
        {
            if (_tree is not null) RemoveWindowSubclass(_tree.WindowHandle, _subclass, 1);
            _tree?.Dispose();
            _tree = null;
            // This optional pane must not take down the working file browsers.
            var message = $"目录树创建失败（{ex.GetType().Name}）；可关闭后重新启动应用再试。";
            Dispatcher.BeginInvoke(new Action(() => Error?.Invoke(message)));
        }
        return new HandleRef(this, _parent);
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (_tree is not null && GetClientRect(_parent, out var rect))
            MoveWindow(_tree.WindowHandle, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), true);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        CancelSynchronization();
        if (_tree is not null) RemoveWindowSubclass(_tree.WindowHandle, _subclass, 1);
        _tree?.Dispose();
        _tree = null;
        _root = null;
        DestroyWindow(hwnd.Handle);
        _parent = 0;
    }

    internal bool HasNativeFocus()
    {
        var focus = GetFocus();
        return _parent != 0 && (focus == _parent || IsChild(_parent, focus));
    }

    internal bool FocusTree()
    {
        CancelSynchronization();
        if (_tree is null || !IsVisible) return false;
        // The native tree HWND contains SysTreeView32; target that item control.
        var content = FindWindowExW(_tree.WindowHandle, 0, "SysTreeView32", null);
        if (content == 0) content = _tree.WindowHandle;
        SetFocus(content);
        return HasNativeFocus();
    }
    protected override bool TabIntoCore(TraversalRequest request) => FocusTree();

    private nint TreeWindowProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        var userSelection = false;
        var generation = _generation;
        var tree = _tree;
        nint target = 0, control = 0;
        try
        {
            if (message == 0x4e && lParam != 0) // WM_NOTIFY
            {
                var header = Marshal.PtrToStructure<NotifyHeader>(lParam);
                if (header.Code is -451 or -402 && IsChild(hwnd, header.From)) // TVN_SELCHANGEDW/A
                {
                    var notice = Marshal.PtrToStructure<TreeSelectionNotice>(lParam);
                    userSelection = notice.Action is 1 or 2; // TVC_BYMOUSE / TVC_BYKEYBOARD
                    target = notice.NewItem.Item;
                    control = header.From;
                }
            }
            if (userSelection)
            {
                CancelSynchronization();
                generation = _generation;
                Activated?.Invoke();
            }
        }
        catch { userSelection = false; } // Never unwind managed code through a window proc.
        var result = DefSubclassProc(hwnd, message, wParam, lParam);
        try
        {
            bool Current() => generation == _generation && ReferenceEquals(tree, _tree) &&
                IsVisible && target != 0 && SendMessageW(control, 0x110a, 9, 0) == target; // TVGN_CARET
            if (userSelection && tree is not null && Current())
            {
                // COM selection callbacks are delayed. Read after the actual user
                // tree notification instead, and verify its item/context survived.
                var revision = tree.SelectionRevision;
                var path = _addressTreeMode ? tree.SelectedFolderIdentity : tree.SelectedDirectory;
                if (Current() && revision == tree.SelectionRevision && path is not null &&
                    !string.Equals(path, _directory, _addressTreeMode ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                    DirectorySelected?.Invoke(path);
            }
        }
        catch { } // Ignore an unavailable/stale selection; native processing already completed.
        return result;
    }
    protected override bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers)
    {
        if (HasNativeFocus() && msg.message is 0x100 or 0x104)
        {
            CancelSynchronization();
            if (unchecked((int)msg.wParam) == 0x1b)
            {
                EscapeRequested?.Invoke();
                return true;
            }
            var shortcut = KeyboardShortcuts.Resolve(unchecked((int)msg.wParam), (int)modifiers);
            if (shortcut is ExplorerShortcut.NextFocusStop or ExplorerShortcut.PreviousFocusStop
                or ExplorerShortcut.NextFocusRegion or ExplorerShortcut.PreviousFocusRegion
                or ExplorerShortcut.Address or ExplorerShortcut.AddressTree or ExplorerShortcut.AddressLocations
                or ExplorerShortcut.AddressComputer)
                return ShortcutHandler?.Invoke(shortcut) == true;
        }
        return base.TranslateAcceleratorCore(ref msg, modifiers);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyHeader { public nint From; public nuint Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)] private struct TreeItem
    {
        public uint Mask;
        public nint Item;
        public uint State, StateMask;
        public nint Text;
        public int TextCapacity, Image, SelectedImage, Children;
        public nint Parameter;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TreeSelectionNotice
    {
        public NotifyHeader Header;
        public uint Action;
        public TreeItem OldItem, NewItem;
        public int X, Y;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string cls, string text, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool MoveWindow(nint hwnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowExW(nint parent, nint after, string cls, string? title);
}
