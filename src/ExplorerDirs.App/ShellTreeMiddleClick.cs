namespace ExplorerDirs.App;

internal static class ShellTreeMiddleClick
{
    internal static bool ShouldOpenInNewTab(uint hit, uint click, uint attributes) =>
        (hit & 0x0046) != 0 && (click & 0x0003) == 0x0002 && (click & 0x0004) == 0 &&
        ShellItemKind.IsFileSystemFolder(attributes);
}
