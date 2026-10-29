using ExplorerDirs.App;
using System.IO;

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
        var root = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.MiddleClick.{Guid.NewGuid():N}");
        var child = Path.Combine(root, "child folder");
        Directory.CreateDirectory(child);
        try
        {
            Require(ShellFolderChildIdentity.TryResolveChildFolder(root, "child folder", out var childIdentity) &&
                    string.Equals(Path.GetFullPath(child), childIdentity, StringComparison.OrdinalIgnoreCase),
                "Shell resolves a clicked child relative to its parent and returns the confirmed folder identity");
            Require(!ShellFolderChildIdentity.TryResolveChildFolder(root, "..", out _) &&
                    !ShellFolderChildIdentity.TryResolveChildFolder(root, "missing", out _),
                "Shell child resolution rejects traversal and names that do not exist under the parent");
            var systemDriveName = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
            Require(ShellFolderChildIdentity.TryResolveChildFolder("shell:MyComputerFolder", systemDriveName,
                        out var virtualDriveIdentity) &&
                    string.Equals(systemDriveName + "\\", virtualDriveIdentity, StringComparison.OrdinalIgnoreCase),
                "a drive opened from the virtual This PC namespace resolves to its filesystem identity");
        }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine("Shell middle-click target smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
