using ExplorerDirs.App;

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
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
