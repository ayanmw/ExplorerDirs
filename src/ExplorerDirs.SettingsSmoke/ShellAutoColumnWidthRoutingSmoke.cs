using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellAutoColumnWidthRoutingSmoke
{
    internal static void Run()
    {
        Check(ShellAutoColumnWidthRouting.ShouldDispatchToListView(0x0100, 0x6B, 2, 1),
            "unhandled Ctrl+VK_ADD keydown is dispatched to the native Details ListView");
        Check(!ShellAutoColumnWidthRouting.ShouldDispatchToListView(0x0101, 0x6B, 2, 1),
            "Ctrl+VK_ADD keyup is not sent as another auto-fit action");
        Check(!ShellAutoColumnWidthRouting.ShouldDispatchToListView(0x0100, 0x6B, 0, 1) &&
              !ShellAutoColumnWidthRouting.ShouldDispatchToListView(0x0100, 0x6B, 6, 1),
            "VK_ADD requires Ctrl without Shift");
        Check(!ShellAutoColumnWidthRouting.ShouldDispatchToListView(0x0100, 0x64, 2, 1),
            "Ctrl+NumPad navigation keys are not treated as Ctrl+VK_ADD");
        Check(!ShellAutoColumnWidthRouting.ShouldDispatchToListView(0x0100, 0x6B, 2, 0) &&
              !ShellAutoColumnWidthRouting.ShouldDispatchToListView(0x0100, 0x6B, 2, unchecked((int)0x80004001)),
            "only Shell S_FALSE declines the key for ListView dispatch");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Failed: " + name);
        Console.WriteLine("PASS " + name);
    }
}
