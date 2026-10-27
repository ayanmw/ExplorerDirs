using System.IO;
using System.Windows.Input;

namespace ExplorerDirs.SettingsSmoke;

internal static class ViewStyleWheelRoutingSmoke
{
    internal static void Run()
    {
        var repoRoot = FindRepoRoot();
        var paneMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));

        Check(paneMarkup.Contains("PreviewMouseWheel=\"RootBorder_PreviewMouseWheel\"", StringComparison.Ordinal),
            "pane WPF chrome routes preview wheel input");
        Check(paneCode.Contains("(Keyboard.Modifiers & ModifierKeys.Control) == 0", StringComparison.Ordinal) &&
              paneCode.Contains("CycleViewStyle(e.Delta)", StringComparison.Ordinal) &&
              paneCode.Contains("ViewStyleWheelDispatch.TryApplyAccumulated(state.ChromeWheelAccumulator", StringComparison.Ordinal) &&
              paneCode.Contains("e.Handled = true", StringComparison.Ordinal),
            "Ctrl+wheel uses a per-tab accumulator and consumes only partial or successful changes");
        Check(paneCode.Contains("browser.CtrlWheelHandler = style => state.Owner.Current == state && state.Owner.CycleViewStyle(style);",
                StringComparison.Ordinal),
            "a delayed wheel callback from an inactive tab cannot change its active sibling");

        Console.WriteLine("View style wheel routing smoke: PASS");

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "PaneControl.xaml")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("View style wheel routing smoke: " + label);
            Console.WriteLine("PASS " + label);
        }
    }
}
