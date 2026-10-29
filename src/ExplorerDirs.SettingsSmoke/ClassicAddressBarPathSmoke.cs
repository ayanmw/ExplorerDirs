using ExplorerDirs.App;

internal static class ClassicAddressBarPathSmoke
{
    internal static void Run()
    {
        Check(ClassicAddressBarPath.Format(@"C:\work\project", true) == @"C:\work\project\",
            "classic address text appends a separator to filesystem directories");
        Check(ClassicAddressBarPath.Format(@"C:\", true) == @"C:\",
            "classic address text preserves a drive root separator");
        Check(ClassicAddressBarPath.Format(@"\\server\share", true) == @"\\server\share\",
            "classic address text appends a separator to UNC directories");
        Check(ClassicAddressBarPath.Format("shell:RecycleBinFolder", false) == "shell:RecycleBinFolder",
            "classic address text leaves virtual Shell locations unchanged");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
