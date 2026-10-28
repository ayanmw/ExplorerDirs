using ExplorerDirs.App;
using System.Runtime.InteropServices;

internal static class AddressPathMenuSmoke
{
    internal static void Run()
    {
        var entries = AddressPathMenu.Build(@"C:\work\project",
            [new AddressPathMenuLocation("此电脑", "shell:MyComputerFolder")]);
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

        var computerTree = AddressPathMenu.Build("shell:RecycleBinFolder",
        [
            new AddressPathMenuLocation("此电脑", "shell:MyComputerFolder"),
            new AddressPathMenuLocation("驱动器 (D:)", @"D:\", "shell:MyComputerFolder")
        ]);
        var drive = computerTree.Single(entry => entry.Identity == @"D:\");
        Check(drive.ParentIdentity == "shell:MyComputerFolder" && drive.Depth == 1,
            "logical drives are nested beneath This PC");

        var liveLocations = AddressPathMenu.Build("shell:RecycleBinFolder");
        CheckDownloadsLocation(liveLocations.Single(entry => entry.Header == "下载").Identity);
        var listedDrives = liveLocations.Where(entry =>
            !entry.IsAncestor && entry.ParentIdentity == "shell:MyComputerFolder").Select(entry => entry.Identity);
        Check(Environment.GetLogicalDrives().All(rootPath => listedDrives.Contains(rootPath,
                StringComparer.OrdinalIgnoreCase)),
            "the path menu includes logical drives discovered at build time");

        Check(AddressPathMenu.GetDriveHeader(@"C:\", System.IO.DriveType.Fixed, "Windows") == "Windows (C:)",
            "drive labels include the local volume label and drive letter");
        Check(AddressPathMenu.GetDriveHeader(@"D:\", System.IO.DriveType.Fixed, "") == "本地磁盘 (D:)",
            "an unlabeled fixed drive uses the localized drive type caption");
        Check(AddressPathMenu.GetDriveHeader(@"Z:\", System.IO.DriveType.Network, "") == "网络驱动器 (Z:)",
            "network drives use a caption without requiring a remote volume query");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    private static void CheckDownloadsLocation(string identity)
    {
        // Independently compare the menu target with Windows' current known-folder
        // mapping. Read only: never redirect the user's Downloads folder for a test.
        var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        nint expectedPath = 0, pidl = 0;
        try
        {
            Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(in downloads, 0, 0, out expectedPath));
            if (!ShellLocationIdentity.TryGetFolderPidl(identity, out pidl) ||
                !ShellLocationIdentity.TryGetPersistedIdentity(pidl, out var actual, out var isFileSystem) ||
                !isFileSystem || !string.Equals(actual, Marshal.PtrToStringUni(expectedPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Downloads menu must resolve to the current Windows known folder");
            Console.WriteLine("Downloads target matches the current Windows known-folder mapping (read only)");
        }
        finally
        {
            Marshal.FreeCoTaskMem(expectedPath);
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid folderId, uint flags, nint token, out nint path);
}
