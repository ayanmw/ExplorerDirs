using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace ExplorerDirs.App;

public partial class PaneControl : UserControl
{
    public static readonly DependencyProperty MultilineTabHeadersProperty = DependencyProperty.Register(
        nameof(MultilineTabHeaders), typeof(bool), typeof(PaneControl), new PropertyMetadata(false));
    public static readonly DependencyProperty LimitTabHeaderWidthProperty = DependencyProperty.Register(
        nameof(LimitTabHeaderWidth), typeof(bool), typeof(PaneControl), new PropertyMetadata(true));

    private const string TabDragFormat = "ExplorerDirs.TabReorder.v1";
    // The Details column header is one Shell focus stop; arrow keys move among columns.
    private readonly Stack<(TabSettings Settings, AddressPathHistory AddressHistory)> _closedTabs = new();
    private readonly List<TabItem> _tabOrder = [];
    private readonly Func<ViewState> _newTabView;
    private readonly QuickFilterFavoritesStore _quickFilterFavoritesStore;
    private readonly QuickFilterHistoryStore _quickFilterHistoryStore;
    private readonly DispatcherTimer _tabHoverActivationTimer = new();
    private readonly TabHoverActivationGate _tabHoverActivationGate = new();
    private int _tabHoverActivationDelayMilliseconds = TabHoverActivationPreference.DefaultDelayMilliseconds;
    private bool _newTabUsesCurrentFolder;
    private bool _showTabCloseButtons;
    private HiddenItemsPreference _hiddenItemsPreference;
    private bool _selectionCheckBoxes;
    private bool? _fullRowSelection;
    private bool _disableGrouping;
    private bool _showSelectionSizeInStatusBar = true;
    private bool _showFolderSizeWhenEmpty;
    private bool _updatingFilterPresets;
    private TabItem? _dragCandidate;
    private Point _dragStart;
    private TabItem? _draggedTab;
    private string? _dragToken;
    private bool _addressEditing;
    private bool _classicAddressBar;
    private TabStripVisibilityMode _tabStripVisibilityMode = TabStripVisibilityMode.Always;
    private bool _addressNavigationPending;
    private bool _searchTextDirty;
    private bool _updatingSearchText;
    private TabState? _searchCloseFocusPending;
    private bool _compactMode;
    private bool? _activePaneVisual;
    private bool _autoExpandTreeSelection = true;
    private long _treeSelectionSyncRequestVersion;
    private ShellTreeHost? _treeHost;
    private double _treeWidth = 160;
    private Func<TreeFocusTarget>? _treeFocusTarget;
    private Func<bool>? _sharedTreeHasFocus;
    private Func<bool>? _focusSharedTree;
    private readonly DiskSpaceTracker _diskSpace = new(DiskSpaceReader.Shared.ReadAsync);
    private TabState? _diskSpaceOwner;
    private readonly DispatcherTimer _itemCountsTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(750)
    };
    private readonly DispatcherTimer _selectionCountsRefreshTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(75)
    };
    private readonly ShellStatusCountRefreshGate _selectionCountsRefreshGate = new();
    private CancellationTokenSource? _recursiveSizeCancellation;
    private TabState? _recursiveSizeOwner;
    private string? _recursiveSizePath;
    private bool _recursiveSizeIsSelection;
    private RecursiveDirectoryScanResult? _lastRecursiveSizeResult;
    private string? _lastRecursiveSizePath;
    private TabState? _lastRecursiveSizeOwner;
    private bool _lastRecursiveSizeIsSelection;

    private sealed class TabState
    {
        public required PaneControl Owner { get; set; }
        public required TabItem Tab { get; init; }
        public required ExplorerBrowserHost Browser { get; init; }
        // Shell confirms location before the queued pane notification updates
        // the chrome. Commands must not use a second, lagging directory model.
        public string Path => Browser.CurrentPath;
        public bool IsPinned { get; set; }
        public Button? CloseButton { get; set; }
        public bool SearchRequestPending { get; set; }
        public string? StartupSelectPath { get; set; }
        public string? SearchScopePath { get; set; }
        public AddressPathHistory AddressHistory { get; init; } = new();
    }

    public event EventHandler? StateChanged;
    internal event EventHandler? CommandContextChanged;
    internal event EventHandler? TreeWidthChanged;
    internal event Action<PaneControl, string>? SelectedFolderForTree;
    public event EventHandler? Activated;

    internal void SetAutoExpandTreeSelection(bool enabled)
    {
        _autoExpandTreeSelection = enabled;
        _treeSelectionSyncRequestVersion++;
    }

    internal void SetTabStripVisibilityMode(TabStripVisibilityMode mode)
    {
        _tabStripVisibilityMode = mode;
        UpdateTabStripVisibility();
    }

    internal void SetTabHoverActivationDelay(int delayMilliseconds)
    {
        if (!TabHoverActivationPreference.AllowedDelayMilliseconds.Contains(delayMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
        CancelTabHoverActivation();
        _tabHoverActivationDelayMilliseconds = delayMilliseconds;
        if (delayMilliseconds > 0)
            _tabHoverActivationTimer.Interval = TimeSpan.FromMilliseconds(delayMilliseconds);
    }

    internal void SetNewTabUsesCurrentFolder(bool enabled) => _newTabUsesCurrentFolder = enabled;

    internal void SetShowTabCloseButtons(bool enabled)
    {
        _showTabCloseButtons = enabled;
        foreach (var tab in _tabOrder)
        {
            var state = (TabState)tab.Tag;
            if (enabled) AddTabCloseButton(state);
            else RemoveTabCloseButton(state);
        }
        UpdateTabCloseButtonStates();
    }

    public bool MultilineTabHeaders
    {
        get => (bool)GetValue(MultilineTabHeadersProperty);
        set => SetValue(MultilineTabHeadersProperty, value);
    }

    public bool LimitTabHeaderWidth
    {
        get => (bool)GetValue(LimitTabHeaderWidthProperty);
        set => SetValue(LimitTabHeaderWidthProperty, value);
    }

    internal void SetMultilineTabHeaders(bool enabled) => MultilineTabHeaders = enabled;
    internal void SetLimitTabHeaderWidth(bool enabled) => LimitTabHeaderWidth = enabled;

    internal void SetStatusBarVisible(bool visible) =>
        StatusBarRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    internal void SetStatusBarSelectionSizeVisible(bool visible)
    {
        _showSelectionSizeInStatusBar = visible;
        RefreshItemCounts();
    }

    internal void SetStatusBarFolderSizeWhenEmpty(bool enabled)
    {
        _showFolderSizeWhenEmpty = enabled;
        RefreshItemCounts();
    }

    internal void SetActivePaneVisual(bool isActive)
    {
        if (_activePaneVisual == isActive) return;
        _activePaneVisual = isActive;
        // Active/inactive borders used to change the content inset by one pixel,
        // resizing both Shell views on every focus transfer. Reserve two pixels
        // in either state while keeping the existing one/two-pixel visual border.
        RootBorder.Padding = isActive ? new Thickness(0) : new Thickness(1);
        RootBorder.BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
        RootBorder.SetResourceReference(Border.BorderBrushProperty,
            isActive ? "ExplorerDirs.AccentBrush" : "ExplorerDirs.BorderBrush");
    }
    internal event Action<PaneControl, ShellMenuPoint>? HeaderPresetMenuRequested;
    internal event Action<PaneControl, PropertyKeyState, string, ShellMenuPoint>? ColumnValueFilterMenuRequested;
    internal event Func<PaneControl, ExplorerShortcut, bool>? ShortcutRequested;
    internal Func<bool>? MayRestoreFileFocus { get; set; }
    internal Func<bool>? MayKeepFileFocusRestorePending { get; set; }

    internal PaneControl(IEnumerable<TabSettings> tabs, Func<ViewState> newTabView,
        IReadOnlySet<int>? pinnedIndices, int selectedTabIndex,
        QuickFilterFavoritesStore quickFilterFavoritesStore,
        QuickFilterHistoryStore quickFilterHistoryStore,
        HiddenItemsPreference hiddenItemsPreference = HiddenItemsPreference.FollowWindows,
        bool selectionCheckBoxes = false, bool? fullRowSelection = null, bool disableGrouping = false)
    {
        InitializeComponent();
        BackButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Back), 14);
        ForwardButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Forward), 14);
        UpButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Up), 14);
        RefreshButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Refresh), 14);
        TreeButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Tree));
        NewTabButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.NewTab), 14);
        CloseTabButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Close), 14);
        ToolbarViewStyleButton.Content = ViewStyleMenu.CreateToolbarIcon();
        ViewStyleButton.Content = ViewStyleMenu.CreateToolbarIcon();
        AddressMenuButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.AddressDropDown), 12);
        AddressHistoryButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.History), 14);
        AddressTreeHost.DirectorySelected += identity =>
        {
            var owner = Current;
            CloseAddressTree();
            if (owner is not null && ReferenceEquals(owner, Current)) Navigate(identity);
        };
        AddressTreeHost.EscapeRequested += () => CloseAddressTree(focusAddress: true);
        AddressTreeHost.ShortcutHandler = shortcut => ShortcutRequested?.Invoke(this, shortcut) == true;
        AddressTreeHost.Error += message => Status.Text = message;
        AddressTreeHost.SetAddressTreeLocation(null);
        _diskSpace.Changed += ShowDiskSpace;
        var viewMenu = new ContextMenu { PlacementTarget = ViewStyleButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        ViewStyleMenu.Populate(viewMenu, SetViewStyle);
        viewMenu.Opened += (_, _) =>
        {
            Activated?.Invoke(this, EventArgs.Empty);
            ViewStyleMenu.Update(viewMenu, ReadViewStyle());
        };
        ViewStyleButton.ContextMenu = viewMenu;
        ToolbarViewStyleButton.ContextMenu = viewMenu;
        _newTabView = newTabView;
        _quickFilterFavoritesStore = quickFilterFavoritesStore;
        _quickFilterHistoryStore = quickFilterHistoryStore;
        _tabHoverActivationTimer.Tick += TabHoverActivationTimer_Tick;
        Unloaded += (_, _) => CancelTabHoverActivation();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) CancelTabHoverActivation();
        };
        QuickFilterBox.ItemsSource = _quickFilterHistoryStore.Load();
        _hiddenItemsPreference = hiddenItemsPreference;
        _selectionCheckBoxes = selectionCheckBoxes;
        _fullRowSelection = fullRowSelection;
        _disableGrouping = disableGrouping;
        QuickFilterPresetCombo.DropDownOpened += (_, _) => RefreshQuickFilterPresets(QuickFilterPresetCombo.Text);
        var tabIndex = 0;
        foreach (var tab in tabs)
            AddTab(tab.Path, tab.View, false, pinnedIndices?.Contains(tabIndex++) == true,
                quickFilter: tab.QuickFilter, previewPaneVisible: tab.PreviewPaneVisible,
                detailsPaneVisible: tab.DetailsPaneVisible);
        if (Tabs.Items.Count == 0)
            AddTab(Home, _newTabView(), false);
        Tabs.SelectedItem = _tabOrder[Math.Clamp(selectedTabIndex, 0, _tabOrder.Count - 1)];
        RenderBreadcrumb(CurrentPath);
        _itemCountsTimer.Tick += (_, _) => { RefreshItemCounts(); RefreshDiskSpace(); };
        _selectionCountsRefreshTimer.Tick += (_, _) =>
        {
            _selectionCountsRefreshTimer.Stop();
            if (_selectionCountsRefreshGate.TryConsume(IsVisible)) RefreshItemCounts();
        };
        Loaded += (_, _) =>
        {
            if (IsVisible) { RefreshItemCounts(); RefreshDiskSpace(); _itemCountsTimer.Start(); }
        };
        Unloaded += (_, _) =>
        {
            CloseAddressTree();
            CancelRecursiveDirectorySize();
            CancelSelectionCountsRefresh();
            _itemCountsTimer.Stop();
            _diskSpace.Reset();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && IsLoaded)
            {
                RefreshItemCounts(); RefreshDiskSpace(); _itemCountsTimer.Start();
                UpdateShellStatusHint(Current?.Browser.ShellStatusText);
            }
            else
            {
                CloseAddressTree();
                CancelRecursiveDirectorySize();
                CancelSelectionCountsRefresh();
                _itemCountsTimer.Stop(); ItemCounts.Text = ""; ItemCounts.ToolTip = null; _diskSpace.Reset();
                UpdateShellStatusHint(null);
            }
        };
    }

    private void RefreshDiskSpace()
    {
        if (!IsLoaded || !IsVisible) { _diskSpace.Reset(); return; }
        if (Current != _diskSpaceOwner) _diskSpace.Reset();
        _diskSpaceOwner = Current;
        _ = _diskSpace.UpdateAsync(Current?.Browser.DiskSpaceDirectory);
    }

    private void ShowDiskSpace(DiskSpaceSnapshot snapshot)
    {
        if (snapshot.Path is null)
        {
            DiskSpace.Visibility = Visibility.Collapsed;
            DiskSpace.Text = "";
            DiskSpace.ToolTip = null;
            return;
        }
        // Navigation may have started since the last timer tick. Do not
        // display a completion for an old directory, even during that gap.
        if (Current != _diskSpaceOwner || !IsVisible ||
            !string.Equals(Current?.Browser.DiskSpaceDirectory, snapshot.Path, StringComparison.OrdinalIgnoreCase))
            return;
        DiskSpace.Visibility = Visibility.Visible;
        DiskSpace.Text = snapshot.Pending ? "空间读取中…" : snapshot.Info?.DisplayText ?? "空间暂不可用";
        DiskSpace.ToolTip = snapshot.Info?.Description ?? DiskSpace.Text;
    }

    private void RefreshItemCounts()
    {
        if (!IsLoaded || !IsVisible) return;
        var state = Current;
        var counts = state?.Browser.ReadItemCounts(includeSelectionSize: _showSelectionSizeInStatusBar,
            includeFolderSize: _showFolderSizeWhenEmpty);
        if (Current == state && IsLoaded && IsVisible)
        {
            ItemCounts.Text = counts?.DisplayText ?? "项目统计暂不可用";
            ItemCounts.ToolTip = counts?.Description;
        }
    }

    private void QueueSelectionCountsRefresh(TabState state)
    {
        if (!_selectionCountsRefreshGate.Request(Current == state, IsVisible)) return;
        _selectionCountsRefreshTimer.Stop();
        _selectionCountsRefreshTimer.Start();
    }

    private void CancelSelectionCountsRefresh()
    {
        _selectionCountsRefreshTimer.Stop();
        _selectionCountsRefreshGate.Cancel();
    }

    public void ToggleRecursiveDirectorySize()
    {
        if (_recursiveSizeCancellation is not null)
        {
            CancelRecursiveDirectorySize();
            return;
        }
        if (Current is not { } state || !state.Browser.IsFileSystemLocation)
        {
            RecursiveSizeStatus.Text = "递归大小仅支持文件系统目录。";
            RecursiveSizeStatus.Visibility = Visibility.Visible;
            return;
        }

        var path = state.Path;
        var cancellation = new CancellationTokenSource();
        _recursiveSizeCancellation = cancellation;
        _recursiveSizeOwner = state;
        _recursiveSizePath = path;
        _recursiveSizeIsSelection = false;
        _lastRecursiveSizeResult = null;
        _lastRecursiveSizePath = null;
        _lastRecursiveSizeOwner = null;
        _lastRecursiveSizeIsSelection = false;
        RecursiveSizeStatus.Text = "正在计算目录总大小… 已检查 0 项 · 再按 F9 取消";
        RecursiveSizeStatus.ToolTip = path;
        RecursiveSizeStatus.Visibility = Visibility.Visible;
        _ = ScanRecursiveDirectoryAsync(state, path, cancellation, null);
    }

    public void CalculateSelectedItemsRecursiveSize()
    {
        if (_recursiveSizeCancellation is not null)
        {
            CancelRecursiveDirectorySize();
        }

        if (Current is not { } state || !state.Browser.IsFileSystemLocation)
        {
            RecursiveSizeStatus.Text = "递归大小仅支持文件系统项目。";
            RecursiveSizeStatus.Visibility = Visibility.Visible;
            return;
        }

        IReadOnlyList<string> paths;
        try { paths = state.Browser.CaptureSelectedFileSystemPaths(); }
        catch (Exception ex)
        {
            RecursiveSizeStatus.Text = $"无法读取所选项目：{ex.Message}";
            RecursiveSizeStatus.Visibility = Visibility.Visible;
            return;
        }
        if (paths.Count == 0)
        {
            RecursiveSizeStatus.Text = "请先选择文件或文件夹。";
            RecursiveSizeStatus.ToolTip = null;
            RecursiveSizeStatus.Visibility = Visibility.Visible;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _recursiveSizeCancellation = cancellation;
        _recursiveSizeOwner = state;
        _recursiveSizePath = state.Path;
        _recursiveSizeIsSelection = true;
        _lastRecursiveSizeResult = null;
        _lastRecursiveSizePath = null;
        _lastRecursiveSizeOwner = null;
        _lastRecursiveSizeIsSelection = false;
        RecursiveSizeStatus.Text = "正在计算所选项目递归大小… 已检查 0 项 · 再按 F9 取消";
        RecursiveSizeStatus.ToolTip = $"选择项目数：{paths.Count:N0}";
        RecursiveSizeStatus.Visibility = Visibility.Visible;
        _ = ScanRecursiveDirectoryAsync(state, state.Path, cancellation, paths.ToArray());
    }

    private async Task ScanRecursiveDirectoryAsync(TabState owner, string path, CancellationTokenSource cancellation,
        string[]? selectedPaths)
    {
        try
        {
            var progressThrottle = new RecursiveScanProgressThrottle(TimeSpan.FromMilliseconds(250));
            var isSelection = selectedPaths is not null;
            void ReportProgress(long entriesChecked)
            {
                if (!progressThrottle.ShouldReport(Stopwatch.GetTimestamp())) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (IsCurrentRecursiveSizeScan(owner, path, cancellation))
                        RecursiveSizeStatus.Text = isSelection
                            ? $"正在计算所选项目递归大小… 已检查 {entriesChecked:N0} 项 · 再按 F9 取消"
                            : $"正在计算目录总大小… 已检查 {entriesChecked:N0} 项 · 再按 F9 取消";
                }));
            }

            var result = await Task.Run(() => selectedPaths is null
                ? RecursiveDirectoryScanner.Scan(path, cancellation.Token, ReportProgress)
                : RecursiveDirectoryScanner.ScanSelection(selectedPaths, cancellation.Token, ReportProgress),
                cancellation.Token).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentRecursiveSizeScan(owner, path, cancellation)) return;
                _lastRecursiveSizeResult = result;
                _lastRecursiveSizePath = path;
                _lastRecursiveSizeOwner = owner;
                _lastRecursiveSizeIsSelection = isSelection;
                RenderRecursiveSizeResult(path, result, isSelection);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentRecursiveSizeScan(owner, path, cancellation)) return;
                // A retired worker may fail after another scan has completed.
                // Check ownership on the UI thread before clearing its result.
                _lastRecursiveSizeResult = null;
                _lastRecursiveSizePath = null;
                _lastRecursiveSizeOwner = null;
                RecursiveSizeStatus.Text = $"无法计算{(selectedPaths is null ? "目录" : "所选项目")}大小：{ex.Message}";
                RecursiveSizeStatus.ToolTip = path;
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(_recursiveSizeCancellation, cancellation))
                {
                    _recursiveSizeCancellation = null;
                    _recursiveSizeOwner = null;
                    _recursiveSizePath = null;
                    _recursiveSizeIsSelection = false;
                }
                cancellation.Dispose();
            });
        }
    }

    private bool IsCurrentRecursiveSizeScan(TabState owner, string path, CancellationTokenSource cancellation) =>
        ReferenceEquals(_recursiveSizeCancellation, cancellation) && ReferenceEquals(_recursiveSizeOwner, owner) &&
        Current == owner && IsVisible && string.Equals(_recursiveSizePath, path, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(owner.Path, path, StringComparison.OrdinalIgnoreCase);

    private void CancelRecursiveDirectorySize()
    {
        var cancellation = _recursiveSizeCancellation;
        _recursiveSizeCancellation = null;
        _recursiveSizeOwner = null;
        _recursiveSizePath = null;
        _recursiveSizeIsSelection = false;
        _lastRecursiveSizeResult = null;
        _lastRecursiveSizePath = null;
        _lastRecursiveSizeOwner = null;
        _lastRecursiveSizeIsSelection = false;
        if (cancellation is not null) cancellation.Cancel();
        RecursiveSizeStatus.Text = "";
        RecursiveSizeStatus.ToolTip = null;
        RecursiveSizeStatus.Visibility = Visibility.Collapsed;
    }

    internal void RefreshSizeFormatting()
    {
        RefreshItemCounts();
        RefreshDiskSpace();
        if (_lastRecursiveSizeResult is { } result && _lastRecursiveSizePath is { } path)
            RenderRecursiveSizeResult(path, result, _lastRecursiveSizeIsSelection);
    }

    private void RenderRecursiveSizeResult(string path, RecursiveDirectoryScanResult result, bool isSelection)
    {
        var summary = ByteSizeFormatter.FormatRecursiveDirectorySummary(result, ByteSizeFormatter.Options);
        RecursiveSizeStatus.Text = isSelection ? $"所选项目：{summary}" : summary;
        RecursiveSizeStatus.ToolTip = $"{(isSelection ? "所选项目" : path)}\n不跟随重解析点；无法读取或已跳过位置：{result.Unreadable:N0}";
    }

    private void RefreshNavigationAvailability()
    {
        BackButton.IsEnabled = Current?.Browser.CanGoBack == true;
        ForwardButton.IsEnabled = Current?.Browser.CanGoForward == true;
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string CurrentPath => Current?.Path ?? Home;
    private string AddressTextPath => ClassicAddressBarPath.Format(CurrentPath,
        Current?.Browser.IsFileSystemLocation == true);
    internal bool IsCurrentFileSystemLocation => Current?.Browser.IsFileSystemLocation == true;
    internal bool TreeIsVisible => TreeVisible;
    internal double TreeWidth
    {
        get => TreeVisible ? TreeColumn.Width.Value : _treeWidth;
        set
        {
            if (!NavigationTreeWidths.IsWidth(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _treeWidth = value;
            if (TreeVisible) TreeColumn.Width = new GridLength(value);
        }
    }

    internal void SetPaneTreeButtonVisible(bool visible) =>
        TreeButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    internal int SelectedTabIndex => Tabs.SelectedItem is TabItem tab
        ? Math.Max(0, _tabOrder.IndexOf(tab)) : 0;
    internal IReadOnlyList<TabSettings> SnapshotTabs()
    {
        CaptureVisibleViewState();
        if (_tabOrder.Any(tab => ((TabState)tab.Tag).Browser.ViewCaptureFailed))
            throw new IOException("有标签的视图配置暂时不可读取；未覆盖保存配置。");
        return _tabOrder.Select(tab =>
        {
            var state = (TabState)tab.Tag;
            // Native completion updates the browser before its queued pane notice.
            // Persist its confirmed directory, not the lagging presentation path.
            return new TabSettings(state.Browser.CurrentPath, state.Browser.CachedViewState(), state.Browser.QuickFilter,
                state.Browser.PreviewPaneVisible, state.Browser.DetailsPaneVisible);
        }).ToArray();
    }
    internal ViewState CaptureCurrentViewState()
    {
        var state = Current ?? throw new IOException("当前窗格没有活动标签。");
        CaptureViewState(state);
        if (state.Browser.ViewCaptureFailed)
            throw new IOException("当前标签的视图配置暂时不可读取。");
        return state.Browser.CachedViewState();
    }
    internal bool ApplyViewPreset(ViewPreset preset)
    {
        var state = Current;
        if (state?.Browser.ApplyViewPreset(preset) != true || Current != state) return false;
        StateChanged?.Invoke(this, EventArgs.Empty);
        FocusCurrentView();
        return true;
    }
    internal void CaptureVisibleViewState()
    {
        if (IsVisible && Current is { } state && state.Browser.IsVisible)
            CaptureViewState(state);
    }

    private void CaptureViewState(TabState state)
    {
        try { state.Browser.CaptureViewState(); }
        catch (Exception) { /* The host records failure; session save will reject its stale cache. */ }
    }
    internal IReadOnlyList<PinnedTabEntry> SnapshotPins(int paneIndex) => _tabOrder
        .Select((tab, index) => (State: (TabState)tab.Tag, Index: index))
        .Where(item => item.State.IsPinned)
        .Select(item => new PinnedTabEntry(paneIndex, item.Index, item.State.Browser.CurrentPath)).ToArray();
    private TabState? Current => (Tabs.SelectedItem as TabItem)?.Tag as TabState;

    private sealed record TabDragPayload(PaneControl Source, TabItem Tab, string Token);

    private bool CanAddTabs(int count)
    {
        if (count <= SessionStore.MaximumTabsPerPane - _tabOrder.Count) return true;
        Status.Text = $"单窗格最多保留 {SessionStore.MaximumTabsPerPane} 个标签；请关闭一些标签后重试。";
        return false;
    }
    public void NewTab() => OpenPathInNewTab(_newTabUsesCurrentFolder ? CurrentPath : Home);
    public void OpenPathInNewTab(string path)
    {
        if (CanAddTabs(1)) AddTab(path, _newTabView());
    }
    internal bool OpenPathAndSelectInNewTab(string directory, string selectedPath)
    {
        if (!CanAddTabs(1)) return false;
        AddTab(directory, _newTabView(), startupSelectPath: selectedPath);
        return true;
    }
    internal void ImportTabs(IReadOnlyList<TabSettings> tabs, int selectedIndex,
        IReadOnlySet<int>? pinned = null)
    {
        // Validate the whole incoming batch before the first AddTab event.
        if (!CanAddTabs(tabs.Count)) throw new InvalidOperationException(Status.Text);
        var imported = tabs.Select((tab, index) =>
            AddTab(tab.Path, tab.View.Copy(), select: false, pinned: pinned?.Contains(index) == true,
                quickFilter: tab.QuickFilter, previewPaneVisible: tab.PreviewPaneVisible,
                detailsPaneVisible: tab.DetailsPaneVisible)).ToArray();
        // Pinned additions insert before ordinary existing tabs. Select by
        // identity instead of a now-invalid appended numeric offset.
        if (imported.Length > 0) Tabs.SelectedItem = imported[Math.Clamp(selectedIndex, 0, imported.Length - 1)];
    }
    public void DuplicateCurrentTab()
    {
        if (Current is { } state)
            DuplicateTab(state);
    }
    private void DuplicateTab(TabState state)
    {
        if (!CanAddTabs(1)) return;
        try
        {
            var view = state.Browser.CaptureViewState();
            if (state.Browser.ViewCaptureFailed)
            {
                Status.Text = "视图配置暂时不可读取，未复制标签。";
                return;
            }
            AddTab(state.Browser.CurrentPath, view, pinned: state.IsPinned, quickFilter: state.Browser.QuickFilter,
                previewPaneVisible: state.Browser.PreviewPaneVisible,
                detailsPaneVisible: state.Browser.DetailsPaneVisible,
                addressHistory: state.AddressHistory.Copy());
        }
        catch (Exception ex)
        {
            Status.Text = $"复制标签失败：{ex.Message}";
        }
    }
    public void RefreshCurrent() => Current?.Browser.Refresh();

    internal IReadOnlyList<string> SetHiddenItemsPreference(HiddenItemsPreference preference)
    {
        if (_hiddenItemsPreference == preference) return [];
        _hiddenItemsPreference = preference;
        List<string> failures = [];
        foreach (var state in _tabOrder.Select(tab => (TabState)tab.Tag))
        {
            try { state.Browser.SetHiddenItemsPreference(preference); }
            catch (Exception ex) { failures.Add($"{state.Path}: {ex.Message}"); }
        }
        return failures;
    }

    internal IReadOnlyList<string> SetSelectionCheckBoxes(bool enabled)
    {
        _selectionCheckBoxes = enabled;
        List<string> failures = [];
        foreach (var state in _tabOrder.Select(tab => (TabState)tab.Tag))
        {
            try
            {
                var result = state.Browser.SetSelectionCheckBoxes(enabled);
                if (result < 0) failures.Add($"{state.Path}: Shell 错误 0x{result:X8}");
            }
            catch (Exception ex) { failures.Add($"{state.Path}: {ex.Message}"); }
        }
        return failures;
    }

    internal IReadOnlyList<string> SetFullRowSelection(bool? enabled)
    {
        _fullRowSelection = enabled;
        if (enabled is not { } value) return [];
        List<string> failures = [];
        foreach (var state in _tabOrder.Select(tab => (TabState)tab.Tag))
        {
            try
            {
                var result = state.Browser.SetFullRowSelection(value);
                if (result < 0) failures.Add($"{state.Path}: Shell 错误 0x{result:X8}");
            }
            catch (Exception ex) { failures.Add($"{state.Path}: {ex.Message}"); }
        }
        return failures;
    }

    internal IReadOnlyList<string> SetGroupingDisabled(bool disabled)
    {
        _disableGrouping = disabled;
        List<string> failures = [];
        foreach (var state in _tabOrder.Select(tab => (TabState)tab.Tag))
        {
            try
            {
                var result = state.Browser.SetGroupingDisabled(disabled);
                if (result < 0) failures.Add($"{state.Path}: Shell 错误 0x{result:X8}");
            }
            catch (Exception ex) { failures.Add($"{state.Path}: {ex.Message}"); }
        }
        return failures;
    }

    internal void RefreshTheme()
    {
        _treeHost?.RefreshTheme();
        AddressTreeHost.RefreshTheme();
        foreach (var tab in _tabOrder.ToArray()) ((TabState)tab.Tag).Browser.RefreshTheme();
    }

    internal IReadOnlyList<string> RefreshForWindowsHiddenItemsChange()
    {
        List<string> failures = [];
        foreach (var state in _tabOrder.Select(tab => (TabState)tab.Tag))
        {
            try { state.Browser.RefreshForWindowsHiddenItemsChange(); }
            catch (Exception ex) { failures.Add($"{state.Path}: {ex.Message}"); }
        }
        return failures;
    }
    internal ProgramStarterPaneContext CaptureProgramStarterPaneContext() => Current?.Browser.CaptureProgramStarterPaneContext()
        ?? throw new InvalidOperationException("当前窗格没有可读取的文件视图。");
    internal IReadOnlyList<string> CaptureSelectedPathsForClipboard() =>
        Current?.Browser.CaptureSelectedPathsForClipboard()
        ?? throw new InvalidOperationException("当前没有可读取的文件视图。");
    internal ExplorerBrowserHost.SelectedItem? GetSelectedQuickLinkItem() => Current?.Browser.GetSelectedItem();
    internal FileViewExportSnapshot CaptureFileViewExportSnapshot() => Current?.Browser.CaptureFileViewExportSnapshot()
        ?? throw new InvalidOperationException("当前窗格没有可导出的文件视图。");
    internal ShellFileViewExportCapture.ColumnValueCapture BeginColumnValueCapture(PropertyKeyState key) =>
        Current?.Browser.BeginColumnValueCapture(key)
        ?? throw new InvalidOperationException("当前窗格没有可读取的文件视图。");

    internal bool IsCurrentColumnValueCapture(ShellFileViewExportCapture.ColumnValueCapture capture) =>
        ReferenceEquals(Current?.Browser, capture.Source) && capture.Source?.IsCurrentColumnValueCapture(capture) == true;

    internal bool ApplyColumnValueFilter(PropertyKeyState key, IReadOnlyList<string> selectedValues)
    {
        if (Current is not { } state) return false;
        var settings = state.Browser.QuickFilter;
        var filters = settings.ColumnFilters.Where(filter => filter.Key != key).ToList();
        if (selectedValues.Count > 0)
            filters.Add(new ColumnValueFilter(key, selectedValues));
        var updated = settings.WithColumnFilters(filters);
        if (SessionStore.ColumnFilterValidationError(updated) is { } validationError)
        {
            Status.Text = validationError;
            return false;
        }
        try
        {
            if (!state.Browser.SetQuickFilter(updated)) return false;
        }
        catch (Exception ex)
        {
            if (Current == state) Status.Text = $"应用列值筛选失败：{ex.Message}";
            return false;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
    internal FileViewPrintSnapshot CaptureFileViewPrintSnapshot() => Current?.Browser.CaptureFileViewPrintSnapshot()
        ?? throw new InvalidOperationException("当前窗格没有可打印的文件视图。");
    internal PhotoPrintSelectionResult CapturePhotoPrintSelection() => Current?.Browser.CapturePhotoPrintSelection()
        ?? throw new InvalidOperationException("当前窗格没有可读取的文件视图。");
    internal (uint Mode, int IconSize)? ReadViewStyle()
    {
        var state = Current;
        var style = state?.Browser.ReadViewStyle();
        return Current == state ? style : null;
    }
    internal bool PreviewPaneVisible => Current?.Browser.PreviewPaneVisible == true;
    internal bool DetailsPaneVisible => Current?.Browser.DetailsPaneVisible == true;
    internal bool SetPreviewPaneVisible(bool visible)
    {
        var state = Current;
        if (state is null || state.Browser.SetPreviewPaneVisible(visible) != true || Current != state)
            return false;
        StateChanged?.Invoke(this, EventArgs.Empty);
        FocusCurrentView();
        return true;
    }
    internal bool SetDetailsPaneVisible(bool visible)
    {
        var state = Current;
        if (state is null || state.Browser.SetDetailsPaneVisible(visible) != true || Current != state)
            return false;
        StateChanged?.Invoke(this, EventArgs.Empty);
        FocusCurrentView();
        return true;
    }
    private void ViewStyle_Click(object sender, RoutedEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);
        if (sender is FrameworkElement target) ViewStyleButton.ContextMenu.PlacementTarget = target;
        ViewStyleButton.ContextMenu.IsOpen = true;
    }
    internal void SetViewStyle(ViewStyle style) => TrySetViewStyle(style);
    private bool TrySetViewStyle(ViewStyle style)
    {
        var state = Current;
        if (state?.Browser.SetViewStyle(style) != true || Current != state) return false;
        StateChanged?.Invoke(this, EventArgs.Empty);
        FocusCurrentView();
        return true;
    }
    private bool CycleViewStyle(int delta)
    {
        var state = Current;
        if (state is null) return false;
        var notches = state.Browser.AccumulateCtrlWheel(delta);
        return notches == 0 || ApplyCompletedViewStyleDelta(state, notches * 120);
    }
    private bool ApplyCompletedViewStyleDelta(TabState state, int delta)
    {
        if (Current != state) return false;
        var current = state.Browser.ReadViewStyle();
        if (Current != state || current is not { } style) return false;
        return TrySetViewStyle(ViewStyleWheelCycle.Next(style.Mode, style.IconSize, delta));
    }
    private void RootBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0) return;

        // Native Shell views handle Ctrl+wheel in their HWND message path. This
        // routed handler extends the same gesture to the pane's WPF chrome.
        Activated?.Invoke(this, EventArgs.Empty);
        if (CycleViewStyle(e.Delta)) e.Handled = true;
    }
    public void NavigateTo(string path) => Navigate(path);
    public void CreateNewFolder() => Current?.Browser.CreateNewFolder();
    internal void ApplySelectionCommand(ShellSelectionCommand command) => Current?.Browser.ApplySelectionCommand(command);
    internal ShellItemCounts? ReadSelectionCounts()
    {
        var state = Current;
        var counts = state?.Browser.ReadItemCounts(includeSelectionSize: false);
        return Current == state ? counts : null;
    }
    internal Dictionary<ShellFileCommand, bool> QueryFileCommands()
    {
        var state = Current;
        var result = state?.Browser.QueryFileCommands();
        return Current == state && result is not null ? result
            : ShellFileCommands.All.ToDictionary(info => info.Command, _ => false);
    }
    internal bool InvokeFileCommand(ShellFileCommand command)
    {
        var state = Current;
        if (state is null) return false;
        // FocusView can select a previously unselected focused item. Invoke against
        // the existing Shell selection; restore content focus only after acceptance.
        return Current == state && state.Browser.InvokeFileCommand(command);
    }
    internal bool NotifyCreatedClipboardFile(string path)
    {
        var state = Current;
        if (state is null || !state.Browser.IsFileSystemLocation) return false;
        var queued = state.Browser.NotifyCreatedClipboardFile(path);
        return Current == state && queued;
    }
    public void GoParent() => Current?.Browser.GoParent();
    public void GoBack() => Current?.Browser.GoBack();
    public void GoForward() => Current?.Browser.GoForward();
    public bool FocusCurrentView() => Current?.Browser.FocusView() == true;
    internal bool ExpandTreeToCurrent()
    {
        if (!TreeVisible || _treeHost is null || Current?.Browser.IsFileSystemLocation != true) return false;
        _treeHost.SetDirectory(CurrentPath);
        return true;
    }

    internal void ExpandTreeToSelectedFolder(string identity)
    {
        if (TreeVisible) _treeHost?.SetSelectedFolderLocation(identity);
    }

    private void SyncTreeToCurrent() { if (Current is { } state) SyncTreeToCurrent(state); }

    private void SyncTreeToCurrent(TabState state)
    {
        if (_treeHost is null) return;
        if (state.Browser.IsFileSystemLocation) _treeHost.SetDirectory(state.Path);
        else _treeHost.SetVirtualLocation();
    }
    internal bool IsShellRenameEditorFocused => Current?.Browser.IsRenameEditorFocused() == true;
    private bool TreeVisible => _treeHost is not null && TreeContainer.Visibility == Visibility.Visible;
    private PaneFocusStopMap FocusMap => PaneFocusStopMap.Create(
        SearchBar.Visibility == Visibility.Visible,
        QuickFilterBar.Visibility == Visibility.Visible,
        CurrentTreeFocusTarget != TreeFocusTarget.None,
        Current?.Browser.HasColumnHeader() == true,
        TreeButton.Visibility == Visibility.Visible,
        paneViewStyleButtonVisible: ToolbarViewStyleButton.Visibility == Visibility.Visible,
        addressHistoryButtonVisible: true,
        addressPathButtonVisible: true,
        tabHeaderVisible: Tabs.Visibility == Visibility.Visible,
        statusBarVisible: StatusBarRow.Visibility == Visibility.Visible);
    internal int FocusStopCount => FocusMap.Count;
    internal IReadOnlyList<int> MajorFocusStops => FocusMap.MajorStops;
    private TreeFocusTarget CurrentTreeFocusTarget =>
        _treeFocusTarget?.Invoke() ?? (TreeVisible ? TreeFocusTarget.Pane : TreeFocusTarget.None);

    internal void SetTreeFocusRouting(Func<TreeFocusTarget> target, Func<bool> sharedHasFocus,
        Func<bool> focusSharedTree)
    {
        _treeFocusTarget = target;
        _sharedTreeHasFocus = sharedHasFocus;
        _focusSharedTree = focusSharedTree;
    }

    internal int CurrentFocusStop()
    {
        var focus = FocusMap;
        UIElement[] controls = focus.TreeButton >= 0
            ? [BackButton, ForwardButton, UpButton, RefreshButton, TreeButton, ToolbarViewStyleButton,
                Address, AddressMenuButton, AddressHistoryButton, GoButton, NewTabButton, CloseTabButton]
            : [BackButton, ForwardButton, UpButton, RefreshButton, ToolbarViewStyleButton,
                Address, AddressMenuButton, AddressHistoryButton, GoButton, NewTabButton, CloseTabButton];
        for (var index = 0; index < controls.Length; index++)
            if (controls[index].IsKeyboardFocusWithin) return index;
        if (focus.SearchCount == 3)
        {
            if (SearchBox.IsKeyboardFocusWithin) return focus.SearchStart;
            if (SearchSubmitButton.IsKeyboardFocusWithin) return focus.SearchStart + 1;
            if (CloseSearchButton.IsKeyboardFocusWithin) return focus.SearchStart + 2;
        }
        if (focus.FilterCount == 8)
        {
            UIElement[] filterControls = [QuickFilterBox, QuickFilterMode, FilterFoldersCheckBox,
                QuickFilterPresetCombo, SaveQuickFilterPresetButton, DeleteQuickFilterPresetButton,
                ApplyQuickFilterButton, CloseQuickFilterButton];
            for (var index = 0; index < filterControls.Length; index++)
                if (filterControls[index].IsKeyboardFocusWithin) return focus.FilterStart + index;
        }
        if (Tabs.Visibility == Visibility.Visible && Tabs.SelectedItem is TabItem tab && tab.IsKeyboardFocused)
            return focus.TabHeader;
        if (focus.StatusTools >= 0 && ViewStyleButton.IsKeyboardFocused) return focus.StatusTools;
        if (CurrentTreeFocusTarget switch
            {
                TreeFocusTarget.Shared => _sharedTreeHasFocus?.Invoke() == true,
                TreeFocusTarget.Pane => _treeHost?.HasNativeFocus() == true,
                _ => false
            }) return focus.Tree;
        if (Current?.Browser.HasNativeFocus() == true)
            return Current.Browser.IsColumnHeaderFocused() ? focus.ColumnHeader : focus.FileView;
        return -1;
    }

    internal bool FocusStop(int stop)
    {
        var focus = FocusMap;
        if (stop == focus.TreeButton) return TreeButton.Focus();
        UIElement? target = stop switch
        {
            0 => BackButton, 1 => ForwardButton, 2 => UpButton, 3 => RefreshButton,
            _ => null
        };
        if (stop == focus.PaneViewStyle) target = ToolbarViewStyleButton;
        if (stop == focus.Address) { FocusAddress(); return Address.IsKeyboardFocused; }
        if (stop == focus.AddressMenu) target = AddressMenuButton;
        if (stop == focus.AddressHistory) target = AddressHistoryButton;
        if (stop == focus.Go) target = GoButton;
        if (stop == focus.NewTab) target = NewTabButton;
        if (stop == focus.CloseTab) target = CloseTabButton;
        if (target is not null) return target.Focus();
        if (focus.SearchCount == 3)
        {
            if (stop == focus.SearchStart) return SearchBox.Focus();
            if (stop == focus.SearchStart + 1) return SearchSubmitButton.Focus();
            if (stop == focus.SearchStart + 2) return CloseSearchButton.Focus();
        }
        if (focus.FilterCount == 8)
        {
            var filterStop = stop - focus.FilterStart;
            UIElement[] filterControls = [QuickFilterBox, QuickFilterMode, FilterFoldersCheckBox,
                QuickFilterPresetCombo, SaveQuickFilterPresetButton, DeleteQuickFilterPresetButton,
                ApplyQuickFilterButton, CloseQuickFilterButton];
            if (filterStop >= 0 && filterStop < filterControls.Length)
                return filterControls[filterStop].Focus();
        }
        if (stop == focus.TabHeader)
            return (Tabs.SelectedItem as TabItem)?.Focus() == true;
        if (stop == focus.Tree)
            return CurrentTreeFocusTarget switch
            {
                TreeFocusTarget.Shared => _focusSharedTree?.Invoke() == true,
                TreeFocusTarget.Pane => _treeHost?.FocusTree() == true,
                _ => false
            };
        if (stop == focus.ColumnHeader && Current?.Browser.HasColumnHeader() == true)
            return Current?.Browser.FocusColumnHeader() == true;
        if (stop == focus.FileView) return FocusCurrentView();
        if (focus.StatusTools >= 0 && stop == focus.StatusTools) return ViewStyleButton.Focus();
        return false;
    }
    public void FocusSearch()
    {
        if (Current is not { } state) return;
        SetSearchScope(state, null);
        FocusSearchInput(state);
    }

    public void FocusSearchInSelectedFolder()
    {
        if (Current is not { } state) return;
        var scopePath = state.Browser.GetSelectedFolderSearchScope();
        if (scopePath is null)
        {
            SetSearchScope(state, null);
            Status.Text = "Alt+F3 需要在文件视图中只选择一个文件系统文件夹。";
            return;
        }

        SetSearchScope(state, scopePath);
        FocusSearchInput(state);
    }

    private void FocusSearchInput(TabState state)
    {
        if (ReferenceEquals(_searchCloseFocusPending, state)) _searchCloseFocusPending = null;
        SearchBar.Visibility = Visibility.Visible;
        if (!_searchTextDirty) SetSearchText(state.Browser.SearchQuery ?? string.Empty);
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SetSearchText(string text)
    {
        _updatingSearchText = true;
        try
        {
            SearchBox.Text = text;
            _searchTextDirty = false;
        }
        finally { _updatingSearchText = false; }
    }

    private void SyncSearchText(string query)
    {
        // Focus can already be on the submit button or another pane. Only a
        // matching result may acknowledge edited text; older results leave it alone.
        if (!_searchTextDirty || string.Equals(SearchBox.Text, query, StringComparison.Ordinal))
            SetSearchText(query);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingSearchText) _searchTextDirty = true;
    }

    private void SetSearchScope(TabState state, string? scopePath)
    {
        state.SearchScopePath = scopePath;
        var folderName = scopePath is null ? null : Path.GetFileName(
            scopePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        SearchScopeLabel.Text = scopePath is null ? "范围：当前目录" : $"范围：{(string.IsNullOrEmpty(folderName) ? scopePath : folderName)}";
        SearchScopeLabel.ToolTip = scopePath ?? state.Path;
        SearchSubmitButton.ToolTip = scopePath is null ? "搜索当前标签目录" : $"搜索所选文件夹：{scopePath}";
        SearchBox.ToolTip = scopePath is null
            ? "输入关键词后按 Enter 搜索；Esc 返回目录"
            : $"输入关键词后按 Enter 搜索所选文件夹：{scopePath}；Esc 返回原目录";
        System.Windows.Automation.AutomationProperties.SetName(SearchBox,
            scopePath is null ? "搜索当前目录" : $"搜索所选文件夹 {scopePath}");
    }

    internal bool IsQuickFilterInputFocused => QuickFilterBox.IsKeyboardFocusWithin;
    internal QuickFilterSettings CurrentQuickFilter => Current?.Browser.QuickFilter ?? QuickFilterSettings.Empty;
    internal bool HasFileViewFocus => _treeHost?.HasNativeFocus() != true && Current?.Browser.HasNativeFocus() == true;
    internal bool ContainsMouseWheelShellTarget(nint hwnd) => IsVisible &&
        (Current?.Browser.ContainsNativeWindow(hwnd) == true ||
         (TreeVisible && _treeHost?.ContainsNativeWindow(hwnd) == true));

    public void FocusQuickFilter()
    {
        if (Current is not { } state) return;
        QuickFilterBar.Visibility = Visibility.Visible;
        RefreshQuickFilterPresets(QuickFilterPresetCombo.Text);
        ShowQuickFilterEditor(state.Browser.QuickFilter);
        FocusQuickFilterEditor(selectAll: true);
    }

    private void ShowQuickFilterEditor(QuickFilterSettings settings)
    {
        QuickFilterBox.Text = settings.Pattern;
        QuickFilterMode.SelectedIndex = settings.ExcludeMatches ? 1 : 0;
        FilterFoldersCheckBox.IsChecked = settings.FilterFolders;
    }

    internal void ApplyQuickFilter()
    {
        if (Current is not { } state) return;
        var settings = state.Browser.QuickFilter.WithNameFilter(QuickFilterBox.Text,
            QuickFilterMode.SelectedIndex == 1, FilterFoldersCheckBox.IsChecked == true);
        try { if (!state.Browser.SetQuickFilter(settings)) return; }
        catch (Exception ex)
        {
            if (Current == state)
            {
                ShowQuickFilterEditor(state.Browser.QuickFilter);
                Status.Text = $"应用筛选失败：{ex.Message}";
            }
            return;
        }
        try
        {
            _quickFilterHistoryStore.Record(settings.Pattern);
            RefreshQuickFilterHistory(settings.Pattern);
        }
        catch (Exception ex) { Status.Text = $"筛选已应用，但保存筛选历史失败：{ex.Message}"; }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QuickFilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (QuickFilterHistoryKeyRouting.ShouldRemoveSelectedEntry((int)e.Key,
                QuickFilterBox.IsDropDownOpen, QuickFilterBox.SelectedItem is string) &&
            QuickFilterBox.SelectedItem is string selectedPattern)
        {
            var textToKeep = QuickFilterBox.Text;
            try
            {
                _quickFilterHistoryStore.Remove(selectedPattern);
                RefreshQuickFilterHistory(textToKeep, keepDropDownOpen: true);
            }
            catch (Exception ex) { Status.Text = $"删除筛选历史失败：{ex.Message}"; }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ApplyQuickFilter();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearQuickFilter();
            e.Handled = true;
        }
    }

    private void QuickFilterHistory_DropDownOpened(object sender, EventArgs e)
    {
        var textToKeep = QuickFilterBox.Text;
        RefreshQuickFilterHistory(textToKeep, keepDropDownOpen: true);
    }

    private void RefreshQuickFilterHistory(string textToKeep, bool keepDropDownOpen = false)
    {
        QuickFilterBox.ItemsSource = _quickFilterHistoryStore.Load();
        QuickFilterBox.SelectedIndex = -1;
        QuickFilterBox.Text = textToKeep;
        QuickFilterBox.IsDropDownOpen = keepDropDownOpen;
    }

    private void FocusQuickFilterEditor(bool selectAll)
    {
        QuickFilterBox.ApplyTemplate();
        if (QuickFilterBox.Template.FindName("PART_EditableTextBox", QuickFilterBox) is TextBox editor)
        {
            editor.Focus();
            if (selectAll) editor.SelectAll();
            return;
        }
        QuickFilterBox.Focus();
    }

    private void ApplyQuickFilter_Click(object sender, RoutedEventArgs e) => ApplyQuickFilter();
    private void ClearQuickFilter_Click(object sender, RoutedEventArgs e) => ClearQuickFilter();

    private void RefreshQuickFilterPresets(string? textToKeep)
    {
        _updatingFilterPresets = true;
        try
        {
            var favorites = _quickFilterFavoritesStore.Load();
            QuickFilterPresetCombo.ItemsSource = favorites;
            QuickFilterPresetCombo.SelectedItem = favorites.FirstOrDefault(item =>
                string.Equals(item.Name, textToKeep, StringComparison.OrdinalIgnoreCase));
            QuickFilterPresetCombo.Text = QuickFilterPresetCombo.SelectedItem is QuickFilterFavorite selected
                ? selected.Name : textToKeep ?? string.Empty;
            DeleteQuickFilterPresetButton.IsEnabled = QuickFilterPresetCombo.SelectedItem is QuickFilterFavorite;
        }
        finally { _updatingFilterPresets = false; }
    }

    private void QuickFilterPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilterPresets || e.Source != QuickFilterPresetCombo ||
            QuickFilterPresetCombo.SelectedItem is not QuickFilterFavorite favorite) return;
        ShowQuickFilterEditor(favorite.Settings);
        QuickFilterPresetCombo.Text = favorite.Name;
        DeleteQuickFilterPresetButton.IsEnabled = true;
        Status.Text = $"已载入筛选预设“{favorite.Name}”；按筛选或 Enter 应用。";
    }

    private void SaveQuickFilterPreset_Click(object sender, RoutedEventArgs e)
    {
        var settings = new QuickFilterSettings(QuickFilterBox.Text,
            QuickFilterMode.SelectedIndex == 1, FilterFoldersCheckBox.IsChecked == true);
        if (!settings.HasPattern)
        {
            Status.Text = "请输入筛选模式后再保存预设。";
            return;
        }
        var name = QuickFilterPresetCombo.Text;
        try
        {
            _quickFilterFavoritesStore.Save([.. _quickFilterFavoritesStore.Load(),
                new QuickFilterFavorite(name, settings)]);
            RefreshQuickFilterPresets(name.Trim());
            Status.Text = $"筛选预设“{name.Trim()}”已保存。";
        }
        catch (Exception ex) { Status.Text = $"保存筛选预设失败：{ex.Message}"; }
    }

    private void DeleteQuickFilterPreset_Click(object sender, RoutedEventArgs e)
    {
        if (QuickFilterPresetCombo.SelectedItem is not QuickFilterFavorite selected) return;
        try
        {
            _quickFilterFavoritesStore.Save(_quickFilterFavoritesStore.Load().Where(item =>
                !string.Equals(item.Name, selected.Name, StringComparison.OrdinalIgnoreCase)));
            RefreshQuickFilterPresets(string.Empty);
            Status.Text = $"筛选预设“{selected.Name}”已删除。";
        }
        catch (Exception ex) { Status.Text = $"删除筛选预设失败：{ex.Message}"; }
    }

    private void ClearQuickFilter()
    {
        QuickFilterBox.IsDropDownOpen = false;
        if (Current is not { } state) return;
        try { if (!state.Browser.SetQuickFilter(QuickFilterSettings.Empty)) return; }
        catch (Exception ex)
        {
            if (Current == state)
            {
                ShowQuickFilterEditor(state.Browser.QuickFilter);
                Status.Text = $"清除筛选失败：{ex.Message}";
            }
            return;
        }
        ShowQuickFilterEditor(QuickFilterSettings.Empty);
        QuickFilterBar.Visibility = Visibility.Collapsed;
        StateChanged?.Invoke(this, EventArgs.Empty);
        RestoreFileFocusWhenActive();
    }

    internal void SetTreeVisible(bool visible)
    {
        if (visible && _treeHost is null)
        {
            _treeHost = new ShellTreeHost();
            _treeHost.Activated += () => Activated?.Invoke(this, EventArgs.Empty);
            _treeHost.ShortcutHandler = shortcut => ShortcutRequested?.Invoke(this, shortcut) == true;
            _treeHost.Error += message => Status.Text = message;
            _treeHost.FolderMiddleClicked += path =>
            {
                if (IsVisible && TreeVisible) OpenPathInNewTab(path);
            };
            _treeHost.DirectorySelected += path =>
            {
                if (!IsVisible || !TreeVisible || Current is not { } state) return;
                try { state.Browser.Navigate(path); }
                catch (Exception ex) { Status.Text = ex.Message; }
            };
            TreeContainer.Child = _treeHost;
        }
        var hadFocus = _treeHost?.HasNativeFocus() == true;
        if (!visible) _treeHost?.CancelSynchronization();
        TreeContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        TreeColumn.MinWidth = visible ? NavigationTreeWidths.Minimum : 0;
        TreeColumn.MaxWidth = NavigationTreeWidths.Maximum;
        TreeColumn.Width = visible ? new GridLength(_treeWidth) : new GridLength(0);
        TreeSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) SyncTreeToCurrent();
        else if (hadFocus && IsVisible) FocusCurrentView();
        TreeButton.IsChecked = visible;
    }

    private void TreeSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // GridSplitter commits the fixed pixel width before raising DragCompleted;
        // ActualWidth can still be the previous layout pass at this point.
        if (!TreeColumn.Width.IsAbsolute) return;
        var width = TreeColumn.Width.Value;
        if (!NavigationTreeWidths.IsWidth(width)) return;
        _treeWidth = width;
        TreeWidthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Tree_Click(object sender, RoutedEventArgs e)
    {
        SetTreeVisible(TreeButton.IsChecked == true);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseSearch();
            e.Handled = true;
        }
    }

    private void SearchSubmit_Click(object sender, RoutedEventArgs e) => SubmitSearch();

    private void SubmitSearch()
    {
        if (Current is not { } state || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        // Shell searches trim the submitted query. Normalize on explicit submit,
        // then keep it protected until its matching result acknowledges the text.
        SearchBox.Text = SearchBox.Text.Trim();
        _searchTextDirty = true;
        state.SearchRequestPending = true;
        if (ReferenceEquals(_searchCloseFocusPending, state)) _searchCloseFocusPending = null;
        Status.Text = $"正在搜索：{SearchBox.Text.Trim()}";
        state.Browser.Search(SearchBox.Text, state.SearchScopePath);
        if (state.SearchRequestPending && Current == state && IsVisible && Window.GetWindow(this)?.IsActive == true &&
            (SearchBox.IsKeyboardFocusWithin || SearchSubmitButton.IsKeyboardFocusWithin))
        {
            // Transfer the explicit submit to the file view now. Returning to an
            // editor afterwards cancels that intent through the normal focus route;
            // late search results must not reactivate it independently.
            Activated?.Invoke(this, EventArgs.Empty);
            FocusCurrentView();
        }
    }

    private void CloseSearch_Click(object sender, RoutedEventArgs e) => CloseSearch();

    private void CloseSearch()
    {
        if (Current is not { } state) return;
        _searchTextDirty = false;
        _searchCloseFocusPending = state.SearchRequestPending || state.Browser.SearchQuery is not null
            ? state : null;
        state.SearchRequestPending = false;
        SetSearchScope(state, null);
        if (_searchCloseFocusPending is not null)
            state.Browser.Navigate(state.Path);
        SearchBar.Visibility = Visibility.Collapsed;
        Activated?.Invoke(this, EventArgs.Empty);
        RestoreFileFocusWhenActive();
    }
    public void FocusAddress()
    {
        if (!_addressEditing)
        {
            Address.Text = AddressTextPath;
            _addressEditing = true;
            BreadcrumbScroll.Visibility = Visibility.Collapsed;
            Address.Visibility = Visibility.Visible;
            Address.IsReadOnly = false;
            GoButton.Content = "转到";
        }
        Address.Focus();
        Address.SelectAll();
    }

    internal void HandleAddressTreeShortcut()
    {
        var action = AddressTreeShortcutState.Next(_classicAddressBar, AddressTreePopup.IsOpen);
        if (action == AddressTreeShortcutAction.FocusAddressEdit)
        {
            CloseAddressTree();
            FocusAddress();
            return;
        }

        var identity = Current is { } state && ShellLocationIdentity.TryGetFolderPidl(state.Path, out var pidl)
            ? FreeAddressTreePidl(pidl, state.Path) : null;
        AddressTreeHost.SetAddressTreeLocation(identity);
        AddressTreePopup.IsOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (AddressTreePopup.IsOpen) AddressTreeHost.FocusTree();
        }));
    }

    internal void CloseAddressTree(bool focusAddress = false)
    {
        if (AddressTreePopup.IsOpen) AddressTreePopup.IsOpen = false;
        AddressTreeHost.CancelSynchronization();
        if (focusAddress && IsLoaded && IsVisible) FocusAddress();
    }

    private static string FreeAddressTreePidl(nint pidl, string identity)
    {
        System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pidl);
        return identity;
    }

    private void AddressTreePopup_Closed(object? sender, EventArgs e) => AddressTreeHost.CancelSynchronization();

    internal void SetClassicAddressBar(bool classic)
    {
        _classicAddressBar = classic;
        if (_addressEditing)
        {
            BreadcrumbScroll.Visibility = Visibility.Collapsed;
            Address.Visibility = Visibility.Visible;
            return;
        }
        ShowBreadcrumb();
    }

    public void ShowAddressHistory()
        => ShowAddressHistory(AddressHost);

    private void ShowAddressHistory(FrameworkElement placementTarget)
    {
        if (Current is not { } state) return;
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Bottom,
            DataContext = state
        };
        foreach (var path in state.AddressHistory.Paths)
        {
            var item = new MenuItem { Header = path, ToolTip = path };
            item.Click += (_, _) => state.Browser.Navigate(path);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void AddressHistory_Click(object sender, RoutedEventArgs e) => ShowAddressHistory();

    private void NavigationHistoryRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement target) return;
        ShowAddressHistory(target);
        e.Handled = true;
    }

    private void AddressMenu_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } state) return;
        var menu = new ContextMenu
        {
            PlacementTarget = AddressHost,
            Placement = PlacementMode.Bottom,
            DataContext = state
        };
        var entries = AddressPathMenu.Build(state.Path);
        var addedCommonSeparator = false;
        var itemsByIdentity = new Dictionary<string, MenuItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!entry.IsAncestor && !addedCommonSeparator)
            {
                menu.Items.Add(new Separator());
                addedCommonSeparator = true;
            }
            var item = new MenuItem
            {
                Header = entry.Header,
                ToolTip = entry.Identity,
                IsCheckable = entry.IsCurrent,
                IsChecked = entry.IsCurrent,
                Icon = new Image
                {
                    Source = ShellIconProvider.GetIcon(entry.Identity, isDirectory: true),
                    Width = 16,
                    Height = 16
                }
            };
            item.Click += (_, _) =>
            {
                if (ReferenceEquals(Current, state)) state.Browser.Navigate(entry.Identity);
            };
            if (entry.ParentIdentity is null) menu.Items.Add(item);
            else itemsByIdentity[entry.ParentIdentity].Items.Add(item);
            itemsByIdentity[entry.Identity] = item;
        }
        menu.IsOpen = true;
    }

    internal void SetCompactMode(bool compact)
    {
        _compactMode = compact;
        RootBorder.Margin = new Thickness(compact ? 1 : 3);
        NavigationRow.Margin = new Thickness(compact ? 2 : 4);
        AddressHost.Margin = new Thickness(compact ? 3 : 6, 0, 0, 0);
        Status.Margin = compact ? new Thickness(4, 1, 4, 1) : new Thickness(6, 3, 6, 3);
        if (compact) Status.FontSize = 11;
        else Status.ClearValue(TextBlock.FontSizeProperty);
        var navigationWidth = compact ? 24 : 28;
        BackButton.Width = navigationWidth;
        ForwardButton.Width = navigationWidth;
        UpButton.Width = navigationWidth;
        RefreshButton.Width = navigationWidth;
        AddressHistoryButton.Width = compact ? 20 : 24;
        AddressMenuButton.Width = compact ? 20 : 24;
        AddressMenuColumn.Width = new GridLength(compact ? 20 : 24);
        AddressHistoryColumn.Width = new GridLength(compact ? 20 : 24);
        NewTabButton.Width = navigationWidth;
        CloseTabButton.Width = navigationWidth;
        GoButton.Padding = compact ? new Thickness(4, 0, 4, 0) : new Thickness(8, 2, 8, 2);
        GoButton.Margin = new Thickness(compact ? 2 : 4, 0, 0, 0);
        NewTabButton.Margin = new Thickness(compact ? 4 : 8, 0, 0, 0);
        CloseTabButton.Margin = new Thickness(compact ? 2 : 4, 0, 0, 0);
        foreach (var tab in _tabOrder)
        {
            if (compact) tab.Padding = new Thickness(4, 1, 4, 1);
            else tab.ClearValue(Control.PaddingProperty);
        }
        foreach (var button in BreadcrumbButtons.Children.OfType<Button>())
            button.Padding = new Thickness(compact ? 2 : 4, 0, compact ? 2 : 4, 0);
    }
    public void SelectRelativeTab(bool reverse)
    {
        if (_tabOrder.Count < 2 || Tabs.SelectedItem is not TabItem selectedTab) return;
        var selected = _tabOrder.IndexOf(selectedTab);
        if (selected < 0) return;
        Tabs.SelectedItem = _tabOrder[KeyboardShortcuts.NextPane(selected, _tabOrder.Count, reverse)];
        FocusCurrentView();
    }
    public void SelectTabByNumber(int number)
    {
        if (number < 1 || number > _tabOrder.Count) return;
        Tabs.SelectedItem = _tabOrder[number - 1];
        FocusCurrentView();
    }
    public void CloseCurrentTab() { if (Current is { } state) CloseTab(state); }
    public void ReopenClosedTab()
    {
        // A full pane must not consume a snapshot that can be reopened later.
        if (_closedTabs.Count == 0 || !CanAddTabs(1)) return;
        if (_closedTabs.TryPop(out var closed))
        {
            var tab = closed.Settings;
            AddTab(tab.Path, tab.View, quickFilter: tab.QuickFilter, previewPaneVisible: tab.PreviewPaneVisible,
                detailsPaneVisible: tab.DetailsPaneVisible, addressHistory: closed.AddressHistory);
        }
    }

    internal bool TryOpenSelectedFolderInNewTab() => Current?.Browser.OpenSelectedFolderInNewTab(
        OpenPathInNewTab) == true;

    public void CloseOtherTabs()
    {
        if (Current is not { } current) return;
        foreach (var tab in Tabs.Items.Cast<TabItem>().ToArray())
        {
            var state = (TabState)tab.Tag;
            if (state != current && !state.IsPinned) CloseTab(state);
        }
    }

    private TabItem AddTab(string path, ViewState? view = null, bool select = true, bool pinned = false,
        QuickFilterSettings? quickFilter = null, bool previewPaneVisible = false, bool detailsPaneVisible = false,
        AddressPathHistory? addressHistory = null, string? startupSelectPath = null)
    {
        if (!CanAddTabs(1)) throw new InvalidOperationException(Status.Text);
        var browser = new ExplorerBrowserHost(path, view ?? _newTabView(), quickFilter ?? QuickFilterSettings.Empty,
            previewPaneVisible, detailsPaneVisible, _hiddenItemsPreference, _selectionCheckBoxes, _fullRowSelection,
            _disableGrouping);
        var tab = new TabItem();
        if (_compactMode) tab.Padding = new Thickness(4, 1, 4, 1);
        var state = new TabState { Owner = this, Tab = tab, Browser = browser, IsPinned = pinned,
            AddressHistory = addressHistory ?? new AddressPathHistory(), StartupSelectPath = startupSelectPath };
        state.AddressHistory.Record(path);
        tab.Tag = state;
        browser.QuickFilterChanged += settings =>
        {
            var owner = state.Owner;
            if (owner.Current != state) return;
            if (!owner.QuickFilterBox.IsKeyboardFocusWithin) owner.ShowQuickFilterEditor(settings);
            owner.StateChanged?.Invoke(owner, EventArgs.Empty);
        };
        browser.NavigationAvailabilityChanged += (_, _) =>
        {
            var owner = state.Owner;
            if (owner.Current == state) owner.RefreshNavigationAvailability();
        };
        browser.ShellStatusTextChanged += (_, text) =>
        {
            var owner = state.Owner;
            if (owner.Current == state) owner.UpdateShellStatusHint(text);
        };
        browser.SelectionContextChanged += (_, _) =>
        {
            var owner = state.Owner;
            if (owner.Current == state)
            {
                if (owner._recursiveSizeIsSelection && ReferenceEquals(owner._recursiveSizeOwner, state))
                    owner.CancelRecursiveDirectorySize();
                owner.CommandContextChanged?.Invoke(owner, EventArgs.Empty);
                owner.QueueSelectionCountsRefresh(state);
                if (owner._autoExpandTreeSelection && owner.IsVisible)
                {
                    var requestVersion = ++owner._treeSelectionSyncRequestVersion;
                    var selectionPath = state.Path;
                    owner.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        var currentOwner = state.Owner;
                        if (!currentOwner._autoExpandTreeSelection || requestVersion != currentOwner._treeSelectionSyncRequestVersion ||
                            currentOwner.Current != state || !currentOwner.IsVisible ||
                            !string.Equals(state.Path, selectionPath, StringComparison.OrdinalIgnoreCase)) return;
                        var folder = state.Browser.GetSelectedFolderLocation();
                        if (folder is not null && currentOwner._autoExpandTreeSelection &&
                            requestVersion == currentOwner._treeSelectionSyncRequestVersion && currentOwner.Current == state && currentOwner.IsVisible &&
                            string.Equals(state.Path, selectionPath, StringComparison.OrdinalIgnoreCase))
                            currentOwner.SelectedFolderForTree?.Invoke(currentOwner, folder);
                    }));
                }
            }
        };
        browser.FolderMiddleClicked += (_, folderPath) =>
        {
            var owner = state.Owner;
            if (owner.Current == state && owner.IsVisible) owner.OpenPathInNewTab(folderPath);
        };
        browser.NavigationStarting += (_, _) =>
        {
            var owner = state.Owner;
            if (!state.SearchRequestPending && state.Browser.SearchQuery is null)
                owner.SetSearchScope(state, null);
            if (ReferenceEquals(owner._recursiveSizeOwner, state) || ReferenceEquals(owner._lastRecursiveSizeOwner, state))
                owner.CancelRecursiveDirectorySize();
            if (owner.Current != state) return;
            owner.UpdateShellStatusHint(null);
            owner.CancelSelectionCountsRefresh();
            owner.RefreshNavigationAvailability();
            owner._treeHost?.CancelSynchronization();
            owner._diskSpace.Reset();
            owner.ItemCounts.Text = "";
            owner.ItemCounts.ToolTip = null;
        };
        // File-area menus remain native-only while menu theme behavior is verified.
        browser.Navigated += (_, notice) =>
        {
            var owner = state.Owner;
            var navigatedPath = notice.Value;
            var completesInput = browser.CanCompleteNavigationRequest(notice.RequestVersion);
            if (completesInput && state.StartupSelectPath is { } startupSelectPath)
            {
                state.StartupSelectPath = null;
                if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(startupSelectPath) ?? string.Empty),
                        Path.TrimEndingDirectorySeparator(navigatedPath), StringComparison.OrdinalIgnoreCase))
                    browser.QueueFileSystemSelection(startupSelectPath);
            }
            if (completesInput)
            {
                state.SearchRequestPending = false;
                owner.SetSearchScope(state, null);
            }
            var restoreSearchCloseFocus = completesInput && ReferenceEquals(owner._searchCloseFocusPending, state);
            if (restoreSearchCloseFocus) owner._searchCloseFocusPending = null;
            state.AddressHistory.Record(navigatedPath);
            owner.UpdateTabHeader(state);
            if (owner.Current == state)
            {
                owner.RenderBreadcrumb(navigatedPath);
                // Shell display-name work can reenter; a later request still
                // owns editing even if it began while this notification ran.
                completesInput = browser.CanCompleteNavigationRequest(notice.RequestVersion);
                restoreSearchCloseFocus &= completesInput;
                var restoreFocus = completesInput && owner._addressNavigationPending &&
                    (owner.Address.IsKeyboardFocusWithin || owner.GoButton.IsKeyboardFocused);
                if (!owner._addressEditing || completesInput && owner._addressNavigationPending)
                {
                    owner.Address.Text = navigatedPath;
                    owner.ShowBreadcrumb();
                }
                if (completesInput)
                {
                    owner._addressNavigationPending = false;
                    owner.SearchBar.Visibility = Visibility.Collapsed;
                    owner.Status.Text = "Shell 视图";
                }
                if (browser.IsFileSystemLocation) owner._treeHost?.SetDirectory(navigatedPath);
                else owner._treeHost?.SetVirtualLocation();
                owner.QueueSelectionCountsRefresh(state);
                if (restoreFocus)
                    owner.Activated?.Invoke(owner, EventArgs.Empty);
                if (restoreFocus || restoreSearchCloseFocus)
                    owner.RestoreFileFocusWhenActive();
            }
            owner.StateChanged?.Invoke(owner, EventArgs.Empty);
            if (owner.Current == state) owner.CommandContextChanged?.Invoke(owner, EventArgs.Empty);
        };
        browser.SearchNavigated += (_, notice) =>
        {
            var owner = state.Owner;
            var query = notice.Value;
            var completesInput = browser.CanCompleteNavigationRequest(notice.RequestVersion);
            if (completesInput) state.SearchRequestPending = false;
            if (ReferenceEquals(owner._searchCloseFocusPending, state)) return;
            owner.UpdateTabHeader(state);
            owner.StateChanged?.Invoke(owner, EventArgs.Empty);
            if (owner.Current != state) return;
            owner.RenderBreadcrumb(state.Path);
            completesInput = browser.CanCompleteNavigationRequest(notice.RequestVersion);
            if (!owner._addressEditing)
            {
                owner.Address.Text = state.Path;
                owner.ShowBreadcrumb();
            }
            if (completesInput)
            {
                owner.SearchBar.Visibility = Visibility.Visible;
                owner.SyncSearchText(query);
                owner.Status.Text = $"搜索结果：{query}";
            }
            owner.QueueSelectionCountsRefresh(state);
            owner.CommandContextChanged?.Invoke(owner, EventArgs.Empty);
        };
        browser.NavigationFailed += (_, notice) =>
        {
            if (!browser.IsCurrentNavigationRequest(notice.RequestVersion)) return;
            var owner = state.Owner;
            state.StartupSelectPath = null;
            state.SearchRequestPending = false;
            if (ReferenceEquals(owner._searchCloseFocusPending, state))
                owner._searchCloseFocusPending = null;
            if (owner.Current == state)
            {
                owner._addressNavigationPending = false;
                if (state.Browser.SearchQuery is { } query)
                {
                    owner.SearchBar.Visibility = Visibility.Visible;
                    owner.SyncSearchText(query);
                }
                owner.Status.Text = notice.Value;
                owner.SyncTreeToCurrent(state);
                owner.QueueSelectionCountsRefresh(state);
            }
            if (owner.Current == state) owner.CommandContextChanged?.Invoke(owner, EventArgs.Empty);
        };
        browser.MenuError += (_, message) =>
        {
            var owner = state.Owner;
            if (owner.Current == state) owner.Status.Text = $"Shell 视图：{message}";
        };
        browser.Activated += (_, _) => state.Owner.Activated?.Invoke(state.Owner, EventArgs.Empty);
        browser.HeaderPresetMenuRequested += point => state.Owner.HeaderPresetMenuRequested?.Invoke(state.Owner, point);
        browser.ColumnValueFilterMenuRequested += (key, name, point) =>
        {
            if (state.Owner.Current == state)
                state.Owner.ColumnValueFilterMenuRequested?.Invoke(state.Owner, key, name, point);
        };
        browser.ShortcutHandler = shortcut => shortcut == ExplorerShortcut.OpenSelectionInNewTab
            ? state.Owner.TryOpenSelectedFolderInNewTab()
            : state.Owner.ShortcutRequested?.Invoke(state.Owner, shortcut) == true;
        browser.CtrlWheelHandler = style => state.Owner.Current == state && state.Owner.ApplyCompletedViewStyleDelta(state, style);
        browser.MayRestoreFocus = () => state.Owner.Current == state && state.Owner.IsVisible &&
            state.Owner._treeHost?.HasNativeFocus() != true && state.Owner.MayRestoreFileFocus?.Invoke() == true;
        browser.MayContinueFileOperationFocusRestore = () => state.Owner.Current == state && state.Owner.IsVisible &&
            state.Owner._treeHost?.HasNativeFocus() != true && state.Owner.MayKeepFileFocusRestorePending?.Invoke() == true;
        UpdateTabHeader(state);
        tab.PreviewMouseLeftButtonDown += (_, e) => state.Owner.TabHeader_PreviewMouseLeftButtonDown(tab, e);
        tab.PreviewMouseRightButtonDown += (_, _) => state.Owner.Tabs.SelectedItem = tab;
        tab.AllowDrop = true;
        tab.DragOver += (sender, e) => state.Owner.Tab_DragOver(sender, e);
        tab.DragLeave += (sender, e) => state.Owner.Tab_DragLeave(sender, e);
        tab.Drop += (sender, e) => state.Owner.Tab_Drop(sender, e);
        tab.ContextMenu = CreateTabContextMenu(state);
        Tabs.Items.Add(tab);
        var insertion = pinned ? _tabOrder.TakeWhile(item => ((TabState)item.Tag).IsPinned).Count() : _tabOrder.Count;
        _tabOrder.Insert(insertion, tab);
        UpdateTabOrders();
        UpdateTabCloseButtonStates();
        if (select) Tabs.SelectedItem = tab;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return tab;
    }

    internal void DisposeViews()
    {
        CloseAddressTree();
        CancelTabHoverActivation();
        CancelRecursiveDirectorySize();
        CancelSelectionCountsRefresh();
        _itemCountsTimer.Stop();
        _diskSpace.Reset();

        // Window closure does not dispose HwndHost children: WPF can park them
        // under its hidden window. Release every tab, including inactive ones.
        foreach (var tab in Tabs.Items.Cast<TabItem>())
            DisposeHost(((TabState)tab.Tag).Browser);
        DisposeHost(_treeHost);
        DisposeHost(AddressTreeHost);

        static void DisposeHost(IDisposable? host)
        {
            try { host?.Dispose(); }
            catch (Exception ex)
            {
                // One failing Shell extension must not prevent other hosts from closing.
                Trace.WriteLine($"Pane host cleanup: {ex.GetType().Name} (0x{ex.HResult:X8})");
            }
        }
    }

    private void UpdateTabHeader(TabState state)
    {
        var name = ShellLocationIdentity.GetDisplayName(state.Path);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Image
        {
            Source = ShellIconProvider.GetIcon(state.Path, true),
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 4, 0)
        });
        if (state.IsPinned)
            header.Children.Add(new TextBlock { Text = "📌", ToolTip = "固定标签", Margin = new Thickness(0, 0, 3, 0) });
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(name) ? state.Path : name,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        state.CloseButton = null;
        if (_showTabCloseButtons)
            AddTabCloseButton(state, header);
        header.MouseDown += (_, e) =>
        {
            if (state.CloseButton?.IsMouseOver == true) return;
            if (e.ChangedButton == MouseButton.Middle ||
                (e.ChangedButton == MouseButton.Left && e.ClickCount == 2))
            {
                CloseTab(state);
                e.Handled = true;
            }
        };
        state.Tab.Header = header;
        state.Tab.ToolTip = $"{(state.IsPinned ? "固定标签 · " : "")}{state.Path}\n拖动到其他窗格可移动；Ctrl+拖动可复制。";
        if (state.Tab.IsSelected)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(() => { if (state.Tab.IsSelected) state.Tab.BringIntoView(); }));
    }

    private void AddTabCloseButton(TabState state, StackPanel? header = null)
    {
        if (state.CloseButton is not null) return;
        header ??= state.Tab.Header as StackPanel;
        if (header is null) return;
        var closeButton = new Button
        {
            Content = "×",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Focusable = false,
            IsTabStop = false,
            ToolTip = "关闭标签页"
        };
        closeButton.SetResourceReference(Control.ForegroundProperty, "ExplorerDirs.ControlTextBrush");
        System.Windows.Automation.AutomationProperties.SetName(closeButton, "关闭标签页");
        closeButton.Click += (_, e) =>
        {
            state.Owner.CloseTab(state);
            e.Handled = true;
        };
        header.Children.Add(closeButton);
        state.CloseButton = closeButton;
    }

    private static void RemoveTabCloseButton(TabState state)
    {
        if (state.CloseButton?.Parent is Panel parent)
            parent.Children.Remove(state.CloseButton);
        state.CloseButton = null;
    }

    private void UpdateTabCloseButtonStates()
    {
        foreach (var tab in _tabOrder)
        {
            var state = (TabState)tab.Tag;
            if (state.CloseButton is null) continue;
            state.CloseButton.Visibility = TabCloseButtonPolicy.IsVisible(
                _showTabCloseButtons, state.IsPinned, _tabOrder.Count)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private ContextMenu CreateTabContextMenu(TabState state)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "使用 Explorer 浏览", () => OpenInSystemExplorer(state));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "新建标签页", NewTab, "Ctrl+T");
        AddMenuItem(menu, "复制当前标签", () => DuplicateTab(state), "Ctrl+K");
        AddMenuItem(menu, "恢复关闭的标签", ReopenClosedTab, "Ctrl+Shift+T");
        menu.Items.Add(new Separator());
        var pinItem = new MenuItem { Header = "固定标签", IsCheckable = true };
        pinItem.Click += (_, _) => TogglePinned(state);
        menu.Items.Add(pinItem);
        var closeItem = new MenuItem { Header = "关闭标签页", InputGestureText = "Ctrl+W" };
        closeItem.Click += (_, _) => CloseTab(state);
        menu.Items.Add(closeItem);
        AddMenuItem(menu, "关闭其他标签", () =>
        {
            Tabs.SelectedItem = state.Tab;
            CloseOtherTabs();
        });
        menu.Opened += (_, _) =>
        {
            pinItem.IsChecked = state.IsPinned;
            closeItem.IsEnabled = !state.IsPinned && Tabs.Items.Count > 1;
        };
        return menu;
    }

    private void OpenInSystemExplorer(TabState state)
    {
        try { SystemExplorerLauncher.Open(state.Browser.CurrentPath); }
        catch (Exception ex) { Status.Text = $"启动 Windows Explorer 失败：{ex.Message}"; }
    }

    private static void AddMenuItem(ContextMenu menu, string header, Action action, string? shortcut = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = shortcut ?? "" };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void CloseTab(TabState state)
    {
        if (state.IsPinned || Tabs.Items.Count <= 1 || !Tabs.Items.Contains(state.Tab)) return;
        if (ReferenceEquals(_searchCloseFocusPending, state)) _searchCloseFocusPending = null;
        if (ReferenceEquals(_dragCandidate, state.Tab)) _dragCandidate = null;
        var closingIndex = _tabOrder.IndexOf(state.Tab);
        var adjacent = state.Tab.IsSelected
            ? _tabOrder[closingIndex + 1 < _tabOrder.Count ? closingIndex + 1 : closingIndex - 1]
            : null;
        CaptureViewState(state);
        // Reopening previously kept only the current address, unlike DuplicateTab.
        // Retain an independent dropdown history without keeping the native host alive.
        _closedTabs.Push((new TabSettings(state.Browser.CurrentPath, state.Browser.CachedViewState(), state.Browser.QuickFilter,
            state.Browser.PreviewPaneVisible, state.Browser.DetailsPaneVisible), state.AddressHistory.Copy()));
        while (_closedTabs.Count > 20)
        {
            var recent = _closedTabs.Take(20).Reverse().ToArray();
            _closedTabs.Clear();
            foreach (var tab in recent) _closedTabs.Push(tab);
        }
        Tabs.Items.Remove(state.Tab);
        _tabOrder.Remove(state.Tab);
        UpdateTabOrders();
        UpdateTabCloseButtonStates();
        if (adjacent is not null) Tabs.SelectedItem = adjacent;
        BrowserLayer.Remove(state.Browser);
        state.Browser.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TogglePinned(TabState state)
    {
        if (!Tabs.Items.Contains(state.Tab)) return;
        _tabOrder.Remove(state.Tab);
        state.IsPinned = !state.IsPinned;
        var insertion = _tabOrder.TakeWhile(tab => ((TabState)tab.Tag).IsPinned).Count();
        _tabOrder.Insert(insertion, state.Tab);
        UpdateTabHeader(state);
        UpdateTabOrders();
        UpdateTabCloseButtonStates();
        state.Tab.BringIntoView();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Navigate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _addressNavigationPending = false;
            RefreshCurrent();
            ShowBreadcrumb();
            return;
        }

        try
        {
            Status.Text = "正在检查目录…";
            Current?.Browser.Navigate(BreadcrumbPath.NormalizeAddressInput(path));
        }
        catch (Exception ex)
        {
            _addressNavigationPending = false;
            Status.Text = $"无法打开目录：{ex.Message}";
        }
    }

    private void Tabs_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is not { } candidate) return;
        if (e.LeftButton != MouseButtonState.Pressed || !Tabs.Items.Contains(candidate))
        {
            _dragCandidate = null;
            return;
        }
        var point = e.GetPosition(Tabs);
        if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragCandidate = null;
        _draggedTab = candidate;
        _dragToken = Guid.NewGuid().ToString("N");
        var data = new DataObject();
        data.SetData(TabDragFormat, new TabDragPayload(this, candidate, _dragToken));
        try { DragDrop.DoDragDrop(candidate, data, DragDropEffects.Move | DragDropEffects.Copy); }
        finally
        {
            _draggedTab = null;
            _dragToken = null;
        }
    }

    private void TabHeader_PreviewMouseLeftButtonDown(TabItem tab, MouseButtonEventArgs e)
    {
        _dragCandidate = e.ClickCount == 1 ? tab : null;
        _dragStart = e.GetPosition(Tabs);
    }

    private void Tabs_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragCandidate = null;

    private void TabHeaderScroll_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || e.ChangedButton != MouseButton.Left ||
            ItemsControl.ContainerFromElement(Tabs, e.OriginalSource as DependencyObject ?? Tabs) is TabItem)
            return;

        NewTab();
        e.Handled = true;
    }

    private void TabHeaderScroll_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Tabs, e.OriginalSource as DependencyObject ?? Tabs) is TabItem ||
            Tabs.SelectedItem is not TabItem selected || selected.Tag is not TabState state)
            return;

        var menu = CreateTabContextMenu(state);
        menu.PlacementTarget = TabHeaderScroll;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void Tabs_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || Keyboard.FocusedElement is not TabItem focused) return;
        var index = _tabOrder.IndexOf(focused);
        if (index < 0) return;
        var next = e.Key switch
        {
            Key.Left => KeyboardShortcuts.NextPane(index, _tabOrder.Count, true),
            Key.Right => KeyboardShortcuts.NextPane(index, _tabOrder.Count, false),
            Key.Home => 0,
            Key.End => _tabOrder.Count - 1,
            _ => -1
        };
        if (next < 0) return;
        Tabs.SelectedItem = _tabOrder[next];
        _tabOrder[next].Focus();
        e.Handled = true;
    }

    private bool IsOwnTabDrag(IDataObject data)
    {
        if (_draggedTab is null || _dragToken is null || !Tabs.Items.Contains(_draggedTab)) return false;
        try
        {
            return TryGetTabDrag(data, out var payload) && ReferenceEquals(payload.Source, this) &&
                ReferenceEquals(payload.Tab, _draggedTab) &&
                string.Equals(payload.Token, _dragToken, StringComparison.Ordinal);
        }
        catch (Exception) { return false; }
    }

    private static bool TryGetTabDrag(IDataObject data, out TabDragPayload payload)
    {
        payload = null!;
        try
        {
            if (!data.GetDataPresent(TabDragFormat) || data.GetData(TabDragFormat) is not TabDragPayload value ||
                value.Source._draggedTab != value.Tab || value.Source._dragToken != value.Token ||
                !value.Source.Tabs.Items.Contains(value.Tab)) return false;
            payload = value;
            return true;
        }
        catch (Exception) { return false; }
    }

    private static TabTransferOperation GetTabTransferOperation(TabDragPayload payload, TabState target,
        bool copyModifier) => TabTransferPolicy.Resolve(
            samePane: ReferenceEquals(payload.Source, target.Owner),
            sourceTabCount: payload.Source.Tabs.Items.Count,
            sourcePinned: ((TabState)payload.Tab.Tag).IsPinned,
            targetPinned: target.IsPinned,
            copyModifier);

    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not TabItem target)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (TryGetTabDrag(e.Data, out var payload))
        {
            CancelTabHoverActivation();
            var operation = GetTabTransferOperation(payload, (TabState)target.Tag,
                (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            if (operation == TabTransferOperation.Reject ||
                (ReferenceEquals(payload.Source, this) && !IsOwnTabDrag(e.Data)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            e.Effects = operation == TabTransferOperation.Copy ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        else
        {
            if (ReferenceEquals(Tabs.SelectedItem, target))
            {
                CancelTabHoverActivation();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            var started = _tabHoverActivationGate.Begin(target, isOwnTabDrag: false,
                hasShellLocationData: ShellTabDropTarget.HasShellLocationData(e.Data),
                delayMilliseconds: _tabHoverActivationDelayMilliseconds);
            if (started)
            {
                _tabHoverActivationTimer.Interval = TimeSpan.FromMilliseconds(_tabHoverActivationDelayMilliseconds);
                _tabHoverActivationTimer.Start();
            }
            else if (!_tabHoverActivationGate.HasPendingTarget)
            {
                _tabHoverActivationTimer.Stop();
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!TryGetTabDrag(e.Data, out _)) e.Effects = DragDropEffects.Move;
        e.Handled = true;
        if (TabHeaderScroll is not { } scroll) return;
        var point = e.GetPosition(scroll);
        if (point.X < 20)
            scroll.ScrollToHorizontalOffset(Math.Max(0, scroll.HorizontalOffset - 18));
        else if (point.X > scroll.ViewportWidth - 20)
            scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + 18);
    }

    private void Tab_Drop(object sender, DragEventArgs e)
    {
        CancelTabHoverActivation();
        if (sender is not TabItem target || target.Tag is not TabState targetState ||
            !TryGetTabDrag(e.Data, out var payload))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var after = e.GetPosition(target).X >= target.ActualWidth / 2;
        var operation = GetTabTransferOperation(payload, targetState,
            (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        if (operation == TabTransferOperation.Reorder)
        {
            if (!IsOwnTabDrag(e.Data)) operation = TabTransferOperation.Reject;
            else ReorderTab(payload.Tab, target, after);
        }
        else if (operation == TabTransferOperation.Move &&
                 !payload.Source.TransferTabTo((TabState)payload.Tab.Tag, this, target, after))
            operation = TabTransferOperation.Reject;
        else if (operation == TabTransferOperation.Copy &&
                 !CopyTabFrom((TabState)payload.Tab.Tag, target, after))
            operation = TabTransferOperation.Reject;
        if (operation == TabTransferOperation.Reject)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = operation == TabTransferOperation.Copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void Tab_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is TabItem target && _tabHoverActivationGate.Leave(target))
            _tabHoverActivationTimer.Stop();
    }

    private void TabHoverActivationTimer_Tick(object? sender, EventArgs e)
    {
        _tabHoverActivationTimer.Stop();
        // Layout changes may hide a pane without unloading it. A queued drag
        // timer must not select a hidden tab or activate that pane afterward.
        if (_tabHoverActivationGate.TakeTarget() is TabItem target && IsLoaded && IsVisible &&
            Tabs.Items.Contains(target))
            Tabs.SelectedItem = target;
    }

    private void CancelTabHoverActivation()
    {
        _tabHoverActivationTimer.Stop();
        _tabHoverActivationGate.Cancel();
    }

    private void TabStrip_DragOver(object sender, DragEventArgs e)
    {
        CancelTabHoverActivation();
        if (TryGetTabDrag(e.Data, out var payload))
        {
            var isPinned = ((TabState)payload.Tab.Tag).IsPinned;
            var operation = TabTransferPolicy.Resolve(
                samePane: ReferenceEquals(payload.Source, this),
                sourceTabCount: payload.Source.Tabs.Items.Count,
                sourcePinned: isPinned,
                targetPinned: isPinned,
                copyModifier: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            e.Effects = operation switch
            {
                TabTransferOperation.Copy => DragDropEffects.Copy,
                TabTransferOperation.Move or TabTransferOperation.Reorder => DragDropEffects.Move,
                _ => DragDropEffects.None
            };
        }
        else
            e.Effects = _draggedTab is null && ShellTabDropTarget.HasShellLocationData(e.Data)
                ? GetTabStripDropEffect(e.AllowedEffects)
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void TabStrip_Drop(object sender, DragEventArgs e)
    {
        CancelTabHoverActivation();
        if (TryGetTabDrag(e.Data, out var payload))
        {
            var state = (TabState)payload.Tab.Tag;
            var operation = TabTransferPolicy.Resolve(
                samePane: ReferenceEquals(payload.Source, this),
                sourceTabCount: payload.Source.Tabs.Items.Count,
                sourcePinned: state.IsPinned,
                targetPinned: state.IsPinned,
                copyModifier: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            switch (operation)
            {
                case TabTransferOperation.Reorder when payload.Source == this && IsOwnTabDrag(e.Data):
                    ReorderTabToEnd(payload.Tab);
                    e.Effects = DragDropEffects.Move;
                    break;
                case TabTransferOperation.Move when payload.Source != this:
                    e.Effects = payload.Source.TransferTabTo(state, this, target: null, after: true)
                        ? DragDropEffects.Move : DragDropEffects.None;
                    break;
                case TabTransferOperation.Copy:
                    e.Effects = CopyTabFrom(state, target: null, after: true)
                        ? DragDropEffects.Copy : DragDropEffects.None;
                    break;
                default:
                    e.Effects = DragDropEffects.None;
                    break;
            }
            e.Handled = true;
            return;
        }
        var effect = GetTabStripDropEffect(e.AllowedEffects);
        if (_draggedTab is null && effect != DragDropEffects.None &&
            ShellTabDropTarget.TryGetDroppedFolder(e.Data, out var location))
        {
            OpenPathInNewTab(location);
            e.Effects = effect;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private static DragDropEffects GetTabStripDropEffect(DragDropEffects allowedEffects) =>
        (allowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link :
        (allowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;

    private void ReorderTab(TabItem source, TabItem target, bool after)
    {
        var original = _tabOrder.ToArray();
        var sourceIndex = Array.IndexOf(original, source);
        var targetIndex = Array.IndexOf(original, target);
        if (sourceIndex < 0 || targetIndex < 0 ||
            ((TabState)source.Tag).IsPinned != ((TabState)target.Tag).IsPinned) return;
        var reordered = TabReorder.Move(original, sourceIndex, targetIndex, after).ToArray();
        if (original.SequenceEqual(reordered)) return;

        _tabOrder.Clear();
        _tabOrder.AddRange(reordered);
        UpdateTabOrders();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            source.BringIntoView();
            UpdateTabScrollButtons();
        }));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TransferTabTo(TabState state, PaneControl destination, TabItem? target, bool after)
    {
        if (ReferenceEquals(this, destination) || !Tabs.Items.Contains(state.Tab) ||
            (target is not null && (!destination.Tabs.Items.Contains(target) || target.Tag is not TabState targetState ||
                targetState.IsPinned != state.IsPinned)) || Tabs.Items.Count <= 1) return false;
        if (!destination.CanAddTabs(1)) return false;
        var originalIndex = _tabOrder.IndexOf(state.Tab);
        var originalSelected = ReferenceEquals(Current, state);
        try
        {
            CaptureViewState(state);
            if (state.Browser.ViewCaptureFailed)
                throw new InvalidOperationException("Shell 视图状态暂时不可读取；未移动标签。");

            CancelTabHoverActivation();
            if (ReferenceEquals(_recursiveSizeOwner, state) || ReferenceEquals(_lastRecursiveSizeOwner, state))
                CancelRecursiveDirectorySize();
            BrowserLayer.Remove(state.Browser);
            Tabs.Items.Remove(state.Tab);
            _tabOrder.Remove(state.Tab);
            UpdateTabOrders();
            UpdateTabCloseButtonStates();

            state.Owner = destination;
            destination.Tabs.Items.Add(state.Tab);
            var insertIndex = destination.GetTabInsertionIndex(state.IsPinned, target, after);
            destination._tabOrder.Insert(insertIndex, state.Tab);
            destination.UpdateTabOrders();
            state.Tab.ContextMenu = destination.CreateTabContextMenu(state);
            destination.BrowserLayer.Show(state.Browser);
            destination.Tabs.SelectedItem = state.Tab;
            destination.UpdateTabHeader(state);
            destination.UpdateTabCloseButtonStates();
            UpdateTabCloseButtonStates();
            destination.StateChanged?.Invoke(destination, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            if (destination.Tabs.Items.Contains(state.Tab))
            {
                destination.BrowserLayer.Remove(state.Browser);
                destination.Tabs.Items.Remove(state.Tab);
                destination._tabOrder.Remove(state.Tab);
                destination.UpdateTabOrders();
                destination.UpdateTabCloseButtonStates();
            }
            state.Owner = this;
            if (!Tabs.Items.Contains(state.Tab))
            {
                Tabs.Items.Insert(Math.Clamp(_tabOrder.Count == 0 ? 0 : originalIndex, 0, Tabs.Items.Count), state.Tab);
                _tabOrder.Insert(Math.Clamp(originalIndex, 0, _tabOrder.Count), state.Tab);
                UpdateTabOrders();
                UpdateTabCloseButtonStates();
                state.Tab.ContextMenu = CreateTabContextMenu(state);
                BrowserLayer.Show(state.Browser);
                if (originalSelected) Tabs.SelectedItem = state.Tab;
            }
            Status.Text = $"移动标签失败：{ex.Message}";
            return false;
        }
    }

    private bool CopyTabFrom(TabState source, TabItem? target, bool after)
    {
        if (target is not null && (target.Tag is not TabState targetState ||
            targetState.IsPinned != source.IsPinned)) return false;
        if (!CanAddTabs(1)) return false;
        try
        {
            var view = source.Browser.CaptureViewState();
            if (source.Browser.ViewCaptureFailed)
                throw new InvalidOperationException("Shell 视图状态暂时不可读取；未复制标签。");
            var copy = AddTab(source.Browser.CurrentPath, view, select: false, pinned: source.IsPinned,
                quickFilter: source.Browser.QuickFilter,
                previewPaneVisible: source.Browser.PreviewPaneVisible,
                detailsPaneVisible: source.Browser.DetailsPaneVisible,
                addressHistory: source.AddressHistory.Copy());
            _tabOrder.Remove(copy);
            // AddTab already inserted the copy. Exclude it before counting the
            // pinned partition, or a blank-strip copy lands after an ordinary tab.
            var destinationIndex = GetTabInsertionIndex(source.IsPinned, target, after);
            _tabOrder.Insert(Math.Clamp(destinationIndex, 0, _tabOrder.Count), copy);
            UpdateTabOrders();
            Tabs.SelectedItem = copy;
            return true;
        }
        catch (Exception ex)
        {
            Status.Text = $"复制标签失败：{ex.Message}";
            return false;
        }
    }

    private int GetTabInsertionIndex(bool pinned, TabItem? target, bool after)
    {
        if (target is not null)
        {
            var targetIndex = _tabOrder.IndexOf(target);
            if (targetIndex >= 0) return Math.Clamp(targetIndex + (after ? 1 : 0), 0, _tabOrder.Count);
        }
        return pinned ? _tabOrder.TakeWhile(tab => ((TabState)tab.Tag).IsPinned).Count() : _tabOrder.Count;
    }

    private void ReorderTabToEnd(TabItem tab)
    {
        if (!_tabOrder.Contains(tab)) return;
        var state = (TabState)tab.Tag;
        var reordered = _tabOrder.Where(item => !ReferenceEquals(item, tab)).ToList();
        var insertion = state.IsPinned
            ? reordered.TakeWhile(item => ((TabState)item.Tag).IsPinned).Count()
            : reordered.Count;
        reordered.Insert(insertion, tab);
        if (_tabOrder.SequenceEqual(reordered)) return;
        _tabOrder.Clear();
        _tabOrder.AddRange(reordered);
        UpdateTabOrders();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTabOrders()
    {
        for (var index = 0; index < _tabOrder.Count; index++)
            TabHeaderPanel.SetOrder(_tabOrder[index], index);
        UpdateTabStripVisibility();
    }

    private void UpdateTabStripVisibility() =>
        Tabs.Visibility = TabStripVisibilityPolicy.ShouldShow(_tabStripVisibilityMode, Tabs.Items.Count)
            ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateShellStatusHint(string? text)
    {
        ShellStatusHint.Text = text ?? string.Empty;
        ShellStatusHint.ToolTip = string.IsNullOrEmpty(text) ? null : text;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs || Current is not { } state) return;
        CancelTabHoverActivation();
        if (e.RemovedItems.Count != 0) CloseAddressTree();
        foreach (var previous in e.RemovedItems.OfType<TabItem>())
        {
            if (previous.Tag is TabState previousState &&
                (ReferenceEquals(_recursiveSizeOwner, previousState) || ReferenceEquals(_lastRecursiveSizeOwner, previousState)))
                CancelRecursiveDirectorySize();
            if (Tabs.Items.Contains(previous) && previous.Tag is TabState oldState && oldState.Browser.IsVisible)
                CaptureViewState(oldState);
        }
        BrowserLayer.Show(state.Browser);
        RefreshNavigationAvailability();
        SyncTreeToCurrent(state);
        _diskSpace.Reset();
        UpdateShellStatusHint(state.Browser.ShellStatusText);
        ItemCounts.Text = ""; // Never carry a previous tab's selection counts across a switch.
        ItemCounts.ToolTip = null;
        CancelSelectionCountsRefresh();
        QueueSelectionCountsRefresh(state);
        _addressNavigationPending = false;
        Address.Text = state.Path;
        RenderBreadcrumb(state.Path);
        ShowBreadcrumb();
        SearchBar.Visibility = state.Browser.SearchQuery is null ? Visibility.Collapsed : Visibility.Visible;
        SetSearchText(state.Browser.SearchQuery ?? string.Empty);
        if (QuickFilterBar.Visibility == Visibility.Visible) ShowQuickFilterEditor(state.Browser.QuickFilter);
        Status.Text = state.Browser.SearchQuery is { } query ? $"搜索结果：{query}" : "Shell 视图";
        Activated?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
        CommandContextChanged?.Invoke(this, EventArgs.Empty);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (Current != state) return;
            state.Tab.BringIntoView();
            UpdateTabScrollButtons();
            // A later click or Tab/arrow move may have chosen another pane,
            // address box or tab header while this callback was queued.
            if (IsLoaded && IsVisible && MayRestoreFileFocus?.Invoke() == true) FocusCurrentView();
        }));
    }

    private ScrollViewer? TabHeaderScroll =>
        Tabs.Template.FindName("TabHeaderScroll", Tabs) as ScrollViewer;

    private void ScrollTabsLeft_Click(object sender, RoutedEventArgs e)
    {
        if (TabHeaderScroll is { } scroll)
            scroll.ScrollToHorizontalOffset(Math.Max(0, scroll.HorizontalOffset - 140));
    }

    private void ScrollTabsRight_Click(object sender, RoutedEventArgs e)
    {
        if (TabHeaderScroll is { } scroll)
            scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + 140);
    }

    private void TabHeaderScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroll || scroll.ExtentWidth <= scroll.ViewportWidth) return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta / 120.0 * 100);
        e.Handled = true;
    }

    private void TabHeaderScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        UpdateTabScrollButtons();

    private void UpdateTabScrollButtons()
    {
        if (TabHeaderScroll is not { } scroll) return;
        if (Tabs.Template.FindName("ScrollTabsLeft", Tabs) is Button left)
            left.IsEnabled = scroll.HorizontalOffset > 0.5;
        if (Tabs.Template.FindName("ScrollTabsRight", Tabs) is Button right)
            right.IsEnabled = scroll.HorizontalOffset + scroll.ViewportWidth < scroll.ExtentWidth - 0.5;
    }

    private void RenderBreadcrumb(string path)
    {
        BreadcrumbButtons.Children.Clear();
        Button? last = null;
        foreach (var segment in BreadcrumbPath.Build(path))
        {
            if (BreadcrumbButtons.Children.Count > 0)
                BreadcrumbButtons.Children.Add(new TextBlock
                {
                    Text = "›", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center
                });
            var button = new Button
            {
                Content = new TextBlock { Text = segment.Label },
                ToolTip = segment.Path,
                Focusable = false,
                IsTabStop = false,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(_compactMode ? 2 : 4, 0, _compactMode ? 2 : 4, 0)
            };
            button.Click += (_, _) =>
            {
                Activated?.Invoke(this, EventArgs.Empty);
                Navigate(segment.Path);
                RestoreFileFocusWhenActive();
            };
            BreadcrumbButtons.Children.Add(button);
            last = button;
        }
        if (last is { } lastButton)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (BreadcrumbButtons.Children.Contains(lastButton)) lastButton.BringIntoView();
            }));
    }

    private void ShowBreadcrumb()
    {
        _addressEditing = false;
        Address.Text = AddressTextPath;
        Address.IsReadOnly = true;
        Address.Visibility = _classicAddressBar ? Visibility.Visible : Visibility.Collapsed;
        BreadcrumbScroll.Visibility = _classicAddressBar ? Visibility.Collapsed : Visibility.Visible;
        GoButton.Content = "编辑";
        if (!_classicAddressBar)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(() => { if (!_addressEditing) BreadcrumbScroll.ScrollToRightEnd(); }));
    }

    private void RestoreFileFocusWhenActive()
    {
        var state = Current;
        if (state is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            // Closing address/search UI can queue this before the user switches
            // tabs. The old request must not focus its replacement tab.
            if (Current == state && MayRestoreFileFocus?.Invoke() == true)
                state.Browser.FocusView();
        }));
    }

    private void BreadcrumbScroll_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Use the bubbling event: breadcrumb buttons consume their own clicks,
        // while blank space and separators provide Explorer-style path editing.
        FocusAddress();
        e.Handled = true;
    }

    private void BreadcrumbScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (BreadcrumbScroll.ExtentWidth <= BreadcrumbScroll.ViewportWidth) return;
        BreadcrumbScroll.ScrollToHorizontalOffset(BreadcrumbScroll.HorizontalOffset - e.Delta / 120.0 * 90);
        e.Handled = true;
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        GoParent();
    }
    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => GoForward();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCurrent();
    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (!_addressEditing) { FocusAddress(); return; }
        _addressNavigationPending = true;
        Navigate(Address.Text);
    }

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _addressNavigationPending = true;
            Navigate(Address.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (AddressTreePopup.IsOpen)
            {
                CloseAddressTree(focusAddress: true);
                e.Handled = true;
                return;
            }
            _addressNavigationPending = false;
            Address.Text = AddressTextPath;
            ShowBreadcrumb();
            Activated?.Invoke(this, EventArgs.Empty);
            RestoreFileFocusWhenActive();
            e.Handled = true;
        }
    }

    private void Address_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Continuing to edit after submission revokes the old completion's
        // permission to replace the draft or leave address editing.
        if (_addressEditing) _addressNavigationPending = false;
    }

    private void Address_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_classicAddressBar && !_addressEditing) FocusAddress();
    }

    private void Address_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_addressEditing || ReferenceEquals(e.NewFocus, GoButton)) return;
        Address.Text = AddressTextPath;
        ShowBreadcrumb();
    }

    private void GoButton_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_addressEditing || ReferenceEquals(e.NewFocus, Address)) return;
        Address.Text = AddressTextPath;
        ShowBreadcrumb();
    }
    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTab();
    private void CloseTab_Click(object sender, RoutedEventArgs e) => CloseCurrentTab();
}
