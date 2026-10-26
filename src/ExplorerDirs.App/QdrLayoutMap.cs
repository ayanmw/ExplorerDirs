using System.Globalization;

namespace ExplorerDirs.App;

// Native layout IDs and pane geometry come from Q-Dir 12.79's layout handler,
// not menu order or translated labels. See docs/qdr-layout-mapping.md.
internal sealed record QdrLayoutMap(int NativeId, int AppId, IReadOnlyList<int> AppToNative)
{
    private static QdrLayoutMap Map(int native, int app, params int[] order) =>
        new(native, app, Array.AsReadOnly(order));

    private static readonly QdrLayoutMap[] Known =
    [
        Map(11, 1, 0, 1, 2, 3), // Single
        Map(3, 2, 0, 1, 2, 3),  // Side by side
        Map(4, 3, 0, 2, 1, 3),  // Stacked: Q-Dir's second visible pane is #3.
        Map(0, 4, 0, 1, 2, 3),  // Quad
        Map(9, 5, 0, 2, 3, 1),  // Large left, two right
        Map(1, 6, 0, 2, 3, 1),  // Large top, two bottom
        Map(10, 7, 0, 1, 2, 3), // Two left, large right
        Map(2, 8, 0, 1, 2, 3),  // Two top, large bottom
        Map(7, 9, 0, 1, 2, 3),  // Three columns
        Map(8, 10, 0, 1, 2, 3), // Three rows
        Map(5, 11, 0, 1, 2, 3), // Four columns
        Map(6, 12, 0, 1, 2, 3)  // Four rows
    ];

    internal static QdrLayoutMap? FromNative(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? Known.FirstOrDefault(map => map.NativeId == id) : null;

    internal static QdrLayoutMap FromApp(int id) =>
        Known.FirstOrDefault(map => map.AppId == id) ??
        throw new ArgumentOutOfRangeException(nameof(id), "此布局尚无已核实的 Q-Dir 映射。");
}
