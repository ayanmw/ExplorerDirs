using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ExplorerDirs.App;

internal sealed class FavoritesManagerWindow : Window
{
    internal IReadOnlyList<FavoriteEntry>? Result { get; private set; }
    internal IReadOnlyList<string> ResultFolders { get; private set; } = [];
    private readonly TreeView _tree = new() { AllowDrop = true };
    private readonly TextBox _name = new() { MinWidth = 150 };
    private readonly TextBlock _path = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private Point _dragStart;
    private TreeViewItem? _dragItem;
    private sealed record Folder(string Name);

    internal FavoritesManagerWindow(Window owner, IEnumerable<FavoriteEntry> entries, string currentPath,
        IEnumerable<string>? folders = null)
    {
        Owner = owner; Title = "管理收藏夹";
        Width = 760; Height = 500; MinWidth = 540; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "ExplorerDirs.WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ExplorerDirs.WindowTextBrush");
        _tree.SetResourceReference(BackgroundProperty, "ExplorerDirs.ControlBackgroundBrush");
        _tree.SetResourceReference(ForegroundProperty, "ExplorerDirs.ControlTextBrush");
        var root = new DockPanel { Margin = new Thickness(12) };
        var hint = new TextBlock { Text = "分组在前、目录在后；拖动调整顺序，放到分组中间移入，放到空白处移回根目录。分组不嵌套。工作区仍由收藏夹菜单独立管理。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var edit = new DockPanel(); bottom.Children.Add(edit);
        var rename = MakeButton("重命名", Rename); DockPanel.SetDock(rename, Dock.Right); edit.Children.Add(rename);
        edit.Children.Add(_name); bottom.Children.Add(_path);
        var buttons = new WrapPanel(); bottom.Children.Add(buttons);
        buttons.Children.Add(MakeButton("新建分组", () =>
        {
            var name = _name.Text.Trim(); if (name.Length == 0) name = "新建分组";
            var original = name; var suffix = 2;
            while (FindFolder(name) is not null) name = original + " " + suffix++;
            Select(AddFolder(name)); _name.Focus(); _name.SelectAll();
        }));
        buttons.Children.Add(MakeButton("添加当前目录", () =>
        {
            if (AllEntries().Any(item => FavoritesStore.SamePath(item.Path, currentPath)))
            { _path.Text = "当前目录已在收藏夹中。"; return; }
            var selected = _tree.SelectedItem as TreeViewItem;
            var parent = selected?.Tag is Folder ? selected : FolderParent(selected);
            var item = MakeEntry(new FavoriteEntry(currentPath));
            (parent?.Items ?? _tree.Items).Add(item); Select(item);
        }));
        buttons.Children.Add(MakeButton("删除", () =>
        {
            if (_tree.SelectedItem is not TreeViewItem item) return;
            if (item.Tag is Folder && item.Items.Count > 0 && MessageBox.Show(this,
                "删除此分组和其中的收藏项？不会删除实际文件。", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            Container(item).Remove(item); _name.Clear(); _path.Text = "";
        }));
        buttons.Children.Add(MakeButton("上移", () => Move(-1)));
        buttons.Children.Add(MakeButton("下移", () => Move(1)));
        buttons.Children.Add(MakeButton("保存", () =>
        {
            var result = AllEntries().ToArray();
            var names = _tree.Items.Cast<TreeViewItem>().Where(item => item.Tag is Folder).Select(item => ((Folder)item.Tag).Name).ToArray();
            if (result.Length > 200 || names.Length > 200) { _path.Text = "最多保存 200 个收藏项和 200 个分组。"; return; }
            Result = result; ResultFolders = names; DialogResult = true;
        }));
        buttons.Children.Add(MakeButton("取消", () => DialogResult = false));
        foreach (var folder in folders ?? []) if (FindFolder(folder) is null) AddFolder(folder);
        foreach (var entry in entries)
            if (entry.Group.Length == 0) _tree.Items.Add(MakeEntry(entry));
            else (FindFolder(entry.Group) ?? AddFolder(entry.Group)).Items.Add(MakeEntry(entry));
        _tree.SelectedItemChanged += (_, _) =>
        {
            if (_tree.SelectedItem is not TreeViewItem item) return;
            _name.Text = item.Tag is Folder folder ? folder.Name : ((FavoriteEntry)item.Tag).Name;
            _path.Text = item.Tag is FavoriteEntry entry ? entry.Path : "收藏夹分组";
        };
        _name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Rename(); e.Handled = true; } };
        _tree.PreviewKeyDown += (_, e) => { if (e.Key == Key.F2) { _name.Focus(); _name.SelectAll(); e.Handled = true; } };
        _tree.PreviewMouseLeftButtonDown += (_, e) =>
        { _dragStart = e.GetPosition(_tree); _dragItem = ItemAt(e.OriginalSource as DependencyObject); };
        _tree.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem is not { } item) return;
            var point = e.GetPosition(_tree);
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragItem = null; DragDrop.DoDragDrop(_tree, new DataObject(typeof(TreeViewItem), item), DragDropEffects.Move);
        };
        _tree.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetData(typeof(TreeViewItem)) is TreeViewItem item && Owns(item) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        _tree.Drop += (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(typeof(TreeViewItem)) is not TreeViewItem item || !Owns(item)) return;
            var target = ItemAt(e.OriginalSource as DependencyObject);
            if (target == item || FolderParent(target) == item) return;
            var y = target is null ? 0 : e.GetPosition(target).Y;
            // Groups remain at root, with leaves at most one level below them.
            var destination = item.Tag is Folder ? null : target?.Tag is Folder && y >= 5 && y <= 20 ? target : FolderParent(target);
            var collection = destination?.Items ?? _tree.Items;
            var index = target is not null && collection.Contains(target) ? collection.IndexOf(target) + (y > 12 ? 1 : 0) : collection.Count;
            var source = Container(item); var previous = source.IndexOf(item);
            if (ReferenceEquals(source, collection) && previous < index) index--;
            source.Remove(item);
            if (destination is null)
            {
                var groupCount = collection.Cast<TreeViewItem>().Count(node => node.Tag is Folder);
                index = item.Tag is Folder ? Math.Min(index, groupCount) : Math.Max(index, groupCount);
            }
            collection.Insert(index, item);
            if (destination is not null) destination.IsExpanded = true;
            Select(item);
        };
        root.Children.Add(_tree); Content = root;
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => action(); return button;
    }
    private TreeViewItem AddFolder(string name)
    {
        var item = new TreeViewItem { Tag = new Folder(name), Header = name, IsExpanded = true };
        _tree.Items.Insert(_tree.Items.Cast<TreeViewItem>().Count(node => node.Tag is Folder), item);
        return item;
    }
    private TreeViewItem? FindFolder(string name) => _tree.Items.Cast<TreeViewItem>().FirstOrDefault(item =>
        item.Tag is Folder folder && folder.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static TreeViewItem MakeEntry(FavoriteEntry entry)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new Image { Source = ShellIconProvider.GetIcon(entry.Path, true), Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) });
        line.Children.Add(new TextBlock { Text = entry.Name.Length == 0 ? entry.Path : entry.Name });
        return new TreeViewItem { Tag = entry, Header = line, ToolTip = entry.Path };
    }
    private static TreeViewItem? FolderParent(TreeViewItem? item) => item is null ? null : ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;
    private ItemCollection Container(TreeViewItem item) => FolderParent(item)?.Items ?? _tree.Items;
    private bool Owns(TreeViewItem item) => _tree.Items.Contains(item) || FolderParent(item) is { } parent && _tree.Items.Contains(parent);
    private static TreeViewItem? ItemAt(DependencyObject? source)
    {
        while (source is not null && source is not TreeViewItem)
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        return source as TreeViewItem;
    }
    private static void Select(TreeViewItem item) { item.IsSelected = true; item.BringIntoView(); }
    private IEnumerable<FavoriteEntry> AllEntries()
    {
        foreach (TreeViewItem item in _tree.Items)
            if (item.Tag is FavoriteEntry entry) yield return entry with { Group = "" };
            else foreach (TreeViewItem child in item.Items) yield return ((FavoriteEntry)child.Tag) with { Group = ((Folder)item.Tag).Name };
    }
    private void Rename()
    {
        if (_tree.SelectedItem is not TreeViewItem item) return;
        var name = _name.Text.Trim();
        if (item.Tag is Folder)
        {
            if (name.Length == 0 || FindFolder(name) is { } other && other != item) { _path.Text = "分组名称不能为空或重复。"; return; }
            item.Tag = new Folder(name); item.Header = name;
        }
        else
        {
            var entry = ((FavoriteEntry)item.Tag) with { Name = name };
            var replacement = MakeEntry(entry); var header = replacement.Header; replacement.Header = null;
            item.Tag = entry; item.Header = header;
        }
        _tree.Focus();
    }
    private void Move(int delta)
    {
        if (_tree.SelectedItem is not TreeViewItem item) return;
        var items = Container(item); var index = items.IndexOf(item); var next = index + delta;
        if (next < 0 || next >= items.Count) return;
        if (FolderParent(item) is null && (((TreeViewItem)items[next]).Tag is Folder) != (item.Tag is Folder)) return;
        items.RemoveAt(index); items.Insert(next, item); Select(item);
    }
}
