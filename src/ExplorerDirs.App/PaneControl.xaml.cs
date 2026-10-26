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
    private const string TabDragFormat = "ExplorerDirs.TabReorder.v1";
    // The Details column header is one Shell focus stop; arrow keys move among columns.
    private readonly Stack<TabSettings> _closedTabs = new();
    private readonly List<TabItem> _tabOrder = [];
    private readonly Func<ViewState> _newTabView;
    private readonly QuickFilterFavoritesStore _quickFilterFavoritesStore;
    private HiddenItemsPreference _hiddenItemsPreference;
    private bool _updatingFilterPresets;
    private TabItem? _dragCandidate;
    private Point _dragStart;
    private TabItem? _draggedTab;
    private string? _dragToken;
    private bool _addressEditing;
    private bool _classicAddressBar;
    private bool _addressNavigationPending;
    private TabState? _searchCloseFocusPending;
    private bool _compactMode;
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
    private RecursiveDirectoryScanResult? _lastRecursiveSizeResult;
    private string? _lastRecursiveSizePath;
    private TabState? _lastRecursiveSizeOwner;

    private sealed class TabState
    {
        public required TabItem Tab { get; init; }
        public required ExplorerBrowserHost Browser { get; init; }
        public required string Path { get; set; }
        public bool IsPinned { get; set; }
        public bool SearchRequestPending { get; set; }
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

    internal void SetActivePaneVisual(bool isActive)
    {
        RootBorder.SetResourceReference(Border.BorderBrushProperty,
            isActive ? "ExplorerDirs.AccentBrush" : "ExplorerDirs.BorderBrush");
        RootBorder.BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
    }
    internal event Action<PaneControl, ShellMenuPoint>? HeaderPresetMenuRequested;
    internal event Func<PaneControl, ExplorerShortcut, bool>? ShortcutRequested;
    internal Func<bool>? MayRestoreFileFocus { get; set; }

    internal PaneControl(IEnumerable<TabSettings> tabs, Func<ViewState> newTabView,
        IReadOnlySet<int>? pinnedIndices, int selectedTabIndex,
        QuickFilterFavoritesStore quickFilterFavoritesStore,
        HiddenItemsPreference hiddenItemsPreference = HiddenItemsPreference.FollowWindows)
    {
        InitializeComponent();
        BackButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Back), 14);
        ForwardButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Forward), 14);
        UpButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Up), 14);
        RefreshButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Refresh), 14);
        TreeButton.Content = ExplorerChromeIcons.CreatePath(ExplorerChromeIcons.For(ExplorerChromeIcon.Tree));
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
        _hiddenItemsPreference = hiddenItemsPreference;
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
            if (IsVisible && IsLoaded) { RefreshItemCounts(); RefreshDiskSpace(); _itemCountsTimer.Start(); }
            else
            {
                CloseAddressTree();
                CancelRecursiveDirectorySize();
                CancelSelectionCountsRefresh();
                _itemCountsTimer.Stop(); ItemCounts.Text = ""; ItemCounts.ToolTip = null; _diskSpace.Reset();
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
        var counts = state?.Browser.ReadItemCounts();
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
        _lastRecursiveSizeResult = null;
        _lastRecursiveSizePath = null;
        RecursiveSizeStatus.Text = "正在计算目录总大小… 已检查 0 项 · 再按 F9 取消";
        RecursiveSizeStatus.ToolTip = path;
        RecursiveSizeStatus.Visibility = Visibility.Visible;
        _ = ScanRecursiveDirectoryAsync(state, path, cancellation);
    }

    private async Task ScanRecursiveDirectoryAsync(TabState owner, string path, CancellationTokenSource cancellation)
    {
        try
        {
            var progressThrottle = new RecursiveScanProgressThrottle(TimeSpan.FromMilliseconds(250));
            var result = await Task.Run(() => RecursiveDirectoryScanner.Scan(path, cancellation.Token, entriesChecked =>
            {
                if (!progressThrottle.ShouldReport(Stopwatch.GetTimestamp())) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (IsCurrentRecursiveSizeScan(owner, path, cancellation))
                        RecursiveSizeStatus.Text = $"正在计算目录总大小… 已检查 {entriesChecked:N0} 项 · 再按 F9 取消";
                }));
            }), cancellation.Token).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentRecursiveSizeScan(owner, path, cancellation)) return;
                _lastRecursiveSizeResult = result;
                _lastRecursiveSizePath = path;
                _lastRecursiveSizeOwner = owner;
                RenderRecursiveSizeResult(path, result);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _lastRecursiveSizeResult = null;
            _lastRecursiveSizePath = null;
            _lastRecursiveSizeOwner = null;
            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentRecursiveSizeScan(owner, path, cancellation)) return;
                RecursiveSizeStatus.Text = $"无法计算目录大小：{ex.Message}";
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
        _lastRecursiveSizeResult = null;
        _lastRecursiveSizePath = null;
        _lastRecursiveSizeOwner = null;
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
            RenderRecursiveSizeResult(path, result);
    }

    private void RenderRecursiveSizeResult(string path, RecursiveDirectoryScanResult result)
    {
        RecursiveSizeStatus.Text = ByteSizeFormatter.FormatRecursiveDirectorySummary(result, ByteSizeFormatter.Options);
        RecursiveSizeStatus.ToolTip = $"{path}\n不跟随重解析点；无法读取或已跳过位置：{result.Unreadable:N0}";
    }

    private void RefreshNavigationAvailability()
    {
        BackButton.IsEnabled = Current?.Browser.CanGoBack == true;
        ForwardButton.IsEnabled = Current?.Browser.CanGoForward == true;
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string CurrentPath => Current?.Path ?? Home;
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
            return new TabSettings(state.Path, state.Browser.CachedViewState(), state.Browser.QuickFilter,
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
        .Select(item => new PinnedTabEntry(paneIndex, item.Index, item.State.Path)).ToArray();
    private TabState? Current => (Tabs.SelectedItem as TabItem)?.Tag as TabState;

    public void NewTab() => AddTab(Home, _newTabView());
    public void OpenPathInNewTab(string path) => AddTab(path, _newTabView());
    internal void ImportTabs(IReadOnlyList<TabSettings> tabs, int selectedIndex,
        IReadOnlySet<int>? pinned = null)
    {
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
        try
        {
            var view = state.Browser.CaptureViewState();
            if (state.Browser.ViewCaptureFailed)
            {
                Status.Text = "视图配置暂时不可读取，未复制标签。";
                return;
            }
            AddTab(state.Path, view, pinned: state.IsPinned, quickFilter: state.Browser.QuickFilter,
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
    internal ExplorerBrowserHost.SelectedItem? GetSelectedQuickLinkItem() => Current?.Browser.GetSelectedItem();
    internal FileViewExportSnapshot CaptureFileViewExportSnapshot() => Current?.Browser.CaptureFileViewExportSnapshot()
        ?? throw new InvalidOperationException("当前窗格没有可导出的文件视图。");
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
        var current = ReadViewStyle();
        if (current is not { } style) return false;
        return TrySetViewStyle(ViewStyleWheelCycle.Next(style.Mode, style.IconSize, delta));
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
        addressHistoryButtonVisible: true,
        addressPathButtonVisible: true);
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
            ? [BackButton, ForwardButton, UpButton, RefreshButton, TreeButton,
                Address, AddressMenuButton, AddressHistoryButton, GoButton, NewTabButton, CloseTabButton]
            : [BackButton, ForwardButton, UpButton, RefreshButton,
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
        if (Tabs.SelectedItem is TabItem tab && tab.IsKeyboardFocused)
            return focus.TabHeader;
        if (ViewStyleButton.IsKeyboardFocused) return focus.StatusTools;
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
        if (stop == focus.StatusTools) return ViewStyleButton.Focus();
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
        SearchBox.Text = state.Browser.SearchQuery ?? string.Empty;
        SearchBox.Focus();
        SearchBox.SelectAll();
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
    internal bool HasFileViewFocus => _treeHost?.HasNativeFocus() != true && Current?.Browser.HasNativeFocus() == true;

    public void FocusQuickFilter()
    {
        if (Current is not { } state) return;
        QuickFilterBar.Visibility = Visibility.Visible;
        RefreshQuickFilterPresets(QuickFilterPresetCombo.Text);
        ShowQuickFilterEditor(state.Browser.QuickFilter);
        QuickFilterBox.Focus();
        QuickFilterBox.SelectAll();
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
        state.Browser.SetQuickFilter(new QuickFilterSettings(QuickFilterBox.Text,
            QuickFilterMode.SelectedIndex == 1, FilterFoldersCheckBox.IsChecked == true));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QuickFilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
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
        if (Current is not { } state) return;
        ShowQuickFilterEditor(QuickFilterSettings.Empty);
        state.Browser.SetQuickFilter(QuickFilterSettings.Empty);
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
        state.SearchRequestPending = true;
        if (ReferenceEquals(_searchCloseFocusPending, state)) _searchCloseFocusPending = null;
        Status.Text = $"正在搜索：{SearchBox.Text.Trim()}";
        state.Browser.Search(SearchBox.Text, state.SearchScopePath);
    }

    private void CloseSearch_Click(object sender, RoutedEventArgs e) => CloseSearch();

    private void CloseSearch()
    {
        if (Current is not { } state) return;
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
            Address.Text = CurrentPath;
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
    {
        if (Current is not { } state) return;
        var menu = new ContextMenu
        {
            PlacementTarget = AddressHost,
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
        if (_closedTabs.TryPop(out var tab))
            AddTab(tab.Path, tab.View, quickFilter: tab.QuickFilter, previewPaneVisible: tab.PreviewPaneVisible,
                detailsPaneVisible: tab.DetailsPaneVisible);
    }

    internal bool TryOpenSelectedFolderInNewTab() => Current?.Browser.OpenSelectedFolderInNewTab(
        path => AddTab(path, _newTabView())) == true;

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
        AddressPathHistory? addressHistory = null)
    {
        var browser = new ExplorerBrowserHost(path, view ?? _newTabView(), quickFilter ?? QuickFilterSettings.Empty,
            previewPaneVisible, detailsPaneVisible, _hiddenItemsPreference);
        var tab = new TabItem();
        if (_compactMode) tab.Padding = new Thickness(4, 1, 4, 1);
        var state = new TabState { Tab = tab, Browser = browser, Path = path, IsPinned = pinned,
            AddressHistory = addressHistory ?? new AddressPathHistory() };
        state.AddressHistory.Record(path);
        tab.Tag = state;
        browser.QuickFilterChanged += settings =>
        {
            if (Current != state) return;
            if (!QuickFilterBox.IsKeyboardFocusWithin) ShowQuickFilterEditor(settings);
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        browser.NavigationAvailabilityChanged += (_, _) =>
        {
            if (Current == state) RefreshNavigationAvailability();
        };
        browser.SelectionContextChanged += (_, _) =>
        {
            if (Current == state)
            {
                CommandContextChanged?.Invoke(this, EventArgs.Empty);
                QueueSelectionCountsRefresh(state);
                if (_autoExpandTreeSelection && IsVisible)
                {
                    var requestVersion = ++_treeSelectionSyncRequestVersion;
                    var selectionPath = state.Path;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (!_autoExpandTreeSelection || requestVersion != _treeSelectionSyncRequestVersion ||
                            Current != state || !IsVisible ||
                            !string.Equals(state.Path, selectionPath, StringComparison.OrdinalIgnoreCase)) return;
                        var folder = state.Browser.GetSelectedFolderLocation();
                        if (folder is not null && _autoExpandTreeSelection &&
                            requestVersion == _treeSelectionSyncRequestVersion && Current == state && IsVisible &&
                            string.Equals(state.Path, selectionPath, StringComparison.OrdinalIgnoreCase))
                            SelectedFolderForTree?.Invoke(this, folder);
                    }));
                }
            }
        };
        browser.FolderMiddleClicked += (_, folderPath) =>
        {
            if (Current == state && IsVisible) AddTab(folderPath, _newTabView());
        };
        browser.NavigationStarting += (_, _) =>
        {
            if (!state.SearchRequestPending && state.Browser.SearchQuery is null)
                SetSearchScope(state, null);
            if (ReferenceEquals(_recursiveSizeOwner, state) || ReferenceEquals(_lastRecursiveSizeOwner, state))
                CancelRecursiveDirectorySize();
            if (Current != state) return;
            CancelSelectionCountsRefresh();
            RefreshNavigationAvailability();
            _treeHost?.CancelSynchronization();
            _diskSpace.Reset();
            ItemCounts.Text = "";
            ItemCounts.ToolTip = null;
        };
        browser.CustomCommands = selectedItem =>
        {
            var actions = new List<ExplorerBrowserHost.MenuAction>();
            if (selectedItem is { IsFileSystemFolder: true })
                actions.Add(new("在新标签页中打开", () => AddTab(selectedItem.Path, _newTabView())));
            actions.Add(new("新建标签页", NewTab));
            actions.Add(new("复制当前标签", () => DuplicateTab(state)));
            actions.Add(new(state.IsPinned ? "取消固定当前标签" : "固定当前标签", () => TogglePinned(state)));
            if (!state.IsPinned) actions.Add(new("关闭当前标签", () => CloseTab(state)));
            actions.Add(new("关闭其他标签", () =>
            {
                Tabs.SelectedItem = state.Tab;
                CloseOtherTabs();
            }));
            actions.Add(new("恢复关闭的标签", ReopenClosedTab));
            actions.Add(new(selectedItem is null ? "复制当前文件夹路径" : "复制完整路径",
                () => Clipboard.SetText(selectedItem?.Path ?? state.Path)));
            return actions;
        };
        browser.Navigated += (_, navigatedPath) =>
        {
            state.SearchRequestPending = false;
            SetSearchScope(state, null);
            var restoreSearchCloseFocus = ReferenceEquals(_searchCloseFocusPending, state);
            if (restoreSearchCloseFocus) _searchCloseFocusPending = null;
            state.Path = navigatedPath;
            state.AddressHistory.Record(navigatedPath);
            UpdateTabHeader(state);
            if (Current == state)
            {
                var restoreFocus = _addressNavigationPending &&
                    (Address.IsKeyboardFocusWithin || GoButton.IsKeyboardFocused);
                RenderBreadcrumb(navigatedPath);
                if (!_addressEditing || _addressNavigationPending)
                {
                    Address.Text = navigatedPath;
                    ShowBreadcrumb();
                }
                _addressNavigationPending = false;
                SearchBar.Visibility = Visibility.Collapsed;
                Status.Text = "Shell 视图";
                if (browser.IsFileSystemLocation) _treeHost?.SetDirectory(navigatedPath);
                else _treeHost?.SetVirtualLocation();
                QueueSelectionCountsRefresh(state);
                if (restoreFocus)
                    Activated?.Invoke(this, EventArgs.Empty);
                if (restoreFocus || restoreSearchCloseFocus)
                    RestoreFileFocusWhenActive();
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            if (Current == state) CommandContextChanged?.Invoke(this, EventArgs.Empty);
        };
        browser.SearchNavigated += (_, query) =>
        {
            state.SearchRequestPending = false;
            if (ReferenceEquals(_searchCloseFocusPending, state)) return;
            state.Path = browser.CurrentPath;
            UpdateTabHeader(state);
            StateChanged?.Invoke(this, EventArgs.Empty);
            if (Current != state) return;
            RenderBreadcrumb(state.Path);
            if (!_addressEditing)
            {
                Address.Text = state.Path;
                ShowBreadcrumb();
            }
            SearchBar.Visibility = Visibility.Visible;
            if (!SearchBox.IsKeyboardFocusWithin || SearchBox.Text == query)
                SearchBox.Text = query;
            Status.Text = $"搜索结果：{query}";
            QueueSelectionCountsRefresh(state);
            CommandContextChanged?.Invoke(this, EventArgs.Empty);
        };
        browser.NavigationFailed += (_, message) =>
        {
            state.SearchRequestPending = false;
            if (ReferenceEquals(_searchCloseFocusPending, state))
                _searchCloseFocusPending = null;
            if (Current == state)
            {
                _addressNavigationPending = false;
                if (state.Browser.SearchQuery is { } query)
                {
                    SearchBar.Visibility = Visibility.Visible;
                    SearchBox.Text = query;
                }
                Status.Text = message;
                SyncTreeToCurrent(state);
                QueueSelectionCountsRefresh(state);
            }
            if (Current == state) CommandContextChanged?.Invoke(this, EventArgs.Empty);
        };
        browser.MenuError += (_, message) =>
        {
            if (Current == state) Status.Text = $"Shell 视图：{message}";
        };
        browser.Activated += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        browser.HeaderPresetMenuRequested += point => HeaderPresetMenuRequested?.Invoke(this, point);
        browser.ShortcutHandler = shortcut => shortcut == ExplorerShortcut.OpenSelectionInNewTab
            ? TryOpenSelectedFolderInNewTab()
            : ShortcutRequested?.Invoke(this, shortcut) == true;
        browser.CtrlWheelHandler = CycleViewStyle;
        browser.MayRestoreFocus = () => Current == state && IsVisible &&
            _treeHost?.HasNativeFocus() != true && MayRestoreFileFocus?.Invoke() == true;
        UpdateTabHeader(state);
        tab.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragCandidate = e.ClickCount == 1 ? tab : null;
            _dragStart = e.GetPosition(Tabs);
        };
        tab.PreviewMouseRightButtonDown += (_, _) => Tabs.SelectedItem = tab;
        tab.AllowDrop = true;
        tab.DragOver += Tab_DragOver;
        tab.Drop += Tab_Drop;
        tab.ContextMenu = CreateTabContextMenu(state);
        Tabs.Items.Add(tab);
        var insertion = pinned ? _tabOrder.TakeWhile(item => ((TabState)item.Tag).IsPinned).Count() : _tabOrder.Count;
        _tabOrder.Insert(insertion, tab);
        UpdateTabOrders();
        if (select) Tabs.SelectedItem = tab;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return tab;
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
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        header.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle ||
                (e.ChangedButton == MouseButton.Left && e.ClickCount == 2))
            {
                CloseTab(state);
                e.Handled = true;
            }
        };
        state.Tab.Header = header;
        state.Tab.ToolTip = state.IsPinned ? $"固定标签 · {state.Path}" : state.Path;
        if (state.Tab.IsSelected)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(() => { if (state.Tab.IsSelected) state.Tab.BringIntoView(); }));
    }

    private ContextMenu CreateTabContextMenu(TabState state)
    {
        var menu = new ContextMenu();
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
        _closedTabs.Push(new TabSettings(state.Path, state.Browser.CachedViewState(), state.Browser.QuickFilter,
            state.Browser.PreviewPaneVisible, state.Browser.DetailsPaneVisible));
        while (_closedTabs.Count > 20)
        {
            var recent = _closedTabs.Take(20).Reverse().ToArray();
            _closedTabs.Clear();
            foreach (var tab in recent) _closedTabs.Push(tab);
        }
        Tabs.Items.Remove(state.Tab);
        _tabOrder.Remove(state.Tab);
        UpdateTabOrders();
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
        state.Tab.BringIntoView();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Navigate(string path)
    {
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
        data.SetData(TabDragFormat, _dragToken);
        try { DragDrop.DoDragDrop(candidate, data, DragDropEffects.Move); }
        finally
        {
            _draggedTab = null;
            _dragToken = null;
        }
    }

    private void Tabs_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragCandidate = null;

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
            return data.GetDataPresent(TabDragFormat) &&
                string.Equals(data.GetData(TabDragFormat) as string, _dragToken, StringComparison.Ordinal);
        }
        catch (Exception) { return false; }
    }

    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not TabItem target || !IsOwnTabDrag(e.Data) || _draggedTab is not { } source ||
            ((TabState)source.Tag).IsPinned != ((TabState)target.Tag).IsPinned)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
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
        if (sender is not TabItem target || !IsOwnTabDrag(e.Data) || _draggedTab is not { } source)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        ReorderTab(source, target, e.GetPosition(target).X >= target.ActualWidth / 2);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void TabStrip_DragOver(object sender, DragEventArgs e)
    {
        // TabItem handles the app's private reorder payload; the strip only accepts Shell folders.
        e.Effects = _draggedTab is null && ShellTabDropTarget.HasShellLocationData(e.Data)
            ? GetTabStripDropEffect(e.AllowedEffects)
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TabStrip_Drop(object sender, DragEventArgs e)
    {
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

    private void UpdateTabOrders()
    {
        for (var index = 0; index < _tabOrder.Count; index++)
            TabHeaderPanel.SetOrder(_tabOrder[index], index);
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs || Current is not { } state) return;
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
        ItemCounts.Text = ""; // Never carry a previous tab's selection counts across a switch.
        ItemCounts.ToolTip = null;
        CancelSelectionCountsRefresh();
        QueueSelectionCountsRefresh(state);
        _addressNavigationPending = false;
        Address.Text = state.Path;
        RenderBreadcrumb(state.Path);
        ShowBreadcrumb();
        SearchBar.Visibility = state.Browser.SearchQuery is null ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.Text = state.Browser.SearchQuery ?? string.Empty;
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
            if (IsLoaded && IsVisible) FocusCurrentView();
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
        Address.Text = CurrentPath;
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
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (MayRestoreFileFocus?.Invoke() == true) FocusCurrentView();
        }));
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
            Address.Text = CurrentPath;
            ShowBreadcrumb();
            Activated?.Invoke(this, EventArgs.Empty);
            RestoreFileFocusWhenActive();
            e.Handled = true;
        }
    }

    private void Address_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_classicAddressBar && !_addressEditing) FocusAddress();
    }

    private void Address_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_addressEditing || ReferenceEquals(e.NewFocus, GoButton)) return;
        Address.Text = CurrentPath;
        ShowBreadcrumb();
    }

    private void GoButton_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_addressEditing || ReferenceEquals(e.NewFocus, Address)) return;
        Address.Text = CurrentPath;
        ShowBreadcrumb();
    }
    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTab();
    private void CloseTab_Click(object sender, RoutedEventArgs e) => CloseCurrentTab();
}
