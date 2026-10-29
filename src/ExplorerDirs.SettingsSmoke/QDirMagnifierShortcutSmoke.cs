using System.IO;
using ExplorerDirs.App;

internal static class QDirMagnifierShortcutSmoke
{
    internal static void Run()
    {
        Check(KeyboardShortcuts.Resolve(0x4c, 1) == ExplorerShortcut.ScreenMagnifier,
            "Q-Dir Alt+L resolves to the Windows screen magnifier command");
        Check(KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.ScreenMagnifier),
            "Alt+L is not intercepted while text input has focus");
        Check(KeyboardShortcuts.Resolve(0x4c, 5) == ExplorerShortcut.None,
            "Alt+Shift+L remains available to Windows and Shell");
        Check(KeyboardShortcuts.Resolve(0x4c, 3) == ExplorerShortcut.NavigateDriveL,
            "Ctrl+Alt+L retains the separate Q-Dir drive-L shortcut");
        Check(!KeyboardShortcuts.ShouldYieldDriveLToMagnifier(ExplorerShortcut.NavigateDriveL, false) &&
              KeyboardShortcuts.ShouldYieldDriveLToMagnifier(ExplorerShortcut.NavigateDriveL, true) &&
              !KeyboardShortcuts.ShouldYieldDriveLToMagnifier(ExplorerShortcut.NavigateDriveC, true),
            "the Windows Magnifier lens shortcut takes precedence over drive L while Magnifier is running");

        var start = WindowsMagnifierLauncher.CreateStartInfo();
        Check(string.Equals(start.FileName,
                  Path.Combine(Environment.SystemDirectory, "Magnify.exe"),
                  StringComparison.OrdinalIgnoreCase) && start.UseShellExecute && File.Exists(start.FileName),
            "installed Windows Magnifier launches from the system directory through Shell execution");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
