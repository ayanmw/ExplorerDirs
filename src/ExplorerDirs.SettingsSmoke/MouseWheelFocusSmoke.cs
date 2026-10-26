using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class MouseWheelFocusSmoke
{
    internal static void Run()
    {
        CheckPortablePreference();
        Check(MouseWheelFocusRouter.ShouldProcessGetMessage(0, 1),
            "the thread GetMessage hook processes a retrieved message");
        Check(!MouseWheelFocusRouter.ShouldProcessGetMessage(0, 0),
            "the thread GetMessage hook ignores peeks that do not remove a message");
        Check(!MouseWheelFocusRouter.ShouldProcessGetMessage(-1, 1),
            "the thread GetMessage hook ignores negative hook codes");
        var point = MouseWheelFocusRouter.GetScreenPoint(unchecked((nint)0xFFF60014UL));
        Check(point.X == 20 && point.Y == -10,
            "wheel-message coordinates preserve signed positions on multiple monitors");
        Check(MouseWheelFocusRouter.TryRetarget(0x020A, 0, true, 10, 20, true, out var target) && target == 20,
            "enabled focus routing targets the Shell view under the pointer");
        Check(!MouseWheelFocusRouter.TryRetarget(0x020A, 0, false, 10, 20, true, out target) && target == 10,
            "disabled focus routing preserves the original HWND");
        Check(!MouseWheelFocusRouter.TryRetarget(0x020A, 0x0008, true, 10, 20, true, out target) && target == 10,
            "Ctrl+wheel remains available to the view-style shortcut");
        Check(!MouseWheelFocusRouter.TryRetarget(0x020A, 0, true, 10, 20, false, out target) && target == 10,
            "non-Shell wheel targets are left unchanged");
        Check(!MouseWheelFocusRouter.TryRetarget(0x020A, 0, true, 10, 10, true, out target) && target == 10,
            "messages already addressed to the hovered HWND are not retargeted");
        Check(!MouseWheelFocusRouter.TryRetarget(0x020E, 0, true, 10, 20, true, out target) && target == 10,
            "horizontal wheel messages are not changed by vertical focus routing");
        Console.WriteLine("Mouse wheel focus smoke: PASS");
    }

    private static void CheckPortablePreference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.MouseWheelFocus.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "MouseWheelFocus.ini");
        try
        {
            var store = new PortableBooleanPreference(path, "Mouse", "FocusOnWheel", "鼠标滚轮焦点");
            Check(!store.Load(), "mouse-wheel focus defaults off");
            store.Save(true);
            Check(new PortableBooleanPreference(path, "Mouse", "FocusOnWheel", "鼠标滚轮焦点").Load(),
                "mouse-wheel focus persists in its portable INI");
            File.WriteAllText(path, "[Mouse]" + Environment.NewLine +
                "FormatVersion=2" + Environment.NewLine + "FocusOnWheel=true" + Environment.NewLine);
            var future = new PortableBooleanPreference(path, "Mouse", "FocusOnWheel", "鼠标滚轮焦点");
            Check(!future.Load() && future.LoadWarning is not null,
                "a newer mouse-wheel focus preference is protected and defaults off");
            try { future.Save(false); throw new InvalidOperationException("A future preference format was overwritten."); }
            catch (IOException) { }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
