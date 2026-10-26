using System.IO;
using System.Windows;
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
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Tab strip visibility smoke failed: {message}");
    }
}
