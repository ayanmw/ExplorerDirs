using System.Windows;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellColumnHeaderArrowHitTestSmoke
{
    internal static void Run()
    {
        var key = new PropertyKeyState(Guid.NewGuid(), 7);
        var target = new ShellColumnHeaderArrowTarget(new Rect(100, 20, 120, 24), key, "类型");
        var targets = new[] { target };

        Require(ShellColumnHeaderArrowHitTest.TryFind(targets, new Point(210, 32), out var hit) &&
                hit.Key == key && hit.DisplayName == "类型",
            "The header arrow zone must resolve to its Shell property key.");
        Require(!ShellColumnHeaderArrowHitTest.TryFind(targets, new Point(130, 32), out _),
            "Clicking the header label must remain available for native sorting.");
        Require(!ShellColumnHeaderArrowHitTest.TryFind(targets, new Point(218, 32), out _),
            "The column resize edge must remain outside the filter arrow zone.");
        Require(!ShellColumnHeaderArrowHitTest.TryFind(targets, new Point(210, 46), out _),
            "Points below the header must not hit the filter arrow.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
