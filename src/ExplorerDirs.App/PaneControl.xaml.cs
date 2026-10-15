using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ExplorerDirs.App;

public partial class PaneControl : UserControl
{
    private sealed class TabState
    {
        public required TabItem Tab { get; init; }
        public required ListView List { get; init; }
        public required string Path { get; set; }
        public CancellationTokenSource? Pending { get; set; }
    }

    public event EventHandler? StateChanged;

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

    public IReadOnlyList<string> Paths => Tabs.Items.Cast<TabItem>()
        .Select(tab => ((TabState)tab.Tag).Path).ToArray();

    private TabState? Current => (Tabs.SelectedItem as TabItem)?.Tag as TabState;

    private void AddTab(string path, bool select = true)
    {
        var list = new ListView();
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "名称", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FileEntry.Name)), Width = 280 });
        view.Columns.Add(new GridViewColumn { Header = "类型", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FileEntry.Kind)), Width = 80 });
        view.Columns.Add(new GridViewColumn { Header = "大小", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FileEntry.SizeText)), Width = 90 });
        view.Columns.Add(new GridViewColumn { Header = "修改时间", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FileEntry.ModifiedText)), Width = 140 });
        list.View = view;
        list.MouseDoubleClick += FileList_MouseDoubleClick;
        var tab = new TabItem { Content = list };
        var state = new TabState { Tab = tab, List = list, Path = path };
        tab.Tag = state;
        tab.Header = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;
        Tabs.Items.Add(tab);
        if (select)
            Tabs.SelectedItem = tab;
        _ = LoadAsync(state);
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
            state.Tab.Header = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;
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

        if (entry.IsDirectory)
            Navigate(entry.FullPath);
        else
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

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null)
            Navigate(parent.FullName);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (Current is { } state)
            _ = LoadAsync(state);
    }

    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(Address.Text);

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Navigate(Address.Text);
            e.Handled = true;
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => AddTab(CurrentPath);

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (Tabs.Items.Count <= 1 || Current is not { } state)
            return;
        state.Pending?.Cancel();
        Tabs.Items.Remove(state.Tab);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
