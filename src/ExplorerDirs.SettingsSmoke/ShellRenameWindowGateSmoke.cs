using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellRenameWindowGateSmoke
{
    internal static void Run()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var focusGate = source.IndexOf("private bool IsAnyShellRenameEditorFocused()", StringComparison.Ordinal);
        Check(focusGate >= 0, "window exposes a shared native rename-focus guard");

        var windowKeyDown = ExtractMethod(source, "private void Window_PreviewKeyDown(", "private bool ExecuteShortcut(");
        Check(windowKeyDown.Contains("if (IsAnyShellRenameEditorFocused()) return;", StringComparison.Ordinal),
            "window shortcuts defer while any pane owns the native rename editor");

        var commandRefresh = ExtractMethod(source, "private void RefreshFileCommands()", "private void InvokeFileCommand(");
        Check(commandRefresh.Contains("IsAnyShellRenameEditorFocused()", StringComparison.Ordinal),
            "file command status queries defer while any pane owns the native rename editor");

        var gateBody = ExtractMethod(source, "private bool IsAnyShellRenameEditorFocused()", "private bool ExecuteShortcut(");
        Check(gateBody.Contains("_panes.Any(pane => pane.IsShellRenameEditorFocused)", StringComparison.Ordinal),
            "rename focus is queried across all pane Shell hosts");

        Console.WriteLine("Shell rename window gate smoke: PASS");

        static string ExtractMethod(string source, string startMarker, string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = start < 0 ? -1 : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            if (start < 0 || end < 0) throw new InvalidOperationException("Could not locate method boundary: " + startMarker);
            return source[start..end];
        }

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "MainWindow.xaml.cs")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Shell rename window gate smoke: " + label);
            Console.WriteLine("PASS " + label);
        }
    }
}
