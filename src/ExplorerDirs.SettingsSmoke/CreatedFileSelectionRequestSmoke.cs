using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class CreatedFileSelectionRequestSmoke
{
    internal static void Run()
    {
        var browser = new object();
        var request = new CreatedFileSelectionRequest(browser, @"C:\work", 4, 8, 12, 16);
        Check(request.IsCurrent(browser, @"c:\WORK", true, false, false, 4, 8, 12, 16),
            "same browser, path and generations remain current");
        Check(!request.IsCurrent(new object(), @"C:\work", true, false, false, 4, 8, 12, 16),
            "a replaced browser cancels selection");
        Check(!request.IsCurrent(browser, @"C:\other", true, false, false, 4, 8, 12, 16),
            "navigation to another path cancels selection");
        Check(!request.IsCurrent(browser, @"C:\work", false, false, false, 4, 8, 12, 16),
            "a virtual location cancels selection");
        var shellRequest = new CreatedFileSelectionRequest(browser, "shell:MyComputerFolder", 4, 8, 12, 16,
            SupportsVirtualLocation: true);
        Check(shellRequest.IsCurrent(browser, "shell:MyComputerFolder", false, false, false, 4, 8, 12, 16),
            "a cloned selection request may target a virtual Shell folder");
        Check(!shellRequest.IsCurrent(browser, "shell:MyComputerFolder", false, true, false, 4, 8, 12, 16),
            "virtual Shell selection restoration still rejects search results");
        Check(!request.IsCurrent(browser, @"C:\work", true, true, false, 4, 8, 12, 16),
            "search results cancel filesystem-file selection");
        Check(!request.IsCurrent(browser, @"C:\work", true, false, true, 4, 8, 12, 16),
            "pending navigation cancels selection");
        Check(!request.IsCurrent(browser, @"C:\work", true, false, false, 5, 8, 12, 16),
            "navigation generation change cancels selection");
        Check(!request.IsCurrent(browser, @"C:\work", true, false, false, 4, 9, 12, 16),
            "view generation change cancels selection");
        Check(!request.IsCurrent(browser, @"C:\work", true, false, false, 4, 8, 13, 16),
            "user selection change cancels selection");
        Check(!request.IsCurrent(browser, @"C:\work", true, false, false, 4, 8, 12, 17),
            "user input during a reentrant Shell call cancels selection and focus restoration");
        Console.WriteLine("Created-file selection request smoke: PASS");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Created-file selection request smoke failed: {name}");
    }
}
