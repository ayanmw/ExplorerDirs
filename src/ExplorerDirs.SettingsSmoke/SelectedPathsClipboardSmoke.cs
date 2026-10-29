using ExplorerDirs.App;
using System.IO;

internal static class SelectedPathsClipboardSmoke
{
    internal static void Run()
    {
        Check(SelectedPathsClipboardText.TryFormat([@"C:\work\one.txt"], out var single) &&
              single == @"C:\work\one.txt",
            "copying one selected path preserves the exact full path without added quoting");
        Check(SelectedPathsClipboardText.TryFormat([@"C:\work\one.txt", @"D:\data\two folder"], out var multiple) &&
              multiple == string.Join(Environment.NewLine, @"C:\work\one.txt", @"D:\data\two folder"),
            "multiple selected full paths are separated by newlines");
        Check(!SelectedPathsClipboardText.TryFormat([], out _) &&
              !SelectedPathsClipboardText.TryFormat([@"C:\work\one.txt", "::{virtual-item}"], out _),
            "empty or mixed virtual selections are rejected instead of copying partial paths");

        var shortcut = KeyboardShortcuts.Resolve(0x43, 6);
        Check(shortcut != ExplorerShortcut.None && KeyboardShortcuts.RequiresFileViewFocus(shortcut) &&
              KeyboardShortcuts.ShouldYieldToTextInput(shortcut),
            "Ctrl+Shift+C is reserved for selected paths in a focused file view and yields to text input");

        var repoRoot = FindRepoRoot();
        var mainWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        Check(mainWindow.Contains("shortcut == ExplorerShortcut.CopySelectedPaths && !source.HasFileViewFocus", StringComparison.Ordinal) &&
              mainWindow.Contains("case ExplorerShortcut.CopySelectedPaths:", StringComparison.Ordinal) &&
              mainWindow.Contains("CopySelectedPaths(source);", StringComparison.Ordinal) &&
              mainWindow.Contains("InputGestureText = \"Ctrl+Shift+C\"", StringComparison.Ordinal),
            "the shortcut dispatches to the existing path copy action only from the file view and is shown in Edit");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "MainWindow.xaml")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }
}
