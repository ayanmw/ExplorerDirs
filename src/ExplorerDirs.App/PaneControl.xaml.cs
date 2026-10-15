using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ExplorerDirs.App;

public partial class PaneControl : UserControl
{
    private readonly Stack<string> _closedPaths = new();

    private sealed class TabState
    {
        public required TabItem Tab { get; init; }
        public required ExplorerBrowserHost Browser { get; init; }
        public required string Path { get; set; }
    }

    public event EventHandler? StateChanged;
    public event EventHandler<int>? LayoutRequested;
    public event EventHandler? Activated;

    public PaneControl(IEnumerable<string> paths)
    {
        InitializeComponent();
        foreach (var path in paths)
            AddTab(Directory.Exists(path) ? path : Home, false);
        if (Tabs.Items.Count == 0)
            AddTab(Home, false);
        Tabs.SelectedIndex = 0;
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string CurrentPath => Current?.Path ?? Home;
    public IReadOnlyList<string> Paths => Tabs.Items.Cast<TabItem>()
        .Select(tab => ((TabState)tab.Tag).Path).ToArray();
    private TabState? Current => (Tabs.SelectedItem as TabItem)?.Tag as TabState;

    public void NewTab() => AddTab(Home);
    public void DuplicateCurrentTab() => AddTab(CurrentPath);
    public void RefreshCurrent() => Current?.Browser.Refresh();
    public void CloseCurrentTab() { if (Current is { } state) CloseTab(state); }
    public void ReopenClosedTab() { if (_closedPaths.TryPop(out var path)) AddTab(path); }

    public void CloseOtherTabs()
    {
        if (Current is not { } current) return;
        foreach (var tab in Tabs.Items.Cast<TabItem>().ToArray())
        {
            var state = (TabState)tab.Tag;
            if (state != current) CloseTab(state);
        }
    }

    private void AddTab(string path, bool select = true)
    {
        var browser = new ExplorerBrowserHost(path);
        var tab = new TabItem { Content = browser };
        var state = new TabState { Tab = tab, Browser = browser, Path = path };
        tab.Tag = state;
        browser.CustomCommands = selectedPath =>
        {
            var actions = new List<ExplorerBrowserHost.MenuAction>();
            if (selectedPath is not null && Directory.Exists(selectedPath))
                actions.Add(new("在新标签页中打开", () => AddTab(selectedPath)));
            actions.Add(new("新建标签页", NewTab));
            actions.Add(new("复制当前标签", () => AddTab(state.Path)));
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
        browser.LayoutCommands = () => LayoutPreset.All.Select(preset =>
            new ExplorerBrowserHost.MenuAction(preset.Name,
                () => LayoutRequested?.Invoke(this, preset.Id))).ToArray();
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
            if (Current == state) Status.Text = $"菜单扩展失败：{message}";
        };
        browser.Activated += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
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
        header.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(name) ? state.Path : name });
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
    }

    private ContextMenu CreateTabContextMenu(TabState state)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "新建标签页", NewTab, "Ctrl+T");
        AddMenuItem(menu, "复制当前标签", () => AddTab(state.Path), "Ctrl+K");
        AddMenuItem(menu, "恢复关闭的标签", ReopenClosedTab, "Ctrl+Shift+T");
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "关闭标签页", () => CloseTab(state), "Ctrl+W");
        AddMenuItem(menu, "关闭其他标签", () =>
        {
            Tabs.SelectedItem = state.Tab;
            CloseOtherTabs();
        });
        menu.Items.Add(new Separator());
        AddLayoutSubmenu(menu);
        return menu;
    }

    private void AddLayoutSubmenu(ContextMenu menu)
    {
        var layout = new MenuItem { Header = "布局" };
        foreach (var preset in LayoutPreset.All)
        {
            var item = new MenuItem { Header = preset.Name };
            item.Click += (_, _) => LayoutRequested?.Invoke(this, preset.Id);
            layout.Items.Add(item);
        }
        menu.Items.Add(layout);
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
        _closedPaths.Push(state.Path);
        while (_closedPaths.Count > 20)
        {
            var recent = _closedPaths.Take(20).Reverse().ToArray();
            _closedPaths.Clear();
            foreach (var path in recent) _closedPaths.Push(path);
        }
        Tabs.Items.Remove(state.Tab);
        state.Browser.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Navigate(string path)
    {
        try
        {
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
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null) Navigate(parent.FullName);
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCurrent();
    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(Address.Text);
    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Navigate(Address.Text); e.Handled = true; }
    }
    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTab();
    private void CloseTab_Click(object sender, RoutedEventArgs e) => CloseCurrentTab();
}
