namespace ExplorerDirs.NativeProbe;

internal static class ShellViewReferenceRetirementSmoke
{
    internal static void Run()
    {
        var released = new List<nint>();
        var destroyed = new List<nint>();
        var reported = new List<string>();
        var retirement = new ShellViewReferenceRetirement(
            view =>
            {
                destroyed.Add(view);
                return view is 2 or 6
                    ? unchecked((int)0x80004005)
                    : view == 3 ? throw new InvalidOperationException("fixture destroy failure") : 0;
            },
            released.Add,
            reported.Add);

        retirement.Retire(view: 1, folder: 101);
        Check(released.SequenceEqual(new nint[] { 1, 101 }),
            "a view and folder are released after successful window destruction");

        retirement.RetireAfterCreateAttempt(view: 4, folder: 104, createViewWindowAttempted: false);
        Check(released.SequenceEqual(new nint[] { 1, 101, 4, 104 }) && !destroyed.Contains(4),
            "a view is released directly when window creation was never attempted");

        retirement.RetireAfterCreateAttempt(view: 5, folder: 105, createViewWindowAttempted: true);
        Check(released.SequenceEqual(new nint[] { 1, 101, 4, 104, 5, 105 }) && destroyed.Contains(5),
            "a failed window creation without an HWND still runs DestroyViewWindow");

        retirement.RetireAfterCreateAttempt(view: 6, folder: 106, createViewWindowAttempted: true);
        Check(released.SequenceEqual(new nint[] { 1, 101, 4, 104, 5, 105 }) &&
              retirement.DeferredCount == 1 && reported.Count == 1 && destroyed.Contains(6),
            "a failed no-HWND destroy retains the partial view and folder");

        retirement.Retire(view: 2, folder: 102);
        Check(released.SequenceEqual(new nint[] { 1, 101, 4, 104, 5, 105 }) && retirement.DeferredCount == 2 &&
              reported.Count == 2,
            "a view and folder remain referenced when window destruction fails");

        retirement.Retire(view: 3, folder: 103);
        Check(released.SequenceEqual(new nint[] { 1, 101, 4, 104, 5, 105 }) && retirement.DeferredCount == 3 &&
              reported.Count == 3 && reported[2].Contains("threw", StringComparison.Ordinal),
            "a thrown window-destruction call also defers Shell references");

        retirement.ReleaseDeferredAfterParentDestroyed();
        Check(released.SequenceEqual(new nint[] { 1, 101, 4, 104, 5, 105, 6, 106, 2, 102, 3, 103 }) && retirement.DeferredCount == 0,
            "deferred Shell references release after their parent window is destroyed");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Shell view retirement failed: {message}.");
        Console.WriteLine($"PASS {message}");
    }
}
