namespace ExplorerDirs.SettingsSmoke;

internal static class ShellSelectionBatchRestoreSmoke
{
    internal static void Run()
    {
        var calls = new List<string>();
        var trace = new List<string>();
        var restored = ExplorerDirs.App.ShellSelectionBatchRestore.TryRestore(
            applyBatch: () => { calls.Add("batch"); return true; },
            selectionMatches: () => { calls.Add("verify"); return true; },
            restoreReplacementFallback: () => { calls.Add("fallback"); return true; },
            trace: trace.Add);
        Assert(restored && calls.SequenceEqual(["batch", "verify"]) && trace.SequenceEqual(["selection-batch-path=direct"]),
            "an exact successful batch restore skips the seed and fallback");

        calls.Clear();
        trace.Clear();
        restored = ExplorerDirs.App.ShellSelectionBatchRestore.TryRestore(
            applyBatch: () => { calls.Add("batch"); return true; },
            selectionMatches: () => { calls.Add("verify"); return false; },
            restoreReplacementFallback: () => { calls.Add("fallback"); return true; },
            trace: trace.Add);
        Assert(restored && calls.SequenceEqual(["batch", "verify", "fallback"]) && trace.SequenceEqual(["selection-batch-path=replacement-fallback"]),
            "a successful but incomplete batch restore uses the selection replacement fallback");

        calls.Clear();
        trace.Clear();
        restored = ExplorerDirs.App.ShellSelectionBatchRestore.TryRestore(
            applyBatch: () => { calls.Add("batch"); return false; },
            selectionMatches: () => { calls.Add("verify"); return true; },
            restoreReplacementFallback: () => { calls.Add("fallback"); return false; },
            trace: trace.Add);
        Assert(!restored && calls.SequenceEqual(["batch", "fallback"]) && trace.SequenceEqual(["selection-batch-path=replacement-fallback"]),
            "a failed batch restore skips verification and reports a failed fallback");

        calls.Clear();
        restored = ExplorerDirs.App.ShellSelectionBatchRestore.TryRestoreReplacementFallback(
            clearSelection: () => { calls.Add("clear"); return true; },
            applyBatch: () => { calls.Add("batch"); return true; },
            selectionMatches: () => { calls.Add("verify"); return false; });
        Assert(!restored && calls.SequenceEqual(["clear", "batch", "verify"]),
            "a replacement fallback clears old selection before batch restore and rejects incomplete readback");
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine($"PASS {description}");
    }
}
