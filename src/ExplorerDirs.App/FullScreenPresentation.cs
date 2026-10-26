using System.Windows;

namespace ExplorerDirs.App;

internal readonly record struct FullScreenPresentation(
    WindowState WindowState,
    WindowStyle WindowStyle,
    ResizeMode ResizeMode,
    bool Topmost,
    double Left,
    double Top,
    double Width,
    double Height,
    Visibility MenuVisibility,
    Visibility ToolBarVisibility,
    Visibility StatusBarVisibility)
{
    public FullScreenPresentation EnterFullScreen() => this with
    {
        WindowState = System.Windows.WindowState.Normal,
        WindowStyle = System.Windows.WindowStyle.None,
        ResizeMode = System.Windows.ResizeMode.NoResize,
        MenuVisibility = Visibility.Collapsed,
        ToolBarVisibility = Visibility.Collapsed,
        StatusBarVisibility = Visibility.Collapsed
    };
}
