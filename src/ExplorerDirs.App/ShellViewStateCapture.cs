namespace ExplorerDirs.App;

internal interface IShellViewStateSource
{
    int GetMode(out uint mode, out int iconSize);
    int GetSortColumnCount(out int count);
    int GetSortColumns(int count, out IReadOnlyList<SortColumnState> columns);
    int GetGroupBy(out PropertyKeyState? key, out bool ascending);
    // S_FALSE: this view does not expose columns; retain the cached layout.
    int GetDisplayColumns(out IReadOnlyList<DisplayColumnState>? columns);
}

internal static class ShellViewStateCapture
{
    public static ViewState Capture(ViewState cached, IShellViewStateSource source, out bool complete,
        Func<bool>? isCurrent = null)
    {
        complete = false;
        // Shell COM getters may pump navigation callbacks. A complete-looking
        // mixture of two visits must never replace the last coherent snapshot.
        bool Current() => isCurrent?.Invoke() != false;
        if (!Current() || source.GetMode(out var mode, out var iconSize) < 0 || !Current() ||
            source.GetSortColumnCount(out var count) < 0 || !Current() || count is < 0 or > 16)
            return cached.Copy();

        IReadOnlyList<SortColumnState> sorts = [];
        if (count > 0 && (source.GetSortColumns(count, out sorts) < 0 || !Current() || sorts.Count != count))
            return cached.Copy();
        if (source.GetGroupBy(out var group, out var ascending) < 0 || !Current())
            return cached.Copy();
        var columnResult = source.GetDisplayColumns(out var columns);
        if (!Current() || columnResult != 1 && (columnResult != 0 || columns is null || !ColumnLayout.IsValid(columns)))
            return cached.Copy();

        complete = true;
        return new ViewState(mode, iconSize, sorts.ToArray(), group, ascending,
            (columnResult == 1 ? cached.DisplayColumns : columns)?.ToArray());
    }
}
