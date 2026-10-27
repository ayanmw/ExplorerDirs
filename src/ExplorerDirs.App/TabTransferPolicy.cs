namespace ExplorerDirs.App;

internal enum TabTransferOperation
{
    Reject,
    Reorder,
    Move,
    Copy
}

internal static class TabTransferPolicy
{
    public static TabTransferOperation Resolve(bool samePane, int sourceTabCount,
        bool sourcePinned, bool targetPinned, bool copyModifier)
    {
        if (sourcePinned != targetPinned) return TabTransferOperation.Reject;
        if (samePane) return TabTransferOperation.Reorder;
        if (copyModifier) return TabTransferOperation.Copy;
        return sourceTabCount > 1 ? TabTransferOperation.Move : TabTransferOperation.Reject;
    }
}
