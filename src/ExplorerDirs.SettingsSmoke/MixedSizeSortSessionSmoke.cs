using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class MixedSizeSortSessionSmoke
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-MixedSort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ExplorerDirs.App", "MainWindow.xaml"));
            var xaml = System.Xml.Linq.XDocument.Load(xamlPath);
            var folderOptions = xaml.Descendants().Single(element => (string?)element.Attribute("Header") == "文件夹");
            var mixedOption = folderOptions.Elements().SingleOrDefault(element =>
                (string?)element.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "MixedSizeSortMenu");
            Require(mixedOption is not null && (string?)mixedOption.Attribute("IsCheckable") == "True" &&
                (string?)mixedOption.Attribute("Click") == "MixedSizeSort_Click" && (string?)mixedOption.Attribute("IsChecked") != "True",
                "positive default-off setting is wired under folder options");
            var normal = ViewState.Default();
            var mixed = normal with { MixedSizeSort = true };
            Require(!normal.MixedSizeSort && mixed.Copy().MixedSizeSort, "default off and copy inheritance");
            var state = new SessionState(4, 4,
                [[new(@"C:\same", normal), new(@"C:\same", mixed)], [], [], []]);
            var text = SessionStore.Render(state);
            var parsed = SessionStore.ParseComplete(text);
            Require(parsed is not null && !parsed.Panes[0][0].View.MixedSizeSort && parsed.Panes[0][1].View.MixedSizeSort,
                "same-path tabs retain independent policies");
            var legacy = string.Join('\n', text.Replace("FormatVersion=15", "FormatVersion=14").Split('\n')
                .Where(line => !line.StartsWith("MixedSizeSort=", StringComparison.Ordinal)));
            var old = SessionStore.ParseComplete(legacy);
            Require(old is not null && old.Panes[0].All(tab => !tab.View.MixedSizeSort), "v14 defaults to separate sorting");
            Require(SessionStore.ParseComplete(text.Replace("MixedSizeSort=True", "MixedSizeSort=invalid")) is null,
                "invalid policy does not become a complete snapshot");
            Require(SessionStore.ParseComplete(text.Replace("FormatVersion=15", "FormatVersion=16")) is null,
                "future format remains protected");
            var missing = string.Join('\n', text.Split('\n').Where(line => !line.StartsWith("MixedSizeSort=", StringComparison.Ordinal)));
            Require(SessionStore.ParseComplete(missing) is null, "current format requires explicit policy");
            var path = Path.Combine(root, "Session.ini");
            File.WriteAllText(path, legacy);
            var store = new SessionStore(path, Path.Combine(root, "absent.json"));
            Require(store.Load() is not null, "load legacy profile");
            store.Save(state);
            Require(Directory.GetFiles(root, "Session.ini.before-v15-*.bak").Length == 1, "upgrade preserves legacy backup");
            var reopened = new SessionStore(path, Path.Combine(root, "absent.json")).Load();
            Require(reopened?.Panes[0][1].View.MixedSizeSort == true && !reopened.Panes[0][0].View.MixedSizeSort,
                "new store instance restores both policies");
            ViewCaptureGenerationSmoke.Run();
            Console.WriteLine("Mixed size sort session: PASS (default/copy/per-tab/legacy/invalid/future/disk reload/capture; no UI)");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Mixed sort session: " + message);
    }
}
