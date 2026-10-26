namespace ExplorerDirs.App;

internal static class ShellAutoColumnWidthRouting
{
    internal static bool ShouldDispatchToListView(int message, int key, int modifiers, int shellResult) =>
        message == 0x0100 && key == 0x6B && modifiers == 2 && shellResult == 1;
}
