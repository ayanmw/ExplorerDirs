using System.IO;
using System.Windows;
using System.Windows.Controls;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class TabStripVisibilitySmoke
{
    internal static void Run(string root)
    {
        Check(TabStripVisibilityPolicy.ShouldShow(alwaysShow: true, tabCount: 1),
            "always-show mode keeps a single tab visible");
        Check(TabStripVisibilityPolicy.ShouldShow(alwaysShow: false, tabCount: 1) == false,
            "multiple-tabs mode hides the strip for one tab");
        Check(TabStripVisibilityPolicy.ShouldShow(alwaysShow: false, tabCount: 2),
            "multiple-tabs mode shows the strip at two tabs");
        Check(TabStripVisibilityPolicy.ShouldShow(alwaysShow: false, tabCount: 0) == false,
            "an empty pane does not show a tab strip");

        var headerSizes = new[] { new Size(40, 10), new Size(35, 10), new Size(50, 10) };
        var singleLine = TabHeaderLayout.Calculate(headerSizes, availableWidth: 80, multiline: false);
        Check(singleLine.DesiredSize == new Size(125, 10) &&
              singleLine.Items[0].X == 0 && singleLine.Items[1].X == 40 && singleLine.Items[2].X == 75,
            "single-line tabs preserve their horizontal order and scrollable extent");
        var multiline = TabHeaderLayout.Calculate(headerSizes, availableWidth: 80, multiline: true);
        Check(multiline.DesiredSize == new Size(75, 20) &&
              multiline.Items[0].X == 0 && multiline.Items[1].X == 40 &&
              multiline.Items[2].X == 0 && multiline.Items[2].Y == 10,
            "multiline tabs wrap in displayed order and report two row heights");

        var path = Path.Combine(root, "TabStripVisibility.ini");
        var store = new TabStripVisibilityStore(path);
        Check(store.Load(), "missing tab-strip preference preserves the existing always-visible default");
        store.Save(false);
        Check(!new TabStripVisibilityStore(path).Load(), "multiple-tabs preference round-trips through portable INI");

        var multilinePath = Path.Combine(root, "MultilineTabHeaders.ini");
        var multilineStore = new MultilineTabHeadersStore(multilinePath);
        Check(!multilineStore.Load(), "multiline tab headers are off by default");
        multilineStore.Save(true);
        Check(new MultilineTabHeadersStore(multilinePath).Load(),
            "multiline tab-header preference round-trips through its independent portable INI");
        multilineStore = new MultilineTabHeadersStore(multilinePath);
        multilineStore.Load();
        multilineStore.Save(false);
        Check(!new MultilineTabHeadersStore(multilinePath).Load(),
            "multiline tab-header preference can be disabled again");

        var maximumWidthPath = Path.Combine(root, "TabHeaderMaximumWidth.ini");
        var maximumWidthStore = new TabHeaderMaximumWidthStore(maximumWidthPath);
        Check(maximumWidthStore.Load(), "tab-header maximum width remains enabled by default");
        maximumWidthStore.Save(false);
        Check(!new TabHeaderMaximumWidthStore(maximumWidthPath).Load(),
            "unlimited tab-header width round-trips through an independent portable INI");
        Check(TabHeaderWidthPolicy.MeasureConstraint(limitMaximumWidth: true) == 180 &&
              double.IsPositiveInfinity(TabHeaderWidthPolicy.MeasureConstraint(limitMaximumWidth: false)),
            "Q-Dir-style maximum width toggle selects the existing 180 DIP cap or full folder-name width");

        Exception? measureFailure = null;
        var measureThread = new Thread(() =>
        {
            try
            {
                var header = new TextBlock { Text = new string('X', 300) };
                var panel = new TabHeaderPanel();
                panel.Children.Add(header);
                panel.Measure(new Size(double.PositiveInfinity, 24));
                Check(header.DesiredSize.Width <= TabHeaderWidthPolicy.MaximumWidth,
                    "tab-header panel applies its configured maximum width during WPF measure");
                panel.LimitMaximumWidth = false;
                panel.Measure(new Size(double.PositiveInfinity, 24));
                Check(header.DesiredSize.Width > TabHeaderWidthPolicy.MaximumWidth,
                    "unlimited tab-header mode lets long titles measure to their natural width");
            }
            catch (Exception ex)
            {
                measureFailure = ex;
            }
        });
        measureThread.SetApartmentState(ApartmentState.STA);
        measureThread.Start();
        measureThread.Join();
        if (measureFailure is not null) throw measureFailure;

        Check(TabCloseButtonPolicy.IsVisible(showCloseButton: true, isPinned: false, tabCount: 2) &&
              !TabCloseButtonPolicy.IsVisible(showCloseButton: false, isPinned: false, tabCount: 2) &&
              !TabCloseButtonPolicy.IsVisible(showCloseButton: true, isPinned: true, tabCount: 2) &&
              !TabCloseButtonPolicy.IsVisible(showCloseButton: true, isPinned: false, tabCount: 1),
            "tab close button is shown only when enabled and the tab can actually close");
        var closeButtonPath = Path.Combine(root, "TabCloseButton.ini");
        var closeButtonStore = new TabCloseButtonStore(closeButtonPath);
        Check(!closeButtonStore.Load(), "tab close buttons remain hidden by default");
        closeButtonStore.Save(true);
        Check(new TabCloseButtonStore(closeButtonPath).Load(),
            "tab close-button preference round-trips through an independent portable INI");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Tab strip visibility smoke failed: {message}");
    }
}
