using ExplorerDirs.App;
using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.SettingsSmoke;

internal static class QuickLinkStoreSmoke
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirsQuickLinksSmoke", Guid.NewGuid().ToString("N"));
        var linksDirectory = Path.Combine(root, "快速链接");
        var target = Path.Combine(root, "测试目录");
        Directory.CreateDirectory(target);
        try
        {
            var available = QuickLinkAvailability.Evaluate(() => true);
            Check(available.CanAdd && available.Error is null,
                "a successful Shell target probe retains its availability result");
            var rejected = QuickLinkAvailability.Evaluate(() => false);
            Check(!rejected.CanAdd && rejected.Error is null,
                "an ordinary unavailable target disables the quick-link command without an error");
            var providerFailure = new COMException("Shell namespace provider failed.");
            var failedProbe = QuickLinkAvailability.Evaluate(() => throw providerFailure);
            Check(!failedProbe.CanAdd && ReferenceEquals(failedProbe.Error, providerFailure),
                "a Shell namespace exception is contained and reported as unavailable");

            var store = new QuickLinkStore(linksDirectory);
            Check(store.Load().Count == 0, "missing quick links directory loads empty");

            var blockedDirectory = Path.Combine(root, "QuickLinks-is-a-file");
            File.WriteAllText(blockedDirectory, "not a directory");
            var blockedStore = new QuickLinkStore(blockedDirectory);
            Check(blockedStore.Load().Count == 0 && blockedStore.LoadWarning is not null,
                "a profile file blocking the quick-link directory is reported instead of treated as empty");

            var first = store.AddFolder(target);
            Check(File.Exists(first) && Path.GetExtension(first).Equals(".lnk", StringComparison.OrdinalIgnoreCase),
                "folder link is a standard Shell shortcut");
            var entries = store.Load();
            Check(entries.Count == 1 && entries[0].Path == first, "folder shortcut is listed");
            Check(store.TryGetTarget(first, out var resolved) &&
                  string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase),
                "folder shortcut resolves its Unicode target");

            var second = store.AddFolder(target);
            Check(second != first && File.Exists(second) && store.Load().Count == 2,
                "adding a duplicate name preserves both shortcuts");

            var executable = Path.Combine(root, "tool.exe");
            File.WriteAllBytes(executable, []);
            var appShortcut = store.AddTarget(executable);
            Check(store.TryGetTarget(appShortcut, out var appTarget) &&
                  string.Equals(Path.GetFullPath(appTarget), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase),
                "program shortcut target is retained for Shell launch");

            var selectedFileShortcut = store.AddLocation(executable);
            Check(store.TryGetTarget(selectedFileShortcut, out var selectedFileTarget) &&
                  string.Equals(Path.GetFullPath(selectedFileTarget), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase),
                "selected files can be added as Shell quick links");
            var selectedFolderShortcut = store.AddLocation(target);
            Check(store.TryGetTarget(selectedFolderShortcut, out var selectedFolderTarget) &&
                  string.Equals(Path.GetFullPath(selectedFolderTarget), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase),
                "selected folders can be added as Shell quick links");
            ExpectThrows<FileNotFoundException>(() => store.AddLocation(Path.Combine(root, "missing.txt")),
                "missing selected items are rejected");

            var rootShortcut = store.AddFolder(Path.GetPathRoot(root)!);
            Check(File.Exists(rootShortcut) && store.TryGetTarget(rootShortcut, out var rootTarget) &&
                  string.Equals(Path.GetFullPath(rootTarget), Path.GetPathRoot(root)!, StringComparison.OrdinalIgnoreCase),
                "drive-root targets receive a valid shortcut filename and remain resolvable");

            var shellFolderShortcut = store.AddFolderLocation("shell:MyComputerFolder");
            var hasTarget = store.TryGetTarget(shellFolderShortcut, out var shellFolderTarget);
            nint shellFolderPidl = 0;
            var isShellFolder = hasTarget &&
                ShellLocationIdentity.TryGetFolderPidl(shellFolderTarget, out shellFolderPidl);
            if (isShellFolder) Marshal.FreeCoTaskMem(shellFolderPidl);
            Check(isShellFolder, "virtual Shell folder shortcut resolves to a confirmed folder parsing name");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Quick link smoke failed: {name}");
    }

    private static void ExpectThrows<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Quick link smoke failed: {name}");
    }
}
