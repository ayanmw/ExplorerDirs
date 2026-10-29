using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

internal static class ShellViewDropTargetCleanupSmoke
{
    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        nint privateDesktopParent = 0;
        nint interactiveParent = 0;
        nint interactivePane = 0;
        try
        {
            privateDesktopParent = CreateWindowExW(0, "STATIC", "Private Desktop cleanup smoke", 0,
                -10000, -10000, 40, 40, 0, 0, 0, 0);
            interactiveParent = CreateWindowExW(0, "STATIC", "Interactive cleanup smoke", 0,
                -10000, -10000, 40, 40, 0, 0, 0, 0);
            interactivePane = CreateWindowExW(0, "STATIC", "Interactive cleanup pane", 0x40000000,
                0, 0, 20, 20, interactiveParent, 0, 0, 0);
            if (privateDesktopParent == 0 || interactiveParent == 0 || interactivePane == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            ShellViewDropTargetProbe.CleanupParentWindow(privateDesktopParent, interactiveOle: false);
            Check(IsWindow(privateDesktopParent), "Private Desktop top-level windows remain for watchdog cleanup");

            ShellViewDropTargetProbe.CleanupParentWindow(interactiveParent, interactiveOle: true);
            Check(!IsWindow(interactiveParent) && !IsWindow(interactivePane),
                "interactive probe top-level window and pane children are destroyed during cleanup");
            interactiveParent = 0;
            interactivePane = 0;

            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The hidden drop-target cleanup smoke changed the foreground window.");
            Console.WriteLine("shell-view-drop-target-cleanup-smoke=passed; interactive-parent-destroyed=True; private-desktop-parent-retained=True; foreground-unchanged=True");
            return 0;
        }
        finally
        {
            if (interactivePane != 0) _ = DestroyWindow(interactivePane);
            if (interactiveParent != 0) _ = DestroyWindow(interactiveParent);
            if (privateDesktopParent != 0) _ = DestroyWindow(privateDesktopParent);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("shell-view-drop-target-cleanup-smoke: " + message);
        Console.WriteLine("PASS " + message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
