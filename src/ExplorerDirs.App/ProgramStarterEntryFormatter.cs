namespace ExplorerDirs.App;

internal static class ProgramStarterEntryFormatter
{
    public static string FormatForReview(ProgramStarterEntry entry) =>
        $"{entry.Name} | 程序：{entry.ExecutablePath} | 参数：{entry.Arguments} | 工作目录：{entry.WorkingDirectory}";
}
