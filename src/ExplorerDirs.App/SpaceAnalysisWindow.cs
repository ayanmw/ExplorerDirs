using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ExplorerDirs.App;

internal sealed class SpaceAnalysisWindow : Window
{
    private readonly TextBox _address = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private readonly DataGrid _rows = new() { AutoGenerateColumns = false, IsReadOnly = true,
        CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, EnableRowVirtualization = true };
    private readonly Canvas _map = new() { ClipToBounds = true, MinHeight = 100 };
    private readonly ComboBox _source = new() { Width = 125, Margin = new Thickness(6, 0, 0, 0) };
    private readonly Action<string> _locate;
    private SpaceAnalysisSnapshot? _snapshot;
    private CancellationTokenSource? _cancellation;
    private long _request;
    private bool _closed;
    private sealed class AnalysisProgress : IProgress<KeyValuePair<string, FolderSizeValue>>
    {
        private int _completed;
        internal int Completed => Volatile.Read(ref _completed);
        public void Report(KeyValuePair<string, FolderSizeValue> value) => Interlocked.Increment(ref _completed);
    }

    internal SpaceAnalysisWindow(Window owner, string directory, FolderSizeSource source, Action<string> locate)
    {
        Owner = owner; _locate = locate;
        Title = "目录空间分析 · 逻辑大小";
        Width = 1050; Height = 720; MinWidth = 650; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "ExplorerDirs.WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ExplorerDirs.WindowTextBrush");
        var root = new DockPanel { Margin = new Thickness(10) };
        var navigation = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(navigation, Dock.Top); root.Children.Add(navigation);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right); navigation.Children.Add(actions);
        foreach (var (label, value) in new[] { ("自动", FolderSizeSource.Auto), ("Everything", FolderSizeSource.Everything), ("内置扫描", FolderSizeSource.BuiltIn) })
            _source.Items.Add(new ComboBoxItem { Content = label, Tag = value, IsSelected = value == source });
        actions.Children.Add(_source);
        void Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            button.Click += (_, _) => action(); actions.Children.Add(button);
        }
        Button("分析", () => _ = LoadAsync(_address.Text));
        Button("上级", NavigateParent);
        Button("取消", () => _cancellation?.Cancel());
        Button("在文件视图定位", LocateSelection);
        _address.Text = directory; _address.VerticalContentAlignment = VerticalAlignment.Center;
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = LoadAsync(_address.Text); } };
        navigation.Children.Add(_address);
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        var panels = new Grid(); panels.RowDefinitions.Add(new RowDefinition());
        panels.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
        panels.RowDefinitions.Add(new RowDefinition());
        _rows.SetResourceReference(BackgroundProperty, "ExplorerDirs.ControlBackgroundBrush");
        _rows.SetResourceReference(ForegroundProperty, "ExplorerDirs.ControlTextBrush");
        _rows.SetResourceReference(DataGrid.RowBackgroundProperty, "ExplorerDirs.WindowBackgroundBrush");
        foreach (var (title, property, width) in new[] { ("名称", "DisplayName", 280d), ("逻辑大小", "DisplaySize", 130d),
                     ("占可用总量", "DisplayPercentage", 100d), ("来源", "DisplaySource", 110d), ("状态", "DisplayDetail", 330d) })
            _rows.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width,
                SortMemberPath = property == "DisplaySize" ? "Bytes" : property == "DisplayPercentage" ? "Percentage" : property });
        _rows.MouseDoubleClick += (_, e) =>
        {
            if (_rows.SelectedItem is not SpaceAnalysisEntry { IsDirectory: true } entry) return;
            // Header or empty-space double clicks must not drill into a previously selected row.
            var element = e.OriginalSource as DependencyObject;
            while (element is not null && element is not DataGridRow)
                element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element);
            if (element is DataGridRow row && ReferenceEquals(row.Item, entry)) _ = LoadAsync(entry.Path);
        };
        _rows.SelectionChanged += (_, _) => UpdateMapSelection();
        panels.Children.Add(_rows);
        var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(splitter, 1); panels.Children.Add(splitter);
        Grid.SetRow(_map, 2); panels.Children.Add(_map);
        _map.SetResourceReference(BackgroundProperty, "ExplorerDirs.ControlBackgroundBrush");
        _map.SizeChanged += (_, _) => DrawMap();
        root.Children.Add(panels); Content = root;
        Loaded += (_, _) => _ = LoadAsync(directory);
        Closed += (_, _) =>
        {
            _closed = true; _request++; _cancellation?.Cancel();
            _snapshot = null; _rows.ItemsSource = null; _map.Children.Clear();
        };
    }

    private void NavigateParent()
    {
        if (_closed) return;
        try
        {
            // An unfinished address draft is not the parent of the displayed analysis.
            var path = System.IO.Path.GetFullPath(_snapshot?.Path ?? _address.Text);
            if (System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path)) is { } parent)
                _ = LoadAsync(parent);
        }
        catch (Exception error) when (error is IOException or ArgumentException)
        { _status.Text = $"无法打开上级目录：{error.Message}"; }
    }

    private void LocateSelection()
    {
        if (_closed || _rows.SelectedItem is not SpaceAnalysisEntry entry) return;
        // Opening a tab can fail at the tab limit; keep the analysis and report it in this window.
        try { _locate(entry.Path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _status.Text = $"定位失败：{error.Message}"; }
    }

    private async Task LoadAsync(string directory)
    {
        if (_closed) return;
        _cancellation?.Cancel();
        var request = ++_request;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        var source = _source.SelectedItem is ComboBoxItem { Tag: FolderSizeSource value } ? value : FolderSizeSource.Auto;
        _status.Text = "正在读取目录并分析大小，可取消…";
        var progress = new AnalysisProgress();
        // Coalesce Everything's batched results instead of queueing one UI callback per directory.
        var progressTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        progressTimer.Tick += (_, _) =>
        {
            if (!_closed && request == _request && !cancellation.IsCancellationRequested)
                _status.Text = $"已处理 {progress.Completed} 个子目录，可取消…";
        };
        progressTimer.Start();
        try
        {
            var path = System.IO.Path.GetFullPath(directory);
            var snapshot = await SpaceAnalysisSnapshot.ReadAsync(path, source, cancellation.Token, progress);
            if (_closed || request != _request || cancellation.IsCancellationRequested) return;
            _snapshot = snapshot; _address.Text = path; _rows.ItemsSource = snapshot.Entries;
            var known = snapshot.Entries.Count(item => item.Bytes.HasValue);
            _status.Text = $"{snapshot.Entries.Count} 项，大小可用 {known} 项；逻辑大小合计 {snapshot.KnownBytes:N0} B。" +
                (snapshot.IsPartial ? " 存在未知或不完整结果，占比仅相对于可用部分。" : "") +
                " 双击目录或图块下钻；图中显示前 150 项，其余合并。";
            DrawMap();
        }
        catch (OperationCanceledException)
        { if (!_closed && request == _request) { _status.Text = "已取消；保留上一次完成的分析。"; if (_snapshot is not null) _address.Text = _snapshot.Path; } }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!_closed && request == _request) { _status.Text = $"分析失败：{error.Message}"; if (_snapshot is not null) _address.Text = _snapshot.Path; } }
        finally { progressTimer.Stop(); if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null; }
    }

    private void DrawMap()
    {
        _map.Children.Clear();
        if (_snapshot is null) return;
        // Resizing redraws only 150 tiles: avoid copying every entry of a large analysis each time.
        var shown = new List<(SpaceAnalysisEntry? Entry, decimal Weight)>(151);
        decimal otherBytes = 0;
        foreach (var entry in _snapshot.Entries)
        {
            if (entry.Bytes is not > 0) continue;
            if (shown.Count < 150) shown.Add((entry, entry.Bytes.Value));
            else otherBytes += entry.Bytes.Value;
        }
        if (otherBytes > 0) shown.Add((null, otherBytes));
        var rectangles = SpaceTreemapLayout.Create(shown.Select(item => item.Weight).ToArray(), _map.ActualWidth, _map.ActualHeight);
        var palette = new[] { 0x376B91u, 0x657B3Cu, 0x86603Eu, 0x73598Eu, 0x337B75u, 0x99565Fu };
        foreach (var rectangle in rectangles)
        {
            var item = shown[rectangle.ItemIndex];
            var color = palette[rectangle.ItemIndex % palette.Length];
            var tile = new Border { Width = rectangle.Width, Height = rectangle.Height,
                Tag = item.Entry,
                Background = new SolidColorBrush(Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color)),
                BorderThickness = new Thickness(ReferenceEquals(_rows.SelectedItem, item.Entry) && item.Entry is not null ? 3 : 1),
                ToolTip = item.Entry is { } entry ? $"{entry.Path}\n{entry.DisplaySize} · {entry.DisplayPercentage}\n{entry.Source} {entry.Detail}" : $"其他项目：{item.Weight:N0} B" };
            tile.SetResourceReference(Border.BorderBrushProperty,
                ReferenceEquals(_rows.SelectedItem, item.Entry) && item.Entry is not null ? "ExplorerDirs.AccentBrush" : "ExplorerDirs.BorderBrush");
            if (rectangle.Width > 65 && rectangle.Height > 32)
                tile.Child = new TextBlock { Text = item.Entry?.Name ?? "其他项目", Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4) };
            tile.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (item.Entry is not { } entry) return;
                if (e.ClickCount == 2 && entry.IsDirectory) _ = LoadAsync(entry.Path);
                else { _rows.SelectedItem = entry; _rows.ScrollIntoView(entry); }
            };
            Canvas.SetLeft(tile, rectangle.X); Canvas.SetTop(tile, rectangle.Y); _map.Children.Add(tile);
        }
    }

    private void UpdateMapSelection()
    {
        // Preserve tile identity during selection, including the interval between double clicks.
        // Recreating all tiles here would repaint the map and can break drill-down hit testing.
        foreach (var tile in _map.Children.OfType<Border>())
        {
            var selected = tile.Tag is not null && ReferenceEquals(tile.Tag, _rows.SelectedItem);
            tile.BorderThickness = new Thickness(selected ? 3 : 1);
            tile.SetResourceReference(Border.BorderBrushProperty, selected ? "ExplorerDirs.AccentBrush" : "ExplorerDirs.BorderBrush");
        }
    }
}
