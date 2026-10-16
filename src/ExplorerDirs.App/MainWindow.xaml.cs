using System.IO;
using System.Windows;
using System.Windows.Automation;
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
    private readonly FavoritesStore _favoritesStore;
    private readonly List<string> _favorites;
    private readonly UiPreferencesStore _uiPreferencesStore;
    private readonly PinnedTabsStore _pinnedTabsStore;
    private readonly EnvironmentRefresh _environmentRefresh = new();
    private readonly Dictionary<int, Button> _layoutButtons = [];
    private readonly Dictionary<int, MenuItem> _layoutMenuItems = [];
    private readonly Dictionary<uint, MenuItem> _defaultViewMenuItems = [];
    private readonly Dictionary<int, int> _lastFocusedPaneByLayout = [];
    private int _layout = 4;
    private uint _defaultViewMode = 4;
    private bool _compactMode;
    private bool _initializing = true;
    private bool _fileViewFocusDesired;
    private bool _openingTerminal;
    private readonly CancellationTokenSource _terminalLaunchStop = new();

    public MainWindow(string? startupDirectory = null, string? startupWarning = null)
    {
        InitializeComponent();
#if DEBUG
        Title = "Explorer Dirs · 开发调试";
#else
        Title = $"Explorer Dirs · Preview {GetType().Assembly.GetName().Version?.ToString(3)}";
#endif
        var state = _sessionStore.Load();
        if (_sessionStore.LoadWarning is { } warning)
            EnvironmentStatus.Text = warning;
        _favoritesStore = new FavoritesStore(Path.Combine(Path.GetDirectoryName(_sessionStore.Path)!, "Favorites.ini"));
        _favorites = _favoritesStore.Load().ToList();
        if (_favoritesStore.LoadWarning is { } favoritesWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? favoritesWarning : $"{EnvironmentStatus.Text} {favoritesWarning}";
        _uiPreferencesStore = new UiPreferencesStore(Path.Combine(
            Path.GetDirectoryName(_sessionStore.Path)!, "UiPreferences.ini"));
        _compactMode = _uiPreferencesStore.Load();
        if (_uiPreferencesStore.LoadWarning is { } appearanceWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? appearanceWarning : $"{EnvironmentStatus.Text} {appearanceWarning}";
        _pinnedTabsStore = new PinnedTabsStore(Path.Combine(
            Path.GetDirectoryName(_sessionStore.Path)!, "PinnedTabs.ini"));
        var pinnedTabs = _pinnedTabsStore.Load();
        if (_pinnedTabsStore.LoadWarning is { } pinsWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? pinsWarning : $"{EnvironmentStatus.Text} {pinsWarning}";
        _defaultViewMode = state?.DefaultViewMode ?? 4;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (var index = 0; index < _panes.Length; index++)
        {
            var tabs = state?.Panes.ElementAtOrDefault(index);
            var initialTabs = tabs is { Count: > 0 } ? tabs :
                [new TabSettings(home, ViewState.Default(_defaultViewMode))];
            var pane = new PaneControl(initialTabs,
                () => ViewState.Default(_defaultViewMode),
                PinnedTabsStore.MatchPane(initialTabs, pinnedTabs, index),
                state?.ActiveTabIndices?.ElementAtOrDefault(index) ?? 0);
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

        _layout = LayoutPreset.Find(state?.Layout ?? 4).Id;
        ActivePane = _panes[Math.Clamp(state?.ActivePane ?? 0, 0, _panes.Length - 1)];
        BuildLayoutControls();
        BuildDefaultViewOptions();
        BuildLayout();
        ApplyCompactMode();
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
        Closed += (_, _) => _terminalLaunchStop.Cancel();
        if (startupDirectory is not null)
        {
            SetActivePane(_panes[0]);
            _panes[0].OpenPathInNewTab(startupDirectory);
        }
        if (startupWarning is not null)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? startupWarning : $"{EnvironmentStatus.Text} {startupWarning}";
    }

    private PaneControl ActivePane { get; set; }

    internal bool OpenExternalDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return false;
        _panes[0].OpenPathInNewTab(path);
        SetActivePane(_panes[0]);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        return true;
    }

    private void SetActivePane(PaneControl pane)
    {
        var changed = !ReferenceEquals(ActivePane, pane);
        ActivePane = pane;
        var index = Array.IndexOf(_panes, pane);
        if (index >= 0 && index < LayoutPreset.Find(_layout).Slots.Length)
            _lastFocusedPaneByLayout[_layout] = index;
        if (changed) ScheduleSave();
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
            AutomationProperties.SetName(button, $"布局：{preset.Name}");
            AutomationProperties.SetAutomationId(button, $"Layout{preset.Id}");
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

    private void CompactMode_Click(object sender, RoutedEventArgs e)
    {
        var next = CompactModeMenu.IsChecked;
        try
        {
            _uiPreferencesStore.Save(next);
            _compactMode = next;
            ApplyCompactMode();
            EnvironmentStatus.Text = next ? "已启用紧凑布局。" : "已恢复标准布局间距。";
        }
        catch (Exception ex)
        {
            CompactModeMenu.IsChecked = _compactMode;
            EnvironmentStatus.Text = $"外观配置保存失败：{ex.Message}";
        }
    }

    private void ApplyCompactMode()
    {
        CompactModeMenu.IsChecked = _compactMode;
        PaneGrid.Margin = _compactMode ? new Thickness(1, 0, 1, 1) : new Thickness(5, 2, 5, 5);
        if (_compactMode) MainToolBar.Padding = new Thickness(1, 0, 1, 0);
        else MainToolBar.ClearValue(Control.PaddingProperty);
        foreach (var button in _layoutButtons.Values)
        {
            button.Width = _compactMode ? 30 : 34;
            button.Height = _compactMode ? 24 : 28;
        }
        foreach (var pane in _panes) pane.SetCompactMode(_compactMode);
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
        foreach (var pane in _panes) pane.CaptureVisibleViewState();
        _lastFocusedPaneByLayout[_layout] = Array.IndexOf(_panes, ActivePane);
        _layout = LayoutPreset.Find(id).Id;
        BuildLayout();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() => ActivePane.FocusCurrentView()));
        ScheduleSave();
    }

    private void BuildLayout()
    {
        var layout = LayoutPreset.Find(_layout);
        PaneLayoutHost.Apply(PaneGrid, _panes, layout);

        var remembered = _lastFocusedPaneByLayout.GetValueOrDefault(_layout, -1);
        if (remembered >= 0 && remembered < layout.Slots.Length)
            ActivePane = _panes[remembered];
        else if (Array.IndexOf(_panes, ActivePane) >= layout.Slots.Length)
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
        if (shortcut is >= ExplorerShortcut.Tab1 and <= ExplorerShortcut.Tab9)
        {
            source.SelectTabByNumber((int)shortcut - (int)ExplorerShortcut.Tab1 + 1);
            return true;
        }
        switch (shortcut)
        {
            case ExplorerShortcut.Parent: source.GoParent(); break;
            case ExplorerShortcut.Back: source.GoBack(); break;
            case ExplorerShortcut.Forward: source.GoForward(); break;
            case ExplorerShortcut.NextFocusStop: MoveFocus(false); break;
            case ExplorerShortcut.PreviousFocusStop: MoveFocus(true); break;
            case ExplorerShortcut.NextFocusRegion: MoveMajorFocus(false); break;
            case ExplorerShortcut.PreviousFocusRegion: MoveMajorFocus(true); break;
            case ExplorerShortcut.Address: source.FocusAddress(); break;
            case ExplorerShortcut.NewTab: source.NewTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.CloseTab: source.CloseCurrentTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.DuplicateTab: source.DuplicateCurrentTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.ReopenTab: source.ReopenClosedTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.NextTab: source.SelectRelativeTab(false); break;
            case ExplorerShortcut.PreviousTab: source.SelectRelativeTab(true); break;
            case ExplorerShortcut.NewFolder: source.CreateNewFolder(); break;
            case ExplorerShortcut.Refresh: source.RefreshCurrent(); break;
            case ExplorerShortcut.Search: source.FocusSearch(); break;
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

    private void MoveMajorFocus(bool reverse)
    {
        var count = LayoutPreset.Find(_layout).Slots.Length;
        var index = Array.IndexOf(_panes, ActivePane);
        var regions = _panes.Take(count).Select(pane => pane.MajorFocusStops).ToArray();
        var current = (Pane: Math.Clamp(index, 0, count - 1), Stop: ActivePane.CurrentFocusStop());
        for (var attempt = 0; attempt < regions.Sum(stops => stops.Count); attempt++)
        {
            current = FocusTraversal.MoveMajor(current.Pane, current.Stop, regions, reverse);
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
    private void SearchCurrent_Click(object sender, RoutedEventArgs e) => ActivePane.FocusSearch();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void FavoritesMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.Source == FavoritesMenu) BuildFavoritesMenu();
    }

    private void BuildFavoritesMenu()
    {
        FavoritesMenu.Items.Clear();
        var currentPath = ActivePane.CurrentPath;
        var isFavorite = _favorites.Any(path => FavoritesStore.SamePath(path, currentPath));
        var add = new MenuItem { Header = "收藏当前目录", IsEnabled = !isFavorite };
        add.Click += (_, _) => ChangeFavorites([.. _favorites, currentPath]);
        FavoritesMenu.Items.Add(add);
        var remove = new MenuItem { Header = "移除当前目录", IsEnabled = isFavorite };
        remove.Click += (_, _) => ChangeFavorites(_favorites.Where(path =>
            !FavoritesStore.SamePath(path, currentPath)).ToList());
        FavoritesMenu.Items.Add(remove);
        FavoritesMenu.Items.Add(new Separator());
        if (_favorites.Count == 0)
            FavoritesMenu.Items.Add(new MenuItem { Header = "（暂无收藏）", IsEnabled = false });
        foreach (var path in _favorites)
        {
            var item = new MenuItem
            {
                Header = new TextBlock { Text = path, MaxWidth = 340, TextTrimming = TextTrimming.CharacterEllipsis },
                ToolTip = path,
                IsCheckable = true,
                IsChecked = FavoritesStore.SamePath(path, currentPath)
            };
            item.Click += (_, _) => OpenFavorite(path);
            FavoritesMenu.Items.Add(item);
        }
    }

    private void ChangeFavorites(List<string> proposed)
    {
        try
        {
            _favoritesStore.Save(proposed);
            _favorites.Clear();
            _favorites.AddRange(proposed);
            EnvironmentStatus.Text = $"已保存 {_favorites.Count} 个收藏目录。";
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"收藏夹保存失败：{ex.Message}";
        }
    }

    private void OpenFavorite(string path)
    {
        var pane = ActivePane;
        _fileViewFocusDesired = true;
        pane.NavigateTo(path);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (IsActive && ReferenceEquals(ActivePane, pane) && Keyboard.FocusedElement is not TextBoxBase)
                pane.FocusCurrentView();
        }));
    }

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

    private async void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_openingTerminal) return;
        _openingTerminal = true;
        var path = ActivePane.CurrentPath;
        try
        {
            await TerminalLauncher.OpenAsync(path, _terminalLaunchStop.Token);
        }
        catch (OperationCanceledException) when (_terminalLaunchStop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsLoaded)
                EnvironmentStatus.Text = $"命令行启动失败：{ex.Message}";
            if (IsLoaded && IsVisible && IsActive)
                MessageBox.Show(this, ex.Message, "无法打开命令行", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _openingTerminal = false; }
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
                _panes.Select(p => p.SnapshotTabs().ToList()).ToList(),
                Array.IndexOf(_panes, ActivePane),
                _panes.Select(p => p.SelectedTabIndex).ToArray()));
            _pinnedTabsStore.Save(_panes.SelectMany((pane, index) => pane.SnapshotPins(index)).ToArray());
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"配置保存失败：{ex.Message}";
        }
    }
}
