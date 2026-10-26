using ExplorerDirs.App;

internal static class AddressPathHistorySmoke
{
    internal static void Run()
    {
        var history = new AddressPathHistory();
        history.Record(@"C:\one");
        history.Record(@"C:\two");
        history.Record(@"c:\ONE");
        Check(history.Paths.SequenceEqual([@"c:\ONE", @"C:\two"]),
            "address history moves a revisited path to the front without duplicates");
        var copy = history.Copy();
        copy.Record(@"D:\only-copy");
        Check(history.Paths.Count == 2 && copy.Paths[0] == @"D:\only-copy",
            "duplicated tab address histories are independent");
        for (var index = 0; index < 25; index++) history.Record($@"C:\path-{index}");
        Check(history.Paths.Count == 20 && history.Paths[0] == @"C:\path-24" &&
              history.Paths[^1] == @"C:\path-5",
            "address history retains the 20 most recently visited paths");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
