using ExplorerDirs.App;

internal static class ShellCommandRefreshGateSmoke
{
    internal static void Run()
    {
        var gate = new ShellCommandRefreshGate();
        Require(!gate.Request(activePane: false, commandSurfaceEngaged: true),
            "selection changes in an inactive pane do not refresh the command toolbar");
        Require(!gate.Request(activePane: true, commandSurfaceEngaged: false),
            "selection changes do not query Shell menus while the command surface is idle");
        Require(gate.Request(activePane: true, commandSurfaceEngaged: true) &&
                gate.Request(activePane: true, commandSurfaceEngaged: true),
            "repeated active selection changes coalesce into one pending command refresh");
        Require(gate.Consume(activePane: true, commandSurfaceEngaged: true) &&
                !gate.Consume(activePane: true, commandSurfaceEngaged: true),
            "one settled active change consumes exactly one refresh request");

        gate.Request(activePane: true, commandSurfaceEngaged: true);
        Require(!gate.Consume(activePane: false, commandSurfaceEngaged: true) &&
                !gate.Consume(activePane: true, commandSurfaceEngaged: true),
            "a pane switch before the coalesced refresh cancels the stale request");
        gate.Request(activePane: true, commandSurfaceEngaged: true);
        Require(!gate.Consume(activePane: true, commandSurfaceEngaged: false),
            "moving away from the command surface before the refresh cancels the request");

        gate = new ShellCommandRefreshGate();
        Require(gate.BeginQuery(), "a fresh Shell command-state query starts");
        gate.Request(activePane: true, commandSurfaceEngaged: true);
        Require(!gate.Consume(activePane: true, commandSurfaceEngaged: true),
            "a reentrant timer cannot consume a refresh request during the current Shell query");
        Require(gate.EndQuery() && gate.Consume(activePane: true, commandSurfaceEngaged: true) &&
                !gate.Consume(activePane: true, commandSurfaceEngaged: true),
            "a selection change during a Shell query schedules exactly one follow-up refresh");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Shell command refresh gate: {message}");
    }
}
