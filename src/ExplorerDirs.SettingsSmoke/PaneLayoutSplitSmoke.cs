using ExplorerDirs.App;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.IO;
using System.Globalization;

internal static class PaneLayoutSplitSmoke
{
    internal static void Run()
    {
        Check(LayoutPreset.All.Count == 12, "all twelve supported layouts are present");
        Check(LayoutPreset.Find(7).Slots.SequenceEqual(new PaneSlot[] { new(0, 0), new(1, 0), new(0, 1, 2) }),
            "right-large layout places the first two panes on the left");
        Check(LayoutPreset.Find(8).Slots.SequenceEqual(new PaneSlot[] { new(0, 0), new(0, 1), new(1, 0, 1, 2) }),
            "bottom-large layout places the first two panes above the third");
        Check(LayoutPreset.ResolveActivePaneIndex(2, 0, 3) == 2 &&
              LayoutPreset.ResolveActivePaneIndex(-1, 1, 2) == 1 &&
              LayoutPreset.ResolveActivePaneIndex(3, 3, 2) == 0,
            "layout changes restore the remembered visible pane or fall back to the current visible pane");
        var grid = new PaneLayoutGrid();
        UIElement[] panes = [new Border(), new Border(), new Border(), new Border()];
        PaneLayoutHost.Apply(grid, panes, LayoutPreset.Find(4));
        if (grid.ColumnDefinitions.Count != 3 || grid.RowDefinitions.Count != 3 ||
            grid.Children.OfType<Thumb>().Count() != 3)
            throw new Exception("Four-pane layout needs dedicated horizontal and vertical splitter tracks.");
        foreach (var layout in LayoutPreset.All)
        {
            var expected = layout.Id >= 9 ? new LayoutProportions(0.5, 0.5,
                new(2, 3, 4, layout.Slots.Length == 4 ? 5 : 0)) : new LayoutProportions(0.3, 0.65);
            PaneLayoutHost.Apply(grid, panes, layout, expected);
            grid.Measure(new Size(1000, 700));
            grid.Arrange(new Rect(0, 0, 1000, 700));
            var dividers = grid.Children.OfType<Thumb>().ToArray();
            Check(dividers.Length == (layout.Id == 4 ? 3 : (layout.Columns - 1) + (layout.Rows - 1)), "divider count");
            Check(panes.All(pane => ReferenceEquals(VisualTreeHelper.GetParent(pane), grid)), "pane parent identity");
            for (var i = 0; i < panes.Length; i++)
            {
                Check(ReferenceEquals(grid.Children[i], panes[i]), "pane object order");
                Check(panes[i].Visibility == (i < layout.Slots.Length ? Visibility.Visible : Visibility.Collapsed), "pane visibility");
            }
            foreach (var divider in dividers)
            {
                Check(!divider.Focusable && !divider.IsTabStop, "no extra Tab stop");
                var dividerRect = new Rect(VisualTreeHelper.GetOffset(divider).X, VisualTreeHelper.GetOffset(divider).Y,
                    divider.ActualWidth, divider.ActualHeight);
                Check(dividerRect.Width > 0 && dividerRect.Height > 0, "nonempty divider geometry");
                foreach (var pane in panes.Take(layout.Slots.Length))
                {
                    var offset = VisualTreeHelper.GetOffset(pane);
                    var bounds = new Rect(offset.X, offset.Y, pane.RenderSize.Width, pane.RenderSize.Height);
                    var overlap = Rect.Intersect(bounds, dividerRect);
                    Check(overlap.IsEmpty || overlap.Width == 0 || overlap.Height == 0, "divider must not cover a native pane");
                }
            }
            var captured = PaneLayoutHost.Capture(grid, layout, expected);
            Check(Math.Abs(captured.Horizontal - expected.Horizontal) < 1e-9 &&
                Math.Abs(captured.Vertical - expected.Vertical) < 1e-9, "independent axis capture");
            Check(captured.Strip == expected.Strip, "all strip weights captured");
            if (layout.Columns == 2 && layout.Id != 4)
                Check(Math.Abs(grid.ColumnDefinitions[0].ActualWidth / (1000 - 5) - 0.3) < 1e-9, "actual horizontal proportion");
            if (layout.Rows == 2 && layout.Id != 4)
                Check(Math.Abs(grid.RowDefinitions[0].ActualHeight / (700 - 5) - 0.65) < 1e-9, "actual vertical proportion");
        }

        var calls = 0;
        LayoutProportions? received = null;
        PaneLayoutHost.Apply(grid, panes, LayoutPreset.Find(2), changed: value => { calls++; received = value; });
        var oldDivider = grid.Children.OfType<GridSplitter>().First();
        grid.ColumnDefinitions[0].Width = new GridLength(0.2, GridUnitType.Star);
        grid.ColumnDefinitions[2].Width = new GridLength(0.8, GridUnitType.Star);
        oldDivider.RaiseEvent(new DragCompletedEventArgs(0, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });
        Check(calls == 0, "cancel does not persist");
        oldDivider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Check(calls == 1 && received == new LayoutProportions(0.2, 0.5), "completed change captures star weights");
        PaneLayoutHost.Apply(grid, panes, LayoutPreset.Find(1));
        oldDivider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Check(calls == 1, "retired divider cannot publish into another layout");

        PaneLayoutHost.Apply(grid, panes, LayoutPreset.Find(2), changed: _ => calls++);
        grid.Measure(new Size(1000, 700));
        grid.Arrange(new Rect(0, 0, 1000, 700));
        var resizing = grid.Children.OfType<GridSplitter>().First();
        resizing.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        resizing.RaiseEvent(new DragDeltaEventArgs(100, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        Check(PaneLayoutHost.Capture(grid, LayoutPreset.Find(2), LayoutProportions.Equal).Horizontal > 0.5,
            "in-process drag changes actual star weights");
        resizing.RaiseEvent(new DragCompletedEventArgs(100, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });
        Check(calls == 1 && PaneLayoutHost.Capture(grid, LayoutPreset.Find(2), LayoutProportions.Equal) == LayoutProportions.Equal,
            "capture-loss cancellation restores pre-drag proportions without saving");

        var narrow = new LayoutProportions(0.05, 0.95);
        PaneLayoutHost.Apply(grid, panes, LayoutPreset.Find(4), narrow);
        grid.Measure(new Size(650, 420));
        grid.Arrange(new Rect(0, 0, 650, 420));
        Check(panes[0].RenderSize.Width >= 120 && panes[2].RenderSize.Height >= 100,
            "minimum pane sizes constrain rendered dimensions");
        Check(PaneLayoutHost.Capture(grid, LayoutPreset.Find(4), narrow) == narrow,
            "window constraints do not replace preferred proportions");
        grid.Measure(new Size(4000, 3000));
        grid.Arrange(new Rect(0, 0, 4000, 3000));
        Check(Math.Abs(panes[0].RenderSize.Width / 3995 - narrow.Horizontal) < 1e-9 &&
            Math.Abs(panes[0].RenderSize.Height / 2995 - narrow.Vertical) < 1e-9,
            "enlarging restores preferred proportions after minimum-size clamp");
        StripChecks(grid, panes);
        QuadLayoutSmoke.Run();
        StoreChecks();
    }

    private static void StripChecks(PaneLayoutGrid grid, UIElement[] panes)
    {
        foreach (var (id, columns, count) in new[] { (9, true, 3), (10, false, 3), (11, true, 4), (12, false, 4) })
        {
            var layout = LayoutPreset.Find(id);
            Check(layout.Rows == (columns ? 1 : count) && layout.Columns == (columns ? count : 1), "strip orientation");
            // Existing two-row minimum reserves 215 DIPs for frame/chrome.
            // Additional rows must not clip below that same available budget.
            var minimumContentHeight = PaneLayoutHost.MinimumWindowHeight(layout) - 215;
            PaneLayoutHost.Apply(grid, panes, layout);
            grid.Measure(new Size(650, minimumContentHeight)); grid.Arrange(new Rect(0, 0, 650, minimumContentHeight));
            var lastOffset = VisualTreeHelper.GetOffset(panes[count - 1]);
            Check(lastOffset.Y + panes[count - 1].RenderSize.Height <= minimumContentHeight + 1e-8,
                "last strip pane fits at the supported minimum height");
            PaneLayoutHost.Apply(grid, panes, layout);
            grid.Measure(new Size(1600, 1200)); grid.Arrange(new Rect(0, 0, 1600, 1200));
            var length = ((columns ? 1600 : 1200) - (count - 1) * 5d) / count;
            for (var i = 0; i < count; i++)
            {
                var offset = VisualTreeHelper.GetOffset(panes[i]);
                Check(Math.Abs((columns ? offset.X : offset.Y) - i * (length + 5)) < 1e-8 &&
                    Math.Abs((columns ? panes[i].RenderSize.Width : panes[i].RenderSize.Height) - length) < 1e-8,
                    "default stripes have independent equal geometry in pane order");
            }
            for (var boundary = 0; boundary < count - 1; boundary++)
            {
                var original = new LayoutProportions(0.5, 0.5, new(2, 3, 4, count == 4 ? 5 : 0));
                LayoutProportions? saved = null;
                PaneLayoutHost.Apply(grid, panes, layout, original, value => saved = value);
                grid.Measure(new Size(1600, 1200)); grid.Arrange(new Rect(0, 0, 1600, 1200));
                var divider = grid.Children.OfType<GridSplitter>().ElementAt(boundary);
                divider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                divider.RaiseEvent(new DragDeltaEventArgs(columns ? 20 : 0, columns ? 0 : 20) { RoutedEvent = Thumb.DragDeltaEvent });
                var changed = PaneLayoutHost.Capture(grid, layout, original);
                Check(changed != original && changed.IsValidFor(layout), "every strip divider changes valid weights");
                divider.RaiseEvent(new DragCompletedEventArgs(0, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });
                Check(saved is null && PaneLayoutHost.Capture(grid, layout, original) == original, "strip cancel restores all original weights");
                divider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                divider.RaiseEvent(new DragDeltaEventArgs(columns ? 20 : 0, columns ? 0 : 20) { RoutedEvent = Thumb.DragDeltaEvent });
                divider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                Check(saved is not null && saved == PaneLayoutHost.Capture(grid, layout, original), "strip completion publishes all weights");
            }
        }
    }

    private static void StoreChecks()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "profile-smoke", "layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "LayoutProportions.ini");
        var store = new LayoutProportionsStore(path);
        Check(store.Load().Count == 0 && store.LoadWarning is null && !File.Exists(path), "missing profile uses equal defaults without writing");
        var sizes = new Dictionary<int, LayoutProportions> { [2] = new(0.37, 0.5), [4] = new(0.63, 0.29), [7] = new(0.42, 0.57), [8] = new(0.61, 0.31) };
        foreach (var id in new[] { 9, 10, 11, 12 }) sizes[id] = new(0.5, 0.5, new(2, 3, 4, id >= 11 ? 5 : 0));
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); store.Save(sizes); }
        finally { CultureInfo.CurrentCulture = culture; }
        var loaded = new LayoutProportionsStore(path).Load();
        Check(loaded.Count == 8 && sizes.All(pair => loaded[pair.Key] == pair.Value), "per-layout culture-invariant restart round trip");
        Check(File.ReadAllText(path).Contains("FormatVersion=4"), "new layout IDs require a protected proportion format");
        File.AppendAllText(path, "; external change\n");
        var external = File.ReadAllText(path);
        try { store.Save(sizes); throw new Exception("External edit was overwritten."); }
        catch (IOException) { Check(File.ReadAllText(path) == external, "external edit retained"); }

