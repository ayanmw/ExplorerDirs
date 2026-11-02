using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ExplorerDirs.App;

internal sealed class FavoritesManagerWindow : Window
{
    internal sealed class Row
    {
        public string Name { get; set; } = "";
        public string Group { get; set; } = "";
        public string Path { get; set; } = "";
    }

    internal IReadOnlyList<FavoriteEntry>? Result { get; private set; }

    internal FavoritesManagerWindow(Window owner, IEnumerable<FavoriteEntry> entries, string currentPath)
    {
        Owner = owner;
        Title = "管理收藏夹";
        Width = 780; Height = 440; MinWidth = 520; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "ExplorerDirs.WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ExplorerDirs.WindowTextBrush");
        var rows = new ObservableCollection<Row>(entries.Select(item => new Row
            { Name = item.Name, Group = item.Group, Path = item.Path }));
        var root = new DockPanel { Margin = new Thickness(12) };
        var hint = new TextBlock { Text = "填写相同分组名即可归入同一文件夹；留空显示在根目录。名称留空时显示路径。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Single };
        grid.SetResourceReference(BackgroundProperty, "ExplorerDirs.ControlBackgroundBrush");
        grid.SetResourceReference(ForegroundProperty, "ExplorerDirs.ControlTextBrush");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "ExplorerDirs.WindowBackgroundBrush");
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "ExplorerDirs.BorderBrush");
        grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "ExplorerDirs.BorderBrush");
        foreach (var (caption, property, width) in new[] { ("名称", "Name", 170d), ("分组", "Group", 130d), ("路径", "Path", 360d) })
            grid.Columns.Add(new DataGridTextColumn { Header = caption, Binding = new Binding(property), Width = width });
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        void Commit() { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); }
        void Add(string caption, Action action, bool isDefault = false)
        {
            var button = new Button { Content = caption, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = isDefault };
            button.Click += (_, _) => { Commit(); action(); }; buttons.Children.Add(button);
        }
        Add("添加当前目录", () => { var row = new Row { Path = currentPath }; rows.Add(row); grid.SelectedItem = row; grid.ScrollIntoView(row); });
        Add("删除", () => { if (grid.SelectedItem is Row row) rows.Remove(row); });
        void Move(int delta)
        {
            if (grid.SelectedItem is not Row row) return;
            var index = rows.IndexOf(row); var next = index + delta;
            if (next >= 0 && next < rows.Count) { rows.Move(index, next); grid.ScrollIntoView(row); }
        }
        Add("上移", () => Move(-1)); Add("下移", () => Move(1));
        Add("保存", () =>
        {
            if (rows.Any(row => string.IsNullOrWhiteSpace(row.Path)) || rows.Count > 200 ||
                rows.Select(row => row.Path.Trim().TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
            { MessageBox.Show(this, "请填写路径、移除重复项；最多保存 200 项。", Title); return; }
            Result = rows.Select(row => new FavoriteEntry(row.Path.Trim(), row.Name.Trim(), row.Group.Trim())).ToArray();
            DialogResult = true;
        }, isDefault: true);
        Add("取消", () => DialogResult = false);
        root.Children.Add(grid); Content = root;
    }
}
