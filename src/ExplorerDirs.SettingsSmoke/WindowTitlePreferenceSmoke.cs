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
            Check(WindowTitlePresentation.Format("Explorer Dirs", WindowTitleMode.RecentFavorite,
                @"C:\Users\marvin", null) == "Explorer Dirs", "recent favorite mode has no suffix before a favorite is opened");
            Check(WindowTitlePresentation.Format("Explorer Dirs", WindowTitleMode.RecentFavorite,
                @"C:\Users\marvin", null, @"D:\Workspaces\server") ==
                @"Explorer Dirs — D:\Workspaces\server", "recent favorite mode shows the explicitly opened favorite path");

            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "WindowTitle.ini");
            var preference = new WindowTitlePreferenceStore(path);
            Check(preference.Load() == WindowTitleMode.None, "default is no location suffix");
            preference.Save(WindowTitleMode.SelectedItemPath);
            Check(new WindowTitlePreferenceStore(path).Load() == WindowTitleMode.SelectedItemPath,
                "portable preference roundtrip");
            preference.Save(WindowTitleMode.RecentFavorite);
            Check(new WindowTitlePreferenceStore(path).Load() == WindowTitleMode.RecentFavorite,
                "recent favorite mode preference roundtrip");

            var firstWindowPath = Path.Combine(root, "window-1.RecentFavorite.ini");
            var secondWindowPath = Path.Combine(root, "window-2.RecentFavorite.ini");
            var firstWindowFavorite = new RecentFavoritePathStore(firstWindowPath);
            var secondWindowFavorite = new RecentFavoritePathStore(secondWindowPath);
            Check(firstWindowFavorite.Load() is null && secondWindowFavorite.Load() is null,
                "recent favorite session starts empty for each window");
            firstWindowFavorite.Save(@"D:\Workspaces\first=workspace");
            secondWindowFavorite.Save(@"D:\Workspaces\second workspace");
            Check(new RecentFavoritePathStore(firstWindowPath).Load() == @"D:\Workspaces\first=workspace" &&
                  new RecentFavoritePathStore(secondWindowPath).Load() == @"D:\Workspaces\second workspace",
                "recent favorite paths roundtrip independently per window session and preserve equals signs");

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
