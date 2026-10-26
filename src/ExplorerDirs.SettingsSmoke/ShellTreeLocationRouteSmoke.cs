using ExplorerDirs.App;

internal static class ShellTreeLocationRouteSmoke
{
    internal static void Run()
    {
        var filesystem = ShellTreeLocationTarget.For(true, @"D:\资料");
        Check(!filesystem.UseVirtualRoot && filesystem.FileSystemPath == @"D:\资料",
            "filesystem locations route to the filesystem-backed tree synchronizer");
        var absoluteShellName = ShellTreeLocationTarget.For(false,
            "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
        Check(absoluteShellName.UseVirtualRoot && absoluteShellName.FileSystemPath is null,
            "absolute Shell parsing names route to the virtual tree root");
        var shellAlias = ShellTreeLocationTarget.For(false, "shell:RecycleBinFolder");
        Check(shellAlias.UseVirtualRoot && shellAlias.FileSystemPath is null,
            "shell aliases route to the virtual tree root");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Shell tree location route: " + message); }
}
