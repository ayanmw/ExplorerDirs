using System.Collections;
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
        Apply("", [textType]);
        VerifyPane0(["initial.txt"], "TXT type value filters the Shell view");
        VerifyOtherPanes();
        Apply("", [binaryType]);
        VerifyPane0(["sample.bin"], "switching the selected type value updates the Shell view");
        VerifyOtherPanes();
        Apply("*.txt", [binaryType]);
        VerifyPane0([], "name pattern and column value are combined with AND");
        VerifyOtherPanes();
        Apply("*.txt", [textType]);
        VerifyPane0(["initial.txt"], "matching name pattern and column value remain visible");
        VerifyOtherPanes();
        Apply("", []);
        VerifyPane0(["initial.txt", "sample.bin"], "clearing column values restores the Shell view");
        VerifyOtherPanes();
        Console.WriteLine($"column-value-filter: property=System.ItemTypeText txt='{textType}' bin='{binaryType}' switch/AND/clear=True independent-panes=True");
        return 0;

        void Apply(string pattern, string[] values)
        {
            var filterArray = Array.CreateInstance(filterType, values.Length);
            if (values.Length > 0) filterArray.SetValue(filterConstructor.Invoke([key, values]), 0);
            var settings = Activator.CreateInstance(settingsType, pattern, false, false, filterArray)
                ?? throw new Exception("Could not create QuickFilterSettings.");
            if (setFilter.Invoke(hosts[0], [settings]) is false)
                throw new Exception("Production SetQuickFilter rejected a column-value filter state.");
        }

        void VerifyPane0(string[] expected, string stage)
        {
            var expectedNames = Names(expected);
            if (!WaitFor(() => count(hosts[0]) == expected.Length &&
                    Names(viewItemPaths(hostType, hosts[0], folders[0], 2))
                        .SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase), TimeSpan.FromSeconds(8)))
                throw new Exception($"{stage}: expected=[{string.Join(',', expectedNames)}], count={count(hosts[0])}, actual=[{string.Join(',', Names(viewItemPaths(hostType, hosts[0], folders[0], 2)))}], status={hostType.GetProperty("LastQuickFilterStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(hosts[0])}.");
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
