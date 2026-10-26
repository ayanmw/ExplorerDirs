using ExplorerDirs.App;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

internal static class QuadLayoutSmoke
{
    internal static void Run()
    {
        var grid = new PaneLayoutGrid();
        UIElement[] panes = [new Border(), new Border(), new Border(), new Border()];
        var layout = LayoutPreset.Find(4);
        var original = new LayoutProportions(.25, .6, BottomHorizontal: .7);
        void Arrange(double width = 1000, double height = 700)
        { grid.Measure(new(width, height)); grid.Arrange(new(0, 0, width, height)); }
        PaneLayoutHost.Apply(grid, panes, layout, original);
        Arrange();
        Check(Near(panes[0].RenderSize.Width, 995 * .25) && Near(panes[2].RenderSize.Width, 995 * .7) &&
            Near(panes[0].RenderSize.Height, 695 * .6), "independent native fractions control actual rectangles");
        Check(Near(VisualTreeHelper.GetOffset(panes[3]).X, 995 * .7 + 5), "bottom-right origin follows only its row");
        Check(panes.All(p => ReferenceEquals(VisualTreeHelper.GetParent(p), grid)), "parent identity retained");
        for (var axis = 0; axis < 3; axis++)
        {
            LayoutProportions? saved = null;
            PaneLayoutHost.Apply(grid, panes, layout, original, value => saved = value); Arrange();
            var divider = grid.Children.OfType<Thumb>().ElementAt(axis);
            divider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            divider.RaiseEvent(new DragDeltaEventArgs(axis == 2 ? 0 : 1000, axis == 2 ? 1000 : 0) { RoutedEvent = Thumb.DragDeltaEvent });
            Arrange();
            var actual = PaneLayoutHost.Capture(grid, layout, original);
            Check(actual.IsValidFor(layout) && actual != original, "all three dividers change valid proportions");
            Check(axis == 0 ? actual.BottomFraction == original.BottomFraction && actual.Vertical == original.Vertical :
                axis == 1 ? actual.Horizontal == original.Horizontal && actual.Vertical == original.Vertical :
                actual.Horizontal == original.Horizontal && actual.BottomFraction == original.BottomFraction, "siblings stay unchanged");
            Check(panes.All(p => p.RenderSize.Width >= 120 && p.RenderSize.Height >= 100), "drag clamps native pane minimums");
            divider.RaiseEvent(new DragDeltaEventArgs(axis == 2 ? 0 : 100, axis == 2 ? 100 : 0) { RoutedEvent = Thumb.DragDeltaEvent }); Arrange();
            // Thumb reports displacement relative to its current arranged origin.
            // Returning inside the bounds must immediately leave the size clamp.
            divider.RaiseEvent(new DragDeltaEventArgs(axis == 2 ? 0 : -25, axis == 2 ? -25 : 0) { RoutedEvent = Thumb.DragDeltaEvent }); Arrange();
            var returned = grid.QuadProportions!;
            Check(axis == 0 ? returned.Horizontal < actual.Horizontal : axis == 1 ? returned.BottomFraction < actual.BottomFraction :
                returned.Vertical < actual.Vertical, "return from overshoot immediately leaves minimum clamp");
            Check(Near(axis == 2 ? VisualTreeHelper.GetOffset(divider).Y : VisualTreeHelper.GetOffset(divider).X,
                axis == 2 ? 570 : 850), "return tracks exact pointer target after repeated upper overshoot");
            divider.RaiseEvent(new DragDeltaEventArgs(axis == 2 ? 0 : -2000, axis == 2 ? -2000 : 0) { RoutedEvent = Thumb.DragDeltaEvent }); Arrange();
            divider.RaiseEvent(new DragDeltaEventArgs(axis == 2 ? 0 : -10, axis == 2 ? -10 : 0) { RoutedEvent = Thumb.DragDeltaEvent }); Arrange();
            divider.RaiseEvent(new DragDeltaEventArgs(axis == 2 ? 0 : 20, axis == 2 ? 20 : 0) { RoutedEvent = Thumb.DragDeltaEvent }); Arrange();
            Check(Near(axis == 2 ? VisualTreeHelper.GetOffset(divider).Y : VisualTreeHelper.GetOffset(divider).X,
                axis == 2 ? 120 : 140), "return tracks exact pointer target after repeated lower overshoot");
            divider.RaiseEvent(new DragCompletedEventArgs(0, 0, true) { RoutedEvent = Thumb.DragCompletedEvent }); Arrange();
            Check(saved is null && grid.QuadProportions == original, "cancel restores all axes without publishing");
            divider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            divider.RaiseEvent(new DragDeltaEventArgs(axis == 2 ? 0 : -20, axis == 2 ? -20 : 0) { RoutedEvent = Thumb.DragDeltaEvent });
            divider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Check(saved is not null && saved == grid.QuadProportions, "completion publishes all independent axes");
            divider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            PaneLayoutHost.Apply(grid, panes, LayoutPreset.Find(2));
            var lastSaved = saved;
            divider.RaiseEvent(new DragDeltaEventArgs(100, 100) { RoutedEvent = Thumb.DragDeltaEvent });
            divider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Check(saved == lastSaved && grid.QuadProportions is null, "retired drag cannot affect replacement layout");
        }
        PaneLayoutHost.Apply(grid, panes, layout, original);
        Arrange(650, 420); Arrange(2000, 1400);
        Check(grid.QuadProportions == original && Near(panes[2].RenderSize.Width, 1995 * .7), "resize preserves independent preference");
        PaneLayoutHost.Apply(grid, panes, layout); Arrange();
        Check(Near(panes[0].RenderSize.Width, panes[2].RenderSize.Width) && grid.QuadProportions == LayoutProportions.Equal, "reset restores both rows");
        Store(original);
    }

    private static void Store(LayoutProportions original)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "profile-smoke", "quad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "LayoutProportions.ini");
        var v3 = "[General]\nFormatVersion=3\n[Layout4]\nHorizontal=0.3\nVertical=0.6\n";
        File.WriteAllText(path, v3);
        var store = new LayoutProportionsStore(path);
        var values = store.Load();
        Check(store.LoadWarning is null && values[4].BottomFraction == .3, "v3 quad inherits old shared fraction");
        values[4] = original; store.Save(values); store.Save(values);
        Check(Directory.GetFiles(root, "*.before-v4-*.bak").Count(p => File.ReadAllText(p) == v3) == 1, "one exact v3 upgrade backup");
        Check(new LayoutProportionsStore(path).Load()[4] == original, "independent bottom fraction survives disk roundtrip");
        foreach (var bad in new[] { "NaN", "0", "1", "-1", "Infinity" })
        {
            File.WriteAllText(path, v3.Replace("Version=3", "Version=4") + "BottomHorizontal=" + bad + "\n[Layout2]\nHorizontal=0.4\nVertical=0.5\n");
            var damaged = new LayoutProportionsStore(path); var recovered = damaged.Load();
            Check(damaged.LoadWarning is not null && recovered.Count == 1 && recovered.ContainsKey(2), "invalid bottom ratio preserves other layouts");
        }
        Check(!new LayoutProportions(.3, .6, BottomHorizontal: .7).IsValidFor(LayoutPreset.Find(2)), "bottom fraction is quad-only");
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-8;
    private static void Check(bool value, string message) { if (!value) throw new Exception("Quad: " + message); }
}
