using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ExplorerDirs.App;

public partial class PaneControl : UserControl
{
    private const string TabDragFormat = "ExplorerDirs.TabReorder.v1";
    // The Details column header is one Shell focus stop; arrow keys move among columns.
    private const int TabHeaderStop = 8;
    private readonly Stack<TabSettings> _closedTabs = new();
    private readonly List<TabItem> _tabOrder = [];
    private readonly Func<ViewState> _newTabView;
    private TabItem? _dragCandidate;
    private Point _dragStart;
    private TabItem? _draggedTab;
    private string? _dragToken;
    private bool _addressEditing;
    private bool _addressNavigationPending;
    private TabState? _searchCloseFocusPending;
    private bool _compactMode;

    private sealed class TabState
    {
        public required TabItem Tab { get; init; }
        public required ExplorerBrowserHost Browser { get; init; }
        public required string Path { get; set; }
        public bool IsPinned { get; set; }
        public bool SearchRequestPending { get; set; }
    }

    public event EventHandler? StateChanged;
    public event EventHandler? Activated;
    internal event Func<PaneControl, ExplorerShortcut, bool>? ShortcutRequested;
    internal Func<bool>? MayRestoreFileFocus { get; set; }

    internal PaneControl(IEnumerable<TabSettings> tabs, Func<ViewState> newTabView,
        IReadOnlySet<int>? pinnedIndices = null, int selectedTabIndex = 0)
    {
        InitializeComponent();
        _newTabView = newTabView;
        var tabIndex = 0;
        foreach (var tab in tabs)
            AddTab(tab.Path, tab.View, false, pinnedIndices?.Contains(tabIndex++) == true);
        if (Tabs.Items.Count == 0)
            AddTab(Home, _newTabView(), false);
        Tabs.SelectedItem = _tabOrder[Math.Clamp(selectedTabIndex, 0, _tabOrder.Count - 1)];
        RenderBreadcrumb(CurrentPath);
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string CurrentPath => Current?.Path ?? Home;
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
            return new TabSettings(state.Path, state.Browser.CachedViewState());
        }).ToArray();
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
            AddTab(state.Path, view, pinned: state.IsPinned);
        }
        catch (Exception ex)
        {
            Status.Text = $"复制标签失败：{ex.Message}";
        }
    }
    public void RefreshCurrent() => Current?.Browser.Refresh();
    public void NavigateTo(string path) => Navigate(path);
    public void CreateNewFolder() => Current?.Browser.CreateNewFolder();
    public void GoParent() => Current?.Browser.GoParent();
    public void GoBack() => Current?.Browser.GoBack();
    public void GoForward() => Current?.Browser.GoForward();
    public bool FocusCurrentView() => Current?.Browser.FocusView() == true;
    internal bool IsShellRenameEditorFocused => Current?.Browser.IsRenameEditorFocused() == true;
    private int SearchStopCount => SearchBar.Visibility == Visibility.Visible ? 3 : 0;
    private int HeaderStop => TabHeaderStop + SearchStopCount + 1;
    internal int FocusStopCount => HeaderStop + (Current?.Browser.HasColumnHeader() == true ? 2 : 1);
    internal IReadOnlyList<int> MajorFocusStops => SearchStopCount == 3
        ? Current?.Browser.HasColumnHeader() == true
            ? [4, TabHeaderStop, TabHeaderStop + 3, HeaderStop, FocusStopCount - 1]
            : [4, TabHeaderStop, TabHeaderStop + 3, FocusStopCount - 1]
        : Current?.Browser.HasColumnHeader() == true
            ? [4, TabHeaderStop, HeaderStop, FocusStopCount - 1]
            : [4, TabHeaderStop, FocusStopCount - 1];

    internal int CurrentFocusStop()
    {
        UIElement[] controls = [BackButton, ForwardButton, UpButton, RefreshButton,
            Address, GoButton, NewTabButton, CloseTabButton];
        for (var index = 0; index < controls.Length; index++)
            if (controls[index].IsKeyboardFocusWithin) return index;
        if (SearchStopCount == 3)
        {
            if (SearchBox.IsKeyboardFocusWithin) return TabHeaderStop;
            if (SearchSubmitButton.IsKeyboardFocusWithin) return TabHeaderStop + 1;
            if (CloseSearchButton.IsKeyboardFocusWithin) return TabHeaderStop + 2;
        }
        if (Tabs.SelectedItem is TabItem tab && tab.IsKeyboardFocused) return TabHeaderStop + SearchStopCount;
        if (Current?.Browser.HasNativeFocus() == true)
            return Current.Browser.IsColumnHeaderFocused() ? HeaderStop : FocusStopCount - 1;
        return -1;
    }

    internal bool FocusStop(int stop)
    {
        UIElement? target = stop switch
        {
            0 => BackButton, 1 => ForwardButton, 2 => UpButton, 3 => RefreshButton,
            4 => null, 5 => GoButton, 6 => NewTabButton, 7 => CloseTabButton,
            _ => null
        };
        if (stop == 4) { FocusAddress(); return Address.IsKeyboardFocused; }
        if (target is not null) return target.Focus();
        if (SearchStopCount == 3)
        {
            if (stop == TabHeaderStop) return SearchBox.Focus();
            if (stop == TabHeaderStop + 1) return SearchSubmitButton.Focus();
            if (stop == TabHeaderStop + 2) return CloseSearchButton.Focus();
        }
        if (stop == TabHeaderStop + SearchStopCount)
            return (Tabs.SelectedItem as TabItem)?.Focus() == true;
        if (stop == HeaderStop && Current?.Browser.HasColumnHeader() == true)
            return Current?.Browser.FocusColumnHeader() == true;
        if (stop == FocusStopCount - 1)
            return FocusCurrentView();
        return false;
    }
    public void FocusSearch()
    {
        if (Current is not { } state) return;
        if (ReferenceEquals(_searchCloseFocusPending, state)) _searchCloseFocusPending = null;
        SearchBar.Visibility = Visibility.Visible;
        SearchBox.Text = state.Browser.SearchQuery ?? string.Empty;
        SearchBox.Focus();
        SearchBox.SelectAll();
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
        state.Browser.Search(SearchBox.Text);
    }

    private void CloseSearch_Click(object sender, RoutedEventArgs e) => CloseSearch();

    private void CloseSearch()
    {
        if (Current is not { } state) return;
        _searchCloseFocusPending = state.SearchRequestPending || state.Browser.SearchQuery is not null
            ? state : null;
        state.SearchRequestPending = false;
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
            GoButton.Content = "转到";
        }
        Address.Focus();
        Address.SelectAll();
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
    public void ReopenClosedTab() { if (_closedTabs.TryPop(out var tab)) AddTab(tab.Path, tab.View); }

    public void CloseOtherTabs()
    {
        if (Current is not { } current) return;
        foreach (var tab in Tabs.Items.Cast<TabItem>().ToArray())
        {
            var state = (TabState)tab.Tag;
            if (state != current && !state.IsPinned) CloseTab(state);
        }
    }

    private void AddTab(string path, ViewState? view = null, bool select = true, bool pinned = false)
    {
        var browser = new ExplorerBrowserHost(path, view ?? _newTabView());
        var tab = new TabItem();
        if (_compactMode) tab.Padding = new Thickness(4, 1, 4, 1);
        var state = new TabState { Tab = tab, Browser = browser, Path = path, IsPinned = pinned };
        tab.Tag = state;
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
            var restoreSearchCloseFocus = ReferenceEquals(_searchCloseFocusPending, state);
            if (restoreSearchCloseFocus) _searchCloseFocusPending = null;
            state.Path = navigatedPath;
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
                if (restoreFocus)
                    Activated?.Invoke(this, EventArgs.Empty);
                if (restoreFocus || restoreSearchCloseFocus)
                    RestoreFileFocusWhenActive();
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
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
            }
        };
        browser.MenuError += (_, message) =>
        {
            if (Current == state) Status.Text = $"Shell 视图：{message}";
        };
        browser.Activated += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        browser.ShortcutHandler = shortcut => ShortcutRequested?.Invoke(this, shortcut) == true;
        browser.MayRestoreFocus = () => Current == state && IsVisible && MayRestoreFileFocus?.Invoke() == true;
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
    }

    private void UpdateTabHeader(TabState state)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(state.Path));
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
        _closedTabs.Push(new TabSettings(state.Path, state.Browser.CachedViewState()));
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
            Current?.Browser.Navigate(path);
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
        foreach (var previous in e.RemovedItems.OfType<TabItem>())
            if (Tabs.Items.Contains(previous) && previous.Tag is TabState oldState && oldState.Browser.IsVisible)
                CaptureViewState(oldState);
        BrowserLayer.Show(state.Browser);
        _addressNavigationPending = false;
        Address.Text = state.Path;
        RenderBreadcrumb(state.Path);
        ShowBreadcrumb();
        SearchBar.Visibility = state.Browser.SearchQuery is null ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.Text = state.Browser.SearchQuery ?? string.Empty;
        Status.Text = state.Browser.SearchQuery is { } query ? $"搜索结果：{query}" : "Shell 视图";
        Activated?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
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
        Address.Visibility = Visibility.Collapsed;
        BreadcrumbScroll.Visibility = Visibility.Visible;
        GoButton.Content = "编辑";
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
            _addressNavigationPending = false;
            Address.Text = CurrentPath;
            ShowBreadcrumb();
            Activated?.Invoke(this, EventArgs.Empty);
            RestoreFileFocusWhenActive();
            e.Handled = true;
        }
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
