using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace ExplorerDirs.App;

internal static class ApplicationThemeWindowBinding
{
    public static void Attach(Window window, ApplicationThemeController? themeController)
    {
        if (themeController is null) return;

        window.SetResourceReference(Control.BackgroundProperty, "ExplorerDirs.WindowBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "ExplorerDirs.WindowTextBrush");

        var sourceInitialized = false;
        void ApplyFrameTheme()
        {
            if (sourceInitialized)
                NativeWindowFrameTheme.Apply(new WindowInteropHelper(window).Handle, themeController.CurrentColors);
        }

        EventHandler themeChanged = (_, _) => ApplyFrameTheme();
        EventHandler sourceInitializedHandler = (_, _) =>
        {
            sourceInitialized = true;
            ApplyFrameTheme();
        };
        EventHandler closed = (_, _) =>
        {
            themeController.ThemeChanged -= themeChanged;
            window.SourceInitialized -= sourceInitializedHandler;
        };

        themeController.ThemeChanged += themeChanged;
        window.SourceInitialized += sourceInitializedHandler;
        window.Closed += closed;
        ApplyFrameTheme();
    }
}
