namespace ExplorerDirs.SettingsSmoke;

internal static class ShellViewRedrawLeaseSmoke
{
    internal static void Run()
    {
        Check(ExplorerDirs.App.ShellViewRedrawLease.ShouldSuppressForRefresh(false) == false &&
            ExplorerDirs.App.ShellViewRedrawLease.ShouldSuppressForRefresh(true),
            "only an explicit user refresh needs a redraw lease");

        var calls = new List<bool>();
        var lease = ExplorerDirs.App.ShellViewRedrawLease.TrySuppress(redraw =>
        {
            calls.Add(redraw);
            return 0;
        }) ?? throw new InvalidOperationException("A successful redraw suppression should return a lease.");
        Check(calls.SequenceEqual([false]), "the scope disables view redraw before the operation");
        lease.Dispose();
        lease.Dispose();
        Check(calls.SequenceEqual([false, true]), "the scope restores redraw exactly once when disposed repeatedly");

        calls.Clear();
        var failure = ExplorerDirs.App.ShellViewRedrawLease.TrySuppress(redraw =>
        {
            calls.Add(redraw);
            return redraw ? 0 : unchecked((int)0x80004005);
        });
        Check(failure is null && calls.SequenceEqual([false]),
            "a failed suppression returns no lease and does not claim that redraw was disabled");

        var order = new List<string>();
        var refreshLease = ExplorerDirs.App.ShellViewRedrawLease.TrySuppress(redraw =>
            {
                order.Add(redraw ? "redraw-on" : "redraw-off");
                return 0;
            }) ?? throw new InvalidOperationException("A successful redraw suppression should return a lease.");
        ExplorerDirs.App.ShellViewRedrawLease.RunSuppressed(
            refreshLease,
            () =>
            {
                order.Add("refresh");
                return 0;
            });
        Check(order.SequenceEqual(["redraw-off", "refresh", "redraw-on"]),
            "refresh suppression ends as soon as the synchronous Shell call returns");
        Console.WriteLine("Shell view redraw lease smoke: PASS");
    }

    private static void Check(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException("Shell view redraw lease smoke failed: " + behavior);
    }
}
