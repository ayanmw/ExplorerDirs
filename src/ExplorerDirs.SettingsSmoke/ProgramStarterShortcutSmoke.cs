using System.Reflection;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ProgramStarterShortcutSmoke
{
    public static void Run()
    {
        var assembly = typeof(UiPreferencesStore).Assembly;
        var shortcutType = assembly.GetType("ExplorerDirs.App.ExplorerShortcut")!;
        var shortcuts = assembly.GetType("ExplorerDirs.App.KeyboardShortcuts")!;
        var resolve = shortcuts.GetMethod("Resolve", BindingFlags.Static | BindingFlags.Public)!;
        Check(resolve.Invoke(null, [0x53, 2])!.ToString() == "ProgramStarter",
            "Ctrl+S opens Program Starter");
        Check(resolve.Invoke(null, [0x53, 6])!.ToString() != "ProgramStarter",
            "Ctrl+Shift+S is not consumed by Program Starter");
        var focusGuard = shortcuts.GetMethod("RequiresFileViewFocus", BindingFlags.Static | BindingFlags.Public)!;
        Check((bool)focusGuard.Invoke(null, [Enum.Parse(shortcutType, "ProgramStarter")])!,
            "Program Starter does not steal Ctrl+S from text entry");
        Console.WriteLine("Program Starter shortcut smoke: PASS");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Program Starter shortcut: " + message);
        Console.WriteLine("PASS " + message);
    }
}
