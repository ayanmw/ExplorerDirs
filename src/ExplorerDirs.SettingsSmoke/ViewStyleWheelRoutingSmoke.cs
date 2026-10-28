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
        var browserCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));

        Check(paneMarkup.Contains("PreviewMouseWheel=\"RootBorder_PreviewMouseWheel\"", StringComparison.Ordinal),
            "pane WPF chrome routes preview wheel input");
        Check(paneCode.Contains("(Keyboard.Modifiers & ModifierKeys.Control) == 0", StringComparison.Ordinal) &&
              paneCode.Contains("CycleViewStyle(e.Delta)", StringComparison.Ordinal) &&
              paneCode.Contains("state.Browser.AccumulateCtrlWheel(delta)", StringComparison.Ordinal) &&
              paneCode.Contains("e.Handled = true", StringComparison.Ordinal),
            "WPF chrome uses the browser's shared accumulator and consumes partial or successful changes");
        Check(browserCode.Contains("_ctrlWheelAccumulator.Add(delta, _viewReadiness.Generation)", StringComparison.Ordinal) &&
              paneCode.Contains("browser.CtrlWheelHandler = style => state.Owner.Current == state && state.Owner.ApplyCompletedViewStyleDelta(state, style);",
                StringComparison.Ordinal),
            "native view and WPF chrome share one accumulator without reaccumulating completed notches");

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
