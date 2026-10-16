using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ExplorerDirs.App;

public partial class PaneControl : UserControl
{
    // The Details column header is one Shell focus stop; arrow keys move among columns.
    private const int ColumnHeaderStop = 9;
    private const int FileStopWithoutHeader = 9;
    private const int FileStopWithHeader = 10;
    private readonly Stack<TabSettings> _closedTabs = new();
    private readonly Func<ViewState> _newTabView;

    private sealed class TabState
    {
        public required TabItem Tab { get; init; }
        public required ExplorerBrowserHost Browser { get; init; }
        public required string Path { get; set; }
    }

    public event EventHandler? StateChanged;
    public event EventHandler? Activated;
    internal event Func<PaneControl, ExplorerShortcut, bool>? ShortcutRequested;
    internal Func<bool>? MayRestoreFileFocus { get; set; }

    internal PaneControl(IEnumerable<TabSettings> tabs, Func<ViewState> newTabView)
    {
        InitializeComponent();
        _newTabView = newTabView;
        foreach (var tab in tabs)
            AddTab(tab.Path, tab.View, false);
        if (Tabs.Items.Count == 0)
            AddTab(Home, _newTabView(), false);
        Tabs.SelectedIndex = 0;
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string CurrentPath => Current?.Path ?? Home;
    internal IReadOnlyList<TabSettings> SnapshotTabs() => Tabs.Items.Cast<TabItem>()
        .Select(tab =>
        {
            var state = (TabState)tab.Tag;
            return new TabSettings(state.Path, state.Browser.CaptureViewState());
        }).ToArray();
    private TabState? Current => (Tabs.SelectedItem as TabItem)?.Tag as TabState;

    public void NewTab() => AddTab(Home, _newTabView());
    public void DuplicateCurrentTab()
    {
        if (Current is { } state)
            AddTab(state.Path, state.Browser.CaptureViewState());
    }
    public void RefreshCurrent() => Current?.Browser.Refresh();
    public void CreateNewFolder() => Current?.Browser.CreateNewFolder();
    public void GoParent() => Current?.Browser.GoParent();
    public void GoBack() => Current?.Browser.GoBack();
    public void GoForward() => Current?.Browser.GoForward();
    public bool FocusCurrentView() => Current?.Browser.FocusView() == true;
    internal bool IsShellRenameEditorFocused => Current?.Browser.IsRenameEditorFocused() == true;
    internal int FocusStopCount => Current?.Browser.HasColumnHeader() == true ? 11 : 10;

    internal int CurrentFocusStop()
    {
        UIElement[] controls = [BackButton, ForwardButton, UpButton, RefreshButton,
            Address, GoButton, NewTabButton, CloseTabButton];
        for (var index = 0; index < controls.Length; index++)
            if (controls[index].IsKeyboardFocusWithin) return index;
        if (Tabs.SelectedItem is TabItem tab && tab.IsKeyboardFocused) return 8;
        if (Current?.Browser.HasNativeFocus() == true)
            return Current.Browser.IsColumnHeaderFocused() ? ColumnHeaderStop : FocusStopCount - 1;
        return -1;
    }

    internal bool FocusStop(int stop)
    {
        UIElement? target = stop switch
        {
            0 => BackButton, 1 => ForwardButton, 2 => UpButton, 3 => RefreshButton,
            4 => Address, 5 => GoButton, 6 => NewTabButton, 7 => CloseTabButton,
            8 => Tabs.SelectedItem as TabItem,
            _ => null
        };
        if (target is not null) return target.Focus();
        if (stop == ColumnHeaderStop && FocusStopCount == 11)
            return Current?.Browser.FocusColumnHeader() == true;
        if (stop == FileStopWithoutHeader && FocusStopCount == 10 ||
            stop == FileStopWithHeader && FocusStopCount == 11)
            return FocusCurrentView();
        return false;
    }
    public void FocusAddress()
    {
        Address.Focus();
        Address.SelectAll();
    }
    public void SelectRelativeTab(bool reverse)
    {
        if (Tabs.Items.Count < 2) return;
        Tabs.SelectedIndex = KeyboardShortcuts.NextPane(Tabs.SelectedIndex, Tabs.Items.Count, reverse);
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
            if (state != current) CloseTab(state);
        }
    }

