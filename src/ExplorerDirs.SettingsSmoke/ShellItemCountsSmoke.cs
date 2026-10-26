using ExplorerDirs.App;

internal static class ShellItemCountsSmoke
{
    internal static void Run()
    {
        var focusScopes = new List<uint>();
        Check(ShellItemCounts.ReadAllViewTotal(scope =>
                {
                    focusScopes.Add(scope);
                    return (0, 4);
                }) == 4 && focusScopes.SequenceEqual([2u]),
            "focus recovery reads SVGIO_ALLVIEW for populated view state");
        focusScopes.Clear();
        Check(ShellItemCounts.ReadAllViewTotal(scope =>
                {
                    focusScopes.Add(scope);
                    return (0, 0);
                }) == 0 && focusScopes.SequenceEqual([2u]),
            "focus recovery recognizes an empty SVGIO_ALLVIEW result");
        Check(ShellItemCounts.ReadAllViewTotal(_ => (unchecked((int)0x80004005), 0)) is null,
            "an unavailable all-view item count remains unknown");

        var flags = new List<uint>();
        var counts = ShellItemCounts.Read(flag =>
        {
            flags.Add(flag);
            return (0, flag == 2 ? 28 : 3);
        });
        Check(counts is { Total: 28, Selected: 3 } && flags.SequenceEqual(new uint[] { 2, 1 }),
            "Shell ALLVIEW and SELECTION are read separately");
        Check(ShellItemCounts.Read(_ => (0, 0)) is { Total: 0, Selected: 0 },
            "a successful empty directory is zero");
        foreach (var failure in new[] { unchecked((int)0x80004005), 1 })
        {
            Check(ShellItemCounts.Read(flag => flag == 2 ? (failure, 0) : (0, 0)) is null,
                "failed or incomplete total is unknown, not zero");
            Check(ShellItemCounts.Read(flag => flag == 1 ? (failure, 0) : (0, 7)) is null,
                "failed or incomplete selection is unknown, not zero");
        }
        Check(ShellItemCounts.Read(_ => (0, -1)) is null, "negative counts are rejected");
        Check(ShellItemCounts.Read(flag => (0, flag == 2 ? 1 : 2)) is null,
            "inconsistent snapshots during changes are retried, not displayed");
        var selected = 1;
        Check(ShellItemCounts.Read(flag => (0, flag == 2 ? 3 : selected))!.Selected == 1,
            "initial selection");
        selected = 0;
        Check(ShellItemCounts.Read(flag => (0, flag == 2 ? 3 : selected))!.Selected == 0,
            "clearing selection is reflected on the next snapshot");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Item counts: {label}");
    }
}
