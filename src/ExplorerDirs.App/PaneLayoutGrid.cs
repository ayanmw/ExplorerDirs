using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ExplorerDirs.App;

// Keep every Shell host attached to the same parent. Independent quad columns
// cannot be expressed by the shared column tracks of the other Grid layouts.
public sealed class PaneLayoutGrid : Grid
{
    private LayoutProportions? _quad;
    private int _generation;
    internal LayoutProportions? QuadProportions => _quad;

    internal void ConfigureQuad(LayoutProportions? proportions)
    {
        _generation++;
        _quad = proportions;
        InvalidateMeasure();
    }

    internal void AddQuadDividers(Action<LayoutProportions>? changed)
    {
        var generation = _generation;
        for (var index = 0; index < 3; index++)
        {
            var axis = index; // top columns, bottom columns, shared rows
            var divider = new Thumb
            {
                Cursor = axis == 2 ? Cursors.SizeNS : Cursors.SizeWE,
                Focusable = false, IsTabStop = false
            };
            divider.SetResourceReference(Control.BackgroundProperty, "ExplorerDirs.BorderBrush");
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            divider.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = border };
            AutomationProperties.SetName(divider, axis switch
            { 0 => "调整上排左右宽度", 1 => "调整下排左右宽度", _ => "调整上下窗格高度" });
            LayoutProportions? before = null;
            bool Current() => generation == _generation && _quad is not null && Children.Contains(divider);
            divider.DragStarted += (_, _) =>
            {
                if (!Current()) return;
                before = _quad;
            };
            divider.DragDelta += (_, e) =>
            {
                if (!Current() || before is null) return;
                var extent = Math.Max(0, (axis == 2 ? RenderSize.Height : RenderSize.Width) - 5);
                if (extent <= 0) return;
                // Thumb deltas are relative to its current arranged origin. Adding
                // them cumulatively retained overshoot and stuck the divider when
                // returning from a size clamp (caught by the review regression).
                var offset = VisualTreeHelper.GetOffset(divider);
                var position = axis == 2 ? offset.Y + e.VerticalChange : offset.X + e.HorizontalChange;
                var fraction = Split(extent, position / extent, axis == 2 ? 100 : 120) / extent;
                // Snapshot all axes: dragging one divider must not move its sibling.
                _quad = axis switch
                {
                    0 => before with { Horizontal = fraction, BottomHorizontal = before.BottomFraction },
                    1 => before with { BottomHorizontal = fraction },
                    _ => before with { Vertical = fraction }
                };
                InvalidateMeasure();
            };
            divider.DragCompleted += (_, e) =>
            {
                if (Current() && before is not null)
                {
                    if (e.Canceled) { _quad = before; InvalidateMeasure(); }
                    else changed?.Invoke(_quad!);
                }
                before = null;
            };
            Children.Add(divider);
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (_quad is null) return base.MeasureOverride(constraint);
        var size = new Size(double.IsInfinity(constraint.Width) ? 245 : constraint.Width,
            double.IsInfinity(constraint.Height) ? 205 : constraint.Height);
        var bounds = Bounds(size, _quad);
        for (var index = 0; index < Children.Count; index++) Children[index].Measure(bounds[index].Size);
        return size;
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (_quad is null) return base.ArrangeOverride(size);
        var bounds = Bounds(size, _quad);
        for (var index = 0; index < Children.Count; index++) Children[index].Arrange(bounds[index]);
        return size;
    }

    private static Rect[] Bounds(Size size, LayoutProportions proportions)
    {
        var gapX = Math.Min(5, size.Width); var gapY = Math.Min(5, size.Height);
        var width = Math.Max(0, size.Width - gapX); var height = Math.Max(0, size.Height - gapY);
        var top = Split(width, proportions.Horizontal, 120);
        var bottom = Split(width, proportions.BottomFraction, 120);
        var row = Split(height, proportions.Vertical, 100);
        return [new(0, 0, top, row), new(top + gapX, 0, width - top, row),
            new(0, row + gapY, bottom, height - row), new(bottom + gapX, row + gapY, width - bottom, height - row),
            new(top, 0, gapX, row), new(bottom, row + gapY, gapX, height - row), new(0, row, size.Width, gapY)];
    }

    private static double Split(double extent, double fraction, double minimum) =>
        Math.Clamp(extent * fraction, Math.Min(minimum, extent / 2), Math.Max(extent - minimum, extent / 2));
}