    private void AddTab(string path, ViewState? view = null, bool select = true)
    {
        var browser = new ExplorerBrowserHost(path, view ?? _newTabView());
        var tab = new TabItem { Content = browser };
        var state = new TabState { Tab = tab, Browser = browser, Path = path };
        tab.Tag = state;
        browser.CustomCommands = selectedPath =>
        {
            var actions = new List<ExplorerBrowserHost.MenuAction>();
            if (selectedPath is not null && Directory.Exists(selectedPath))
                actions.Add(new("在新标签页中打开", () => AddTab(selectedPath, _newTabView())));
            actions.Add(new("新建标签页", NewTab));
            actions.Add(new("复制当前标签", () => AddTab(state.Path, state.Browser.CaptureViewState())));
            actions.Add(new("关闭当前标签", () => CloseTab(state)));
            actions.Add(new("关闭其他标签", () =>
            {
                Tabs.SelectedItem = state.Tab;
                CloseOtherTabs();
            }));
            actions.Add(new("恢复关闭的标签", ReopenClosedTab));
            actions.Add(new(selectedPath is null ? "复制当前文件夹路径" : "复制完整路径",
                () => Clipboard.SetText(selectedPath ?? state.Path)));
            return actions;
        };
        browser.Navigated += (_, navigatedPath) =>
        {
            state.Path = navigatedPath;
            UpdateTabHeader(state);
            if (Current == state)
            {
                Address.Text = navigatedPath;
                Status.Text = "Shell 视图";
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        browser.NavigationFailed += (_, message) =>
        {
            if (Current == state) Status.Text = message;
        };
        browser.MenuError += (_, message) =>
        {
            if (Current == state) Status.Text = $"Shell 视图：{message}";
        };
        browser.Activated += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        browser.ShortcutHandler = shortcut => ShortcutRequested?.Invoke(this, shortcut) == true;
        browser.MayRestoreFocus = () => Current == state && IsVisible && MayRestoreFileFocus?.Invoke() == true;
        UpdateTabHeader(state);
        tab.PreviewMouseRightButtonDown += (_, _) => Tabs.SelectedItem = tab;
        tab.ContextMenu = CreateTabContextMenu(state);
        Tabs.Items.Add(tab);
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
        state.Tab.ToolTip = state.Path;
        if (state.Tab.IsSelected)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(() => { if (state.Tab.IsSelected) state.Tab.BringIntoView(); }));
    }

    private ContextMenu CreateTabContextMenu(TabState state)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "新建标签页", NewTab, "Ctrl+T");
        AddMenuItem(menu, "复制当前标签", () => AddTab(state.Path, state.Browser.CaptureViewState()), "Ctrl+K");
        AddMenuItem(menu, "恢复关闭的标签", ReopenClosedTab, "Ctrl+Shift+T");
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "关闭标签页", () => CloseTab(state), "Ctrl+W");
        AddMenuItem(menu, "关闭其他标签", () =>
        {
            Tabs.SelectedItem = state.Tab;
            CloseOtherTabs();
        });
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
        if (Tabs.Items.Count <= 1) return;
        _closedTabs.Push(new TabSettings(state.Path, state.Browser.CaptureViewState()));
        while (_closedTabs.Count > 20)
        {
            var recent = _closedTabs.Take(20).Reverse().ToArray();
            _closedTabs.Clear();
            foreach (var tab in recent) _closedTabs.Push(tab);
        }
        Tabs.Items.Remove(state.Tab);
        state.Browser.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Navigate(string path)
    {
        try
        {
            Status.Text = "正在检查目录…";
            Current?.Browser.Navigate(path);
        }
        catch (Exception ex) { Status.Text = $"无法打开目录：{ex.Message}"; }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs || Current is not { } state) return;
        Address.Text = state.Path;
        Status.Text = "Shell 视图";
        Activated?.Invoke(this, EventArgs.Empty);
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

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        GoParent();
    }
    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => GoForward();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCurrent();
    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(Address.Text);
    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Navigate(Address.Text); e.Handled = true; }
    }
    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTab();
    private void CloseTab_Click(object sender, RoutedEventArgs e) => CloseCurrentTab();
}
