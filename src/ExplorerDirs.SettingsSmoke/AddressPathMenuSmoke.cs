using ExplorerDirs.App;

internal static class AddressPathMenuSmoke
{
    internal static void Run()
    {
        var entries = AddressPathMenu.Build(@"C:\work\project", [("此电脑", "shell:MyComputerFolder")]);
        Check(entries.Take(3).Select(entry => entry.Identity).SequenceEqual(
                [@"C:\", @"C:\work", @"C:\work\project"]),
            "path dropdown lists the root-to-current hierarchy");
        Check(entries.Take(3).All(entry => entry.IsAncestor), "filesystem path entries are marked as ancestors");
        Check(entries.Take(3).Select(entry => entry.Depth).SequenceEqual([0, 1, 2]),
            "path hierarchy entries expose increasing menu depth");
        Check(entries[0].ParentIdentity is null &&
              entries[1].ParentIdentity == entries[0].Identity &&
              entries[2].ParentIdentity == entries[1].Identity,
            "each path menu item is nested under its direct parent");
        Check(entries.Take(3).Count(entry => entry.IsCurrent) == 1 && entries[2].IsCurrent,
            "current directory is marked distinctly from its ancestors");
        Check(entries[^1].Identity == "shell:MyComputerFolder" && !entries[^1].IsAncestor,
            "common Shell locations follow path ancestors");
        Check(AddressPathMenu.Build("shell:RecycleBinFolder", []).Single().Identity == "shell:RecycleBinFolder",
            "virtual Shell locations remain directly navigable without filesystem path splitting");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