        var old = "[General]\nFormatVersion=1\n[Layout2]\nHorizontal=0.4\nVertical=0.5\n";
        File.WriteAllText(path, old);
        var upgrading = new LayoutProportionsStore(path);
        var oldValues = upgrading.Load();
        Check(upgrading.LoadWarning is null && oldValues[2] == new LayoutProportions(0.4, 0.5), "v1 proportions load cleanly");
        upgrading.Save(oldValues);
        upgrading.Save(oldValues);
        var upgrades = Directory.GetFiles(root, "*.before-v4-*.bak");
        Check(upgrades.Length == 1 && File.ReadAllText(upgrades[0]) == old, "v1 upgrade preserves one exact snapshot");
        var v2 = old.Replace("FormatVersion=1", "FormatVersion=2");
        File.WriteAllText(path, v2);
        var v2Store = new LayoutProportionsStore(path);
        var v2Values = v2Store.Load();
        Check(v2Store.LoadWarning is null && v2Values[2] == new LayoutProportions(0.4, 0.5), "v2 clean migration");
        v2Store.Save(v2Values); v2Store.Save(v2Values);
        Check(Directory.GetFiles(root, "*.before-v4-*.bak").Count(file => File.ReadAllText(file) == v2) == 1, "one exact v2 backup");
        foreach (var invalid in new[] { "1,2", "1,2,0", "1,2,-3", "1,2,NaN", "1,2,3,4", "1e308,1e308,1" })
        {
            var badStrip = "[General]\nFormatVersion=3\n[Layout9]\nWeights=" + invalid + "\n";
            File.WriteAllText(path, badStrip);
            var badStore = new LayoutProportionsStore(path);
            Check(badStore.Load().Count == 0 && badStore.LoadWarning is not null, "bad strip weights rejected and backed up");
        }

