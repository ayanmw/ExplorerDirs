using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
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
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid ShellItemArrayId = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
    private const uint WmCommand = 0x0111;
    private const uint BrowseParent = 0x2000;
    private const uint BrowseBack = 0x4000;
    private const uint BrowseForward = 0x8000;
    // Standard Shell view command IDs from shlobj.h; the Shell executes the operations.
    private const int ShellCut = 0x7018;
    private const int ShellCopy = 0x7019;
    private const int ShellPaste = 0x701A;
    private const int ShellUndo = 0x701B;
    private const int ShellSelectAll = 0x7021;
    private static readonly string? FocusTracePath = Environment.GetEnvironmentVariable("EXPLORER_DIRS_FOCUS_TRACE");
    private IExplorerBrowser? _browser;
    private IntPtr _hostWindow;
    private IntPtr _viewWindow;
    private readonly List<IntPtr> _subclassedWindows = new();
    private readonly SubclassProc _subclassProc;
    private readonly DispatcherTimer _focusRestoreTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private bool _menuPrepared;
    private readonly Dictionary<ushort, Action> _menuActions = new();
    private uint _cookie;
    private bool _advised;
    private bool _hasNavigated;
    private bool _focusWhenReady;
    private bool _restoreFocusAfterNavigation;
    private IntPtr _lastContentFocusWindow;
    private ViewState _viewState;

    public string CurrentPath { get; private set; }
    public event EventHandler<string>? Navigated;
    public event EventHandler<string>? NavigationFailed;
    public event EventHandler<string>? MenuError;
    public event EventHandler? Activated;
    public Func<string?, IReadOnlyList<MenuAction>>? CustomCommands { get; set; }
    public Func<ExplorerShortcut, bool>? ShortcutHandler { get; set; }
    public Func<bool>? MayRestoreFocus { get; set; }

    public sealed record MenuAction(string Label, Action Action);

    public ExplorerBrowserHost(string initialPath, ViewState initialView)
    {
        CurrentPath = initialPath;
        _viewState = initialView.Copy();
        _subclassProc = ViewWindowProc;
        _focusRestoreTimer.Tick += (_, _) =>
        {
            _focusRestoreTimer.Stop();
            RestoreContentFocusIfNeeded();
        };
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
            var settings = new FolderSettings { ViewMode = _viewState.Mode, Flags = 0 };
            Check(_browser.Initialize(_hostWindow, ref rect, ref settings));
            Check(_browser.SetOptions(0x40 | 0x80)); // EBO_NOBORDER | EBO_NOPERSISTVIEWSTATE
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

    protected override bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers)
    {
        if (FocusTracePath is not null && msg.message is 0x0100 or 0x0104 && IsTraceKey(msg.wParam.ToInt64()))
            Trace($"translate key={msg.wParam.ToInt64():X} mods={modifiers} focus={FocusClass()} hwnd={GetFocus()} item={FolderFocusState()}");
        if (_browser is null || _viewWindow == IntPtr.Zero ||
            (msg.message != 0x0100 && msg.message != 0x0104))
            return base.TranslateAcceleratorCore(ref msg, modifiers);

        var focused = GetFocus();
        if (focused == IntPtr.Zero || (focused != _viewWindow && !IsChild(_viewWindow, focused)))
            return base.TranslateAcceleratorCore(ref msg, modifiers);
        var focusedClass = new StringBuilder(128);
        GetClassName(focused, focusedClass, focusedClass.Capacity);
        if (focusedClass.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase))
            return base.TranslateAcceleratorCore(ref msg, modifiers);

        if (msg.message == 0x0100 || msg.message == 0x0104)
        {
            var shortcut = KeyboardShortcuts.Resolve(unchecked((int)msg.wParam.ToInt64()), (int)modifiers);
            if (shortcut != ExplorerShortcut.None && ShortcutHandler?.Invoke(shortcut) == true)
                return true;
        }

        var iid = ShellViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) < 0)
            return base.TranslateAcceleratorCore(ref msg, modifiers);
        try
        {
            var nativeMessage = new NativeMessage
            {
                Hwnd = msg.hwnd,
                Message = msg.message,
                WParam = msg.wParam,
                LParam = msg.lParam,
                Time = (uint)msg.time,
                PointX = msg.pt_x,
                PointY = msg.pt_y
            };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMessage>());
            try
            {
                Marshal.StructureToPtr(nativeMessage, pointer, false);
                var shellResult = ((IShellView)viewObject).TranslateAccelerator(pointer);
                if (FocusTracePath is not null && IsTraceKey(msg.wParam.ToInt64()))
                    Trace($"shell result={shellResult:X8} focus={FocusClass()} item={FolderFocusState()}");
                if (shellResult == 0)
                {
                    QueueFocusRestoreAfterFileKey(msg, modifiers);
                    return true;
                }
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
        if (modifiers == ModifierKeys.Control && msg.message == 0x0100)
        {
            var command = msg.wParam.ToInt64() switch
            {
                0x41 => ShellSelectAll, // Ctrl+A
                0x43 => ShellCopy,      // Ctrl+C
                0x56 => ShellPaste,     // Ctrl+V
                0x58 => ShellCut,       // Ctrl+X
                0x5A => ShellUndo,      // Ctrl+Z
                _ => 0
            };
            if (command != 0)
            {
                SendMessage(_viewWindow, WmCommand, (IntPtr)command, IntPtr.Zero);
                Trace($"fallback command={command:X} focus={FocusClass()}");
                QueueFocusRestore();
                return true;
            }
        }
        QueueFocusRestoreAfterFileKey(msg, modifiers);
        return base.TranslateAcceleratorCore(ref msg, modifiers);
    }

    protected override bool TabIntoCore(TraversalRequest request) => FocusView();

    public bool FocusView()
    {
        if (!IsVisible) return false;
        if (_browser is null || _viewWindow == IntPtr.Zero)
        {
            _focusWhenReady = true;
            return true;
        }

        var target = FindContentFocusWindow();
        if (target == IntPtr.Zero) return false;
        Trace($"focus-view before={FocusClass()} target={target}");
        _focusWhenReady = false;
        var focused = GetFocus();
        if (focused != IntPtr.Zero && IsChild(_viewWindow, focused) &&
            WindowClass(focused).Contains("Edit", StringComparison.OrdinalIgnoreCase))
            return true; // Shell rename field owns its editing keys and focus.
        if (focused != target && !IsChild(target, focused))
            SetFocus(target);
        var iid = ShellViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) >= 0)
        {
            try { ((IShellView)viewObject).UIActivate(2); } // SVUIA_ACTIVATE_FOCUS
            finally { Marshal.ReleaseComObject(viewObject); }
        }
        FocusFolderItem();
        focused = GetFocus();
        Trace($"focus-view after={FocusClass()} hwnd={focused}");
        return focused == target || IsChild(target, focused);
    }

    public bool HasNativeFocus()
    {
        var focused = GetFocus();
        return focused != IntPtr.Zero && _viewWindow != IntPtr.Zero &&
            (focused == _viewWindow || IsChild(_viewWindow, focused));
    }

    public bool HasColumnHeader() => FindColumnHeader() is not null;

    public bool IsColumnHeaderFocused()
    {
        if (!HasNativeFocus()) return false;
        try
        {
            var element = AutomationElement.FocusedElement;
            return element?.Current.ControlType == ControlType.SplitButton &&
                TreeWalker.ControlViewWalker.GetParent(element)?.Current.ControlType == ControlType.Header;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (COMException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public bool FocusColumnHeader()
    {
        if (!IsVisible) return false;
        var header = FindColumnHeader();
        if (header is null) return false;
        try
        {
            header.SetFocus();
            return IsColumnHeaderFocused();
        }
        catch (ElementNotAvailableException) { return false; }
        catch (COMException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private AutomationElement? FindColumnHeader()
    {
        var content = FindContentFocusWindow();
        if (content == IntPtr.Zero) return null;
        try
        {
            var root = AutomationElement.FromHandle(content);
            var group = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Header));
            return group?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton));
        }
        catch (ElementNotAvailableException) { return null; }
        catch (COMException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private void FocusFolderItem()
    {
        if (_browser is null) return;
        try
        {
            if (AutomationElement.FocusedElement?.Current.ControlType == ControlType.ListItem)
                return;
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        catch (InvalidOperationException) { }
        var iid = FolderViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return;
        try
        {
            var view = (IFolderView)viewObject;
            if (view.GetFocusedItem(out var index) >= 0 && index >= 0)
            {
                var hr = view.SelectItem(index, 0x11); // SVSI_SELECT | SVSI_FOCUSED: preserve other selections.
                if (hr >= 0) FocusSelectedAutomationItem();
                if (FocusTracePath is not null)
                    Trace($"focus-item index={index} result={hr:X8} foreground={GetForegroundWindow()} focus={FocusClass()} item={FolderFocusState()}");
            }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private void FocusSelectedAutomationItem()
    {
        var content = FindContentFocusWindow();
        if (content == IntPtr.Zero) return;
        try
        {
            var root = AutomationElement.FromHandle(content);
            var selected = root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true)));
            selected?.SetFocus();
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        catch (InvalidOperationException) { }
    }

    private IntPtr FindContentFocusWindow()
    {
        if (_viewWindow == IntPtr.Zero) return IntPtr.Zero;
        if (_lastContentFocusWindow != IntPtr.Zero && IsWindow(_lastContentFocusWindow) &&
            IsChild(_viewWindow, _lastContentFocusWindow))
            return _lastContentFocusWindow;

        IntPtr content = IntPtr.Zero;
        IntPtr list = IntPtr.Zero;
        EnumChildWindows(_viewWindow, (child, _) =>
        {
            var name = new StringBuilder(64);
            GetClassName(child, name, name.Capacity);
            if (name.ToString() == "DirectUIHWND") content = child;
            else if (name.ToString() == "SysListView32") list = child;
            return true;
        }, IntPtr.Zero);
        return content != IntPtr.Zero ? content : list != IntPtr.Zero ? list : _viewWindow;
    }

    private void QueueFocusRestoreAfterFileKey(MSG msg, ModifierKeys modifiers)
    {
        var key = msg.wParam.ToInt64();
        if ((modifiers == ModifierKeys.Control && key is 0x41 or 0x43 or 0x56 or 0x58 or 0x5A) ||
            (modifiers == ModifierKeys.Shift && key == 0x2E))
            QueueFocusRestore();
    }

    private void QueueFocusRestore()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(RestoreContentFocusIfNeeded));
        _focusRestoreTimer.Stop();
        _focusRestoreTimer.Start();
    }

    private void RestoreContentFocusIfNeeded()
    {
        var window = Window.GetWindow(this);
        if (MayRestoreFocus?.Invoke() != true || window?.IsActive != true ||
            !IsVisible || _viewWindow == IntPtr.Zero)
            return;
        // Shell file-operation windows can close with no foreground HWND while
        // WPF still regards this window as active. Reassert only in that gap.
        if (GetForegroundWindow() == IntPtr.Zero)
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
        var focused = GetFocus();
        var target = FindContentFocusWindow();
        Trace($"restore-check focus={FocusClass()} target={target}");
        if (target != IntPtr.Zero && !IsColumnHeaderFocused() &&
            (focused == target || IsChild(target, focused) || focused == IntPtr.Zero ||
                focused == _viewWindow || !IsChild(_viewWindow, focused)))
            FocusView();
    }

    public bool GoParent() => BrowseRelative(BrowseParent);
    public bool GoBack() => BrowseRelative(BrowseBack);
    public bool GoForward() => BrowseRelative(BrowseForward);

    private bool BrowseRelative(uint flag)
    {
        if (_browser is null) return false;
        var hr = _browser.BrowseToIDList(IntPtr.Zero, flag);
        if (hr < 0 && hr != unchecked((int)0x80004005))
            NavigationFailed?.Invoke(this, $"目录导航失败：0x{hr:X8}");
        return hr >= 0;
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

    public ViewState CaptureViewState()
    {
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero) return _viewState.Copy();
        try
        {
            var mode = _viewState.Mode;
            var iconSize = _viewState.IconSize;
            if (GetMethod<GetViewModeAndIconSizeDelegate>(view, 36)(view, out var currentMode, out var currentSize) >= 0)
            {
                mode = currentMode;
                iconSize = currentSize;
            }

            var sorts = _viewState.SortColumns.ToList();
            if (GetMethod<GetSortColumnCountDelegate>(view, 26)(view, out var count) >= 0 && count is >= 0 and <= 16)
            {
                if (count == 0) sorts.Clear();
                else
                {
                    var columns = new NativeSortColumn[count];
                    if (GetMethod<GetSortColumnsDelegate>(view, 28)(view, columns, count) >= 0)
                        sorts = columns.Select(column => new SortColumnState(
                            new PropertyKeyState(column.Key.FormatId, column.Key.PropertyId), column.Direction)).ToList();
                }
            }

            var group = _viewState.GroupKey;
            var ascending = _viewState.GroupAscending;
            if (GetMethod<GetGroupByDelegate>(view, 18)(view, out var groupKey, out var groupAscending) >= 0)
            {
                group = groupKey.FormatId != Guid.Empty
                    ? new PropertyKeyState(groupKey.FormatId, groupKey.PropertyId) : null;
                ascending = groupAscending != 0;
            }
            _viewState = new ViewState(mode, iconSize, sorts, group, ascending);
            return _viewState.Copy();
        }
        finally { Marshal.Release(view); }
    }

    private void ApplyViewState()
    {
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero) return;
        try
        {
            var modeResult = GetMethod<SetViewModeAndIconSizeDelegate>(view, 35)(view, _viewState.Mode, _viewState.IconSize);
            if (modeResult < 0)
                MenuError?.Invoke(this, $"查看方式未能恢复：0x{modeResult:X8}");
            if (_viewState.SortColumns.Count > 0)
            {
                var columns = _viewState.SortColumns.Select(sort => new NativeSortColumn
                {
                    Key = new NativePropertyKey { FormatId = sort.Key.FormatId, PropertyId = sort.Key.PropertyId },
                    Direction = sort.Direction
                }).ToArray();
                var sortResult = GetMethod<SetSortColumnsDelegate>(view, 27)(view, columns, columns.Length);
                if (sortResult < 0)
                    MenuError?.Invoke(this, $"排序方式未能恢复：0x{sortResult:X8}");
            }
            var group = _viewState.GroupKey is { } savedGroup
                ? new NativePropertyKey { FormatId = savedGroup.FormatId, PropertyId = savedGroup.PropertyId }
                : default;
            var groupResult = GetMethod<SetGroupByDelegate>(view, 17)(view, ref group, _viewState.GroupAscending ? 1 : 0);
            if (groupResult < 0)
                MenuError?.Invoke(this, $"分组方式未能恢复：0x{groupResult:X8}");
        }
        finally { Marshal.Release(view); }
    }

    private IntPtr GetFolderView2Pointer()
    {
        if (_browser is null) return IntPtr.Zero;
        var iid = FolderView2Id;
        if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return IntPtr.Zero;
        try
        {
            var unknown = Marshal.GetIUnknownForObject(viewObject);
            try { return Marshal.QueryInterface(unknown, in iid, out var pointer) >= 0 ? pointer : IntPtr.Zero; }
            finally { Marshal.Release(unknown); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private static T GetMethod<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

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
        _focusRestoreTimer.Stop();
        if (_browser is null) return;
        DetachViewWindow();
        if (_advised) { _browser.Unadvise(_cookie); _advised = false; }
        _browser.Destroy();
        Marshal.ReleaseComObject(_browser);
        _browser = null;
    }

    int IExplorerBrowserEvents.OnNavigationPending(IntPtr pidl)
    {
        var focused = GetFocus();
        _restoreFocusAfterNavigation = _viewWindow != IntPtr.Zero &&
            (focused == _viewWindow || IsChild(_viewWindow, focused));
        if (_hasNavigated)
        {
            try { CaptureViewState(); }
            catch (Exception ex) { MenuError?.Invoke(this, $"读取视图配置失败：{ex.Message}"); }
        }
        return 0;
    }
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
                    _hasNavigated = true;
                    try { AttachCurrentViewWindow(); }
                    catch (Exception ex) { MenuError?.Invoke(this, ex.Message); }
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        if (CurrentPath != path) return;
                        try { ApplyViewState(); }
                        catch (Exception ex) { MenuError?.Invoke(this, $"恢复视图配置失败：{ex.Message}"); }
                        if ((_focusWhenReady || _restoreFocusAfterNavigation) && IsVisible)
                            FocusView();
                        _restoreFocusAfterNavigation = false;
                    }));
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
        _lastContentFocusWindow = IntPtr.Zero;
        _menuActions.Clear();
    }

    private IntPtr ViewWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, IntPtr data)
    {
        try
        {
            if (message == 0x0100 && IsTraceKey(wParam.ToInt64()))
                Trace($"native key={wParam.ToInt64():X} hwnd={hwnd} class={WindowClass(hwnd)} focus={FocusClass()}");
            if (message is 0x0007 or 0x0008)
                Trace($"native focus-message={message:X} hwnd={hwnd} class={WindowClass(hwnd)} next={wParam}");
            switch (message)
            {
                case 0x0007: // WM_SETFOCUS
                    var focusedClass = new StringBuilder(64);
                    GetClassName(hwnd, focusedClass, focusedClass.Capacity);
                    if (focusedClass.ToString() is "DirectUIHWND" or "SysListView32")
                        _lastContentFocusWindow = hwnd;
                    Activated?.Invoke(this, EventArgs.Empty);
                    break;
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
        if (commands.Count == 0) return;

        var id = (ushort)0xE000;
        var entries = new List<(ushort Id, string Label)>();
        foreach (var command in commands)
        {
            _menuActions[id] = command.Action;
            entries.Add((id++, command.Label));
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

    private static bool IsTraceKey(long key) => key is 0x09 or 0x25 or 0x26 or 0x27 or 0x28 or 0x43 or 0x56 or 0x2E or 0x0D;

    private static string FocusClass() => WindowClass(GetFocus());

    private string FolderFocusState()
    {
        if (_browser is null) return "unavailable";
        var iid = FolderViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return "no-view";
        try
        {
            var view = (IFolderView)viewObject;
            var focused = view.GetFocusedItem(out var focusIndex) >= 0 ? focusIndex.ToString() : "none";
            var marked = view.GetSelectionMarkedItem(out var markedIndex) >= 0 ? markedIndex.ToString() : "none";
            var selected = view.ItemCount(1, out var count) >= 0 ? count.ToString() : "unknown";
            return $"focused:{focused},marked:{marked},selected:{selected}";
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private static string WindowClass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "none";
        var name = new StringBuilder(80);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    private static void Trace(string message)
    {
        if (string.IsNullOrEmpty(FocusTracePath)) return;
        try { File.AppendAllText(FocusTracePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
        catch { /* Diagnostic output must not affect file operations. */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FolderSettings { public uint ViewMode, Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePropertyKey { public Guid FormatId; public uint PropertyId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSortColumn { public NativePropertyKey Key; public int Direction; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public int Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetViewModeAndIconSizeDelegate(IntPtr instance, out uint mode, out int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetViewModeAndIconSizeDelegate(IntPtr instance, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSortColumnCountDelegate(IntPtr instance, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSortColumnsDelegate(IntPtr instance, [Out] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetSortColumnsDelegate(IntPtr instance, [In] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetGroupByDelegate(IntPtr instance, out NativePropertyKey key, out int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetGroupByDelegate(IntPtr instance, ref NativePropertyKey key, int ascending);

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
        [PreserveSig] int GetSelectionMarkedItem(out int index);
        [PreserveSig] int GetFocusedItem(out int index);
        [PreserveSig] int GetItemPosition(IntPtr pidl, IntPtr point);
        [PreserveSig] int GetSpacing(IntPtr point);
        [PreserveSig] int GetDefaultSpacing(IntPtr point);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int index, uint flags);
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InsertMenu(IntPtr menu, uint position, uint flags, nuint id, string? text);
    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maximum);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

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
