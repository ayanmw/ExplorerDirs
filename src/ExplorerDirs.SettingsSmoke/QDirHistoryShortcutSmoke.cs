using ExplorerDirs.App;

internal static class QDirHistoryShortcutSmoke
{
    internal static void Run()
    {
        var release = new AltNavigationReleaseGate();
        Check(!release.Filter(0x104, 0x12) && !release.Filter(0x105, 0x12), "standalone Alt retains normal menu activation");
        foreach (var navigation in new[] { ExplorerShortcut.Parent, ExplorerShortcut.Back, ExplorerShortcut.Forward })
        {
            release.Mark(navigation, 1);
            Check(!release.Filter(0x105, 0x26) && release.Filter(0x105, 0x12) && !release.Filter(0x105, 0x12),
                "Alt navigation consumes exactly its Alt release, never the arrow release");
        }
        release.Mark(ExplorerShortcut.OpenViewMenu, 1);
        Check(!release.Filter(0x105, 0x12), "menu access keys are not suppressed");
        release.Mark(ExplorerShortcut.Parent, 1); release.Reset();
        Check(!release.Filter(0x105, 0x12), "deactivation retires a pending Alt release");
        release.Mark(ExplorerShortcut.Parent, 0);
        Check(!release.Filter(0x105, 0x12), "Backspace without Alt leaves menu activation unchanged");
        Check(KeyboardShortcuts.Resolve(0x08, 1) == ExplorerShortcut.Back,
            "Q-Dir Alt+Backspace goes back");
        Check(KeyboardShortcuts.Resolve(0x08, 5) == ExplorerShortcut.Forward,
            "Q-Dir Alt+Shift+Backspace goes forward");
        Check(KeyboardShortcuts.Resolve(0x08, 0) == ExplorerShortcut.Parent,
            "plain Backspace still goes to parent");
        Check(KeyboardShortcuts.Resolve(0x08, 3) == ExplorerShortcut.None &&
              KeyboardShortcuts.Resolve(0x08, 7) == ExplorerShortcut.None,
            "Ctrl-modified Alt+Backspace combinations remain unclaimed");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
