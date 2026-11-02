namespace ExplorerDirs.App;

internal readonly record struct SpaceMapRectangle(int ItemIndex, double X, double Y, double Width, double Height);

internal static class SpaceTreemapLayout
{
    // Balanced binary treemap, implemented here without importing GPL application code.
    // Decimal sums avoid overflow when many large logical sizes are combined.
    internal static IReadOnlyList<SpaceMapRectangle> Create(IReadOnlyList<decimal> sizes, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return [];
        var items = sizes.Select((size, index) => (Size: size, Index: index)).Where(item => item.Size > 0)
            .OrderByDescending(item => item.Size).ToArray();
        if (items.Length == 0) return [];
        var prefix = new decimal[items.Length + 1];
        for (var index = 0; index < items.Length; index++) prefix[index + 1] = prefix[index] + items[index].Size;
        var result = new List<SpaceMapRectangle>(items.Length);
        void Split(int start, int end, double x, double y, double w, double h)
        {
            if (end - start == 1) { result.Add(new(items[start].Index, x, y, w, h)); return; }
            var total = prefix[end] - prefix[start];
            var middle = start + 1;
            while (middle < end - 1 && Math.Abs((prefix[middle + 1] - prefix[start]) * 2 - total) <
                Math.Abs((prefix[middle] - prefix[start]) * 2 - total)) middle++;
            var ratio = (double)((prefix[middle] - prefix[start]) / total);
            if (w >= h)
            { Split(start, middle, x, y, w * ratio, h); Split(middle, end, x + w * ratio, y, w * (1 - ratio), h); }
            else
            { Split(start, middle, x, y, w, h * ratio); Split(middle, end, x, y + h * ratio, w, h * (1 - ratio)); }
        }
        Split(0, items.Length, 0, 0, width, height);
        return result;
    }
}
