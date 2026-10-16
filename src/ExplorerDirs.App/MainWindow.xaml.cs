using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ExplorerDirs.App;

public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;
    private readonly PaneControl[] _panes = new PaneControl[4];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _viewPollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly SessionStore _sessionStore = new();
    private readonly EnvironmentRefresh _environmentRefresh = new();
    private readonly Dictionary<int, Button> _layoutButtons = [];
    private readonly Dictionary<int, MenuItem> _layoutMenuItems = [];
    private readonly Dictionary<uint, MenuItem> _defaultViewMenuItems = [];
    private readonly Dictionary<int, int> _lastFocusedPaneByLayout = [];
    private int _layout = 4;
    private uint _defaultViewMode = 4;
    private bool _initializing = true;
    private bool _fileViewFocusDesired;

    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        Title = "Explorer Dirs · 开发调试";
#else
        Title = $"Explorer Dirs · Preview {GetType().Assembly.GetName().Version?.ToString(3)}";
#endif
        var state = _sessionStore.Load();
        _defaultViewMode = state?.DefaultViewMode ?? 4;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (var index = 0; index < _panes.Length; index++)
        {
            var tabs = state?.Panes.ElementAtOrDefault(index);
            var pane = new PaneControl(tabs is { Count: > 0 } ? tabs :
                [new TabSettings(home, ViewState.Default(_defaultViewMode))],
                () => ViewState.Default(_defaultViewMode));
            _panes[index] = pane;
            pane.StateChanged += (_, _) => ScheduleSave();
            pane.GotKeyboardFocus += (_, _) => SetActivePane(pane);
            pane.Activated += (_, _) =>
            {
                SetActivePane(pane);
                _fileViewFocusDesired = true;
            };
            pane.ShortcutRequested += ExecuteShortcut;
            pane.MayRestoreFileFocus = () => ReferenceEquals(ActivePane, pane) && _fileViewFocusDesired && IsActive;
        }

        ActivePane = _panes[0];
        _layout = LayoutPreset.Find(state?.Layout ?? 4).Id;
        BuildLayoutControls();
        BuildDefaultViewOptions();
        BuildLayout();
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() => ActivePane.FocusCurrentView()));
        _initializing = false;
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSession();
        };
        _viewPollTimer.Tick += (_, _) => SaveSession();
        _viewPollTimer.Start();
        PreviewGotKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is TextBoxBase or ButtonBase or MenuItem or TabItem)
                _fileViewFocusDesired = false;
        };
        Activated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (_fileViewFocusDesired && IsActive)
                ActivePane.FocusCurrentView();
        }));
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);
        Closing += (_, _) => { _viewPollTimer.Stop(); SaveSession(); };
    }

    private PaneControl ActivePane { get; set; }

    private void SetActivePane(PaneControl pane)
    {
        ActivePane = pane;
        var index = Array.IndexOf(_panes, pane);
        if (index >= 0 && index < LayoutPreset.Find(_layout).Slots.Length)
            _lastFocusedPaneByLayout[_layout] = index;
    }

    private void BuildLayoutControls()
    {
        foreach (var preset in LayoutPreset.All)
        {
            var menuItem = new MenuItem { Header = preset.Name, IsCheckable = true };
            menuItem.Click += (_, _) => SetLayout(preset.Id);
            LayoutMenu.Items.Add(menuItem);
            _layoutMenuItems[preset.Id] = menuItem;

            var button = new Button
            {
                Content = CreateLayoutIcon(preset),
                ToolTip = preset.Name,
                Width = 34,
                Height = 28,
                Margin = new Thickness(1, 0, 1, 0)
            };
            button.Click += (_, _) => SetLayout(preset.Id);
            LayoutButtons.Children.Add(button);
            _layoutButtons[preset.Id] = button;
        }
    }

    private void BuildDefaultViewOptions()
    {
        foreach (var (mode, name) in new (uint Mode, string Name)[]
        {
            (4, "详细信息"), (3, "列表"), (2, "小图标"),
            (1, "图标"), (6, "平铺"), (8, "内容")
        })
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = mode == _defaultViewMode };
            item.Click += (_, _) =>
            {
                _defaultViewMode = mode;
                foreach (var option in _defaultViewMenuItems)
                    option.Value.IsChecked = option.Key == mode;
                ScheduleSave();
            };
            DefaultViewMenu.Items.Add(item);
            _defaultViewMenuItems[mode] = item;
        }
    }

    private static Grid CreateLayoutIcon(LayoutPreset preset)
    {
        var icon = new Grid { Width = 22, Height = 18 };
        for (var row = 0; row < preset.Rows; row++)
            icon.RowDefinitions.Add(new RowDefinition());
        for (var column = 0; column < preset.Columns; column++)
            icon.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var slot in preset.Slots)
        {
            var panel = new Border
            {
                BorderBrush = Brushes.SteelBlue,
                BorderThickness = new Thickness(1),
                Background = Brushes.AliceBlue,
                Margin = new Thickness(1)
            };
            Grid.SetRow(panel, slot.Row);
            Grid.SetColumn(panel, slot.Column);
            Grid.SetRowSpan(panel, slot.RowSpan);
            Grid.SetColumnSpan(panel, slot.ColumnSpan);
            icon.Children.Add(panel);
        }
        return icon;
    }

    private void SetLayout(int id)
    {
        if (_layout == id)
            return;
        _lastFocusedPaneByLayout[_layout] = Array.IndexOf(_panes, ActivePane);
        _layout = LayoutPreset.Find(id).Id;
        BuildLayout();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() => ActivePane.FocusCurrentView()));
        ScheduleSave();
    }

    private void BuildLayout()
    {
        PaneGrid.Children.Clear();
        PaneGrid.RowDefinitions.Clear();
        PaneGrid.ColumnDefinitions.Clear();
        var layout = LayoutPreset.Find(_layout);
        for (var col = 0; col < layout.Columns; col++)
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < layout.Rows; row++)
            PaneGrid.RowDefinitions.Add(new RowDefinition());
        for (var index = 0; index < layout.Slots.Length; index++)
        {
            var pane = _panes[index];
            var slot = layout.Slots[index];
            Grid.SetColumn(pane, slot.Column);
            Grid.SetRow(pane, slot.Row);
            Grid.SetColumnSpan(pane, slot.ColumnSpan);
            Grid.SetRowSpan(pane, slot.RowSpan);
            PaneGrid.Children.Add(pane);
        }

        var remembered = _lastFocusedPaneByLayout.GetValueOrDefault(_layout, -1);
        if (remembered >= 0 && remembered < layout.Slots.Length)
            ActivePane = _panes[remembered];
        else if (!PaneGrid.Children.Contains(ActivePane))
            ActivePane = _panes[0];
        foreach (var preset in LayoutPreset.All)
        {
            _layoutMenuItems[preset.Id].IsChecked = preset.Id == _layout;
            _layoutButtons[preset.Id].Background = preset.Id == _layout ? Brushes.LightSteelBlue : SystemColors.ControlBrush;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is MenuItem) return;
        if (ActivePane.IsShellRenameEditorFocused) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var shortcut = KeyboardShortcuts.Resolve(KeyInterop.VirtualKeyFromKey(key), (int)Keyboard.Modifiers);
        if (shortcut == ExplorerShortcut.Parent && Keyboard.FocusedElement is TextBoxBase)
            return;
        e.Handled = ExecuteShortcut(ActivePane, shortcut);
    }

    private bool ExecuteShortcut(PaneControl source, ExplorerShortcut shortcut)
    {
        if (shortcut == ExplorerShortcut.None) return false;
        SetActivePane(source);
        switch (shortcut)
        {
            case ExplorerShortcut.Parent: source.GoParent(); break;
            case ExplorerShortcut.Back: source.GoBack(); break;
            case ExplorerShortcut.Forward: source.GoForward(); break;
            case ExplorerShortcut.NextFocusStop: MoveFocus(false); break;
            case ExplorerShortcut.PreviousFocusStop: MoveFocus(true); break;
            case ExplorerShortcut.Address: source.FocusAddress(); break;
            case ExplorerShortcut.NewTab: source.NewTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.CloseTab: source.CloseCurrentTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.DuplicateTab: source.DuplicateCurrentTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.ReopenTab: source.ReopenClosedTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.NextTab: source.SelectRelativeTab(false); break;
            case ExplorerShortcut.PreviousTab: source.SelectRelativeTab(true); break;
            case ExplorerShortcut.NewFolder: source.CreateNewFolder(); break;
            case ExplorerShortcut.Refresh: source.RefreshCurrent(); break;
            case ExplorerShortcut.ToggleMaximize:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
        }
        return true;
    }

    private void MoveFocus(bool reverse)
    {
        var count = LayoutPreset.Find(_layout).Slots.Length;
        var index = Array.IndexOf(_panes, ActivePane);
        var stops = _panes.Take(count).Select(pane => pane.FocusStopCount).ToArray();
        var current = (Pane: Math.Clamp(index, 0, count - 1), Stop: ActivePane.CurrentFocusStop());
        for (var attempt = 0; attempt < stops.Sum(); attempt++)
        {
            current = FocusTraversal.Move(current.Pane, current.Stop, stops, reverse);
            SetActivePane(_panes[current.Pane]);
            if (ActivePane.FocusStop(current.Stop)) return;
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => ActivePane.NewTab();
    private void NewFolder_Click(object sender, RoutedEventArgs e) => ActivePane.CreateNewFolder();
    private void DuplicateTab_Click(object sender, RoutedEventArgs e) => ActivePane.DuplicateCurrentTab();
    private void CloseTab_Click(object sender, RoutedEventArgs e) => ActivePane.CloseCurrentTab();
    private void CloseOtherTabs_Click(object sender, RoutedEventArgs e) => ActivePane.CloseOtherTabs();
    private void ReopenTab_Click(object sender, RoutedEventArgs e) => ActivePane.ReopenClosedTab();
    private void RefreshCurrent_Click(object sender, RoutedEventArgs e) => ActivePane.RefreshCurrent();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshEnvironment_Click(object sender, RoutedEventArgs e) => RefreshEnvironment();

    private void RefreshEnvironment()
    {
        try
        {
            var count = _environmentRefresh.Reload();
            EnvironmentStatus.Text = $"已刷新 {count} 个持久化变量 · {DateTime.Now:T}";
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"环境变量刷新失败：{ex.Message}";
        }
    }

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSettingChange && lParam != IntPtr.Zero &&
            string.Equals(System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam), "Environment", StringComparison.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(RefreshEnvironment);
        return IntPtr.Zero;
    }

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        var path = ActivePane.CurrentPath;
        if (!Directory.Exists(path))
        {
            MessageBox.Show(this, "当前目录不可用。", "Explorer Dirs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe")
            {
                WorkingDirectory = path,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "无法打开命令行", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScheduleSave()
    {
        if (!_initializing)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private void SaveSession()
    {
        try
        {
            _sessionStore.Save(new SessionState(_layout, _defaultViewMode,
                _panes.Select(p => p.SnapshotTabs().ToList()).ToList()));
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"会话保存失败：{ex.Message}";
        }
    }
}
