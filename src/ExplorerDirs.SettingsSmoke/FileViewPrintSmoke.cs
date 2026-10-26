using ExplorerDirs.App;
using System.IO;
using System.Windows.Documents;

internal static class FileViewPrintSmoke
{
    internal static void Run()
    {
        Require(FileViewPrintLayout.ForShellView(1, 256) == FileViewPrintKind.ThumbnailGrid &&
                FileViewPrintLayout.ForShellView(1, 16) == FileViewPrintKind.ThumbnailGrid,
            "icon view prints as a thumbnail grid at every icon size");
        Require(FileViewPrintLayout.ForShellView(2, 16) == FileViewPrintKind.CompactList,
            "small icon view prints as a compact list");
        Require(FileViewPrintLayout.ForShellView(3, 16) == FileViewPrintKind.CompactList,
            "list view prints as a compact list");
        Require(FileViewPrintLayout.ForShellView(4, 16) == FileViewPrintKind.DetailsTable,
            "details view prints its visible columns");
        Require(FileViewPrintLayout.ForShellView(6, 48) == FileViewPrintKind.TileGrid,
            "tiles view prints its thumbnails and visible properties");
        Require(FileViewPrintLayout.ForShellView(8, 16) == FileViewPrintKind.DetailsTable,
            "content view prints its visible properties");
        Require(FileViewPrintLayout.ForShellView(99, 16) == FileViewPrintKind.DetailsTable,
            "unknown Shell view mode falls back to visible columns");

        var details = FileViewPrintDocument.Create(new FileViewPrintSnapshot(
            ["Name", "Size"], [["a.txt", "10 KB"], ["b.txt", "20 KB"]],
            FileViewPrintKind.DetailsTable, []), 720, 960);
        var detailsTable = (Table)details.Blocks.Single();
        Require(detailsTable.RowGroups.Single().Rows.Count == 3,
            "details print includes the visible-column header and every current view row");

        var list = FileViewPrintDocument.Create(new FileViewPrintSnapshot(
            ["Name"], [["a.txt"], ["b.txt"]], FileViewPrintKind.CompactList, [null, null]), 720, 960);
        var listTable = (Table)list.Blocks.Single();
        Require(listTable.RowGroups.Single().Rows.Count == 2,
            "compact print includes each current view item without a synthetic header");

        Throws<InvalidDataException>(() => FileViewPrintDocument.Create(new FileViewPrintSnapshot(
                ["Name", "Size"], [["a.txt"]], FileViewPrintKind.DetailsTable, []), 720, 960),
            "print rejects incomplete view rows");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("File view print regression: " + description);
    }

    private static void Throws<TException>(Action action, string description) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new Exception("File view print regression: " + description);
    }
}
