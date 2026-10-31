using System.IO;
using System.Reflection;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellFilteredSelectionNavigationSmoke
{
    internal static void Run()
    {
        var hostSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));
        var acceleratorStart = hostSource.IndexOf("protected override bool TranslateAcceleratorCore", StringComparison.Ordinal);
        var shellTranslation = hostSource.IndexOf("((IShellView)viewObject).TranslateAccelerator(pointer)", acceleratorStart,
            StringComparison.Ordinal);
        var filteredNavigation = acceleratorStart >= 0 && shellTranslation > acceleratorStart
            ? hostSource[acceleratorStart..shellTranslation] : string.Empty;
        Check(filteredNavigation.Contains("ShellFilteredSelectionNavigation.ShouldJump", StringComparison.Ordinal) &&
              filteredNavigation.Contains("TryFocusAdjacentSelectedItem(viewObject", StringComparison.Ordinal) &&
              filteredNavigation.Contains("SameFilteredJumpRequest", StringComparison.Ordinal),
            "filtered selection jump is routed only for the current native file-view visit before Shell accelerator handling");

        var policy = typeof(QuickFilterSettings).Assembly.GetType(
            "ExplorerDirs.App.ShellFilteredSelectionNavigation", throwOnError: false)
            ?? throw new InvalidOperationException("Filtered selection navigation policy is not implemented.");
        var resolve = policy.GetMethod("ResolveAdjacentSelectedOrdinal", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Filtered selection navigation policy has no ordinal resolver.");
        var shouldJump = policy.GetMethod("ShouldJump", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Filtered selection navigation policy has no key gate.");

        int? Resolve(int count, int focusedOrdinal, int direction) =>
            (int?)resolve.Invoke(null, [count, focusedOrdinal, direction]);

        Check(Resolve(3, 1, 1) == 2, "Down jumps to the next selected item in view order");
        Check(Resolve(3, 1, -1) == 0, "Up jumps to the previous selected item in view order");
        Check(Resolve(3, 2, 1) is null && Resolve(3, 0, -1) is null,
            "selection navigation does not wrap at either end");
        Check(Resolve(3, -1, 1) is null && Resolve(3, 3, -1) is null,
            "an unselected or invalid focus falls through to native Shell navigation");
        Check(Resolve(1, 0, 1) is null && Resolve(3, 1, 0) is null,
            "single selection and non-directional input keep native Shell behavior");
        Check((bool)shouldJump.Invoke(null, [0x0100, 0x28, 0, true, false, true])! &&
              (bool)shouldJump.Invoke(null, [0x0100, 0x26, 0, true, false, true])!,
            "unmodified Up/Down jump while a filter is active in file content");
        Check(!(bool)shouldJump.Invoke(null, [0x0100, 0x28, 0, false, false, true])! &&
              !(bool)shouldJump.Invoke(null, [0x0100, 0x28, 0, true, true, true])! &&
              !(bool)shouldJump.Invoke(null, [0x0100, 0x28, 2, true, false, true])! &&
              !(bool)shouldJump.Invoke(null, [0x0100, 0x28, 0, true, false, false])!,
            "unfiltered, search, modified and non-content input keep native Shell behavior");
    }

    private static void Check(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException("Filtered selection navigation failed: " + behavior);
        Console.WriteLine("PASS " + behavior);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }
}
