using ExplorerDirs.App;

internal static class ShellMiddleClickTargetSmoke
{
    internal static void Run()
    {
        Require(ShellMiddleClickTarget.TryGetChildPath("C:\\root", "folder", out var path) &&
                path == "C:\\root\\folder",
            "UI Automation item label resolves beneath the captured filesystem folder");
        Require(ShellMiddleClickTarget.IsResolvedChild("C:\\root", "folder", "C:\\root\\folder") &&
                !ShellMiddleClickTarget.IsResolvedChild("C:\\root", "folder", "C:\\other\\folder") &&
                !ShellMiddleClickTarget.IsResolvedChild("C:\\root", "folder", "C:\\root\\other"),
            "Shell resolution must retain the clicked item's name and parent directory");
        Require(ShellMiddleClickTarget.IsCurrentRequest("C:\\root", 4, 9, (nint)10,
                    "C:\\root", 4, 9, (nint)10, browserAvailable: true, searchActive: false) &&
                !ShellMiddleClickTarget.IsCurrentRequest("C:\\root", 4, 9, (nint)10,
                    "C:\\root", 5, 9, (nint)10, browserAvailable: true, searchActive: false) &&
                !ShellMiddleClickTarget.IsCurrentRequest("C:\\root", 4, 9, (nint)10,
                    "C:\\root", 4, 10, (nint)10, browserAvailable: true, searchActive: false) &&
                !ShellMiddleClickTarget.IsCurrentRequest("C:\\root", 4, 9, (nint)10,
                    "C:\\other", 4, 9, (nint)10, browserAvailable: true, searchActive: false) &&
                !ShellMiddleClickTarget.IsCurrentRequest("C:\\root", 4, 9, (nint)10,
                    "C:\\root", 4, 9, (nint)11, browserAvailable: true, searchActive: false) &&
                !ShellMiddleClickTarget.IsCurrentRequest("C:\\root", 4, 9, (nint)10,
                    "C:\\root", 4, 9, (nint)10, browserAvailable: false, searchActive: false) &&
                !ShellMiddleClickTarget.IsCurrentRequest("C:\\root", 4, 9, (nint)10,
                    "C:\\root", 4, 9, (nint)10, browserAvailable: true, searchActive: true),
            "stale navigation, input, path, view, search, or closed-browser requests are rejected");
        Require(!ShellMiddleClickTarget.TryGetChildPath("C:\\root", "..", out _) &&
                !ShellMiddleClickTarget.TryGetChildPath("C:\\root", "C:\\other", out _) &&
                !ShellMiddleClickTarget.TryGetChildPath("C:\\root", "sub\\folder", out _) &&
                !ShellMiddleClickTarget.TryGetChildPath("C:\\root", "folder?", out _) &&
                !ShellMiddleClickTarget.TryGetChildPath("shell:Desktop", "folder", out _),
            "middle-click target path rejects traversal, rooted, nested, invalid, and virtual parent names");
        Console.WriteLine("Shell middle-click target smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
