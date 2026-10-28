using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class WindowTitlePreferenceSmoke
{
    internal static void Run(string root)
    {
        try
        {
            Check(WindowTitlePresentation.Format("Explorer Dirs · Preview 0.6.0", WindowTitleMode.None,
                @"C:\Users\marvin", null) == "Explorer Dirs · Preview 0.6.0", "none keeps app identity");
            Check(WindowTitlePresentation.Format("Explorer Dirs · Preview 0.6.0", WindowTitleMode.FullPath,
                @"C:\Users\marvin", null) == @"Explorer Dirs · Preview 0.6.0 — C:\Users\marvin", "full path mode");
            Check(WindowTitlePresentation.Format("Explorer Dirs", WindowTitleMode.FolderName,
                @"C:\Users\marvin\", null) == "Explorer Dirs — marvin", "folder name mode trims trailing separator");
            Check(WindowTitlePresentation.Format("Explorer Dirs", WindowTitleMode.FolderName,
                @"C:\", null) == @"Explorer Dirs — C:\", "root folder name remains the drive root");
            Check(WindowTitlePresentation.Format("Explorer Dirs", WindowTitleMode.SelectedItemPath,
                @"C:\Users\marvin", null) == "Explorer Dirs", "no unique selection omits suffix");
            Check(WindowTitlePresentation.Format("Explorer Dirs", WindowTitleMode.SelectedItemPath,
                @"C:\Users\marvin", @"C:\Users\marvin\file.txt") ==
                @"Explorer Dirs — C:\Users\marvin\file.txt", "selected item path mode");

            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "WindowTitle.ini");
            var preference = new WindowTitlePreferenceStore(path);
            Check(preference.Load() == WindowTitleMode.None, "default is no location suffix");
            preference.Save(WindowTitleMode.SelectedItemPath);
            Check(new WindowTitlePreferenceStore(path).Load() == WindowTitleMode.SelectedItemPath,
                "portable preference roundtrip");
            File.WriteAllText(path, "[WindowTitle]\nFormatVersion=99\nMode=FullPath\n");
            var future = new WindowTitlePreferenceStore(path);
            Check(future.Load() == WindowTitleMode.None && future.LoadWarning is not null,
                "future format falls back and warns");
            try
            {
                future.Save(WindowTitleMode.FullPath);
                throw new Exception("Future-version title preferences must not be overwritten.");
            }
            catch (IOException) { }
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            var expectedParent = Path.GetFullPath(AppContext.BaseDirectory);
            if (Path.GetDirectoryName(resolved) == Path.TrimEndingDirectorySeparator(expectedParent) &&
                Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Failed: {name}");
    }
}
