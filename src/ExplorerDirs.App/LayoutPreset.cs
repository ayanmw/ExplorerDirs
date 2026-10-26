namespace ExplorerDirs.App;

internal sealed record PaneSlot(int Row, int Column, int RowSpan = 1, int ColumnSpan = 1);

internal sealed record LayoutPreset(int Id, string Name, int Rows, int Columns, PaneSlot[] Slots)
{
    public static IReadOnlyList<LayoutPreset> All { get; } =
    [
        new(1, "单窗格", 1, 1, [new(0, 0)]),
        new(2, "左右双窗格", 1, 2, [new(0, 0), new(0, 1)]),
        new(3, "上下双窗格", 2, 1, [new(0, 0), new(1, 0)]),
        new(4, "四窗格", 2, 2, [new(0, 0), new(0, 1), new(1, 0), new(1, 1)]),
        new(5, "左大右双", 2, 2, [new(0, 0, 2), new(0, 1), new(1, 1)]),
        new(6, "上大下双", 2, 2, [new(0, 0, 1, 2), new(1, 0), new(1, 1)]),
        new(7, "左双右大", 2, 2, [new(0, 0), new(1, 0), new(0, 1, 2)]),
        new(8, "上双下大", 2, 2, [new(0, 0), new(0, 1), new(1, 0, 1, 2)]),
        new(9, "三列", 1, 3, [new(0, 0), new(0, 1), new(0, 2)]),
        new(10, "三行", 3, 1, [new(0, 0), new(1, 0), new(2, 0)]),
        new(11, "四列", 1, 4, [new(0, 0), new(0, 1), new(0, 2), new(0, 3)]),
        new(12, "四行", 4, 1, [new(0, 0), new(1, 0), new(2, 0), new(3, 0)])
    ];

    public static LayoutPreset Find(int id) => All.FirstOrDefault(layout => layout.Id == id) ?? All[3];

    internal static int ResolveActivePaneIndex(int remembered, int current, int visibleCount)
    {
        if (visibleCount < 1) throw new ArgumentOutOfRangeException(nameof(visibleCount));
        if (remembered >= 0 && remembered < visibleCount) return remembered;
        return current >= 0 && current < visibleCount ? current : 0;
    }
}
