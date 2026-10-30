namespace ExplorerDirs.NativeProbe;

// Maps ListView selection/focus through a refreshed row order by stable Shell row identity.
internal sealed class ShellListViewSelectionSnapshot
{
    private readonly HashSet<long> _selectedRowIds;

    private ShellListViewSelectionSnapshot(HashSet<long> selectedRowIds, long? focusedRowId)
    {
        _selectedRowIds = selectedRowIds;
        FocusedRowId = focusedRowId;
    }

    internal long? FocusedRowId { get; }

    internal static ShellListViewSelectionSnapshot Capture(IReadOnlyList<ShellListViewRow> rows,
        IEnumerable<long> selectedRowIds, long? focusedRowId)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(selectedRowIds);
        var presentIds = rows.Select(row => row.Id).ToHashSet();
        var selected = selectedRowIds.Where(presentIds.Contains).ToHashSet();
        return new ShellListViewSelectionSnapshot(selected,
            focusedRowId is { } id && presentIds.Contains(id) ? id : null);
    }

    internal IReadOnlyList<ShellListViewRowSelection> Resolve(IReadOnlyList<ShellListViewRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Select(row => new ShellListViewRowSelection(
            row.Id,
            _selectedRowIds.Contains(row.Id),
            FocusedRowId == row.Id)).ToArray();
    }
}

internal sealed record ShellListViewRowSelection(long RowId, bool Selected, bool Focused);
