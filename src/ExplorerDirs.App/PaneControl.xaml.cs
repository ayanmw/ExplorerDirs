using System.Diagnostics;
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
        public required ListView List { get; init; }
        public required string Path { get; set; }
        public CancellationTokenSource? Pending { get; set; }
    }

    public event EventHandler? StateChanged;
    public event EventHandler<int>? LayoutRequested;

    public PaneControl(IEnumerable<string> paths)
    {
        InitializeComponent();
        foreach (var path in paths)
            AddTab(Directory.Exists(path) ? path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), false);
        if (Tabs.Items.Count == 0)
            AddTab(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), false);
        Tabs.SelectedIndex = 0;
    }

    public string CurrentPath => Current?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void NewTab() => AddTab(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    public void DuplicateCurrentTab() => AddTab(CurrentPath);
    public void RefreshCurrent()
    {
        if (Current is { } state)
            _ = LoadAsync(state);
    }

    public void CloseCurrentTab()
    {
        if (Current is { } state)
            CloseTab(state);
    }

    public void CloseOtherTabs()
    {
        if (Current is not { } current)
            return;
        foreach (var tab in Tabs.Items.Cast<TabItem>().ToArray())
        {
            var state = (TabState)tab.Tag;
            if (state != current)
                CloseTab(state);
        }
    }

    public void ReopenClosedTab()
    {
        if (_closedPaths.TryPop(out var path))
            AddTab(path);
    }

    public IReadOnlyList<string> Paths => Tabs.Items.Cast<TabItem>()
        .Select(tab => ((TabState)tab.Tag).Path).ToArray();

    private TabState? Current => (Tabs.SelectedItem as TabItem)?.Tag as TabState;

    private void AddTab(string path, bool select = true)
    {
        var list = new ListView();
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "名称", CellTemplate = (DataTemplate)Resources["FileNameCellTemplate"], Width = 280 });
        view.Columns.Add(new GridViewColumn { Header = "类型", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FileEntry.Kind)), Width = 80 });
        view.Columns.Add(new GridViewColumn { Header = "大小", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FileEntry.SizeText)), Width = 90 });
        view.Columns.Add(new GridViewColumn { Header = "修改时间", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FileEntry.ModifiedText)), Width = 140 });
        list.View = view;
        list.MouseDoubleClick += FileList_MouseDoubleClick;
        list.PreviewMouseRightButtonDown += FileList_PreviewMouseRightButtonDown;
        var tab = new TabItem { Content = list };
        var state = new TabState { Tab = tab, List = list, Path = path };
        tab.Tag = state;
        UpdateTabHeader(state);
        tab.PreviewMouseRightButtonDown += (_, _) => Tabs.SelectedItem = tab;
        tab.ContextMenu = CreateTabContextMenu(state);
        list.ContextMenu = CreateFileContextMenu(state);
        Tabs.Items.Add(tab);
        if (select)
            Tabs.SelectedItem = tab;
        _ = LoadAsync(state);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void UpdateTabHeader(TabState state)
    {
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(state.Path));
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Image
        {
            Source = ShellIconProvider.GetIcon(state.Path, true),
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 4, 0)
        });
        header.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(name) ? state.Path : name });
        state.Tab.Header = header;
        state.Tab.ToolTip = state.Path;
    }

    private ContextMenu CreateTabContextMenu(TabState state)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "新建标签页", () => NewTab(), "Ctrl+T");
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

    private ContextMenu CreateFileContextMenu(TabState state)
    {
        var menu = new ContextMenu();
        var open = AddMenuItem(menu, "打开", () => OpenSelected(state, false));
        var openNew = AddMenuItem(menu, "在新标签页中打开", () => OpenSelected(state, true));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "新建标签页", NewTab, "Ctrl+T");
        AddMenuItem(menu, "复制当前标签", DuplicateCurrentTab, "Ctrl+K");
        AddMenuItem(menu, "关闭当前标签", CloseCurrentTab, "Ctrl+W");
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "复制完整路径", () =>
        {
            var path = (state.List.SelectedItem as FileEntry)?.FullPath ?? state.Path;
            Clipboard.SetText(path);
        });
        AddLayoutSubmenu(menu);
        menu.Opened += (_, _) =>
        {
            var entry = state.List.SelectedItem as FileEntry;
            open.IsEnabled = entry is not null;
            openNew.IsEnabled = entry?.IsDirectory == true;
        };
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

    private static MenuItem AddMenuItem(ContextMenu menu, string header, Action action, string? shortcut = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = shortcut ?? "" };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    private void FileList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView list &&
            ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListViewItem item)
            list.SelectedItem = item.DataContext;
    }

    private void OpenSelected(TabState state, bool newTab)
    {
        if (state.List.SelectedItem is FileEntry entry)
            OpenEntry(entry, newTab);
    }

    private void OpenEntry(FileEntry entry, bool newTab)
    {
        if (entry.IsDirectory)
        {
            if (newTab)
                AddTab(entry.FullPath);
            else
                Navigate(entry.FullPath);
        }
        else if (!newTab)
        {
            try
            {
                Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Status.Text = $"无法打开文件：{ex.Message}";
            }
        }
    }

    private void CloseTab(TabState state)
    {
        if (Tabs.Items.Count <= 1)
            return;
        _closedPaths.Push(state.Path);
        while (_closedPaths.Count > 20)
        {
            var recent = _closedPaths.Take(20).Reverse().ToArray();
            _closedPaths.Clear();
            foreach (var path in recent)
                _closedPaths.Push(path);
        }
        state.Pending?.Cancel();
        Tabs.Items.Remove(state.Tab);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadAsync(TabState state)
    {
        state.Pending?.Cancel();
        var pending = new CancellationTokenSource();
        state.Pending = pending;
        if (Current == state)
            Status.Text = "正在读取…";

        try
        {
            var items = await Task.Run(() => FileEntry.ReadDirectory(state.Path, pending.Token), pending.Token);
            if (state.Pending != pending || pending.IsCancellationRequested)
                return;
            state.List.ItemsSource = items;
            if (Current == state)
                Status.Text = $"{items.Count} 项";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (state.Pending == pending && Current == state)
                Status.Text = $"读取失败：{ex.Message}";
        }
        finally
        {
            if (state.Pending == pending)
                state.Pending = null;
            pending.Dispose();
        }
    }

    private void Navigate(string path)
    {
        var state = Current;
        if (state is null)
            return;

        try
        {
            path = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(path);
            state.Path = path;
            UpdateTabHeader(state);
            Address.Text = path;
            _ = LoadAsync(state);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status.Text = $"无法打开目录：{ex.Message}";
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs || Current is not { } state)
            return;
        Address.Text = state.Path;
        Status.Text = state.List.ItemsSource is IReadOnlyCollection<FileEntry> items ? $"{items.Count} 项" : "正在读取…";
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView list ||
            ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is not ListViewItem item ||
            item.DataContext is not FileEntry entry)
            return;

        OpenEntry(entry, false);
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null)
            Navigate(parent.FullName);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCurrent();

    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(Address.Text);

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Navigate(Address.Text);
            e.Handled = true;
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTab();

    private void CloseTab_Click(object sender, RoutedEventArgs e) => CloseCurrentTab();
}
