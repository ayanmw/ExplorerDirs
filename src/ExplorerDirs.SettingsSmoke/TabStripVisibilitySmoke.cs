using System.IO;
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
