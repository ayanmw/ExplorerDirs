using ExplorerDirs.App;
using System.IO;

internal static class HiddenItemsPreferenceSmoke
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-HiddenItems-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "HiddenItems.ini");
            var store = new HiddenItemsPreferenceStore(path);
            Check(store.Load() == HiddenItemsPreference.FollowWindows, "missing preference follows Windows by default");
            store.Save(HiddenItemsPreference.Show);
            Check(new HiddenItemsPreferenceStore(path).Load() == HiddenItemsPreference.Show,
                "explicit show preference round-trips through the profile INI");
            var hide = new HiddenItemsPreferenceStore(path);
            _ = hide.Load();
            hide.Save(HiddenItemsPreference.Hide);
            Check(new HiddenItemsPreferenceStore(path).Load() == HiddenItemsPreference.Hide,
                "explicit hide preference round-trips through the profile INI");

            Check(ShellFolderFilter.GetEnumerationFlags(HiddenItemsPreference.FollowWindows, false) == 0x60 &&
                  ShellFolderFilter.GetEnumerationFlags(HiddenItemsPreference.FollowWindows, true) == 0xE0 &&
                  ShellFolderFilter.GetEnumerationFlags(HiddenItemsPreference.Show, false) == 0xE0 &&
                  ShellFolderFilter.GetEnumerationFlags(HiddenItemsPreference.Hide, true) == 0x60,
                "preference selects ordinary hidden enumeration flags without requesting protected system items");
            Check(!ShellFolderFilter.ShouldAttachFilter(false, false, HiddenItemsPreference.FollowWindows) &&
                  ShellFolderFilter.ShouldAttachFilter(true, false, HiddenItemsPreference.FollowWindows) &&
                  ShellFolderFilter.ShouldAttachFilter(false, true, HiddenItemsPreference.Show) &&
                  !ShellFolderFilter.ShouldApplyQuickFilter(true, hasFilter: true) &&
                  ShellFolderFilter.ShouldApplyQuickFilter(false, hasFilter: true),
                "explicit visibility attaches the Shell filter while search ignores stale quick-filter patterns");
            Check(!HiddenItemsPreferencePolicy.ShouldRefreshExistingView(preferenceChanged: true, viewAvailable: false) &&
                  !HiddenItemsPreferencePolicy.ShouldRefreshExistingView(preferenceChanged: false, viewAvailable: true) &&
                  HiddenItemsPreferencePolicy.ShouldRefreshExistingView(preferenceChanged: true, viewAvailable: true),
                "uncreated tab hosts store the preference without attempting an ExplorerBrowser refresh");
            Check(!HiddenItemsPreferencePolicy.ShouldRefreshFollowWindowsChange(HiddenItemsPreference.Show, false, true) &&
                  !HiddenItemsPreferencePolicy.ShouldRefreshFollowWindowsChange(HiddenItemsPreference.FollowWindows, true, true) &&
                  HiddenItemsPreferencePolicy.ShouldRefreshFollowWindowsChange(HiddenItemsPreference.FollowWindows, false, true),
                "external Shell hidden-item changes refresh only a changed Follow Windows preference");

            File.WriteAllText(path, "[HiddenItems]\nFormatVersion=9\nMode=Show\n");
            var newer = new HiddenItemsPreferenceStore(path);
            Check(newer.Load() == HiddenItemsPreference.FollowWindows && newer.LoadWarning is not null,
                "newer preference formats warn and use the safe Windows default");
            ExpectIOException(() => newer.Save(HiddenItemsPreference.Hide), "newer preference is not overwritten");
            Check(File.ReadAllText(path) == "[HiddenItems]\nFormatVersion=9\nMode=Show\n",
                "future-format source remains byte-for-byte unchanged");

            File.WriteAllText(path, "[HiddenItems]\nFormatVersion=1\nMode=not-a-mode\n");
            var malformed = new HiddenItemsPreferenceStore(path);
            Check(malformed.Load() == HiddenItemsPreference.FollowWindows && malformed.LoadWarning is not null &&
                  Directory.GetFiles(root, "HiddenItems.ini.invalid-*.bak").Length == 1,
                "malformed preference is backed up before following Windows");

            var conflict = new HiddenItemsPreferenceStore(Path.Combine(root, "Conflict.ini"));
            _ = conflict.Load();
            File.WriteAllText(conflict.Path, "external edit");
            ExpectIOException(() => conflict.Save(HiddenItemsPreference.Show), "external preference edits are preserved");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void ExpectIOException(Action action, string label)
    {
        try { action(); throw new Exception($"Expected IOException: {label}"); }
        catch (IOException) { Console.WriteLine($"PASS {label}"); }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Hidden items preference: {label}");
        Console.WriteLine($"PASS {label}");
    }
}
