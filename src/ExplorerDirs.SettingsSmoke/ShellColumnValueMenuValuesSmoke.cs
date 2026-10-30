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

        Console.WriteLine("Column value menu values smoke: PASS");
    }
}
