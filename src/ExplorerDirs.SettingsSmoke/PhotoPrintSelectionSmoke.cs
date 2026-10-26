using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class PhotoPrintSelectionSmoke
{
    internal static void Run()
    {
        var empty = PhotoPrintSelection.Filter([]);
        Require(empty.CandidatePaths.Count == 0 && empty.RejectedItems.Count == 0,
            "an empty active selection has no photo candidates");

        var result = PhotoPrintSelection.Filter([
            new("a.jpg", @"C:\photos\a.jpg", IsFileSystemItem: true, IsDirectory: false),
            new("folder", @"C:\photos\folder", IsFileSystemItem: true, IsDirectory: true),
            new("readme.txt", @"C:\photos\readme.txt", IsFileSystemItem: true, IsDirectory: false),
            new("Virtual image", null, IsFileSystemItem: false, IsDirectory: false),
            new("b.PNG", @"C:\photos\b.PNG", IsFileSystemItem: true, IsDirectory: false)
        ]);

        Require(result.CandidatePaths.SequenceEqual([@"C:\photos\a.jpg", @"C:\photos\readme.txt", @"C:\photos\b.PNG"]),
            "ordinary file paths are passed to WIC in selection order so installed codecs can decide support");
        Require(result.RejectedItems.Count == 2 &&
                result.RejectedItems.Select(item => item.DisplayName).SequenceEqual(["folder", "Virtual image"]) &&
                result.RejectedItems.All(item => !string.IsNullOrWhiteSpace(item.Reason)),
            "folders and virtual items are rejected before decoding with individual reasons");
        var blockedOnly = PhotoPrintSelection.Filter([
            new("folder", @"C:\photos\folder", IsFileSystemItem: true, IsDirectory: true),
            new("Virtual image", null, IsFileSystemItem: false, IsDirectory: false)
        ]);
        Require(blockedOnly.CandidatePaths.Count == 0 && blockedOnly.RejectedItems.Count == 2,
            "a selection containing no ordinary files cannot open the photo printer");
        Console.WriteLine("Photo print selection smoke: PASS");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("Photo print selection: " + description);
    }
}
