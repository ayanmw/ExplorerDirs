using ExplorerDirs.App;

internal static class ShellTreeMiddleClickSmoke
{
    internal static void Run()
    {
        var folder = 0x20000000u | 0x40000000u;
        Require(ShellTreeMiddleClick.ShouldOpenInNewTab(hit: 0x0004, click: 0x0002, folder),
            "middle-click on a tree folder opens in a new tab");
        Require(!ShellTreeMiddleClick.ShouldOpenInNewTab(hit: 0x0002, click: 0x0006, folder),
            "double-click notification does not open a duplicate tab");
        Require(!ShellTreeMiddleClick.ShouldOpenInNewTab(hit: 0x0004, click: 0x0001, folder) &&
                !ShellTreeMiddleClick.ShouldOpenInNewTab(hit: 0x0004, click: 0x0003, folder),
            "left and right tree clicks keep their native behavior");
        Require(!ShellTreeMiddleClick.ShouldOpenInNewTab(hit: 0x0010, click: 0x0002, folder) &&
                !ShellTreeMiddleClick.ShouldOpenInNewTab(hit: 0x0001, click: 0x0002, folder),
            "middle-click on an expando or empty tree area keeps its native behavior");
        Require(!ShellTreeMiddleClick.ShouldOpenInNewTab(hit: 0x0004, click: 0x0002, 0x40000000),
            "middle-click on a tree file is not opened as a folder");
        Require(!ShellTreeMiddleClick.ShouldOpenInNewTab(hit: 0x0004, click: 0x0002, 0x20000000),
            "middle-click on a virtual folder is not treated as a filesystem path");
        Console.WriteLine("Shell tree middle-click smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
