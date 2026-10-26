using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellRefreshRequestSmoke
{
    internal static void Run()
    {
        var browser = new object();
        var request = new ShellRefreshRequest(browser, 12, @"C:\alpha");

        Check(request.IsCurrent(browser, 12, @"C:\alpha"),
            "a refresh request remains current while its browser, navigation, and path match");
        Check(!request.IsCurrent(browser, 13, @"C:\alpha"),
            "a refresh result is rejected after navigation advances during a Shell call");
        Check(!request.IsCurrent(browser, 12, @"C:\beta"),
            "a refresh result is rejected when the current path changes");
        Check(!request.IsCurrent(new object(), 12, @"C:\alpha"),
            "a refresh result is rejected after the browser instance is replaced");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Console.WriteLine($"PASS {description}");
    }
}
