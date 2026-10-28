using System.Windows;

namespace ExplorerDirs.App;

internal readonly record struct ShellColumnHeaderArrowTarget(Rect Bounds, PropertyKeyState Key,
    string DisplayName);

internal static class ShellColumnHeaderArrowHitTest
{
    private const double ArrowHitWidth = 20;
    private const double ResizeEdgeWidth = 3;

    internal static bool TryFind(IReadOnlyList<ShellColumnHeaderArrowTarget> targets, Point point,
        out ShellColumnHeaderArrowTarget target)
    {
        foreach (var candidate in targets)
        {
            var bounds = candidate.Bounds;
            if (bounds.IsEmpty || bounds.Width <= ArrowHitWidth + ResizeEdgeWidth || bounds.Height <= 0 ||
                !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Right) ||
                !double.IsFinite(bounds.Top) || !double.IsFinite(bounds.Bottom))
                continue;

            if (point.X < Math.Max(bounds.Left, bounds.Right - ArrowHitWidth) ||
                point.X >= bounds.Right - ResizeEdgeWidth ||
                point.Y < bounds.Top || point.Y >= bounds.Bottom)
                continue;

            target = candidate;
            return true;
        }

        target = default;
        return false;
    }
}
