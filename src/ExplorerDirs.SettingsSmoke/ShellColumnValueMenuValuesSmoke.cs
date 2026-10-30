using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellColumnValueMenuValuesSmoke
{
    internal static void Run()
    {
        var viewValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TXT 文件", "1 KB", "legacy"
        };
        var result = ShellColumnValueMenuValues.SortForMenu(viewValues,
            ["txt 文件", "stale selected value"]);

        if (!result.SequenceEqual(["1 KB", "legacy", "stale selected value", "TXT 文件"]) ||
            viewValues.Count != 4)
            throw new InvalidOperationException("Column filter menu values changed distinct or checked-value behavior.");

        var pageSize = 3;
        var pageCount = ShellColumnValueMenuValues.GetPageCount(result.Length, pageSize);
        var secondPage = ShellColumnValueMenuValues.GetPage(result, 1, pageSize);
        if (pageCount != 2 || !secondPage.SequenceEqual(["TXT 文件"]) ||
            ShellColumnValueMenuValues.GetPage(result, 2, pageSize).Length != 0)
            throw new InvalidOperationException("Column filter menu paging lost or repeated values.");

        Console.WriteLine("Column value menu values smoke: PASS");
    }
}
