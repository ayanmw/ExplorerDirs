using System.Windows.Media;

namespace ExplorerDirs.App;

internal static class ViewStyleIconSet
{
    // Each 16-unit path mirrors one entry in Explorer's Ctrl+Shift+1..8 view order.
    internal static IReadOnlyList<Geometry> All { get; } = Array.AsReadOnly(new[]
    {
        Geometry.Parse("M1,1 H15 V15 H1 Z M3,3 H13 V11 H3 Z M6,12 H10 V13 H6 Z"),
        Geometry.Parse("M1,1 H7 V7 H1 Z M9,1 H15 V7 H9 Z M1,9 H7 V15 H1 Z M9,9 H15 V15 H9 Z"),
        Geometry.Parse("M1,1 H5 V5 H1 Z M6,1 H10 V5 H6 Z M11,1 H15 V5 H11 Z M1,6 H5 V10 H1 Z M6,6 H10 V10 H6 Z M11,6 H15 V10 H11 Z M1,11 H5 V15 H1 Z M6,11 H10 V15 H6 Z M11,11 H15 V15 H11 Z"),
        Geometry.Parse("M1,1 H4 V4 H1 Z M6,2 H15 M1,6 H4 V9 H1 Z M6,7 H15 M1,11 H4 V14 H1 Z M6,12 H15"),
        Geometry.Parse("M1,2 H3 M5,2 H8 M10,2 H15 M1,6 H3 M5,6 H8 M10,6 H15 M1,10 H3 M5,10 H8 M10,10 H15 M1,14 H3 M5,14 H8 M10,14 H15"),
        Geometry.Parse("M1,1 H15 V4 H1 Z M1,6 H15 M5,4 V15 M10,4 V15 M1,10 H15 M1,14 H15"),
        Geometry.Parse("M1,1 H6 V6 H1 Z M8,2 H15 M8,5 H13 M1,9 H6 V14 H1 Z M8,10 H15 M8,13 H13"),
        Geometry.Parse("M1,1 H4 V4 H1 Z M6,1 H15 M6,3 H12 M1,7 H4 V10 H1 Z M6,7 H15 M6,9 H12 M1,13 H4 V16 H1 Z M6,13 H15 M6,15 H12")
    });

}
