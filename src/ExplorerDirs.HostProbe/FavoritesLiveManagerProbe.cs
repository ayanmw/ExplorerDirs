using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.IO;

internal static class FavoritesLiveManagerProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object? _store;
    private static int _writes;
    public static void Persist(object entries, IReadOnlyList<string> folders)
    {
        _store!.GetType().GetMethod("SaveEntries", Members)!.Invoke(_store, [entries, folders]);
        _writes++;
    }
    internal static int Run()
    {
        var assembly = Assembly.Load("ExplorerDirs.App");
        var managerType = assembly.GetType("ExplorerDirs.App.FavoritesManagerWindow", true)!;
        var entryType = assembly.GetType("ExplorerDirs.App.FavoriteEntry", true)!;
        var storeType = assembly.GetType("ExplorerDirs.App.FavoritesStore", true)!;
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-FavoritesLive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "Favorites.ini");
        _store = Activator.CreateInstance(storeType, [file]);
        storeType.GetMethod("Load", Members)!.Invoke(_store, null);
        var constructor = managerType.GetConstructors(Members).Single();
        var callback = Delegate.CreateDelegate(constructor.GetParameters()[4].ParameterType,
            typeof(FavoritesLiveManagerProbe).GetMethod(nameof(Persist))!);
        Window? owner = null;
        var manager = (Window)constructor.Invoke([owner, Array.CreateInstance(entryType, 0), root, Array.Empty<string>(), callback]);
        try
        {
            var tree = (TreeView)managerType.GetField("_tree", Members)!.GetValue(manager)!;
            var name = (TextBox)managerType.GetField("_name", Members)!.GetValue(manager)!;
            var buttons = Children((DependencyObject)manager.Content).OfType<Button>().ToArray();
            if (buttons.Any(button => button.Content is "保存" or "取消" or "添加当前目录")) throw new Exception("Obsolete explicit-save controls remain.");
            if (!buttons.Any(button => Equals(button.Content, "添加目录…"))) throw new Exception("Directory chooser button missing.");
            void Click(string label) => buttons.Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string[] ReadFolders()
            {
                var reader = Activator.CreateInstance(storeType, [file])!;
                storeType.GetMethod("Load", Members)!.Invoke(reader, null);
                return ((IReadOnlyList<string>)storeType.GetProperty("Folders", Members)!.GetValue(reader)!).ToArray();
            }
            name.Text = "first"; Click("新建分组");
            if (!ReadFolders().SequenceEqual(new[] { "first" })) throw new Exception("New group was not saved before closing.");
            ((TreeViewItem)tree.Items[0]).IsSelected = true;
            name.Text = "renamed"; Click("重命名");
            if (!ReadFolders().SequenceEqual(new[] { "renamed" })) throw new Exception("Rename was not saved immediately.");
            name.Text = "second"; Click("新建分组");
            ((TreeViewItem)tree.Items[1]).IsSelected = true; Click("上移");
            if (!ReadFolders().SequenceEqual(new[] { "second", "renamed" })) throw new Exception("Move order was not saved immediately.");
            Click("删除");
            if (!ReadFolders().SequenceEqual(new[] { "renamed" })) throw new Exception("Delete was not saved immediately.");
            if (_writes != 5) throw new Exception($"Expected one write per mutation, actual {_writes}.");
            Console.WriteLine("Favorites live manager: PASS (actual unshown Window, production callbacks, five immediate disk writes/reloads, controls; no folder picker or physical drag/drop).");
            return 0;
        }
        finally { manager.Close(); owner?.Close(); _store = null; }
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
}
