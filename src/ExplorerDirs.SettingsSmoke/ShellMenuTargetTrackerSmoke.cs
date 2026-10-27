using ExplorerDirs.App;

internal static class ShellMenuTargetTrackerSmoke
{
    internal static void Run()
    {
        Check(ShellMenuTargetTracker.IsNativeListItemHit(itemIndex: 0, hitFlags: 0x4),
            "native subitem hit confirms a click on file text");
        Check(ShellMenuTargetTracker.IsNativeListItemHit(itemIndex: 0, hitFlags: 0x04000000),
            "the Vista extended on-contents flag confirms a native item-content hit");
        Check(!ShellMenuTargetTracker.IsNativeListItemHit(itemIndex: 0, hitFlags: 0),
            "a row index without an icon, label, or state-icon hit is blank-space background");
        Check(!ShellMenuTargetTracker.IsNativeListItemHit(itemIndex: -1, hitFlags: 0x4),
            "item hit flags without a valid row cannot target a file");
        Check(ShellMenuTargetTracker.ResolveNativeListViewHitTest(available: false, itemIndex: 0, hitFlags: 0x4) ==
              ShellMenuTarget.Unknown,
            "without native list hit testing, a target remains unknown");
        Check(ShellMenuTargetTracker.ResolveNativeListViewHitTest(available: true, itemIndex: 2, hitFlags: 0) ==
              ShellMenuTarget.Background,
            "an available native list hit-test classifies blank row space as the background");
        Check(ShellMenuTargetTracker.DecodeContextPoint(unchecked((nint)0xFFF8000C)) == new ShellMenuPoint(12, -8),
            "context-menu screen coordinates decode as signed 16-bit values");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Shell menu hit test: {label}");
    }
}
