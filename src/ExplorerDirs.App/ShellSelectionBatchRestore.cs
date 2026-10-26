namespace ExplorerDirs.App;

internal static class ShellSelectionBatchRestore
{
    internal static bool TryRestoreReplacementFallback(Func<bool> clearSelection, Func<bool> applyBatch,
        Func<bool> selectionMatches) => clearSelection() && applyBatch() && selectionMatches();

    internal static bool TryRestore(Func<bool> applyBatch, Func<bool> selectionMatches,
        Func<bool> restoreReplacementFallback, Action<string>? trace = null)
    {
        if (applyBatch() && selectionMatches())
        {
            trace?.Invoke("selection-batch-path=direct");
            return true;
        }
        trace?.Invoke("selection-batch-path=replacement-fallback");
        return restoreReplacementFallback();
    }
}
