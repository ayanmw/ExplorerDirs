using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ExplorerDirs.App;

public partial class MainWindow : Window
{
    private readonly PaneControl[] _panes = new PaneControl[4];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _viewPollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _fileCommandRefreshTimer = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly ShellCommandRefreshGate _fileCommandRefreshGate = new();
    private PaneControl? _fileCommandRefreshPane;
    private readonly SessionStore _sessionStore;
    private readonly WindowPlacementStore _windowPlacementStore;
    private readonly WindowPlacementState? _savedWindowPlacement;
    private WindowPlacementState? _fullScreenWindowPlacement;
    private bool _windowPlacementReady;
    private bool _lastNonMinimizedMaximized;
    private readonly ApplicationThemeController? _themeController;
    private readonly string _profileDirectory;
    private readonly Action<MainWindow>? _createWindow;
    private readonly Action? _restartApplication;
    private FullScreenPresentation? _fullScreenRestoreState;
    private readonly FavoritesStore _favoritesStore;
    private readonly ViewPresetStore _viewPresetStore;
    private readonly List<string> _favorites;
    private readonly QuickLinkStore _quickLinkStore;
    private readonly ProgramStarterStore _programStarterStore;
    private IReadOnlyList<ProgramStarterEntry> _programStarterEntries = [];
    private ProgramStarterWindow? _programStarterWindow;
    private readonly QuickFilterFavoritesStore _quickFilterFavoritesStore;
    private readonly QuickFilterHistoryStore _quickFilterHistoryStore;
    private readonly UiPreferencesStore _uiPreferencesStore;
    private readonly TabStripVisibilityStore _tabStripVisibilityStore;
    private readonly MultilineTabHeadersStore _multilineTabHeadersStore;
    private readonly TabHoverActivationPreference _tabHoverActivationPreference;
    private readonly PortableBooleanPreference _newTabLocationPreference;
    private readonly StatusBarVisibilityStore _statusBarVisibilityStore;
    private readonly StatusBarSelectionSizeStore _statusBarSelectionSizeStore;
    private readonly StatusBarFolderSizeWhenEmptyStore _statusBarFolderSizeWhenEmptyStore;
    private readonly SelectionCheckBoxesStore _selectionCheckBoxesStore;
    private readonly FullRowSelectionStore _fullRowSelectionStore;
    private readonly PortableBooleanPreference _mouseWheelFocusPreferenceStore;
    private bool _alwaysShowTabStrip;
    private bool _multilineTabHeaders;
    private int _tabHoverActivationDelayMilliseconds = TabHoverActivationPreference.DefaultDelayMilliseconds;
    private bool _newTabUsesCurrentFolder;
    private bool _showStatusBar = true;
    private bool _showSelectionSizeInStatusBar = true;
    private bool _showFolderSizeWhenEmpty;
    private bool _selectionCheckBoxes;
    private bool? _fullRowSelection;
    private readonly HiddenItemsPreferenceStore _hiddenItemsPreferenceStore;
    private readonly SizeDisplayPreferenceStore _sizeDisplayPreferenceStore;
    private readonly PortableBooleanPreference _treePreferences;
    private readonly PortableBooleanPreference _treeSelectionAutoExpandPreferenceStore;
    private readonly NavigationTreeWidthsStore _treeWidthsStore;
    private NavigationTreeWidths _treeWidths;
    private readonly bool[] _paneTreeVisibility = new bool[4];
    private TreeDisplayMode _treeDisplayMode = TreeDisplayMode.PerPane;
    private bool _treeSelectionAutoExpand = true;
    private ShellTreeHost? _sharedTreeHost;
    private readonly PinnedTabsStore _pinnedTabsStore;
    private readonly LayoutProportionsStore _layoutProportionsStore;
    private readonly AddressBarStyleStore _addressBarStyleStore;
    private readonly Dictionary<int, LayoutProportions> _layoutProportions;
    private readonly MenuItem _resetLayoutProportionsMenu = new() { Header = "恢复当前布局均分" };
    private readonly EnvironmentRefresh _environmentRefresh = new();
    private readonly NetworkDriveDialogs _networkDriveDialogs = NetworkDriveDialogs.CreateSystem();
    private SystemSettingChangeHook? _systemSettingChangeHook;
    private readonly Dictionary<int, Button> _layoutButtons = [];
    private readonly Dictionary<int, MenuItem> _layoutMenuItems = [];
    private readonly Dictionary<ShellFileCommand, MenuItem> _fileCommandMenus = [];
    private readonly Dictionary<ShellFileCommand, Button> _fileCommandButtons = [];
    private readonly Dictionary<ShellSelectionCommand, MenuItem> _selectionMenus = [];
    private bool _queryingFileCommands;
    private readonly Dictionary<ViewStyle, MenuItem> _defaultViewMenuItems = [];
    private readonly MenuItem _customDefaultViewMenu = new()
        { Header = "已保存的自定义默认查看方式", IsCheckable = true, IsEnabled = false };
    private readonly Dictionary<int, int> _lastFocusedPaneByLayout = [];
    private int _layout = 4;
    private uint _defaultViewMode = 4;
    private int _defaultIconSize = -1;
    private bool _compactMode;
    private HiddenItemsPreference _hiddenItemsPreference = HiddenItemsPreference.FollowWindows;
    private bool _windowsShowsHiddenItems;
    private bool _classicAddressBar;
    private bool _mouseWheelFocusEnabled;
    private bool _initializing = true;
    private bool _closed;
    private bool _fileViewFocusDesired = true;
    private bool _openingTerminal;
    private bool _importingQdr;
    private bool _exportingQdr;
    private bool _loadingPrintPhotos;
    private Task<WorkspaceListing>? _workspaceListingTask;
    private readonly CancellationTokenSource _windowOperationStop = new();

    public string SessionPath => _sessionStore.Path;
    internal bool HasSavedWindowPlacement => _savedWindowPlacement is not null;

    public MainWindow(string? startupDirectory = null, string? startupWarning = null)
        : this(null, startupDirectory, startupWarning) { }

    internal MainWindow(ApplicationThemeController? themeController, string? startupDirectory = null, string? startupWarning = null,
        string? sessionPath = null, string? profileDirectory = null, Action<MainWindow>? createWindow = null,
        Action? restartApplication = null)
    {
        _themeController = themeController;
        InitializeComponent();
        NewTabToolbarButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.NewTab));
        NewWindowToolbarButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.NewWindow));
        DuplicateTabToolbarButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.DuplicateTab));
        CloseTabToolbarButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Close));
        RefreshCurrentDirectoryButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Refresh));
        SearchCurrentDirectoryButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Search));
        PrintFileViewToolbarButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Print));
        FolderSizeToolbarButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.FolderSize));
        PreviewPaneToggle.Content = ExplorerChromeIcons.CreateLabeledButtonContent(ExplorerChromeIcon.Preview, "预览");
        DetailsPaneToggle.Content = ExplorerChromeIcons.CreateLabeledButtonContent(ExplorerChromeIcon.Details, "详情");
        RefreshEnvironmentToolbarButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Environment));
        OpenTerminalToolbarButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Terminal));
        if (_themeController is not null)
            _themeController.ThemeChanged += ThemeController_ThemeChanged;
        ByteSizeFormatter.OptionsChanged += ByteSizeFormatter_OptionsChanged;
        Closed += (_, _) => ByteSizeFormatter.OptionsChanged -= ByteSizeFormatter_OptionsChanged;
        Deactivated += (_, _) =>
        {
            foreach (var pane in _panes) pane.CloseAddressTree();
        };
        _fileCommandRefreshTimer.Tick += FileCommandRefreshTimer_Tick;
        var primarySessionPath = new SessionStore().Path;
        _sessionStore = new SessionStore(sessionPath ?? primarySessionPath);
        _windowPlacementStore = new WindowPlacementStore(WindowPlacementStore.PathForSession(_sessionStore.Path));
        _savedWindowPlacement = _windowPlacementStore.Load();
        _lastNonMinimizedMaximized = _savedWindowPlacement?.Maximized == true;
        if (HasSavedWindowPlacement) WindowStartupLocation = WindowStartupLocation.Manual;
        _profileDirectory = Path.GetFullPath(profileDirectory ?? Path.GetDirectoryName(primarySessionPath)!);
        _sizeDisplayPreferenceStore = new SizeDisplayPreferenceStore(Path.Combine(_profileDirectory, "SizeDisplay.ini"));
        ByteSizeFormatter.SetOptions(_sizeDisplayPreferenceStore.Load());
        if (_sizeDisplayPreferenceStore.LoadWarning is { } sizeDisplayWarning)
            EnvironmentStatus.Text = sizeDisplayWarning;
        _viewPresetStore = new ViewPresetStore(Path.Combine(_profileDirectory, "ViewPresets.ini"));
        _viewPresetStore.Load();
        if (_viewPresetStore.LoadWarning is { } viewPresetsWarning)
            EnvironmentStatus.Text = viewPresetsWarning;
        _createWindow = createWindow;
        _restartApplication = restartApplication;
#if DEBUG
        Title = "Explorer Dirs · 开发调试";
#else
        Title = $"Explorer Dirs · Preview {GetType().Assembly.GetName().Version?.ToString(3)}";