        var future = "[General]\nFormatVersion=1\nFormatVersion=999999999999999999999999\n";
        File.WriteAllText(path, future);
        var futureStore = new LayoutProportionsStore(path);
        Check(futureStore.Load().Count == 0 && futureStore.LoadWarning is not null, "future version rejected even in duplicate field");
        try { futureStore.Save(sizes); throw new Exception("Future file was overwritten."); }
        catch (IOException) { Check(File.ReadAllText(path) == future, "future file retained"); }
        var damaged = "[General]\nFormatVersion=1\n[Layout2]\nHorizontal=0.4\nVertical=0.5\n[Layout4]\nHorizontal=NaN\nVertical=1.1\n";
        File.WriteAllText(path, damaged);
        var damagedStore = new LayoutProportionsStore(path);
        var partial = damagedStore.Load();
        Check(partial.Count == 1 && partial[2] == new LayoutProportions(0.4, 0.5) && damagedStore.LoadWarning is not null, "invalid layout falls back without losing valid layout");
        Check(Directory.GetFiles(root, "*.bak").Any(file => File.ReadAllText(file) == damaged), "exact damaged file backed up");
        damagedStore.Save(partial);
        Check(new LayoutProportionsStore(path).Load().Count == 1, "repair saves readable layouts");
        var valid = File.ReadAllText(path);
        try { damagedStore.Save(new Dictionary<int, LayoutProportions> { [4] = new(double.PositiveInfinity, 0.5) }); throw new Exception("Invalid value saved."); }
        catch (ArgumentException) { Check(File.ReadAllText(path) == valid, "invalid save leaves profile intact"); }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception("Layout proportions: " + label);
    }
}
