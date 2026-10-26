using ExplorerDirs.App;

internal static class AddressPathMenuSmoke
{
    internal static void Run()
    {
        var entries = AddressPathMenu.Build(@"C:\work\project", [("此电脑", "shell:MyComputerFolder")]);
        Check(entries.Take(3).Select(entry => entry.Identity).SequenceEqual(
                [@"C:\work\project", @"C:\work", @"C:\"]),
            "path dropdown lists the current directory and every ancestor, nearest first");
        Check(entries.Take(3).All(entry => entry.IsAncestor), "filesystem path entries are marked as ancestors");
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