#endif
        var state = _sessionStore.Load();
        if (_sessionStore.LoadWarning is { } warning)
            EnvironmentStatus.Text = warning;
        _favoritesStore = new FavoritesStore(Path.Combine(_profileDirectory, "Favorites.ini"));
        _favorites = _favoritesStore.Load().ToList();
        _quickLinkStore = new QuickLinkStore(Path.Combine(_profileDirectory, "QuickLinks"));
        _programStarterStore = new ProgramStarterStore(Path.Combine(_profileDirectory, "ProgramStarter.ini"));
        _programStarterEntries = _programStarterStore.Load();
        if (_programStarterStore.LoadWarning is { } programStarterWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? programStarterWarning : $"{EnvironmentStatus.Text} {programStarterWarning}";
        if (_favoritesStore.LoadWarning is { } favoritesWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? favoritesWarning : $"{EnvironmentStatus.Text} {favoritesWarning}";
        _quickFilterFavoritesStore = new QuickFilterFavoritesStore(Path.Combine(
            _profileDirectory, "QuickFilterFavorites.ini"));
        if (_quickFilterFavoritesStore.LoadWarning is { } filterFavoritesWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? filterFavoritesWarning : $"{EnvironmentStatus.Text} {filterFavoritesWarning}";
        _quickFilterHistoryStore = new QuickFilterHistoryStore(Path.Combine(
            _profileDirectory, "QuickFilterHistory.ini"));
        _ = _quickFilterHistoryStore.Load();
        if (_quickFilterHistoryStore.LoadWarning is { } filterHistoryWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? filterHistoryWarning : $"{EnvironmentStatus.Text} {filterHistoryWarning}";
        _uiPreferencesStore = new UiPreferencesStore(Path.Combine(
            _profileDirectory, "UiPreferences.ini"));
        _compactMode = _uiPreferencesStore.Load();
        _mouseWheelFocusPreferenceStore = new PortableBooleanPreference(Path.Combine(
            _profileDirectory, "MouseWheelFocus.ini"), "Mouse", "FocusOnWheel", "鼠标滚轮焦点");
        _mouseWheelFocusEnabled = _mouseWheelFocusPreferenceStore.Load();
        MouseWheelFocusMenu.IsChecked = _mouseWheelFocusEnabled;
        _tabStripVisibilityStore = new TabStripVisibilityStore(Path.Combine(
            _profileDirectory, "TabStripVisibility.ini"));
        _alwaysShowTabStrip = _tabStripVisibilityStore.Load();
        _multilineTabHeadersStore = new MultilineTabHeadersStore(Path.Combine(
            _profileDirectory, "MultilineTabHeaders.ini"));
        _multilineTabHeaders = _multilineTabHeadersStore.Load();
        _tabHoverActivationPreference = new TabHoverActivationPreference(Path.Combine(
            _profileDirectory, "TabHoverActivation.ini"));
        _tabHoverActivationDelayMilliseconds = _tabHoverActivationPreference.Load();
        if (_tabHoverActivationPreference.LoadWarning is { } tabHoverWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? tabHoverWarning : $"{EnvironmentStatus.Text} {tabHoverWarning}";
        _newTabLocationPreference = new PortableBooleanPreference(Path.Combine(
            _profileDirectory, "NewTabLocation.ini"), "Tabs", "UseCurrentFolder", "新标签目录");
        _newTabUsesCurrentFolder = _newTabLocationPreference.Load();
        NewTabUsesCurrentFolderMenu.IsChecked = _newTabUsesCurrentFolder;
        if (_newTabLocationPreference.LoadWarning is { } newTabLocationWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? newTabLocationWarning : $"{EnvironmentStatus.Text} {newTabLocationWarning}";
        _statusBarVisibilityStore = new StatusBarVisibilityStore(Path.Combine(
            _profileDirectory, "StatusBar.ini"));
        _showStatusBar = _statusBarVisibilityStore.Load();
        _statusBarSelectionSizeStore = new StatusBarSelectionSizeStore(Path.Combine(
            _profileDirectory, "StatusBarSelectionSize.ini"));
        _showSelectionSizeInStatusBar = _statusBarSelectionSizeStore.Load();
        StatusBarSelectionSizeMenu.IsChecked = _showSelectionSizeInStatusBar;
        _statusBarFolderSizeWhenEmptyStore = new StatusBarFolderSizeWhenEmptyStore(Path.Combine(
            _profileDirectory, "StatusBarFolderSize.ini"));
        _showFolderSizeWhenEmpty = _statusBarFolderSizeWhenEmptyStore.Load();
        StatusBarFolderSizeWhenEmptyMenu.IsChecked = _showFolderSizeWhenEmpty;
        _selectionCheckBoxesStore = new SelectionCheckBoxesStore(Path.Combine(
            _profileDirectory, "SelectionCheckBoxes.ini"));
        _selectionCheckBoxes = _selectionCheckBoxesStore.Load();
        SelectionCheckBoxesMenu.IsChecked = _selectionCheckBoxes;
        _fullRowSelectionStore = new FullRowSelectionStore(Path.Combine(
            _profileDirectory, "FullRowSelection.ini"));
        var fullRowSelectionValue = _fullRowSelectionStore.Load();
        _fullRowSelection = _fullRowSelectionStore.HasExplicitValue ? fullRowSelectionValue : null;
        FullRowSelectionMenu.IsChecked = _fullRowSelection == true;
        if (_fullRowSelectionStore.LoadWarning is { } fullRowSelectionWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? fullRowSelectionWarning : $"{EnvironmentStatus.Text} {fullRowSelectionWarning}";
        _hiddenItemsPreferenceStore = new HiddenItemsPreferenceStore(Path.Combine(
            _profileDirectory, "HiddenItems.ini"));
        _hiddenItemsPreference = _hiddenItemsPreferenceStore.Load();
        _windowsShowsHiddenItems = ShellFolderFilter.ReadShowAllObjectsSetting();
        if (_hiddenItemsPreferenceStore.LoadWarning is { } hiddenItemsWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? hiddenItemsWarning : $"{EnvironmentStatus.Text} {hiddenItemsWarning}";
        _addressBarStyleStore = new AddressBarStyleStore(Path.Combine(
            _profileDirectory, "AddressBar.ini"));
        _classicAddressBar = _addressBarStyleStore.Load();
        if (_addressBarStyleStore.LoadWarning is { } addressBarWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? addressBarWarning : $"{EnvironmentStatus.Text} {addressBarWarning}";
        _treePreferences = new PortableBooleanPreference(Path.Combine(
            _profileDirectory, "NavigationTree.ini"), "NavigationTree", "Visible", "目录树");
        _treeSelectionAutoExpandPreferenceStore = new PortableBooleanPreference(Path.Combine(
            _profileDirectory, "NavigationTreeSelection.ini"), "NavigationTree", "SelectFromListAndExpand",
            "目录树自动展开", defaultValue: true);
        _treeSelectionAutoExpand = _treeSelectionAutoExpandPreferenceStore.Load();
        _treeWidthsStore = new NavigationTreeWidthsStore(Path.Combine(
            _profileDirectory, "NavigationTreeWidths.ini"));
        _treeWidths = _treeWidthsStore.Load();
        var defaultTreeVisible = _treePreferences.Load();
        _treeDisplayMode = state?.TreeDisplayMode ??
            (defaultTreeVisible ? TreeDisplayMode.PerPane : TreeDisplayMode.None);
        for (var index = 0; index < _paneTreeVisibility.Length; index++)
            _paneTreeVisibility[index] = state?.PaneTreeVisibility?.ElementAtOrDefault(index) ?? defaultTreeVisible;
        if (_treePreferences.LoadWarning is { } treeWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? treeWarning : $"{EnvironmentStatus.Text} {treeWarning}";
        if (_treeSelectionAutoExpandPreferenceStore.LoadWarning is { } treeSelectionWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? treeSelectionWarning : $"{EnvironmentStatus.Text} {treeSelectionWarning}";
        if (_treeWidthsStore.LoadWarning is { } treeWidthsWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? treeWidthsWarning : $"{EnvironmentStatus.Text} {treeWidthsWarning}";
        if (_uiPreferencesStore.LoadWarning is { } appearanceWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? appearanceWarning : $"{EnvironmentStatus.Text} {appearanceWarning}";
        if (_mouseWheelFocusPreferenceStore.LoadWarning is { } wheelFocusWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? wheelFocusWarning : $"{EnvironmentStatus.Text} {wheelFocusWarning}";
        if (_tabStripVisibilityStore.LoadWarning is { } tabStripWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? tabStripWarning : $"{EnvironmentStatus.Text} {tabStripWarning}";
        if (_statusBarVisibilityStore.LoadWarning is { } statusBarWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? statusBarWarning : $"{EnvironmentStatus.Text} {statusBarWarning}";
        if (_statusBarSelectionSizeStore.LoadWarning is { } statusBarSelectionSizeWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? statusBarSelectionSizeWarning : $"{EnvironmentStatus.Text} {statusBarSelectionSizeWarning}";
        if (_statusBarFolderSizeWhenEmptyStore.LoadWarning is { } statusBarFolderSizeWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? statusBarFolderSizeWarning : $"{EnvironmentStatus.Text} {statusBarFolderSizeWarning}";
        if (_selectionCheckBoxesStore.LoadWarning is { } checkBoxesWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? checkBoxesWarning : $"{EnvironmentStatus.Text} {checkBoxesWarning}";
        var isPrimarySession = string.Equals(Path.GetFullPath(_sessionStore.Path),
            Path.GetFullPath(primarySessionPath), StringComparison.OrdinalIgnoreCase);
        var pinnedTabsPath = isPrimarySession ? Path.Combine(_profileDirectory, "PinnedTabs.ini") :
            Path.Combine(_profileDirectory, Path.GetFileNameWithoutExtension(_sessionStore.Path) + ".PinnedTabs.ini");
        _pinnedTabsStore = new PinnedTabsStore(pinnedTabsPath);
        _layoutProportionsStore = new LayoutProportionsStore(Path.Combine(
            Path.GetDirectoryName(_sessionStore.Path)!, "LayoutProportions.ini"));
        _layoutProportions = _layoutProportionsStore.Load();
        if (_layoutProportionsStore.LoadWarning is { } proportionsWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? proportionsWarning : $"{EnvironmentStatus.Text} {proportionsWarning}";
        var pinnedTabs = _pinnedTabsStore.Load();
        if (_pinnedTabsStore.LoadWarning is { } pinsWarning)
            EnvironmentStatus.Text = string.IsNullOrEmpty(EnvironmentStatus.Text)
                ? pinsWarning : $"{EnvironmentStatus.Text} {pinsWarning}";
        _defaultViewMode = state?.DefaultViewMode ?? 4;
        _defaultIconSize = state?.DefaultIconSize ?? -1;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (var index = 0; index < _panes.Length; index++)
        {
            var tabs = state?.Panes.ElementAtOrDefault(index);
            var initialTabs = tabs is { Count: > 0 } ? tabs :
                [new TabSettings(home, ViewState.Default(_defaultViewMode, _defaultIconSize))];
            var pane = new PaneControl(initialTabs,
                () => ViewState.Default(_defaultViewMode, _defaultIconSize),
                PinnedTabsStore.MatchPane(initialTabs, pinnedTabs, index),
                state?.ActiveTabIndices?.ElementAtOrDefault(index) ?? 0,
                _quickFilterFavoritesStore, _quickFilterHistoryStore, _hiddenItemsPreference,
                _selectionCheckBoxes, _fullRowSelection);
            _panes[index] = pane;
            pane.SetClassicAddressBar(_classicAddressBar);
            pane.SetStatusBarVisible(_showStatusBar);
            pane.SetStatusBarSelectionSizeVisible(_showSelectionSizeInStatusBar);
            pane.SetStatusBarFolderSizeWhenEmpty(_showFolderSizeWhenEmpty);
            pane.CommandContextChanged += (_, _) => ScheduleFileCommandRefresh(pane);
            pane.SelectedFolderForTree += Pane_SelectedFolderForTree;
            var paneIndex = index;
            pane.TreeWidth = _treeWidths.GetPaneWidth(index);
            pane.TreeWidthChanged += (_, _) =>
            {
                _treeWidths = _treeWidths.WithPaneWidth(paneIndex, pane.TreeWidth);
                SaveTreeWidths();
            };
            pane.StateChanged += (_, _) =>
            {
                if (_treeDisplayMode == TreeDisplayMode.PerPane)
                    _paneTreeVisibility[paneIndex] = pane.TreeIsVisible;
                UpdateTreeModeMenu();
                ScheduleSave();
                if (ReferenceEquals(ActivePane, pane)) SynchronizeSharedTree();
                if (ReferenceEquals(ActivePane, pane)) UpdateShellPaneToggles();
            };
            pane.GotKeyboardFocus += (_, _) => SetActivePane(pane);
            pane.Activated += (_, _) =>
            {
                SetActivePane(pane);
                _fileViewFocusDesired = true;
            };
            pane.HeaderPresetMenuRequested += ShowViewPresetContextMenu;
            pane.ShortcutRequested += ExecuteShortcut;
            pane.MayRestoreFileFocus = () => ReferenceEquals(ActivePane, pane) && _fileViewFocusDesired && IsActive;
            pane.MayKeepFileFocusRestorePending = () => ReferenceEquals(ActivePane, pane) && _fileViewFocusDesired;
        }

        _layout = LayoutPreset.Find(state?.Layout ?? 4).Id;
        ActivePane = _panes[Math.Clamp(state?.ActivePane ?? 0, 0, _panes.Length - 1)];
        UpdateActivePaneVisuals();
        BuildLayoutControls();
        BuildFileCommandControls();
        BuildDefaultViewOptions();
        BuildLayout();
        ApplyThemePreferenceMenuState();
        ApplySizeDisplayMenuState();
        ApplyCompactMode();
        ApplyStatusBarVisibility();
        ApplyTabStripVisibility();
        ApplyTabHoverActivationDelay();
        ApplyNewTabLocationPreference();
        ApplyMultilineTabHeaders();
        ApplyAddressBarStyle();
        UpdateTreeModeMenu();
        UpdatePreviewPaneToggle();
        for (var index = 0; index < _panes.Length; index++)
        {
            _panes[index].SetAutoExpandTreeSelection(_treeSelectionAutoExpand);
            _panes[index].SetTreeVisible(_treeDisplayMode == TreeDisplayMode.PerPane && _paneTreeVisibility[index]);
            _panes[index].SetPaneTreeButtonVisible(_treeDisplayMode == TreeDisplayMode.PerPane);
            var paneIndex = index;
            _panes[index].SetTreeFocusRouting(
                () => FocusTraversal.TreeTarget(_treeDisplayMode, _paneTreeVisibility[paneIndex]),
                () => _sharedTreeHost?.HasNativeFocus() == true,
                () => _sharedTreeHost?.FocusTree() == true);
        }
        ApplyTreeDisplayMode();
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (_fileViewFocusDesired && IsActive) ActivePane.FocusCurrentView();
            }));
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
        SourceInitialized += (_, _) =>
        {
            RestoreSavedWindowPlacement();
            try { MouseWheelFocusMessageHook.Register(this, _mouseWheelFocusEnabled); }
            catch (Exception ex)
            {
                SetMouseWheelFocusEnabled(false);
                try { _mouseWheelFocusPreferenceStore.Save(false); }
                catch (Exception saveError) { ex = new AggregateException(ex, saveError); }
                MouseWheelFocusMessageHook.Register(this, false);
                EnvironmentStatus.Text = ex.Message;
            }
            if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source)
                _systemSettingChangeHook = new SystemSettingChangeHook(source, RefreshEnvironment,
                    RefreshFollowedWindowsHiddenItems);
            ApplyWindowFrameTheme();
        };
        StateChanged += (_, _) =>
        {
            if (_windowPlacementReady && _fullScreenRestoreState is null && WindowState != WindowState.Minimized)
                _lastNonMinimizedMaximized = WindowState == WindowState.Maximized;
        };
        Closing += (_, _) =>
        {
            _viewPollTimer.Stop();
            _fileCommandRefreshTimer.Stop();
            _fileCommandRefreshGate.Cancel();
            SaveSession();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _saveTimer.Stop();
            foreach (var pane in _panes) pane.DisposeViews();
            try { _sharedTreeHost?.Dispose(); }
            catch (Exception ex) { Trace.WriteLine($"Shared tree cleanup: {ex.GetType().Name} (0x{ex.HResult:X8})"); }
            MouseWheelFocusMessageHook.Unregister(this);
            if (_themeController is not null)
                _themeController.ThemeChanged -= ThemeController_ThemeChanged;
            _windowOperationStop.Cancel();
            _systemSettingChangeHook?.Dispose();
        };
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
        RestoreFromMinimized();
        Activate();
        return true;
    }

    internal void RestoreFromMinimized()
    {
        if (WindowState != WindowState.Minimized) return;
        // Both single-instance activation routes used Normal and discarded the
        // pre-minimize maximized state. Immersive F11 uses a normal borderless
        // window and keeps its separate return state until the user exits F11.
        WindowState = _fullScreenRestoreState is null && _lastNonMinimizedMaximized
            ? WindowState.Maximized : WindowState.Normal;
    }

    internal void CascadeFrom(MainWindow source)
    {
        if (HasSavedWindowPlacement) return;
        // All new-window routes previously clamped WPF coordinates to the
        // primary work area, pulling windows away from a secondary monitor.
        // Wait for a native target handle, and keep this operation in pixels.
        SourceInitialized += Position;
        void Position(object? sender, EventArgs args)
        {
            SourceInitialized -= Position;
            try
            {
                var sourceHandle = new WindowInteropHelper(source).Handle;
                if (sourceHandle != 0)
                    WindowCascade.Place(new WindowInteropHelper(this).Handle, sourceHandle);
            }
            catch (Exception ex) { EnvironmentStatus.Text = $"新窗口定位失败：{ex.Message}"; }
        }
    }

    private void SetActivePane(PaneControl pane)
    {
        var changed = !ReferenceEquals(ActivePane, pane);
        ActivePane = pane;
        UpdateActivePaneVisuals();
        var index = Array.IndexOf(_panes, pane);
        if (index >= 0 && index < LayoutPreset.Find(_layout).Slots.Length)
            _lastFocusedPaneByLayout[_layout] = index;
        if (changed) SynchronizeSharedTree();
        UpdateShellPaneToggles();
        if (changed) ScheduleFileCommandRefresh(pane);
        if (changed) ScheduleSave();
    }

    private void UpdateActivePaneVisuals()
    {
        foreach (var pane in _panes)
            pane.SetActivePaneVisual(ReferenceEquals(pane, ActivePane));
    }

    private void ViewMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        UpdateShellPaneToggles();
        UpdateTabStripVisibilityMenu();
    }

    private void MouseWheelFocus_Click(object sender, RoutedEventArgs e)
    {
        var next = MouseWheelFocusMenu.IsChecked;
        try
        {
            _mouseWheelFocusPreferenceStore.Save(next);
            MouseWheelFocusMessageHook.SetEnabled(next);
        }
        catch (Exception ex)
        {
            MouseWheelFocusMenu.IsChecked = _mouseWheelFocusEnabled;
            try { _mouseWheelFocusPreferenceStore.Save(_mouseWheelFocusEnabled); }
            catch (Exception saveError) { ex = new AggregateException(ex, saveError); }
            EnvironmentStatus.Text = ex.Message;
        }
    }

    internal bool TryRetargetMouseWheel(ref MSG message, nint hovered)
    {
        var pane = _panes.FirstOrDefault(candidate => candidate.ContainsMouseWheelShellTarget(hovered));
        var sharedTreeTarget = _treeDisplayMode == TreeDisplayMode.Shared &&
            _sharedTreeHost?.ContainsNativeWindow(hovered) == true;
        if (!MouseWheelFocusRouter.TryRetarget(unchecked((uint)message.message),
                unchecked((nuint)message.wParam.ToInt64()), _mouseWheelFocusEnabled,
                message.hwnd, hovered, pane is not null || sharedTreeTarget, out var target))
            return false;

        message.hwnd = target;
        if (pane is not null) SetActivePane(pane);
        return true;
    }

    internal void SetMouseWheelFocusEnabled(bool enabled)
    {
        _mouseWheelFocusEnabled = enabled;
        MouseWheelFocusMenu.IsChecked = enabled;
    }

    private void HiddenItemsMenu_SubmenuOpened(object sender, RoutedEventArgs e) => UpdateHiddenItemsPreferenceMenu();

    private void UpdateHiddenItemsPreferenceMenu()
    {
        HiddenItemsFollowWindowsMenu.IsChecked = _hiddenItemsPreference == HiddenItemsPreference.FollowWindows;
        HiddenItemsShowMenu.IsChecked = _hiddenItemsPreference == HiddenItemsPreference.Show;
        HiddenItemsHideMenu.IsChecked = _hiddenItemsPreference == HiddenItemsPreference.Hide;
    }

    private void HiddenItemsPreference_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } ||
            !Enum.TryParse<HiddenItemsPreference>(value, out var preference)) return;
        try
        {
            _hiddenItemsPreferenceStore.Save(preference);
            _hiddenItemsPreference = preference;
            UpdateHiddenItemsPreferenceMenu();
            var failures = _panes.SelectMany(pane => pane.SetHiddenItemsPreference(preference)).ToArray();
            EnvironmentStatus.Text = failures.Length == 0
                ? preference switch
                {
                    HiddenItemsPreference.FollowWindows => "隐藏项目显示已跟随 Windows 设置。",
                    HiddenItemsPreference.Show => "已显示普通隐藏项目。",
                    HiddenItemsPreference.Hide => "已隐藏普通隐藏项目。",
                    _ => "隐藏项目设置已更新。"
                }
                : $"隐藏项目设置已保存，但部分视图刷新失败：{string.Join("；", failures)}";
        }
        catch (Exception ex)
        {
            UpdateHiddenItemsPreferenceMenu();
            EnvironmentStatus.Text = $"隐藏项目设置保存失败：{ex.Message}";
        }
    }

    private void ViewPresetsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Name: "SavedViewPresetsMenu" } menu)
            PopulateSavedViewPresets(menu);
    }

    private void PopulateSavedViewPresets(MenuItem menu)
    {
        menu.Items.Clear();
        if (_viewPresetStore.Presets.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "（无已保存预设）", IsEnabled = false });
            return;
        }

        foreach (var preset in _viewPresetStore.Presets)
        {
            var entry = new MenuItem { Header = preset.Name };
            var apply = new MenuItem { Header = "应用到当前标签" };
            apply.Click += (_, _) =>
            {
                if (!ActivePane.ApplyViewPreset(preset))
                    EnvironmentStatus.Text = "查看预设未能完整应用；请检查当前 Shell 视图状态。";
                else EnvironmentStatus.Text = $"已将查看预设“{preset.Name}”应用到当前标签。";
            };
            var delete = new MenuItem { Header = "删除预设" };
            delete.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"删除查看预设“{preset.Name}”？", "删除查看预设",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK)
                    return;
                try
                {
                    _viewPresetStore.DeletePreset(preset.Name);
                    PopulateSavedViewPresets(menu);
                    EnvironmentStatus.Text = $"已删除查看预设“{preset.Name}”。";
                }
                catch (Exception ex) { EnvironmentStatus.Text = $"删除查看预设失败：{ex.Message}"; }
            };
            entry.Items.Add(apply);
            entry.Items.Add(delete);
            menu.Items.Add(entry);
        }
    }

    private void SaveViewPreset_Click(object sender, RoutedEventArgs e)
        => SaveViewPreset(ActivePane);

    private void SaveViewPreset(PaneControl pane)
    {
        ViewState current;
        try { current = pane.CaptureCurrentViewState(); }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"无法读取当前标签的查看设置：{ex.Message}";
            return;
        }
        if (current.DisplayColumns is not { } columns || !ColumnLayout.IsValid(columns))
        {
            EnvironmentStatus.Text = "当前标签尚无完整的 Shell 列布局，无法保存查看预设。";
            return;
        }
        var name = PromptViewPresetName();
        if (name is null) return;
        var existing = _viewPresetStore.Presets.FirstOrDefault(preset =>
            preset.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is not null && MessageBox.Show(this, $"覆盖已有查看预设“{existing.Name}”？",
                "覆盖查看预设", MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK) return;

        try
        {
            _viewPresetStore.SavePreset(new ViewPreset(name, current.Mode, current.IconSize, columns));
            PopulateSavedViewPresets(SavedViewPresetsMenu);
            EnvironmentStatus.Text = $"已保存查看预设“{name.Trim()}”。";
        }
        catch (Exception ex) { EnvironmentStatus.Text = $"保存查看预设失败：{ex.Message}"; }
    }

    private void ShowViewPresetContextMenu(PaneControl pane, ShellMenuPoint point)
    {
        SetActivePane(pane);
        var localPoint = pane.PointFromScreen(new Point(point.X, point.Y));
        var menu = new ContextMenu
        {
            PlacementTarget = pane,
            Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint,
            HorizontalOffset = localPoint.X,
            VerticalOffset = localPoint.Y
        };
        var save = new MenuItem { Header = "保存当前查看和列设置…" };
        save.Click += (_, _) => SaveViewPreset(pane);
        menu.Items.Add(save);
        menu.Items.Add(new Separator());
        if (_viewPresetStore.Presets.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "（无已保存预设）", IsEnabled = false });
        }
        else
        {
            foreach (var preset in _viewPresetStore.Presets)
            {
                var apply = new MenuItem { Header = preset.Name };
                apply.Click += (_, _) =>
                {
                    if (!pane.ApplyViewPreset(preset))
                        EnvironmentStatus.Text = "查看预设未能完整应用；请检查当前 Shell 视图状态。";
                    else EnvironmentStatus.Text = $"已将查看预设“{preset.Name}”应用到当前标签。";
                };
                menu.Items.Add(apply);
            }
        }
        menu.IsOpen = true;
    }

    private string? PromptViewPresetName()
    {
        var dialog = new Window
        {
            Title = "保存查看预设", Owner = this, Width = 390, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ApplicationThemeWindowBinding.Attach(dialog, _themeController);
        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = "预设名称（最多 64 个字符）：" });
        var input = new TextBox { Margin = new Thickness(0, 8, 0, 12), MinWidth = 350 };
        AutomationProperties.SetName(input, "查看预设名称");
        content.Children.Add(input);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right
        };
        var save = new Button { Content = "保存", IsDefault = true, MinWidth = 76, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => dialog.DialogResult = true;
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 76 };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        content.Children.Add(buttons);
        dialog.Content = content;
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void PreviewPaneMenu_Click(object sender, RoutedEventArgs e) =>
        SetPreviewPaneVisible(PreviewPaneMenu.IsChecked);

    private void PreviewPaneToggle_Click(object sender, RoutedEventArgs e) =>
        SetPreviewPaneVisible(PreviewPaneToggle.IsChecked == true);

    private void DetailsPaneMenu_Click(object sender, RoutedEventArgs e) =>
        SetDetailsPaneVisible(DetailsPaneMenu.IsChecked);

    private void DetailsPaneToggle_Click(object sender, RoutedEventArgs e) =>
        SetDetailsPaneVisible(DetailsPaneToggle.IsChecked == true);

    private void SetPreviewPaneVisible(bool visible)
    {
        if (!ActivePane.SetPreviewPaneVisible(visible))
            EnvironmentStatus.Text = "无法切换预览窗格；当前文件视图保持不变。";
        UpdateShellPaneToggles();
    }

    private void SetDetailsPaneVisible(bool visible)
    {
        if (!ActivePane.SetDetailsPaneVisible(visible))
            EnvironmentStatus.Text = "无法切换详情窗格；当前文件视图保持不变。";
        UpdateShellPaneToggles();
    }

    private void UpdatePreviewPaneToggle()
    {
        if (_panes.Length == 0 || ActivePane is null) return;
        var visible = ActivePane.PreviewPaneVisible;
        PreviewPaneMenu.IsChecked = visible;
        PreviewPaneToggle.IsChecked = visible;
        var detailsVisible = ActivePane.DetailsPaneVisible;
        DetailsPaneMenu.IsChecked = detailsVisible;
        DetailsPaneToggle.IsChecked = detailsVisible;
    }

    private void UpdateShellPaneToggles() => UpdatePreviewPaneToggle();

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
        LayoutMenu.Items.Add(new Separator());
        _resetLayoutProportionsMenu.Click += (_, _) =>
        {
            SaveLayoutProportions(_layout, LayoutProportions.Equal);
            BuildLayout();
        };
        LayoutMenu.Items.Add(_resetLayoutProportionsMenu);
    }

    private void BuildDefaultViewOptions()
    {
        ViewStyleMenu.Populate(CurrentViewMenu, style => ActivePane.SetViewStyle(style));
        CurrentViewMenu.SubmenuOpened += (_, e) =>
        {
            if (e.OriginalSource != CurrentViewMenu) return;
            var pane = ActivePane;
            var style = pane.ReadViewStyle();
            ViewStyleMenu.Update(CurrentViewMenu, ActivePane == pane ? style : null);
        };
        foreach (var style in ViewStyle.All)
        {
            var item = new MenuItem { Header = style.Name, IsCheckable = true };
            item.Click += (_, _) =>
            {
                _defaultViewMode = style.Mode;
                _defaultIconSize = style.IconSize;
                UpdateDefaultViewChecks();
                ScheduleSave();
            };
            DefaultViewMenu.Items.Add(item);
            _defaultViewMenuItems[style] = item;
        }
        DefaultViewMenu.Items.Add(_customDefaultViewMenu);
        UpdateDefaultViewChecks();
    }

    private void UpdateDefaultViewChecks()
    {
        var size = ViewStyle.NativeIconSize(_defaultViewMode, _defaultIconSize);
        var matched = false;
        foreach (var (style, item) in _defaultViewMenuItems)
        {
            item.IsChecked = style.Mode == _defaultViewMode && style.IconSize == size;
            matched |= item.IsChecked;
        }
        // Preserve legacy/default-provider sizes rather than silently choosing a preset.
        _customDefaultViewMenu.IsChecked = !matched;
        _customDefaultViewMenu.Visibility = matched ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TreeMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } ||
            !Enum.TryParse<TreeDisplayMode>(value, out var mode)) return;
        var oldMode = _treeDisplayMode;
        _treeDisplayMode = mode;
        try
        {
            ApplyTreeDisplayMode();
            UpdateTreeModeMenu();
            ScheduleSave();
            EnvironmentStatus.Text = mode switch
            {
                TreeDisplayMode.Shared => "已切换为共享目录树。",
                TreeDisplayMode.PerPane => "已切换为每窗格独立目录树。",
                _ => "已关闭目录树。"
            };
        }
        catch (Exception ex)
        {
            _treeDisplayMode = oldMode;
            ApplyTreeDisplayMode();
            UpdateTreeModeMenu();
            EnvironmentStatus.Text = ex.Message;
        }
    }

    private void UpdateTreeModeMenu()
    {
        TreeModeSharedMenu.IsChecked = _treeDisplayMode == TreeDisplayMode.Shared;
        TreeModePerPaneMenu.IsChecked = _treeDisplayMode == TreeDisplayMode.PerPane;
        TreeModeNoneMenu.IsChecked = _treeDisplayMode == TreeDisplayMode.None;
        TreeSelectionAutoExpandMenu.IsChecked = _treeSelectionAutoExpand;
    }

    private void TreeSelectionAutoExpand_Click(object sender, RoutedEventArgs e)
    {
        var next = TreeSelectionAutoExpandMenu.IsChecked;
        try
        {
            _treeSelectionAutoExpandPreferenceStore.Save(next);
            _treeSelectionAutoExpand = next;
            foreach (var pane in _panes) pane.SetAutoExpandTreeSelection(next);
            EnvironmentStatus.Text = next ? "已启用从文件列表自动展开目录树。" : "已关闭从文件列表自动展开目录树。";
        }
        catch (Exception ex)
        {
            TreeSelectionAutoExpandMenu.IsChecked = _treeSelectionAutoExpand;
            EnvironmentStatus.Text = $"目录树偏好保存失败：{ex.Message}";
        }
    }

    private void Pane_SelectedFolderForTree(PaneControl pane, string identity)
    {
        var sharedTreeTarget = _treeDisplayMode == TreeDisplayMode.Shared && ReferenceEquals(pane, ActivePane);
        var paneTreeTarget = _treeDisplayMode == TreeDisplayMode.PerPane && pane.TreeIsVisible;
        if (!TreeSelectionAutoExpand.ShouldSync(_treeSelectionAutoExpand,
                currentPane: pane.IsVisible && (_treeDisplayMode != TreeDisplayMode.Shared || sharedTreeTarget),
                treeVisible: sharedTreeTarget || paneTreeTarget,
                selectedCount: 1, selectedFolder: true)) return;

        if (sharedTreeTarget) _sharedTreeHost?.SetSelectedFolderLocation(identity);
        else if (paneTreeTarget) pane.ExpandTreeToSelectedFolder(identity);
    }

    private void ApplyTreeDisplayMode()
    {
        for (var index = 0; index < _panes.Length; index++)
        {
            _panes[index].SetTreeVisible(_treeDisplayMode == TreeDisplayMode.PerPane && _paneTreeVisibility[index]);
            _panes[index].SetPaneTreeButtonVisible(_treeDisplayMode == TreeDisplayMode.PerPane);
        }
        if (_treeDisplayMode == TreeDisplayMode.Shared)
        {
            if (_sharedTreeHost is null)
            {
                _sharedTreeHost = new ShellTreeHost();
                _sharedTreeHost.Activated += () => SetActivePane(ActivePane);
                _sharedTreeHost.ShortcutHandler = shortcut => ExecuteShortcut(ActivePane, shortcut);
                _sharedTreeHost.Error += message => EnvironmentStatus.Text = message;
                _sharedTreeHost.DirectorySelected += path =>
                {
                    try { ActivePane.NavigateTo(path); }
                    catch (Exception ex) { EnvironmentStatus.Text = ex.Message; }
                };
                _sharedTreeHost.FolderMiddleClicked += path => ActivePane.OpenPathInNewTab(path);
                SharedTreeContainer.Child = _sharedTreeHost;
            }
            SharedTreeContainer.Visibility = Visibility.Visible;
            SharedTreeColumn.MinWidth = NavigationTreeWidths.Minimum;
            SharedTreeColumn.MaxWidth = NavigationTreeWidths.Maximum;
            SharedTreeColumn.Width = new GridLength(_treeWidths.Shared);
            SharedTreeSplitter.Visibility = Visibility.Visible;
            SynchronizeSharedTree();
        }
        else
        {
            _sharedTreeHost?.CancelSynchronization();
            SharedTreeContainer.Visibility = Visibility.Collapsed;
            SharedTreeColumn.MinWidth = 0;
            SharedTreeColumn.Width = new GridLength(0);
            SharedTreeSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void SharedTreeSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // GridSplitter commits the fixed pixel width before raising DragCompleted;
        // ActualWidth can still be the previous layout pass at this point.
        if (!SharedTreeColumn.Width.IsAbsolute) return;
        var width = SharedTreeColumn.Width.Value;
        if (!NavigationTreeWidths.IsWidth(width)) return;
        _treeWidths = _treeWidths with { Shared = width };
        SaveTreeWidths();
    }

    private void SaveTreeWidths()
    {
        try { _treeWidthsStore.Save(_treeWidths); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        { EnvironmentStatus.Text = ex.Message; }
    }

    private void SynchronizeSharedTree()
    {
        if (_treeDisplayMode != TreeDisplayMode.Shared || _sharedTreeHost is null) return;
        var target = ShellTreeLocationTarget.For(
            ActivePane.IsCurrentFileSystemLocation, ActivePane.CurrentPath);
        if (target.UseVirtualRoot) _sharedTreeHost.SetVirtualLocation();
        else _sharedTreeHost.SetDirectory(target.FileSystemPath!);
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

    private void TabStripVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value }) return;
        var next = value.Equals("Always", StringComparison.Ordinal);
        try
        {
            _tabStripVisibilityStore.Save(next);
            _alwaysShowTabStrip = next;
            ApplyTabStripVisibility();
            EnvironmentStatus.Text = next ? "标签栏始终显示。" : "标签栏仅在多个标签时显示。";
        }
        catch (Exception ex)
        {
            ApplyTabStripVisibility();
            EnvironmentStatus.Text = $"标签栏显示设置保存失败：{ex.Message}";
        }
    }

    private void MultilineTabHeaders_Click(object sender, RoutedEventArgs e)
    {
        var next = MultilineTabHeadersMenu.IsChecked;
        try
        {
            _multilineTabHeadersStore.Save(next);
            _multilineTabHeaders = next;
            ApplyMultilineTabHeaders();
            EnvironmentStatus.Text = next ? "标签栏已改为多行显示。" : "标签栏已恢复单行滚动显示。";
        }
        catch (Exception ex)
        {
            ApplyMultilineTabHeaders();
            EnvironmentStatus.Text = $"多行标签栏设置保存失败：{ex.Message}";
        }
    }

    private void TabHoverActivationMenu_SubmenuOpened(object sender, RoutedEventArgs e) =>
        UpdateTabHoverActivationMenu();

    private void TabHoverActivation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int delayMilliseconds }) return;
        try
        {
            _tabHoverActivationPreference.Save(delayMilliseconds);
            _tabHoverActivationDelayMilliseconds = delayMilliseconds;
            ApplyTabHoverActivationDelay();
            EnvironmentStatus.Text = delayMilliseconds == 0
                ? "已关闭拖动悬停标签自动切换。"
                : $"拖动悬停 {delayMilliseconds / 1000d:0.##} 秒后切换标签。";
        }
        catch (Exception ex)
        {
            UpdateTabHoverActivationMenu();
            EnvironmentStatus.Text = $"标签悬停设置保存失败：{ex.Message}";
        }
    }

    private void NewTabUsesCurrentFolder_Click(object sender, RoutedEventArgs e)
    {
        var next = NewTabUsesCurrentFolderMenu.IsChecked;
        try
        {
            _newTabLocationPreference.Save(next);
            _newTabUsesCurrentFolder = next;
            ApplyNewTabLocationPreference();
            EnvironmentStatus.Text = next
                ? "新建标签将使用当前窗格的文件夹。"
                : "新建标签将从用户文件夹开始。";
        }
        catch (Exception ex)
        {
            NewTabUsesCurrentFolderMenu.IsChecked = _newTabUsesCurrentFolder;
            EnvironmentStatus.Text = $"新标签目录设置保存失败：{ex.Message}";
        }
    }

    private void StatusBar_Click(object sender, RoutedEventArgs e)
    {
        var next = StatusBarMenu.IsChecked;
        try
        {
            _statusBarVisibilityStore.Save(next);
            _showStatusBar = next;
            ApplyStatusBarVisibility();
            EnvironmentStatus.Text = next ? "已显示状态栏。" : "已隐藏状态栏。";
        }
        catch (Exception ex)
        {
            StatusBarMenu.IsChecked = _showStatusBar;
            EnvironmentStatus.Text = $"状态栏显示设置保存失败：{ex.Message}";
        }
    }

    private void ApplyStatusBarVisibility()
    {
        StatusBarMenu.IsChecked = _showStatusBar;
        foreach (var pane in _panes) pane.SetStatusBarVisible(_showStatusBar);
    }

    private void StatusBarSelectionSize_Click(object sender, RoutedEventArgs e)
    {
        var next = StatusBarSelectionSizeMenu.IsChecked;
        try
        {
            _statusBarSelectionSizeStore.Save(next);
            _showSelectionSizeInStatusBar = next;
            foreach (var pane in _panes) pane.SetStatusBarSelectionSizeVisible(next);
            EnvironmentStatus.Text = next ? "状态栏将显示可取得的选中项目大小。" : "状态栏将只显示项目数量。";
        }
        catch (Exception ex)
        {
            StatusBarSelectionSizeMenu.IsChecked = _showSelectionSizeInStatusBar;
            EnvironmentStatus.Text = $"状态栏选中大小设置保存失败：{ex.Message}";
        }
    }

    private void StatusBarFolderSizeWhenEmpty_Click(object sender, RoutedEventArgs e)
    {
        var next = StatusBarFolderSizeWhenEmptyMenu.IsChecked;
        try
        {
            _statusBarFolderSizeWhenEmptyStore.Save(next);
            _showFolderSizeWhenEmpty = next;
            foreach (var pane in _panes) pane.SetStatusBarFolderSizeWhenEmpty(next);
            EnvironmentStatus.Text = next
                ? "状态栏将在没有选中项目时显示可取得的当前目录直接子项大小。"
                : "已关闭状态栏的当前目录大小读取。";
        }
        catch (Exception ex)
        {
            StatusBarFolderSizeWhenEmptyMenu.IsChecked = _showFolderSizeWhenEmpty;
            EnvironmentStatus.Text = $"状态栏当前目录大小设置保存失败：{ex.Message}";
        }
    }

    private void SelectionCheckBoxes_Click(object sender, RoutedEventArgs e)
    {
        var next = SelectionCheckBoxesMenu.IsChecked;
        try
        {
            _selectionCheckBoxesStore.Save(next);
            _selectionCheckBoxes = next;
            var failures = _panes.SelectMany(pane => pane.SetSelectionCheckBoxes(next)).ToArray();
            EnvironmentStatus.Text = failures.Length == 0
                ? next ? "已为文件视图启用复选框选择。" : "已关闭文件视图复选框选择。"
                : $"设置已保存，但有 {failures.Length} 个标签未能立即应用：{string.Join("；", failures.Take(3))}";
        }
        catch (Exception ex)
        {
            SelectionCheckBoxesMenu.IsChecked = _selectionCheckBoxes;
            EnvironmentStatus.Text = $"复选框选择设置保存失败：{ex.Message}";
        }
    }

    private void FullRowSelection_Click(object sender, RoutedEventArgs e)
    {
        var next = FullRowSelectionMenu.IsChecked == true;
        try
        {
            _fullRowSelectionStore.Save(next);
            _fullRowSelection = next;
            var failures = _panes.SelectMany(pane => pane.SetFullRowSelection(next)).ToArray();
            EnvironmentStatus.Text = failures.Length == 0
                ? next ? "已为文件视图启用整行选择。" : "已关闭文件视图整行选择。"
                : $"整行选择设置已保存，但有 {failures.Length} 个标签未能立即应用：{string.Join("；", failures.Take(3))}";
        }
        catch (Exception ex)
        {
            FullRowSelectionMenu.IsChecked = _fullRowSelection == true;
            EnvironmentStatus.Text = $"整行选择设置保存失败：{ex.Message}";
        }
    }

    private void ApplyTabStripVisibility()
    {
        foreach (var pane in _panes) pane.SetAlwaysShowTabStrip(_alwaysShowTabStrip);
        UpdateTabStripVisibilityMenu();
    }

    private void ApplyMultilineTabHeaders()
    {
        MultilineTabHeadersMenu.IsChecked = _multilineTabHeaders;
        foreach (var pane in _panes) pane.SetMultilineTabHeaders(_multilineTabHeaders);
    }

    private void ApplyTabHoverActivationDelay()
    {
        foreach (var pane in _panes)
            pane.SetTabHoverActivationDelay(_tabHoverActivationDelayMilliseconds);
        UpdateTabHoverActivationMenu();
    }

    private void ApplyNewTabLocationPreference()
    {
        NewTabUsesCurrentFolderMenu.IsChecked = _newTabUsesCurrentFolder;
        foreach (var pane in _panes)
            pane.SetNewTabUsesCurrentFolder(_newTabUsesCurrentFolder);
    }

    private void UpdateTabHoverActivationMenu()
    {
        foreach (var item in TabHoverActivationMenu.Items.OfType<MenuItem>())
            item.IsChecked = int.TryParse(item.Tag?.ToString(), out var delay) &&
                delay == _tabHoverActivationDelayMilliseconds;
    }

    private void UpdateTabStripVisibilityMenu()
    {
        AlwaysShowTabsMenu.IsChecked = _alwaysShowTabStrip;
        ShowTabsWhenMultipleMenu.IsChecked = !_alwaysShowTabStrip;
    }

    private void ThemePreference_Click(object sender, RoutedEventArgs e)
    {
        if (_themeController is null || sender is not MenuItem { Tag: string value } ||
            !Enum.TryParse<ThemePreference>(value, out var preference)) return;
        try
        {
            _themeController.SetPreference(preference);
            EnvironmentStatus.Text = preference switch
            {
                ThemePreference.System => "主题已设置为跟随系统。",
                ThemePreference.Light => "已切换为浅色主题。",
                ThemePreference.Dark => "已切换为深色主题。",
                _ => "主题已更新。"
            };
        }
        catch (Exception ex)
        {
            ApplyThemePreferenceMenuState();
            EnvironmentStatus.Text = $"主题配置保存失败：{ex.Message}";
        }
    }

    private void WindowsAccent_Click(object sender, RoutedEventArgs e)
    {
        if (_themeController is null) return;
        try
        {
            _themeController.SetCustomAccentColor(null);
            EnvironmentStatus.Text = "强调色已设置为跟随 Windows。";
        }
        catch (Exception ex)
        {
            ApplyThemePreferenceMenuState();
            EnvironmentStatus.Text = $"强调色配置保存失败：{ex.Message}";
        }
    }

    private void CustomAccent_Click(object sender, RoutedEventArgs e)
    {
        if (_themeController is null) return;
        var current = _themeController.CurrentColors?.Accent ?? ThemePalette.DefaultAccent;
        var dialog = new AccentColorDialog(current) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedColor is not { } selected)
        {
            ApplyThemePreferenceMenuState();
            return;
        }
        try
        {
            _themeController.SetCustomAccentColor(selected);
            EnvironmentStatus.Text = $"自定义强调色已应用：#{selected.R:X2}{selected.G:X2}{selected.B:X2}";
        }
        catch (Exception ex)
        {
            ApplyThemePreferenceMenuState();
            EnvironmentStatus.Text = $"强调色配置保存失败：{ex.Message}";
        }
    }

    private void ThemeController_ThemeChanged(object? sender, EventArgs e)
    {
        ApplyThemePreferenceMenuState();
        ApplyWindowFrameTheme();
        _sharedTreeHost?.RefreshTheme();
        foreach (var pane in _panes) pane.RefreshTheme();
    }

    private void ApplyWindowFrameTheme() =>
        NativeWindowFrameTheme.Apply(new WindowInteropHelper(this).Handle, _themeController?.CurrentColors);

    private void ByteSizeFormatter_OptionsChanged(object? sender, EventArgs e)
    {
        ApplySizeDisplayMenuState();
        foreach (var pane in _panes) pane?.RefreshSizeFormatting();
    }

    private void SizeDisplayUnit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } ||
            !Enum.TryParse<SizeDisplayUnit>(value, out var unit) || !Enum.IsDefined(unit)) return;
        SaveSizeDisplayOptions(ByteSizeFormatter.Options with { Unit = unit });
    }

    private void SizeDisplayDecimals_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } || !int.TryParse(value, out var decimalPlaces)) return;
        SaveSizeDisplayOptions(ByteSizeFormatter.Options with { DecimalPlaces = decimalPlaces });
    }

    private void SizeDisplayThousandsSeparator_Click(object sender, RoutedEventArgs e) =>
        SaveSizeDisplayOptions(ByteSizeFormatter.Options with
        {
            UseThousandsSeparator = SizeThousandsSeparatorMenu.IsChecked
        });

    private void SizeDisplayRecursiveCounts_Click(object sender, RoutedEventArgs e) =>
        SaveSizeDisplayOptions(ByteSizeFormatter.Options with
        {
            ShowRecursiveCounts = SizeRecursiveCountsMenu.IsChecked
        });

    private void SaveSizeDisplayOptions(SizeDisplayOptions options)
    {
        try
        {
            _sizeDisplayPreferenceStore.Save(options);
            ByteSizeFormatter.SetOptions(options);
            EnvironmentStatus.Text = "大小显示格式已更新。";
        }
        catch (Exception ex)
        {
            ApplySizeDisplayMenuState();
            EnvironmentStatus.Text = $"大小显示配置保存失败：{ex.Message}";
        }
    }

    private void ApplySizeDisplayMenuState()
    {
        var options = ByteSizeFormatter.Options;
        SizeUnitAutoMenu.IsChecked = options.Unit == SizeDisplayUnit.Auto;
        SizeUnitBytesMenu.IsChecked = options.Unit == SizeDisplayUnit.Bytes;
        SizeUnitKbMenu.IsChecked = options.Unit == SizeDisplayUnit.Kilobytes;
        SizeUnitMbMenu.IsChecked = options.Unit == SizeDisplayUnit.Megabytes;
        SizeUnitGbMenu.IsChecked = options.Unit == SizeDisplayUnit.Gigabytes;
        SizeUnitTbMenu.IsChecked = options.Unit == SizeDisplayUnit.Terabytes;
        SizeDecimals0Menu.IsChecked = options.DecimalPlaces == 0;
        SizeDecimals1Menu.IsChecked = options.DecimalPlaces == 1;
        SizeDecimals2Menu.IsChecked = options.DecimalPlaces == 2;
        SizeDecimals3Menu.IsChecked = options.DecimalPlaces == 3;
        SizeThousandsSeparatorMenu.IsChecked = options.UseThousandsSeparator;
        SizeRecursiveCountsMenu.IsChecked = options.ShowRecursiveCounts;
    }

    private void ApplyThemePreferenceMenuState()
    {
        var preference = _themeController?.Preference ?? ThemePreference.System;
        SystemThemeMenu.IsChecked = preference == ThemePreference.System;
        LightThemeMenu.IsChecked = preference == ThemePreference.Light;
        DarkThemeMenu.IsChecked = preference == ThemePreference.Dark;
        var usesCustomAccent = _themeController?.CustomAccentColor.HasValue == true;
        WindowsAccentMenu.IsChecked = !usesCustomAccent;
        CustomAccentMenu.IsChecked = usesCustomAccent;
    }

    private void AddressBarStyle_Click(object sender, RoutedEventArgs e)
    {
        var next = ReferenceEquals(sender, ClassicAddressBarMenu);
        try
        {
            _addressBarStyleStore.Save(next);
            _classicAddressBar = next;
            ApplyAddressBarStyle();
            EnvironmentStatus.Text = next ? "已切换为经典完整路径地址栏。" : "已切换为现代面包屑地址栏。";
        }
        catch (Exception ex)
        {
            ApplyAddressBarStyle();
            EnvironmentStatus.Text = $"地址栏样式保存失败：{ex.Message}";
        }
    }

    private void ApplyAddressBarStyle()
    {
        ClassicAddressBarMenu.IsChecked = _classicAddressBar;
        ModernAddressBarMenu.IsChecked = !_classicAddressBar;
        foreach (var pane in _panes) pane.SetClassicAddressBar(_classicAddressBar);
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
                BorderThickness = new Thickness(1),
                Margin = new Thickness(1)
            };
            panel.SetResourceReference(Border.BorderBrushProperty, "ExplorerDirs.BorderBrush");
            panel.SetResourceReference(Border.BackgroundProperty, "ExplorerDirs.ControlBackgroundBrush");
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
        _fileViewFocusDesired = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (_fileViewFocusDesired && IsActive) ActivePane.FocusCurrentView();
            }));
        ScheduleSave();
    }

    private void BuildLayout()
    {
        var layout = LayoutPreset.Find(_layout);
        MinHeight = PaneLayoutHost.MinimumWindowHeight(layout);
        PaneLayoutHost.Apply(PaneGrid, _panes, layout,
            _layoutProportions.GetValueOrDefault(layout.Id, LayoutProportions.Equal),
            proportions => SaveLayoutProportions(layout.Id, proportions));
        _resetLayoutProportionsMenu.IsEnabled = layout.Slots.Length > 1;

        var remembered = _lastFocusedPaneByLayout.GetValueOrDefault(_layout, -1);
        var current = Array.IndexOf(_panes, ActivePane);
        ActivePane = _panes[LayoutPreset.ResolveActivePaneIndex(remembered, current, layout.Slots.Length)];
        SynchronizeSharedTree();
        foreach (var preset in LayoutPreset.All)
        {
            var isSelected = preset.Id == _layout;
            _layoutMenuItems[preset.Id].IsChecked = isSelected;
            _layoutButtons[preset.Id].SetResourceReference(Control.BackgroundProperty,
                isSelected ? "ExplorerDirs.SelectionBackgroundBrush" : "ExplorerDirs.ControlBackgroundBrush");
            _layoutButtons[preset.Id].SetResourceReference(Control.BorderBrushProperty,
                isSelected ? "ExplorerDirs.AccentBrush" : "ExplorerDirs.BorderBrush");
        }
    }

    private void SaveLayoutProportions(int layoutId, LayoutProportions proportions)
    {
        if (layoutId != _layout || !proportions.IsValid) return;
        _layoutProportions[layoutId] = proportions;
        // Persist user adjustments only. Window resizing can temporarily clamp
        // a pane to its minimum size and must not replace the chosen proportions.
        try { _layoutProportionsStore.Save(_layoutProportions); }
        catch (Exception ex) { EnvironmentStatus.Text = $"布局比例仅在本次会话生效，保存失败：{ex.Message}"; }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is MenuItem && _fullScreenRestoreState is null) return;
        if (IsAnyShellRenameEditorFocused()) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var modifiers = (int)Keyboard.Modifiers;
        var shortcut = KeyboardShortcuts.ResolveTabHeaderArrow(virtualKey, modifiers,
            Keyboard.FocusedElement is TabItem);
        if (shortcut == ExplorerShortcut.None)
            shortcut = KeyboardShortcuts.Resolve(virtualKey, modifiers);
        if (shortcut == ExplorerShortcut.Refresh && ActivePane.IsQuickFilterInputFocused)
        {
            ActivePane.ApplyQuickFilter();
            e.Handled = true;
            return;
        }
        if (shortcut == ExplorerShortcut.Parent && Keyboard.FocusedElement is TextBoxBase)
            return;
        if (Keyboard.FocusedElement is TextBoxBase && KeyboardShortcuts.RequiresFileViewFocus(shortcut))
            return;
        e.Handled = ExecuteShortcut(ActivePane, shortcut);
    }

    private bool IsAnyShellRenameEditorFocused() =>
        _panes.Any(pane => pane.IsShellRenameEditorFocused);

    private bool ExecuteShortcut(PaneControl source, ExplorerShortcut shortcut)
    {
        if (shortcut == ExplorerShortcut.None) return false;
        if (shortcut == ExplorerShortcut.AutoFitColumns) return false; // HwndHost forwards this Shell action after TranslateAccelerator declines it.
        if (shortcut == ExplorerShortcut.ExpandTree)
        {
            if (!source.ExpandTreeToCurrent()) return false;
            SetActivePane(source);
            return true;
        }
        SetActivePane(source);
        if (shortcut is >= ExplorerShortcut.View1 and <= ExplorerShortcut.View8)
        {
            source.SetViewStyle(ViewStyle.All[(int)shortcut - (int)ExplorerShortcut.View1]);
            return true;
        }
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
            case ExplorerShortcut.AddressTree: source.HandleAddressTreeShortcut(); break;
            case ExplorerShortcut.NewTab: source.NewTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.NewWindow: _createWindow?.Invoke(this); break;
            case ExplorerShortcut.QuickFolderMenu:
                QuickLinksMenu.Focus();
                QuickLinksMenu.IsSubmenuOpen = true;
                break;
            case ExplorerShortcut.ProgramStarter:
                OpenProgramStarterWindow();
                break;
            case ExplorerShortcut.RestartApplication:
                RequestRestartApplication();
                break;
            case ExplorerShortcut.ActivateMenu:
                if (MainMenu.Items.Count == 0 || MainMenu.Items[0] is not MenuItem firstMenu) return false;
                firstMenu.Focus();
                firstMenu.IsSubmenuOpen = true;
                break;
            case ExplorerShortcut.CloseTab: source.CloseCurrentTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.DuplicateTab: source.DuplicateCurrentTab(); source.FocusCurrentView(); break;
            case ExplorerShortcut.ReopenTab:
                if (source.IsShellRenameEditorFocused) break;
                TabRecoveryShortcut.Dispatch(source.HasFileViewFocus && Keyboard.FocusedElement is not TextBoxBase,
                    source.TryOpenSelectedFolderInNewTab, source.ReopenClosedTab);
                source.FocusCurrentView();
                break;
            case ExplorerShortcut.NextTab: source.SelectRelativeTab(false); break;
            case ExplorerShortcut.PreviousTab: source.SelectRelativeTab(true); break;
            case ExplorerShortcut.OpenSelectionInNewTab:
                return FileViewShortcutDispatch.TryOpenSelectionInNewTab(
                    source.HasFileViewFocus, source.TryOpenSelectedFolderInNewTab);
            case ExplorerShortcut.NewFolder: CreateNewFolder(source); break;
            case ExplorerShortcut.InvertSelection:
                _fileViewFocusDesired = true;
                source.ApplySelectionCommand(ShellSelectionCommand.Invert);
                break;
            case ExplorerShortcut.PasteSpecial:
                if (!source.HasFileViewFocus) return false;
                _fileViewFocusDesired = true;
                FileCommandsMenu.IsSubmenuOpen = true;
                PasteSpecialMenu.IsSubmenuOpen = true;
                break;
            case ExplorerShortcut.Refresh: source.RefreshCurrent(); break;
            case ExplorerShortcut.CalculateDirectorySize: source.ToggleRecursiveDirectorySize(); break;
            case ExplorerShortcut.Search: source.FocusSearch(); break;
            case ExplorerShortcut.SearchSelectedFolder:
                if (!source.HasFileViewFocus) return false;
                source.FocusSearchInSelectedFolder();
                break;
            case ExplorerShortcut.QuickFilter: source.FocusQuickFilter(); break;
            case ExplorerShortcut.TogglePreviewPane:
                SetPreviewPaneVisible(!source.PreviewPaneVisible);
                break;
            case ExplorerShortcut.ToggleDetailsPane:
                SetDetailsPaneVisible(!source.DetailsPaneVisible);
                break;
            case ExplorerShortcut.ToggleFullScreen:
                ToggleFullScreen();
                break;
        }
        return true;
    }

    private void ToggleFullScreen()
    {
        if (_fullScreenRestoreState is { } saved)
        {
            RestoreWindowPresentation(saved);
            _fullScreenRestoreState = null;
            _fullScreenWindowPlacement = null;
            return;
        }

        var restoreBounds = WindowState == WindowState.Maximized ? RestoreBounds :
            new Rect(Left, Top, ActualWidth, ActualHeight);
        var original = new FullScreenPresentation(WindowState, WindowStyle, ResizeMode, Topmost,
            restoreBounds.Left, restoreBounds.Top, restoreBounds.Width, restoreBounds.Height,
            MainMenu.Visibility, MainToolBar.Visibility, EnvironmentStatus.Visibility);
        var fullScreen = original.EnterFullScreen();

        try
        {
            _fullScreenWindowPlacement = NativeWindowPlacement.Capture(new WindowInteropHelper(this).Handle,
                _lastNonMinimizedMaximized);
            _fullScreenRestoreState = original;
            WindowState = fullScreen.WindowState;
            WindowStyle = fullScreen.WindowStyle;
            ResizeMode = fullScreen.ResizeMode;
            MainMenu.Visibility = fullScreen.MenuVisibility;
            MainToolBar.Visibility = fullScreen.ToolBarVisibility;
            EnvironmentStatus.Visibility = fullScreen.StatusBarVisibility;
            FullScreenMenu.IsChecked = true;

            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var bounds = monitorInfo.Monitor;
            if (!SetWindowPos(hwnd, 0, bounds.Left, bounds.Top, bounds.Right - bounds.Left,
                    bounds.Bottom - bounds.Top, SwpNoZOrder | SwpNoActivate | SwpFrameChanged))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception ex)
        {
            RestoreWindowPresentation(original);
            _fullScreenRestoreState = null;
            _fullScreenWindowPlacement = null;
            FullScreenMenu.IsChecked = false;
            EnvironmentStatus.Text = $"无法切换全屏：{ex.Message}";
        }
    }

    private void RestoreWindowPresentation(FullScreenPresentation saved)
    {
        WindowState = WindowState.Normal;
        WindowStyle = saved.WindowStyle;
        ResizeMode = saved.ResizeMode;
        Topmost = saved.Topmost;
        Left = saved.Left;
        Top = saved.Top;
        Width = saved.Width;
        Height = saved.Height;
        MainMenu.Visibility = saved.MenuVisibility;
        MainToolBar.Visibility = saved.ToolBarVisibility;
        EnvironmentStatus.Visibility = saved.StatusBarVisibility;
        FullScreenMenu.IsChecked = false;
        WindowState = saved.WindowState;
    }

    private void FullScreenMenu_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    private void ProgramStarter_Click(object sender, RoutedEventArgs e) => OpenProgramStarterWindow();

    private void OpenProgramStarterWindow()
    {
        if (_programStarterWindow is { IsVisible: true })
        {
            _programStarterWindow.Activate();
            return;
        }
        _programStarterWindow = new ProgramStarterWindow(_programStarterStore, _programStarterEntries, _themeController,
            CaptureProgramStarterContext, message => EnvironmentStatus.Text = message) { Owner = this };
        _programStarterWindow.Closed += (_, _) =>
        {
            _programStarterEntries = _programStarterStore.Load();
            _programStarterWindow = null;
        };
        _programStarterWindow.Show();
    }

    private ProgramStarterContext CaptureProgramStarterContext()
    {
        var paneCount = LayoutPreset.Find(_layout).Slots.Length;
        var activeIndex = Array.IndexOf(_panes, ActivePane);
        if (activeIndex < 0 || activeIndex >= paneCount)
            throw new InvalidOperationException("当前活动窗格不在可见布局中。");
        var panes = _panes.Take(paneCount).Select(pane => pane.CaptureProgramStarterPaneContext()).ToArray();
        string Special(Environment.SpecialFolder folder) =>
            Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        var appPath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
        return new ProgramStarterContext(activeIndex, panes, appPath,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.SystemDirectory,
            Special(Environment.SpecialFolder.DesktopDirectory), Special(Environment.SpecialFolder.ApplicationData),
            Special(Environment.SpecialFolder.UserProfile));
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

    private void NewWindow_Click(object sender, RoutedEventArgs e) => _createWindow?.Invoke(this);
    private void BuildFileCommandControls()
    {
        foreach (var info in ShellFileCommands.All)
        {
            var item = new MenuItem
            {
                Header = info.Label, Icon = ExplorerChromeIcons.CreateMenuIcon(info.Command),
                InputGestureText = info.Shortcut, IsEnabled = false
            };
            item.Click += (_, _) => InvokeFileCommand(info.Command);
            FileCommandsMenu.Items.Add(item);
            _fileCommandMenus[info.Command] = item;
            if (info.Command is not (ShellFileCommand.Properties or ShellFileCommand.NewFolder)) continue;
            var button = new Button
            {
                Content = ExplorerChromeIcons.CreateLabeledButtonContent(info.Command, info.Label),
                ToolTip = $"{info.Label} ({info.Shortcut})",
                IsEnabled = false, Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(1, 0, 1, 0)
            };
            AutomationProperties.SetName(button, info.Label);
            button.Click += (_, _) => InvokeFileCommand(info.Command);
            FileCommandButtons.Children.Add(button);
            _fileCommandButtons[info.Command] = button;
        }
        FileCommandsMenu.Items.Add(new Separator());
        foreach (var (command, label, shortcut) in new[]
        {
            (ShellSelectionCommand.All, "全选", "Ctrl+A"),
            (ShellSelectionCommand.None, "取消选择", ""),
            (ShellSelectionCommand.Invert, "反向选择", "Ctrl+Shift+A")
        })
        {
            var item = new MenuItem { Header = label, InputGestureText = shortcut, IsEnabled = false };
            item.Click += (_, _) =>
            {
                _fileViewFocusDesired = true;
                ActivePane.ApplySelectionCommand(command);
                RefreshFileCommands();
            };
            _selectionMenus[command] = item;
            FileCommandsMenu.Items.Add(item);
        }
    }

    private void PasteSpecialMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource)) return;
        PasteSpecialMenu.Items.Clear();
        try
        {
            var hasImage = Clipboard.ContainsImage();
            var hasText = Clipboard.ContainsText();
            if (hasImage)
            {
                AddPasteSpecialOption("图片保存为 PNG…", PasteSpecialFormat.Png);
                AddPasteSpecialOption("图片保存为 BMP…", PasteSpecialFormat.Bmp);
                AddPasteSpecialOption("图片保存为 JPG…", PasteSpecialFormat.Jpeg);
            }
            if (hasImage && hasText) PasteSpecialMenu.Items.Add(new Separator());
            if (hasText)
            {
                AddPasteSpecialOption("文本保存为 UTF-8…", PasteSpecialFormat.Utf8);
                AddPasteSpecialOption("文本保存为 UTF-16…", PasteSpecialFormat.Utf16);
                AddPasteSpecialOption("文本保存为 ANSI…", PasteSpecialFormat.Ansi);
            }
            if (!hasImage && !hasText)
                PasteSpecialMenu.Items.Add(new MenuItem { Header = "剪贴板中没有支持的图片或文本", IsEnabled = false });
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            PasteSpecialMenu.Items.Add(new MenuItem { Header = $"无法读取剪贴板：{ex.Message}", IsEnabled = false });
        }
    }

    private void AddPasteSpecialOption(string label, PasteSpecialFormat format)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => SaveClipboardToFile(format);
        PasteSpecialMenu.Items.Add(item);
    }

    private void SaveClipboardToFile(PasteSpecialFormat format)
    {
        var pane = ActivePane;
        if (!pane.IsCurrentFileSystemLocation || !Path.IsPathFullyQualified(pane.CurrentPath) ||
            !Directory.Exists(pane.CurrentPath))
        {
            EnvironmentStatus.Text = "粘贴特殊仅支持当前可写的文件系统文件夹。";
            return;
        }

        try
        {
            byte[] content;
            if (format is PasteSpecialFormat.Png or PasteSpecialFormat.Bmp or PasteSpecialFormat.Jpeg)
            {
                var image = Clipboard.GetImage();
                if (image is null) throw new InvalidOperationException("剪贴板中没有可保存的图片。");
                content = PasteSpecialOutput.EncodeImage(image, format);
            }
            else
            {
                if (!Clipboard.ContainsText()) throw new InvalidOperationException("剪贴板中没有可保存的文本。");
                content = PasteSpecialOutput.EncodeText(Clipboard.GetText(TextDataFormat.UnicodeText), format);
            }

            var createdPath = PasteSpecialOutput.WriteUniqueFile(pane.CurrentPath, format, content,
                DateTimeOffset.Now);
            _fileViewFocusDesired = true;
            if (!pane.NotifyCreatedClipboardFile(createdPath))
                EnvironmentStatus.Text = $"已保存 {Path.GetFileName(createdPath)}；当前 Shell 视图尚未确认更新。";
            else
                EnvironmentStatus.Text = $"已保存 {Path.GetFileName(createdPath)}；Shell 正在更新视图并尝试选择。";
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or IOException or
                                   UnauthorizedAccessException or EncoderFallbackException)
        {
            EnvironmentStatus.Text = $"粘贴特殊失败：{ex.Message}";
        }
    }

    private void FileCommandsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource)) return;
        RefreshFileCommands();
        if (ReferenceEquals(sender, FileMenu)) RefreshPhotoPrintCommandState();
    }
    private void FileCommandButtons_Refresh(object sender, RoutedEventArgs e) => RefreshFileCommands();

    private bool IsFileCommandSurfaceEngaged =>
        FileCommandButtons.IsMouseOver || FileCommandButtons.IsKeyboardFocusWithin;

    private void ScheduleFileCommandRefresh(PaneControl pane)
    {
        if (!_fileCommandRefreshGate.Request(ReferenceEquals(pane, ActivePane), IsFileCommandSurfaceEngaged))
            return;
        _fileCommandRefreshPane = pane;
        _fileCommandRefreshTimer.Stop();
        _fileCommandRefreshTimer.Start();
    }

    private void FileCommandRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _fileCommandRefreshTimer.Stop();
        var pane = _fileCommandRefreshPane;
        if (!_fileCommandRefreshGate.Consume(ReferenceEquals(pane, ActivePane), IsFileCommandSurfaceEngaged))
        {
            if (!_fileCommandRefreshGate.IsQuerying) _fileCommandRefreshPane = null;
            return;
        }
        _fileCommandRefreshPane = null;
        RefreshFileCommands();
    }

    private void RefreshFileCommands()
    {
        if (_initializing || _queryingFileCommands || !_fileCommandRefreshGate.BeginQuery()) return;
        _fileCommandRefreshTimer.Stop();
        _fileCommandRefreshPane = null;
        _fileCommandRefreshGate.Cancel();
        _queryingFileCommands = true;
        var pane = ActivePane;
        var renameEditorFocused = IsAnyShellRenameEditorFocused();
        try
        {
            var states = renameEditorFocused
                ? ShellFileCommands.All.ToDictionary(info => info.Command, _ => false)
                : pane.QueryFileCommands();
            if (!ReferenceEquals(pane, ActivePane)) return;
            foreach (var (command, enabled) in states)
            {
                _fileCommandMenus[command].IsEnabled = enabled;
                if (_fileCommandButtons.TryGetValue(command, out var button)) button.IsEnabled = enabled;
            }
            NewFolderMenu.IsEnabled = states.GetValueOrDefault(ShellFileCommand.NewFolder);
            var counts = renameEditorFocused ? null : pane.ReadSelectionCounts();
            if (!ReferenceEquals(pane, ActivePane)) counts = null;
            foreach (var (command, item) in _selectionMenus)
                item.IsEnabled = counts is not null &&
                    (command == ShellSelectionCommand.None ? counts.Selected > 0 : counts.Total > 0);
        }
        finally
        {
            _queryingFileCommands = false;
            if (_fileCommandRefreshGate.EndQuery())
            {
                _fileCommandRefreshTimer.Stop();
                _fileCommandRefreshTimer.Start();
            }
        }
    }

    private void InvokeFileCommand(ShellFileCommand command)
    {
        var pane = ActivePane;
        _fileViewFocusDesired = true;
        pane.InvokeFileCommand(command);
        RefreshFileCommands();
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e) => CreateNewFolder(ActivePane);
    private void CreateNewFolder(PaneControl pane)
    {
        _fileViewFocusDesired = true;
        pane.CreateNewFolder();
    }
    private void DuplicateTab_Click(object sender, RoutedEventArgs e) => ActivePane.DuplicateCurrentTab();
    private void CloseTab_Click(object sender, RoutedEventArgs e) => ActivePane.CloseCurrentTab();
    private void CloseOtherTabs_Click(object sender, RoutedEventArgs e) => ActivePane.CloseOtherTabs();
    private void ReopenTab_Click(object sender, RoutedEventArgs e) => ActivePane.ReopenClosedTab();
    private void RefreshCurrent_Click(object sender, RoutedEventArgs e) => ActivePane.RefreshCurrent();
    private void SearchCurrent_Click(object sender, RoutedEventArgs e) => ActivePane.FocusSearch();
    private void CalculateDirectorySize_Click(object sender, RoutedEventArgs e) => ActivePane.ToggleRecursiveDirectorySize();
    private void CalculateSelectedItemsRecursiveSize_Click(object sender, RoutedEventArgs e)
        => ActivePane.CalculateSelectedItemsRecursiveSize();
    private void RestartApplication_Click(object sender, RoutedEventArgs e)
        => RequestRestartApplication();

    private void RequestRestartApplication()
    {
        if (_restartApplication is null) return;
        if (MessageBox.Show(this,
                "将关闭当前 Explorer Dirs 进程中的所有窗口，保存窗口会话后重新启动。继续吗？",
                "重启 Explorer Dirs", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _restartApplication();
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void FavoritesMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.Source == FavoritesMenu) BuildFavoritesMenu();
    }

    private void QuickLinksMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.Source == QuickLinksMenu) BuildQuickLinksMenu();
    }

    private void BuildQuickLinksMenu()
    {
        QuickLinksMenu.Items.Clear();
        var currentPath = ActivePane.CurrentPath;
        var canAddCurrent = Directory.Exists(currentPath);
        if (!canAddCurrent && ShellLocationIdentity.TryGetFolderPidl(currentPath, out var currentFolderPidl))
        {
            Marshal.FreeCoTaskMem(currentFolderPidl);
            canAddCurrent = true;
        }
        var addCurrent = new MenuItem
        {
            Header = "添加当前目录",
            IsEnabled = canAddCurrent
        };
        addCurrent.Click += (_, _) =>
        {
            try
            {
                var path = _quickLinkStore.AddFolderLocation(currentPath);
                EnvironmentStatus.Text = $"已添加快速链接：{Path.GetFileNameWithoutExtension(path)}";
            }
            catch (Exception ex)
            {
                EnvironmentStatus.Text = $"添加快速链接失败：{ex.Message}";
            }
        };
        QuickLinksMenu.Items.Add(addCurrent);
        var selectionPane = ActivePane;
        ExplorerBrowserHost.SelectedItem? selectedItem = null;
        try { selectedItem = selectionPane.GetSelectedQuickLinkItem(); }
        catch (Exception ex) { EnvironmentStatus.Text = $"读取所选项目失败：{ex.Message}"; }
        var canAddSelectedItem = false;
        if (selectedItem is not null)
        {
            var availability = QuickLinkAvailability.Evaluate(() => _quickLinkStore.CanAddLocation(selectedItem.Path));
            canAddSelectedItem = availability.CanAdd;
            if (availability.Error is { } error)
                EnvironmentStatus.Text = $"检查快速链接目标失败：{error.Message}";
        }
        var addSelected = new MenuItem
        {
            Header = "添加所选项目",
            IsEnabled = canAddSelectedItem
        };
        addSelected.Click += (_, _) =>
        {
            try
            {
                if (!ReferenceEquals(ActivePane, selectionPane) ||
                    selectionPane.GetSelectedQuickLinkItem() is not { } currentSelection)
                    throw new InvalidOperationException("活动窗格或单选项目已变化；请重新打开快速链接菜单。");
                var path = _quickLinkStore.AddLocation(currentSelection.Path);
                EnvironmentStatus.Text = $"已添加快速链接：{Path.GetFileNameWithoutExtension(path)}";
            }
            catch (Exception ex)
            {
                EnvironmentStatus.Text = $"添加快速链接失败：{ex.Message}";
            }
        };
        QuickLinksMenu.Items.Add(addSelected);
        var manage = new MenuItem { Header = "管理快速链接…" };
        manage.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(_profileDirectory, "QuickLinks"));
                Process.Start(new ProcessStartInfo(Path.Combine(_profileDirectory, "QuickLinks"))
                    { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                EnvironmentStatus.Text = $"打开快速链接目录失败：{ex.Message}";
            }
        };
        QuickLinksMenu.Items.Add(manage);
        QuickLinksMenu.Items.Add(new Separator());
        IReadOnlyList<QuickLinkEntry> entries = [];
        var entriesLoaded = true;
        try { entries = _quickLinkStore.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Profile link enumeration can fail independently of the rest of this menu.
            entriesLoaded = false;
            EnvironmentStatus.Text = $"读取快速链接失败：{ex.Message}";
        }
        if (_quickLinkStore.LoadWarning is { } loadWarning)
        {
            entriesLoaded = false;
            EnvironmentStatus.Text = loadWarning;
        }
        if (!entriesLoaded)
            QuickLinksMenu.Items.Add(new MenuItem { Header = "（无法读取快速链接）", IsEnabled = false });
        else if (entries.Count == 0)
            QuickLinksMenu.Items.Add(new MenuItem { Header = "（暂无快速链接）", IsEnabled = false });
        foreach (var entry in entries)
        {
            var item = new MenuItem { Header = entry.Name, ToolTip = entry.Path };
            item.Click += (_, _) => OpenQuickLink(entry.Path);
            QuickLinksMenu.Items.Add(item);
        }
    }

    private void OpenQuickLink(string shortcutPath)
    {
        try
        {
            if (_quickLinkStore.TryGetTarget(shortcutPath, out var target) &&
                ShellLocationIdentity.TryGetFolderPidl(target, out var folderPidl))
            {
                Marshal.FreeCoTaskMem(folderPidl);
                ActivePane.NavigateTo(target);
                return;
            }
            Process.Start(new ProcessStartInfo(shortcutPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"打开快速链接失败：{ex.Message}";
        }
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
        var saveWorkspace = new MenuItem
        {
            Header = "保存当前工作区…", IsEnabled = !_importingQdr && !_exportingQdr
        };
        saveWorkspace.Click += async (_, _) => await ExportQdrAsync(saveWorkspace: true);
        FavoritesMenu.Items.Add(saveWorkspace);
        var workspaces = new MenuItem { Header = "追加已保存工作区" };
        workspaces.Items.Add(new MenuItem { Header = "正在读取…", IsEnabled = false });
        workspaces.SubmenuOpened += async (_, e) =>
        {
            if (e.Source == workspaces) await BuildWorkspaceMenuAsync(workspaces);
        };
        FavoritesMenu.Items.Add(workspaces);
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

    private async Task BuildWorkspaceMenuAsync(MenuItem menu)
    {
        if (_closed) return;
        try
        {
            // Share an outstanding enumeration across reopenings. A slow volume
            // must not accumulate a new blocked worker for every menu opening.
            if (_workspaceListingTask is null || _workspaceListingTask.IsCompleted)
                _workspaceListingTask = WindowBackgroundOperation.RunAsync(
                    WorkspaceCatalog.DirectoryFor(_sessionStore.Path), static path => WorkspaceCatalog.Read(path),
                    _windowOperationStop.Token);
            var listing = await _workspaceListingTask;
            if (_closed || !IsVisible || !menu.IsSubmenuOpen || !FavoritesMenu.Items.Contains(menu)) return;
            menu.Items.Clear();
            foreach (var entry in listing.Entries)
            {
                var item = new MenuItem
                {
                    Header = new TextBlock { Text = entry.Name, MaxWidth = 340,
                        TextTrimming = TextTrimming.CharacterEllipsis },
                    ToolTip = entry.Path, IsEnabled = !_importingQdr && !_exportingQdr
                };
                item.Click += async (_, _) => await ImportQdrAsync(entry.Path);
                menu.Items.Add(item);
            }
            if (listing.Entries.Count == 0)
                menu.Items.Add(new MenuItem { Header = "（暂无工作区收藏）", IsEnabled = false });
            if (listing.Truncated)
                menu.Items.Add(new MenuItem { Header = "仅显示 200 项；其他文件请从文件菜单导入", IsEnabled = false });
        }
        catch (Exception ex)
        {
            if (_closed || !IsVisible || !menu.IsSubmenuOpen || !FavoritesMenu.Items.Contains(menu)) return;
            menu.Items.Clear();
            menu.Items.Add(new MenuItem { Header = "工作区列表读取失败，请重新展开", IsEnabled = false });
            EnvironmentStatus.Text = $"工作区列表读取失败：{ex.Message}";
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

    private void ConnectNetworkDrive_Click(object sender, RoutedEventArgs e) =>
        ShowNetworkDriveDialog(_networkDriveDialogs.Connect, "映射网络驱动器");

    private void DisconnectNetworkDrive_Click(object sender, RoutedEventArgs e) =>
        ShowNetworkDriveDialog(_networkDriveDialogs.Disconnect, "断开网络驱动器");

    private void ShowNetworkDriveDialog(Func<nint, int> showDialog, string operation)
    {
        var result = showDialog(new WindowInteropHelper(this).Handle);
        if (result is 0 or -1) return; // NO_ERROR or the native dialog's cancel result.
        MessageBox.Show(this, NetworkDriveDialogs.GetErrorMessage(result), operation,
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OpenCrashLogDirectory_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(_profileDirectory, "crash-logs");
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "无法打开崩溃诊断目录",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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

    private void RefreshFollowedWindowsHiddenItems()
    {
        var current = ShellFolderFilter.ReadShowAllObjectsSetting();
        if (!HiddenItemsPreferencePolicy.ShouldRefreshFollowWindowsChange(
                _hiddenItemsPreference, _windowsShowsHiddenItems, current))
        {
            _windowsShowsHiddenItems = current;
            return;
        }

        _windowsShowsHiddenItems = current;
        var failures = _panes.SelectMany(pane => pane.RefreshForWindowsHiddenItemsChange()).ToArray();
        if (failures.Length > 0)
            EnvironmentStatus.Text = $"Windows 隐藏项目设置已更新，但 {failures.Length} 个视图刷新失败：{string.Join("；", failures)}";
    }

    private async void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_openingTerminal) return;
        _openingTerminal = true;
        var path = ActivePane.CurrentPath;
        try
        {
            await TerminalLauncher.OpenAsync(path, _windowOperationStop.Token);
        }
        catch (OperationCanceledException) when (_windowOperationStop.IsCancellationRequested) { }
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
        if (!_initializing && !_closed)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private async void ImportQdr_Click(object sender, RoutedEventArgs e)
    {
        if (_importingQdr || _exportingQdr) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 Q-Dir 配置", Filter = "Q-Dir 配置 (*.qdr)|*.qdr", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await ImportQdrAsync(dialog.FileName);
    }

    private async Task ImportQdrAsync(string path)
    {
        if (_closed || _importingQdr || _exportingQdr) return;
        _importingQdr = true;
        ImportQdrMenu.IsEnabled = false;
        ExportQdrMenu.IsEnabled = false;
        string? backupPath = null;
        var appending = false;
        try
        {
            var import = await WindowBackgroundOperation.RunAsync(path, static value => QdrImporter.Load(value),
                _windowOperationStop.Token);
            if (_closed || !IsVisible) return;
            if (!IsActive)
            {
                // Parsing can finish after the user switches to another app.
                // Do not open a modal confirmation on that app's desktop work.
                EnvironmentStatus.Text = "导入解析已结束；窗口未激活，未应用配置。返回后可重新选择导入。";
                return;
            }
            var summary = $"识别到 {import.TabCount} 个标签，将追加到对应窗格，保留已打开的标签。\n" +
                string.Join("，", import.Panes.Select((pane, index) => $"窗格 {index + 1}：{pane.Count}")) +
                $"\n其中 {import.Panes.SelectMany(pane => pane).Count(tab => tab.View.DisplayColumns is not null)} 个标签包含可导入的排序、分组、图标大小和列设置。" +
                (import.Layout is { } importedLayout ? $"\n布局将切换为{LayoutPreset.Find(importedLayout).Name}。" : "\n保留当前布局。") +
                (import.ActivePane is { } importedActivePane ? $"\n活动窗格将切换为窗格 {importedActivePane + 1}。" : "") +
                (import.TreeDisplayMode is { } importedTreeModePreview ? $"\n目录树模式将切换为{importedTreeModePreview switch { TreeDisplayMode.Shared => "共享一棵树", TreeDisplayMode.PerPane => "每窗格各一棵树", _ => "关闭" }}。" : "") +
                (import.PaneTreeVisibility is not null ? "\n将恢复可识别的逐窗格目录树状态。" : "") +
                (import.NavigationTreeWidths is { } importedTreeWidthsPreview ?
                    $"\n将恢复本程序目录树宽度（共享树 {importedTreeWidthsPreview.Shared:0.#}，各窗格 {importedTreeWidthsPreview.Pane0:0.#}/{importedTreeWidthsPreview.Pane1:0.#}/{importedTreeWidthsPreview.Pane2:0.#}/{importedTreeWidthsPreview.Pane3:0.#} DIP）。" : "") +
                (import.Proportions is { Count: > 0 } importedProportions ? $"\n将恢复 {importedProportions.Count} 种布局的分隔比例。" : "\n保留本程序已有分隔比例。") +
                (import.Pins is { Count: > 0 } pins ? $"\n其中 {pins.Count} 个固定标签将进入各窗格的固定区。" : "") +
                "\n\n" + string.Join("\n", import.Warnings) + "\n\n确认导入？导入前会备份当前会话。";
            if (MessageBox.Show(this, summary, "导入 Q-Dir 配置", MessageBoxButton.OKCancel,
                    MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            var snapshot = new SessionState(_layout, _defaultViewMode,
                _panes.Select(pane => pane.SnapshotTabs().ToList()).ToList(), Array.IndexOf(_panes, ActivePane),
                _panes.Select(pane => pane.SelectedTabIndex).ToArray(), _defaultIconSize,
                _paneTreeVisibility.ToArray(), _treeDisplayMode);
            if (Enumerable.Range(0, 4).Any(index => snapshot.Panes[index].Count + import.Panes[index].Count > SessionStore.MaximumTabsPerPane))
                throw new InvalidOperationException($"导入后单窗格将超过 {SessionStore.MaximumTabsPerPane} 个标签，请先关闭一些标签再导入。");
            var backup = _sessionStore.Path + $".before-qdr-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.bak";
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.WriteAllText(backup, SessionStore.Render(snapshot));
            File.WriteAllText(backup + ".qdr", QdrExporter.Render(snapshot,
                _panes.SelectMany((pane, index) => pane.SnapshotPins(index)).ToArray(), SnapshotLayoutProportions(),
                _treeWidths), System.Text.Encoding.Unicode);
            backupPath = backup;
            appending = true;
            for (var pane = 0; pane < 4; pane++)
                _panes[pane].ImportTabs(import.Panes[pane], import.SelectedTabs[pane],
                    import.Pins?.Where(pin => pin.Pane == pane).Select(pin => pin.Index).ToHashSet());
            if (import.NavigationTreeWidths is { } importedTreeWidths)
            {
                _treeWidthsStore.Save(importedTreeWidths);
                _treeWidths = importedTreeWidths;
                for (var pane = 0; pane < _panes.Length; pane++)
                    _panes[pane].TreeWidth = importedTreeWidths.GetPaneWidth(pane);
            }
            if (import.PaneTreeVisibility is { Count: 4 } importedTreeVisibility)
                for (var pane = 0; pane < 4; pane++)
                {
                    _paneTreeVisibility[pane] = importedTreeVisibility[pane];
                    _panes[pane].SetTreeVisible(_treeDisplayMode == TreeDisplayMode.PerPane && importedTreeVisibility[pane]);
                }
            if (import.TreeDisplayMode is { } importedTreeMode) _treeDisplayMode = importedTreeMode;
            ApplyTreeDisplayMode();
            UpdateTreeModeMenu();
            if (import.Proportions is { } incomingProportions)
            {
                var merged = new Dictionary<int, LayoutProportions>(_layoutProportions);
                foreach (var pair in incomingProportions) merged[pair.Key] = pair.Value;
                // Commit through the existing conflict guard before publishing
                // ratios in memory. An external edit must not be overwritten.
                _layoutProportionsStore.Save(merged);
                foreach (var pair in incomingProportions) _layoutProportions[pair.Key] = pair.Value;
            }
            if (import.Layout is { } layout)
            {
                // SetLayout intentionally skips an unchanged layout ID; imported
                // proportions still need to be arranged immediately in that case.
                if (_layout == layout && import.Proportions?.ContainsKey(layout) == true) BuildLayout();
                else SetLayout(layout);
            }
            if (import.ExtendedState is { } extended)
            {
                _defaultViewMode = extended.DefaultViewMode;
                _defaultIconSize = extended.DefaultIconSize;
                UpdateDefaultViewChecks();
            }
            if (import.ActivePane is { } activePane) SetActivePane(_panes[activePane]);
            ScheduleSave();
            EnvironmentStatus.Text = $"已导入 {import.TabCount} 个标签；原会话已备份。未支持的配置未导入。";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            var message = appending
                ? $"导入未完整完成，可能已追加部分标签。原会话备份：{backupPath}\n{ex.Message}"
                : $"导入失败，未追加标签：{ex.Message}";
            EnvironmentStatus.Text = message;
            if (IsActive) MessageBox.Show(this, message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _importingQdr = false;
            if (!_closed) ImportQdrMenu.IsEnabled = ExportQdrMenu.IsEnabled = true;
        }
    }

    private async void SaveWorkspaceToDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _importingQdr || _exportingQdr) return;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存当前工作区到桌面",
            Filter = "Explorer Dirs 桌面项 (*.lnk)|*.lnk",
            DefaultExt = ".lnk",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = desktop,
            FileName = "ExplorerDirs 工作区.lnk"
        };
        if (dialog.ShowDialog(this) != true) return;
        _exportingQdr = true;
        ImportQdrMenu.IsEnabled = ExportQdrMenu.IsEnabled = false;
        try
        {
            var document = await WindowBackgroundOperation.RunAsync((Session: SnapshotSession(),
                Pins: _panes.SelectMany((pane, index) => pane.SnapshotPins(index)).ToArray(),
                Proportions: SnapshotLayoutProportions(), Widths: _treeWidths),
                static state => QdrExporter.Prepare(state.Session, state.Pins, state.Proportions, state.Widths),
                _windowOperationStop.Token);
            if (!ConfirmQdrCompatibility(document)) return;
            var qdrPath = Path.Combine(WorkspaceCatalog.DirectoryFor(_sessionStore.Path), $"Desktop-{Guid.NewGuid():N}.qdr");
            var executable = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            var isDotnetHost = Path.GetFileNameWithoutExtension(executable)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
            await WindowBackgroundOperation.RunAsync((Shortcut: dialog.FileName, Executable: executable,
                Assembly: isDotnetHost ? Environment.GetCommandLineArgs()[0] : null, Qdr: qdrPath,
                WorkingDirectory: AppContext.BaseDirectory, Text: document.Text),
                static state =>
                {
                    DesktopWorkspaceShortcut.CreateWithWorkspace(state.Shortcut, state.Executable,
                        state.Assembly, state.Qdr, state.WorkingDirectory, state.Text);
                    return true;
                }, _windowOperationStop.Token);
            if (_closed) return;
            EnvironmentStatus.Text = $"已创建桌面工作区项；状态文件保存在 {Path.GetFileName(qdrPath)}。";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            EnvironmentStatus.Text = $"创建桌面工作区项失败：{ex.Message}";
            if (IsActive)
                MessageBox.Show(this, EnvironmentStatus.Text, "创建桌面工作区项失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _exportingQdr = false;
            if (!_closed) ImportQdrMenu.IsEnabled = ExportQdrMenu.IsEnabled = true;
        }
    }

    private bool ConfirmQdrCompatibility(QdrExportDocument document)
    {
        if (_closed) return false;
        if (document.UnencodedLocationCount == 0) return true;
        var warning = $"{document.UnencodedLocationCount} 个含百分号的目录未能取得 Shell 编码身份（可能已离线）。\n\n" +
            "全部标签仍会保留，但 Q-Dir 读取文本路径时可能展开同名环境变量。本程序扩展保留原路径。\n\n继续导出？";
        EnvironmentStatus.Text = warning;
        return IsActive && MessageBox.Show(this, warning, "QDR 路径兼容性提示",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }
    private async void ExportQdr_Click(object sender, RoutedEventArgs e) => await ExportQdrAsync(false);

    private async void ExportFileView_Click(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        try
        {
            // Capture before opening the dialog so the export reflects the active Shell view at invocation time.
            var snapshot = ActivePane.CaptureFileViewExportSnapshot();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出当前文件视图",
                Filter = "CSV 文件 (*.csv)|*.csv|制表符文本 (*.txt)|*.txt|HTML 表格 (*.html)|*.html|Excel 97-2003 (*.xls)|*.xls",
                FilterIndex = 1,
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = $"ExplorerDirs-View-{DateTime.Now:yyyyMMdd-HHmmss}"
            };
            if (dialog.ShowDialog(this) != true) return;
            var format = dialog.FilterIndex switch
            {
                1 => FileViewExportFormat.Csv,
                2 => FileViewExportFormat.Text,
                3 => FileViewExportFormat.Html,
                4 => FileViewExportFormat.Xls,
                _ => throw new InvalidOperationException("未知的导出格式。")
            };
            // Like QDR export, a confirmed file write may outlive its window;
            // keep the worker independent of both the window and save dialog.
            await WindowBackgroundOperation.RunAsync((Path: dialog.FileName, Format: format, Snapshot: snapshot),
                static state =>
                {
                    FileViewExportFile.WriteAtomically(state.Path,
                        output => FileViewExportWriter.Write(output, state.Format, state.Snapshot));
                    return true;
                }, _windowOperationStop.Token);
            if (_closed) return;
            EnvironmentStatus.Text = $"已导出 {snapshot.Rows.Count:N0} 项，{snapshot.Columns.Count} 列到 {Path.GetFileName(dialog.FileName)}。导出整个活动视图，当前选区不缩小导出范围。";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            EnvironmentStatus.Text = $"文件视图导出失败：{ex.Message}";
            if (IsActive) MessageBox.Show(this, EnvironmentStatus.Text, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PrintFileView_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Capture before the printer dialog so the print job uses the view present at invocation time.
            var snapshot = ActivePane.CaptureFileViewPrintSnapshot();
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() != true) return;
            var document = FileViewPrintDocument.Create(snapshot, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Explorer Dirs - 当前文件视图");
            EnvironmentStatus.Text = $"已提交当前视图打印任务：{snapshot.Rows.Count:N0} 项。";
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"打印当前文件视图失败：{ex.Message}";
            if (IsActive) MessageBox.Show(this, EnvironmentStatus.Text, "打印失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshPhotoPrintCommandState()
    {
        if (_closed || _loadingPrintPhotos)
        {
            PrintSelectedPhotosMenu.IsEnabled = false;
            return;
        }
        try { PrintSelectedPhotosMenu.IsEnabled = ActivePane.CapturePhotoPrintSelection().CandidatePaths.Count > 0; }
        catch (Exception) { PrintSelectedPhotosMenu.IsEnabled = false; }
    }

    private async void PrintSelectedPhotos_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _loadingPrintPhotos) return;
        _loadingPrintPhotos = true;
        try
        {
            var selection = ActivePane.CapturePhotoPrintSelection();
            if (selection.CandidatePaths.Count == 0)
            {
                EnvironmentStatus.Text = selection.RejectedItems.Count == 0
                    ? "请先在当前文件视图选择照片。"
                    : "所选项目中没有可读取的文件；文件夹和虚拟项目不能打印。";
                return;
            }

            PrintSelectedPhotosMenu.IsEnabled = false;
            EnvironmentStatus.Text = $"正在读取 {selection.CandidatePaths.Count:N0} 个所选文件…";
            var decoded = await PhotoPrintImageLoader.DecodeAsync(selection.CandidatePaths, _windowOperationStop.Token)
                .WaitAsync(_windowOperationStop.Token);
            if (_closed || !IsVisible) return;
            if (!IsActive)
            {
                EnvironmentStatus.Text = "图片读取已结束；窗口未激活，未打开打印预览。返回后可重新选择打印。";
                return;
            }
            var rejected = selection.RejectedItems.Concat(decoded.RejectedItems).ToArray();
            if (decoded.Images.Count == 0)
            {
                EnvironmentStatus.Text = $"所选文件中没有可打印的图片（{rejected.Length:N0} 项无法读取）。";
                MessageBox.Show(this, EnvironmentStatus.Text, "打印所选照片", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new PhotoPrintWindow(decoded.Images, rejected, _themeController) { Owner = this };
            if (window.ShowDialog() == true)
                EnvironmentStatus.Text = $"已提交照片打印任务：{window.SubmittedPhotoCount:N0} 张，{window.SubmittedPageCount:N0} 页。";
            else
                EnvironmentStatus.Text = "照片打印已取消，未提交打印任务。";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            EnvironmentStatus.Text = $"打印所选照片失败：{ex.Message}";
            if (IsActive) MessageBox.Show(this, EnvironmentStatus.Text, "打印所选照片失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _loadingPrintPhotos = false;
            if (!_closed) RefreshPhotoPrintCommandState();
        }
    }

    private async Task ExportQdrAsync(bool saveWorkspace)
    {
        if (_closed || _exportingQdr || _importingQdr) return;
        _exportingQdr = true;
        ImportQdrMenu.IsEnabled = ExportQdrMenu.IsEnabled = false;
        try
        {
            var workspaceDirectory = WorkspaceCatalog.DirectoryFor(_sessionStore.Path);
            if (saveWorkspace)
            {
                await WindowBackgroundOperation.RunAsync(workspaceDirectory,
                    static path => Directory.CreateDirectory(path), _windowOperationStop.Token);
                if (_closed || !IsVisible || !IsActive) return;
            }
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = saveWorkspace ? "保存当前工作区收藏" : "导出 Q-Dir 状态",
                Filter = "Q-Dir 状态 (*.qdr)|*.qdr",
                DefaultExt = ".qdr", AddExtension = true, OverwritePrompt = false,
                FileName = $"ExplorerDirs-{DateTime.Now:yyyyMMdd-HHmmss}.qdr"
            };
            if (saveWorkspace) dialog.InitialDirectory = workspaceDirectory;
            if (dialog.ShowDialog(this) != true) return;
            var expected = await WindowBackgroundOperation.RunAsync(dialog.FileName,
                static path => File.Exists(path) ? QdrImporter.ReadText(path) : null, _windowOperationStop.Token);
            if (_closed || !IsVisible || !IsActive) return;
            var note = "导出全部窗格与标签（包括隐藏窗格），以及查看设置、预览/详情窗格、固定标签和新标签默认查看方式。\n\n" +
                "导出当前十二种布局及各窗格标签。已取得完整列设置的标签同时写入 Q-Dir 格式的查看、排序、分组与列设置；其余标签通过本程序扩展保留视图状态。\n\n" +
                "当前布局分隔比例写入 Q-Dir 字段（精度为万分之一）；全部已记忆的布局比例通过本程序扩展完整保存。不同程序的边框尺寸可能影响实际像素位置。\n\n" +
                $"目录树宽度（共享树 { _treeWidths.Shared:0.#}，各窗格 {_treeWidths.Pane0:0.#}/{_treeWidths.Pane1:0.#}/{_treeWidths.Pane2:0.#}/{_treeWidths.Pane3:0.#} DIP）通过本程序扩展保存，不写入语义未确认的 Q-Dir TreeNsplit 字段。\n\n" +
                "本程序导入时追加标签。固定标签、目录树宽度和新标签默认查看方式使用本程序扩展；Q-Dir 实际读取效果仍待验证。\n\n" +
                (expected is null ? "确认导出？" : "目标文件已经存在，确认覆盖？");
            if (MessageBox.Show(this, note, "导出 Q-Dir 状态", MessageBoxButton.OKCancel,
                    MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            var snapshot = SnapshotSession();
            var document = await WindowBackgroundOperation.RunAsync((Session: snapshot,
                Pins: _panes.SelectMany((pane, index) => pane.SnapshotPins(index)).ToArray(),
                Proportions: SnapshotLayoutProportions(), Widths: _treeWidths),
                static state => QdrExporter.Prepare(state.Session, state.Pins, state.Proportions, state.Widths),
                _windowOperationStop.Token);
            if (_closed) return;
            if (!ConfirmQdrCompatibility(document)) return;
            var text = document.Text;
            await WindowBackgroundOperation.RunAsync((Path: dialog.FileName, Expected: expected, Text: text),
                static state => { QdrExporter.Save(state.Path, state.Expected, state.Text); return true; },
                _windowOperationStop.Token);
            if (_closed) return;
            EnvironmentStatus.Text = WorkspaceCatalog.IsInDirectory(dialog.FileName, workspaceDirectory)
                ? "已保存工作区；可从“收藏夹 → 追加已保存工作区”打开。"
                : "已导出 .qdr 工作区状态到所选位置；该位置不在工作区收藏目录中。";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            EnvironmentStatus.Text = $"导出失败：{ex.Message}";
            if (IsActive) MessageBox.Show(this, EnvironmentStatus.Text, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _exportingQdr = false;
            if (!_closed) ImportQdrMenu.IsEnabled = ExportQdrMenu.IsEnabled = true;
        }
    }

    private Dictionary<int, LayoutProportions> SnapshotLayoutProportions()
    {
        var snapshot = new Dictionary<int, LayoutProportions>(_layoutProportions);
        snapshot.TryAdd(_layout, LayoutProportions.Equal);
        return snapshot;
    }

    private void RestoreSavedWindowPlacement()
    {
        try
        {
            if (_savedWindowPlacement is { } placement)
            {
                NativeWindowPlacement.Restore(new WindowInteropHelper(this).Handle, placement);
                WindowState = placement.Maximized ? WindowState.Maximized : WindowState.Normal;
            }
            if (_windowPlacementStore.LoadWarning is { } warning) EnvironmentStatus.Text = warning;
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"恢复窗口位置失败：{ex.Message}";
        }
        finally { _windowPlacementReady = true; }
    }

    private void SaveWindowPlacement()
    {
        if (!_windowPlacementReady) return;
        // F11 has its own transient geometry; restarting from full screen must
        // retain the window placement captured before entering immersive mode.
        var placement = _fullScreenWindowPlacement ?? NativeWindowPlacement.Capture(
            new WindowInteropHelper(this).Handle, _lastNonMinimizedMaximized);
        _windowPlacementStore.Save(placement);
    }

    private bool SaveSession()
    {
        // The placement sidecar has an independent external-edit guard. A
        // placement conflict must not prevent the existing tab session saving.
        string? placementFailure = null;
        try { SaveWindowPlacement(); }
        catch (Exception ex) { placementFailure = $"窗口位置保存失败：{ex.Message}"; }
        try
        {
            _sessionStore.Save(new SessionState(_layout, _defaultViewMode,
                _panes.Select(p => p.SnapshotTabs().ToList()).ToList(),
                Array.IndexOf(_panes, ActivePane),
                _panes.Select(p => p.SelectedTabIndex).ToArray(), _defaultIconSize,
                _paneTreeVisibility.ToArray(), _treeDisplayMode));
            _pinnedTabsStore.Save(_panes.SelectMany((pane, index) => pane.SnapshotPins(index)).ToArray());
            if (placementFailure is not null)
            {
                EnvironmentStatus.Text = placementFailure;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"配置保存失败：{ex.Message}" +
                (placementFailure is null ? "" : $"；{placementFailure}");
            return false;
        }
    }

    internal bool SaveSessionForRestart()
    {
        if (SaveSession()) return true;
        MessageBox.Show(this, EnvironmentStatus.Text, "无法重启 Explorer Dirs",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    internal SessionState SnapshotSession() => new(_layout, _defaultViewMode,
        _panes.Select(p => p.SnapshotTabs().ToList()).ToList(), Array.IndexOf(_panes, ActivePane),
        _panes.Select(p => p.SelectedTabIndex).ToArray(), _defaultIconSize,
        _paneTreeVisibility.ToArray(), _treeDisplayMode);

    internal IReadOnlyList<PinnedTabEntry> SnapshotPinnedTabs() =>
        _panes.SelectMany((pane, index) => pane.SnapshotPins(index)).ToArray();

    internal void ReportSessionIssue(string message) => EnvironmentStatus.Text = message;

}
