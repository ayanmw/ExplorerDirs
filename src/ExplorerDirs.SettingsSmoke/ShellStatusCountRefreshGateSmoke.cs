using ExplorerDirs.App;

internal static class ShellStatusCountRefreshGateSmoke
{
    internal static void Run()
    {
        var gate = new ShellStatusCountRefreshGate();
        Check(!gate.Request(isCurrentTab: false, isVisible: true),
            "selection from an inactive tab does not queue a status read");
        Check(!gate.Request(isCurrentTab: true, isVisible: false),
            "selection in a hidden pane does not queue a status read");
        Check(gate.Request(isCurrentTab: true, isVisible: true),
            "visible current-tab selection queues a status read");
        Check(gate.Request(isCurrentTab: true, isVisible: true),
            "repeated selection notifications remain pending for one coalesced read");
        Check(gate.TryConsume(isVisible: true), "pending visible status read can be consumed");
        Check(!gate.TryConsume(isVisible: true), "one debounce tick consumes pending work once");
        Check(gate.Request(isCurrentTab: true, isVisible: true), "later selection can queue a new read");
        gate.Cancel();
        Check(!gate.TryConsume(isVisible: true), "navigation or hiding cancels the pending status read");
        Check(gate.Request(isCurrentTab: true, isVisible: true), "status read can be queued after cancellation");
        Check(!gate.TryConsume(isVisible: false), "a pane hidden before the tick does not refresh status");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Shell status count refresh gate: {label}");
    }
}
