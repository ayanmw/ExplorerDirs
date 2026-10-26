using System.Windows;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class FullScreenPresentationSmoke
{
    internal static void Run()
    {
        var original = new FullScreenPresentation(
            WindowState.Maximized, WindowStyle.SingleBorderWindow, ResizeMode.CanResize,
            false, 120, 80, 1280, 760,
            Visibility.Visible, Visibility.Visible, Visibility.Collapsed);

        var fullScreen = original.EnterFullScreen();
        Check(fullScreen.WindowState == WindowState.Normal &&
              fullScreen.WindowStyle == WindowStyle.None &&
              fullScreen.ResizeMode == ResizeMode.NoResize,
            "full-screen presentation removes the frame and resize handles");
        Check(fullScreen.MenuVisibility == Visibility.Collapsed &&
              fullScreen.ToolBarVisibility == Visibility.Collapsed &&
              fullScreen.StatusBarVisibility == Visibility.Collapsed,
            "full-screen presentation hides the app chrome");
        Check(fullScreen.Left == original.Left && fullScreen.Top == original.Top &&
              fullScreen.Width == original.Width && fullScreen.Height == original.Height &&
              fullScreen.Topmost == original.Topmost,
            "full-screen transition preserves the saved normal bounds and topmost state");

        Console.WriteLine("Full-screen presentation smoke: PASS");

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Full-screen presentation smoke: " + label);
            Console.WriteLine("PASS " + label);
        }
    }
}
