using System.Collections;
using System.IO.Compression;
using System.IO;
using System.Reflection;
using System.Windows.Interop;
using System.Windows.Threading;

// Exercises column-value filtering through the production Shell filter on hidden ExplorerBrowser views.
internal static class ColumnValueFilterProbe
{
    private static readonly Guid ItemTypeTextFormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");

    internal static int Run(Type hostType, IReadOnlyList<HwndHost> hosts, IReadOnlyList<string> folders,
        Func<HwndHost, int> count, Func<Type, HwndHost, string, uint, string[]> viewItemPaths)
    {
        var assembly = hostType.Assembly;
        var settingsType = assembly.GetType("ExplorerDirs.App.QuickFilterSettings", true)!;
        var filterType = assembly.GetType("ExplorerDirs.App.ColumnValueFilter", true)!;
        var keyType = assembly.GetType("ExplorerDirs.App.PropertyKeyState", true)!;
        var key = Activator.CreateInstance(keyType, ItemTypeTextFormatId, 4u)
            ?? throw new Exception("Could not create the System.ItemTypeText property key.");
        var commentKey = Activator.CreateInstance(keyType,
            Guid.Parse("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 6u)
            ?? throw new Exception("Could not create the System.Comment property key.");
        var itemNameKey = Activator.CreateInstance(keyType, ItemTypeTextFormatId, 10u)
            ?? throw new Exception("Could not create the System.ItemNameDisplay property key.");
        var filterConstructor = filterType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 2);
        var setFilter = hostType.GetMethod("SetQuickFilter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new Exception("Production ExplorerBrowserHost.SetQuickFilter is not implemented.");
        var otherPaneBaseline = hosts.Skip(1)
            .Select((host, index) => Names(viewItemPaths(hostType, host, folders[index + 1], 2))).ToArray();

        var snapshot = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(hosts[0], null) ?? throw new Exception("Could not capture the unfiltered Shell view fixture.");
        var columns = ((IEnumerable)snapshot.GetType().GetProperty("Columns")!.GetValue(snapshot)!).Cast<string>().ToArray();
        var nameColumn = FindColumn(columns, "Name", "名称");
        var typeColumn = FindColumn(columns, "Type", "类型");
        var rows = ((IEnumerable)snapshot.GetType().GetProperty("Rows")!.GetValue(snapshot)!).Cast<IEnumerable>()
            .Select(row => row.Cast<object?>().Select(value => value?.ToString() ?? string.Empty).ToArray()).ToArray();
        var valuesByName = rows.ToDictionary(row => Path.GetFileName(row[nameColumn]), row => row[typeColumn],
            StringComparer.OrdinalIgnoreCase);
        var textType = valuesByName["initial.txt"];
        var binaryType = valuesByName["sample.bin"];
        if (string.Equals(textType, binaryType, StringComparison.OrdinalIgnoreCase))
            throw new Exception($"The Shell type column did not distinguish TXT and BIN fixtures: '{textType}'.");

        Apply("", []);
        VerifyPane0(["initial.txt", "sample.bin"], "clearing initial filter restores the complete fixture");
        Apply("", (key, [textType]));
        VerifyPane0(["initial.txt"], "TXT type value filters the Shell view");
        VerifyOtherPanes();
        Apply("", (key, [textType, binaryType]));
        VerifyPane0(["initial.txt", "sample.bin"], "multiple selected values in one column use OR");
        VerifyOtherPanes();
        Apply("", (key, [binaryType]));
        VerifyPane0(["sample.bin"], "switching the selected type value updates the Shell view");
        VerifyOtherPanes();
        Apply("", (key, [textType]), (itemNameKey, ["initial.txt"]));
        VerifyPane0(["initial.txt"], "different column values combine with AND");
        VerifyOtherPanes();
        Apply("", (key, [textType]), (itemNameKey, ["sample.bin"]));
        VerifyPane0([], "conflicting values in different columns hide all rows");
        VerifyOtherPanes();
        Apply("*.txt", (key, [binaryType]));
        VerifyPane0([], "name pattern and column value are combined with AND");
        VerifyOtherPanes();
        Apply("*.txt", (key, [textType]));
        VerifyPane0(["initial.txt"], "matching name pattern and column value remain visible");
        VerifyOtherPanes();
        Apply("", []);
        VerifyPane0(["initial.txt", "sample.bin"], "clearing column values restores the Shell view");
        VerifyOtherPanes();

        Apply("", (commentKey, [""]));
        VerifyPane0(["initial.txt", "sample.bin"], "blank System.Comment values remain selectable through the production Shell provider");
        Apply("", (commentKey, ["ExplorerDirs filter probe sentinel"]));
        VerifyPane0([], "a nonblank System.Comment value excludes files with no comment");
        Apply("", []);
        VerifyPane0(["initial.txt", "sample.bin"], "clearing an empty-property filter restores the Shell view");
        VerifyOtherPanes();

        var zipPath = Path.Combine(folders[0], "column-filter-provider.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("archive.txt").Open())) writer.Write("zip text");
            using (var writer = new StreamWriter(archive.CreateEntry("package.bin").Open())) writer.Write("zip binary");
        }
        var navigate = hostType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == "Navigate" && method.GetParameters().Length == 1);
        navigate.Invoke(hosts[0], [zipPath]);
        var currentPath = hostType.GetProperty("CurrentPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        if (!WaitFor(() => string.Equals((string?)currentPath.GetValue(hosts[0]), zipPath, StringComparison.OrdinalIgnoreCase) &&
                           count(hosts[0]) == 2,
                TimeSpan.FromSeconds(10)))
            throw new Exception($"The production ExplorerBrowser did not enumerate the ZIP Shell folder: path={currentPath.GetValue(hosts[0])}, count={count(hosts[0])}.");

        var zipSnapshot = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(hosts[0], null) ?? throw new Exception("Could not capture the ZIP Shell view fixture.");
        var zipColumns = ((IEnumerable)zipSnapshot.GetType().GetProperty("Columns")!.GetValue(zipSnapshot)!).Cast<string>().ToArray();
        var zipNameColumn = FindColumn(zipColumns, "Name", "名称");
        var zipTypeColumn = FindColumn(zipColumns, "Type", "类型");
        var zipRows = ((IEnumerable)zipSnapshot.GetType().GetProperty("Rows")!.GetValue(zipSnapshot)!).Cast<IEnumerable>()
            .Select(row => row.Cast<object?>().Select(value => value?.ToString() ?? string.Empty).ToArray()).ToArray();
        var zipAllNames = Names(zipRows.Select(row => Path.GetFileName(row[zipNameColumn])!));
        if (!zipAllNames.SequenceEqual(Names(["archive.txt", "package.bin"]), StringComparer.OrdinalIgnoreCase))
            throw new Exception($"ZIP Shell view exposed unexpected files: [{string.Join(',', zipAllNames)}].");
        var zipNamesByType = zipRows.GroupBy(row => row[zipTypeColumn], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => Names(group.Select(row => Path.GetFileName(row[zipNameColumn])!)), StringComparer.OrdinalIgnoreCase);
        foreach (var (zipType, expectedNames) in zipNamesByType)
        {
            Apply("", (key, [zipType]));
            VerifyZipPane(expectedNames, $"ZIP Shell provider applies System.ItemTypeText='{zipType}'");
        }
        Apply("", []);
        VerifyZipPane(["archive.txt", "package.bin"], "clearing a ZIP Shell filter restores the archive contents");
        navigate.Invoke(hosts[0], [folders[0]]);
        if (!WaitFor(() => string.Equals((string?)currentPath.GetValue(hosts[0]), folders[0], StringComparison.OrdinalIgnoreCase) &&
                           count(hosts[0]) == 3, TimeSpan.FromSeconds(10)))
            throw new Exception("The production ExplorerBrowser did not return from the ZIP Shell folder to its source fixture.");
        File.Delete(zipPath);
        if (!WaitFor(() => count(hosts[0]) == 2 &&
                           Names(viewItemPaths(hostType, hosts[0], folders[0], 2))
                               .SequenceEqual(Names(["initial.txt", "sample.bin"]), StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(8)))
            throw new Exception("The source fixture did not converge after removing the temporary ZIP archive.");
        VerifyOtherPanes();
        Console.WriteLine($"column-value-filter: property=System.ItemTypeText txt='{textType}' bin='{binaryType}' same-column-OR=True cross-column-AND=True pattern-AND=True switch/clear=True System.Comment-empty-value=True zip-provider-groups={zipNamesByType.Count} independent-panes=True");
        return 0;

        void Apply(string pattern, params (object Key, string[] Values)[] filters)
        {
            var filterArray = Array.CreateInstance(filterType, filters.Length);
            for (var index = 0; index < filters.Length; index++)
                filterArray.SetValue(filterConstructor.Invoke([filters[index].Key, filters[index].Values]), index);
            var settings = Activator.CreateInstance(settingsType, pattern, false, false, filterArray)
                ?? throw new Exception("Could not create QuickFilterSettings.");
            if (setFilter.Invoke(hosts[0], [settings]) is false)
                throw new Exception("Production SetQuickFilter rejected a column-value filter state.");
        }

        void VerifyPane0(string[] expected, string stage, string? location = null)
        {
            var expectedNames = Names(expected);
            location ??= folders[0];
            if (!WaitFor(() => count(hosts[0]) == expected.Length &&
                    Names(viewItemPaths(hostType, hosts[0], location, 2))
                        .SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase), TimeSpan.FromSeconds(8)))
                throw new Exception($"{stage}: expected=[{string.Join(',', expectedNames)}], count={count(hosts[0])}, actual=[{string.Join(',', Names(viewItemPaths(hostType, hosts[0], location, 2)))}], status={hostType.GetProperty("LastQuickFilterStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(hosts[0])}.");
        }

        void VerifyOtherPanes()
        {
            for (var index = 1; index < hosts.Count; index++)
            {
                var current = Names(viewItemPaths(hostType, hosts[index], folders[index], 2));
                if (!otherPaneBaseline[index - 1].SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
                    throw new Exception($"Column filter changed pane {index}: expected=[{string.Join(',', otherPaneBaseline[index - 1])}], actual=[{string.Join(',', current)}].");
            }
        }

        void VerifyZipPane(string[] expected, string stage)
        {
            var expectedNames = Names(expected);
            if (!WaitFor(() => count(hosts[0]) == expected.Length && CurrentZipNames()
                    .SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase), TimeSpan.FromSeconds(8)))
                throw new Exception($"{stage}: expected=[{string.Join(',', expectedNames)}], count={count(hosts[0])}, actual=[{string.Join(',', CurrentZipNames())}], status={hostType.GetProperty("LastQuickFilterStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(hosts[0])}.");
        }

        string[] CurrentZipNames()
        {
            var snapshot = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(hosts[0], null);
            if (snapshot is null) return [];
            var rows = ((IEnumerable)snapshot.GetType().GetProperty("Rows")!.GetValue(snapshot)!).Cast<IEnumerable>();
            return Names(rows.Select(row =>
            {
                var values = row.Cast<object?>().Select(value => value?.ToString() ?? string.Empty).ToArray();
                return Path.GetFileName(values[zipNameColumn])!;
            }));
        }
    }

    private static int FindColumn(IReadOnlyList<string> columns, params string[] candidates)
    {
        for (var index = 0; index < columns.Count; index++)
            if (candidates.Contains(columns[index], StringComparer.OrdinalIgnoreCase)) return index;
        throw new Exception($"The Shell view did not expose expected column [{string.Join('/', candidates)}]; actual=[{string.Join(',', columns)}].");
    }

    private static string[] Names(IEnumerable<string> paths) =>
        paths.Select(path => Path.GetFileName(path)!).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            if (condition()) return true;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < deadline);
        return condition();
    }
}
