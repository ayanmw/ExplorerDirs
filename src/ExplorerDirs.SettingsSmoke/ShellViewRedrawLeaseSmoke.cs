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

        var coordinator = new ExplorerDirs.App.ShellViewRedrawLeaseCoordinator();
        calls.Clear();
        var first = coordinator.Acquire(7, () => ExplorerDirs.App.ShellViewRedrawLease.TrySuppress(redraw =>
        {
            calls.Add(redraw);
            return 0;
        })) ?? throw new InvalidOperationException("The first refresh should acquire redraw suppression.");
        var second = coordinator.Acquire(7, () => throw new InvalidOperationException(
            "A matching view generation must reuse its existing redraw suppression."))
            ?? throw new InvalidOperationException("The overlapping refresh should share redraw suppression.");
        Check(calls.SequenceEqual([false]), "overlapping refreshes disable redraw only once for the same view");
        first.Dispose();
        Check(calls.SequenceEqual([false]), "one completed refresh cannot release another refresh's redraw hold");
        second.Dispose();
        Check(calls.SequenceEqual([false, true]), "the last completed refresh restores redraw");

        calls.Clear();
        var oldGeneration = coordinator.Acquire(8, () => ExplorerDirs.App.ShellViewRedrawLease.TrySuppress(redraw =>
        {
            calls.Add(redraw);
            return 0;
        })) ?? throw new InvalidOperationException("A new view generation should acquire its own redraw hold.");
        var newGeneration = coordinator.Acquire(9, () => ExplorerDirs.App.ShellViewRedrawLease.TrySuppress(redraw =>
        {
            calls.Add(redraw);
            return 0;
        })) ?? throw new InvalidOperationException("A second view generation should acquire its own redraw hold.");
        oldGeneration.Dispose();
        Check(calls.SequenceEqual([false, false, true]),
            "releasing an older view does not release the current view's independent redraw hold");
        newGeneration.Dispose();
        Console.WriteLine("Shell view redraw lease smoke: PASS");
    }

    private static void Check(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException("Shell view redraw lease smoke failed: " + behavior);
    }
}
