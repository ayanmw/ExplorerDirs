using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellSelectionSizeRefreshGateSmoke
{
    internal static void Run()
    {
        var key = new ShellSelectionSizeCacheKey(ViewGeneration: 4, SelectionVersion: 7, SelectedCount: 1000);
        Check(ShellSelectionSizeRefreshGate.ShouldRead(null, key, TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            "first selected-size snapshot is read");
        Check(!ShellSelectionSizeRefreshGate.ShouldRead(key, key, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)),
            "unchanged large selection reuses its size within the refresh interval");
        Check(ShellSelectionSizeRefreshGate.ShouldRead(key, key with { SelectionVersion = 8 },
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)),
            "a new Shell selection is measured on the next status refresh");
        Check(ShellSelectionSizeRefreshGate.ShouldRead(key, key with { ViewGeneration = 5 },
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)),
            "a recreated or navigated view invalidates the old size snapshot");
        Check(ShellSelectionSizeRefreshGate.ShouldRead(key, key with { SelectedCount = 999 },
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)),
            "a changed selected-item count invalidates the old size snapshot");
        Check(ShellSelectionSizeRefreshGate.ShouldRead(key, key, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
            "an unchanged selection is rechecked after the bounded freshness interval");
        Check(!ShellSelectionSizeRefreshGate.ShouldRead(key, key with { SelectedCount = 0 },
                TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            "an empty selection does not query aggregate size");
    }

    private static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException("Selection size refresh gate: " + description);
    }
}
