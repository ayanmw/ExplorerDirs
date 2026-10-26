using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class MouseWheelFocusSmoke
{
    internal static void Run()
    {
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

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
