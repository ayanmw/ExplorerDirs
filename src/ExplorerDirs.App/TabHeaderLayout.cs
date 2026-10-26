using System.Windows;

namespace ExplorerDirs.App;

internal readonly record struct TabHeaderLayoutResult(Size DesiredSize, IReadOnlyList<Rect> Items);

internal static class TabHeaderLayout
{
    internal static TabHeaderLayoutResult Calculate(IReadOnlyList<Size> sizes, double availableWidth,
        bool multiline)
    {
        var slots = new Rect[sizes.Count];
        var left = 0.0;
        var top = 0.0;
        var rowHeight = 0.0;
        var maximumWidth = 0.0;

        for (var index = 0; index < sizes.Count; index++)
        {
            var size = sizes[index];
            if (multiline && left > 0 && left + size.Width > availableWidth)
            {
                maximumWidth = Math.Max(maximumWidth, left);
                left = 0;
                top += rowHeight;
                rowHeight = 0;
            }

            slots[index] = new Rect(left, top, size.Width, size.Height);
            left += size.Width;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        maximumWidth = Math.Max(maximumWidth, left);
        var height = sizes.Count == 0 ? 0 : top + rowHeight;
        var desiredWidth = multiline ? Math.Min(maximumWidth, availableWidth) : maximumWidth;
        return new TabHeaderLayoutResult(new Size(desiredWidth, height), slots);
    }
}
