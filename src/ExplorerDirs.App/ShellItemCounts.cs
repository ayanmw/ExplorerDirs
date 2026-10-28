namespace ExplorerDirs.App;

internal sealed record ShellItemCounts(int Total, int Selected, ulong? KnownSelectionBytes = null,
    ulong? KnownFolderBytes = null)
{
    internal static int? ReadAllViewTotal(Func<uint, (int Result, int Count)> read)
    {
        // SVGIO_ALLVIEW is the explicit scope for the displayed folder contents.
        var total = read(2);
        return total.Result == 0 && total.Count >= 0 ? total.Count : null;
    }

    internal static ShellItemCounts? Read(Func<uint, (int Result, int Count)> read)
    {
        // SVGIO_ALLVIEW and SVGIO_SELECTION query the already displayed Shell
        // collection; never enumerate the directory to obtain these counts.
        var total = ReadAllViewTotal(read);
        if (total is null) return null;
        var selected = read(1);
        if (selected.Result != 0 || selected.Count < 0 || selected.Count > total.Value) return null;
        return new(total.Value, selected.Count);
    }
    internal string DisplayText => $"{Total:N0} 项 · 已选 {Selected:N0} 项" +
        (Selected > 0 && KnownSelectionBytes is { } selectionSize
            ? $" · 可取得大小 {ByteSizeFormatter.Format(selectionSize)}"
            : Selected == 0 && KnownFolderBytes is { } folderSize
                ? $" · 当前目录可取得大小 {ByteSizeFormatter.Format(folderSize)}"
                : "");
    internal string Description => DisplayText + (Selected > 0
        ? "\n仅汇总可取得的选中项目大小，未提供大小的项目不计入；未递归统计选中的文件夹。"
        : KnownFolderBytes is not null
            ? "\n仅汇总 Windows Shell 可快速取得的当前目录直接子项大小；未提供大小的项目不计入，不递归统计文件夹。"
            : "");
}
