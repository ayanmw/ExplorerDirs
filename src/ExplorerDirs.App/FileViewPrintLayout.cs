namespace ExplorerDirs.App;

internal enum FileViewPrintKind
{
    ThumbnailGrid,
    CompactList,
    TileGrid,
    DetailsTable
}

internal static class FileViewPrintLayout
{
    internal static FileViewPrintKind ForShellView(uint mode, int iconSize) => mode switch
    {
        1 => FileViewPrintKind.ThumbnailGrid,
        2 or 3 => FileViewPrintKind.CompactList,
        4 or 8 => FileViewPrintKind.DetailsTable,
        6 => FileViewPrintKind.TileGrid,
        _ => FileViewPrintKind.DetailsTable
    };
}
