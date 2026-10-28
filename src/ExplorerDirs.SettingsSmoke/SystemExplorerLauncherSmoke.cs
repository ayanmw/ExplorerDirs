using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class SystemExplorerLauncherSmoke
{
    internal static void Run()
    {
        foreach (var path in new[]
        {
            @"D:\projects\folder with spaces\",
            "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"
        })
        {
            var start = SystemExplorerLauncher.CreateStartInfo(path);
            Require(start.FileName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase),
                "launches the Windows Explorer executable");
            Require(start.UseShellExecute && start.ArgumentList.SequenceEqual([path]),
                "passes the exact filesystem or Shell namespace path as one argument");
        }
        Console.WriteLine("System Explorer launcher smoke: PASS (process not started)");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("System Explorer launcher smoke: " + message);
    }
}
