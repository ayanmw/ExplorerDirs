using System.IO;
using System.Xml.Linq;

internal static class PaneThemeMarkupSmoke
{
    internal static void Run()
    {
        var repoRoot = FindRepoRoot();
        var appResources = XDocument.Load(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "App.xaml"));
        var document = XDocument.Load(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var (target, properties) in new[]
        {
            ("Menu", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush") }),
            ("MenuItem", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush") }),
            ("ContextMenu", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush"), ("BorderBrush", "ExplorerDirs.BorderBrush") }),
            ("ToolBar", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush"), ("BorderBrush", "ExplorerDirs.BorderBrush") }),
            ("ToolBarTray", new[] { ("Background", "ExplorerDirs.WindowBackgroundBrush") }),
            ("TextBox", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush"), ("BorderBrush", "ExplorerDirs.BorderBrush"), ("CaretBrush", "ExplorerDirs.ControlTextBrush"), ("SelectionBrush", "ExplorerDirs.SelectionBackgroundBrush"), ("SelectionTextBrush", "ExplorerDirs.SelectionTextBrush") }),
            ("ComboBox", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush"), ("BorderBrush", "ExplorerDirs.BorderBrush") }),
            ("ComboBoxItem", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush") }),
            ("Button", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush"), ("BorderBrush", "ExplorerDirs.BorderBrush") }),
            ("ToggleButton", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush"), ("BorderBrush", "ExplorerDirs.BorderBrush") }),
            ("CheckBox", new[] { ("Foreground", "ExplorerDirs.ControlTextBrush") }),
            ("RadioButton", new[] { ("Foreground", "ExplorerDirs.ControlTextBrush") }),
            ("ListBox", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush"), ("BorderBrush", "ExplorerDirs.BorderBrush") }),
            ("ListBoxItem", new[] { ("Background", "ExplorerDirs.ControlBackgroundBrush"), ("Foreground", "ExplorerDirs.ControlTextBrush") })
        })
        {
            var surfaceStyle = appResources.Descendants(presentation + "Style")
                .SingleOrDefault(element => (string?)element.Attribute("TargetType") == $"{{x:Type {target}}}");
            Check(surfaceStyle is not null && properties.All(property => surfaceStyle.Elements(presentation + "Setter").Any(setter =>
                    (string?)setter.Attribute("Property") == property.Item1 &&
                    (string?)setter.Attribute("Value") == $"{{DynamicResource {property.Item2}}}")),
                $"{target} surfaces and text use dynamic semantic theme roles");
        }
        var listItemStyle = appResources.Descendants(presentation + "Style")
            .SingleOrDefault(element => (string?)element.Attribute("TargetType") == "{x:Type ListBoxItem}");
        var listSelectedTrigger = listItemStyle?.Elements(presentation + "Style.Triggers")
            .Elements(presentation + "Trigger")
            .SingleOrDefault(trigger => (string?)trigger.Attribute("Property") == "IsSelected" &&
                                        (string?)trigger.Attribute("Value") == "True");
        Check(listSelectedTrigger?.Elements(presentation + "Setter").Any(setter =>
                  (string?)setter.Attribute("Property") == "Background" &&
                  (string?)setter.Attribute("Value") == "{DynamicResource ExplorerDirs.SelectionBackgroundBrush}") == true &&
              listSelectedTrigger.Elements(presentation + "Setter").Any(setter =>
                  (string?)setter.Attribute("Property") == "Foreground" &&
                  (string?)setter.Attribute("Value") == "{DynamicResource ExplorerDirs.SelectionTextBrush}"),
            "selected WPF list items use the contrast-checked selection palette");
        var tabControl = document.Descendants(presentation + "TabControl")
            .Single(element => (string?)element.Attribute(xaml + "Name") == "Tabs");
        var style = tabControl.Element(presentation + "TabControl.ItemContainerStyle")?
            .Element(presentation + "Style");
        if (style is null) throw new InvalidOperationException("Pane theme markup: tab item style is missing.");

        var accentSetter = style.Descendants(presentation + "Setter").SingleOrDefault(setter =>
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Attribute("Value") == "{DynamicResource ExplorerDirs.AccentBrush}");
        var selectedTrigger = style.Descendants(presentation + "Trigger").SingleOrDefault(trigger =>
            (string?)trigger.Attribute("Property") == "IsSelected" &&
            (string?)trigger.Attribute("Value") == "True");
        var selectedAccentSetter = selectedTrigger?.Element(presentation + "Setter");
        Check(accentSetter is not null && selectedAccentSetter is not null &&
              (string?)selectedAccentSetter.Attribute("Property") == "BorderBrush" &&
              (string?)selectedAccentSetter.Attribute("Value") == "{DynamicResource ExplorerDirs.AccentBrush}",
            "selected tab underline follows the dynamic system accent resource");
        Check((string?)accentSetter!.Attribute("Property") == "BorderBrush" &&
              style.Descendants(presentation + "Setter").Any(setter =>
                  (string?)setter.Attribute("Property") == "BorderThickness" &&
                  (string?)setter.Attribute("Value") == "0,0,0,2"),
            "tab indicator uses only a two-DIP bottom border without replacing the Shell view");

        var mainWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        var paneMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var photoPrintWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PhotoPrintWindow.xaml"));
        var programStarterWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ProgramStarterWindow.cs"));
        var windowThemeBinding = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ApplicationThemeWindowBinding.cs"));
        var layoutWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var layoutGridCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneLayoutGrid.cs"));
        var layoutHostCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneLayoutHost.cs"));
        Check(mainWindow.Contains("ExplorerDirs.WindowBackgroundBrush", StringComparison.Ordinal) &&
              mainWindow.Contains("ExplorerDirs.WindowTextBrush", StringComparison.Ordinal) &&
              mainWindow.Contains("ExplorerDirs.MutedTextBrush", StringComparison.Ordinal) &&
              mainWindow.Contains("ExplorerDirs.ControlBackgroundBrush", StringComparison.Ordinal),
            "main window and shared navigation chrome consume semantic theme roles");
        Check(paneMarkup.Contains("ExplorerDirs.BorderBrush", StringComparison.Ordinal) &&
              paneMarkup.Contains("ExplorerDirs.WindowBackgroundBrush", StringComparison.Ordinal) &&
              paneMarkup.Contains("ExplorerDirs.MutedTextBrush", StringComparison.Ordinal) &&
              paneMarkup.Contains("ExplorerDirs.ControlBackgroundBrush", StringComparison.Ordinal),
            "pane address, status, tree, and divider chrome consume semantic theme roles");
        Check(paneMarkup.Contains("x:Name=\"ShellStatusHint\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.AutomationId=\"PaneShellStatusHint\"", StringComparison.Ordinal) &&
              paneCode.Contains("ShellStatusTextChanged", StringComparison.Ordinal) &&
              paneCode.Contains("UpdateShellStatusHint(state.Browser.ShellStatusText)", StringComparison.Ordinal),
            "Shell status text has a separate accessible pane slot with active-tab restoration");
        Check(paneCode.Contains("isActive ? \"ExplorerDirs.AccentBrush\" : \"ExplorerDirs.BorderBrush\"", StringComparison.Ordinal),
            "active pane accent and inactive pane border remain distinct semantic roles");
        Check(photoPrintWindow.Contains("ExplorerDirs.WindowBackgroundBrush", StringComparison.Ordinal) &&
              photoPrintWindow.Contains("ExplorerDirs.BorderBrush", StringComparison.Ordinal),
            "photo print window follows application surface and border roles");
        Check(programStarterWindow.Contains("ApplicationThemeWindowBinding.Attach(this, themeController)", StringComparison.Ordinal) &&
              programStarterWindow.Split("ApplicationThemeWindowBinding.Attach(this, themeController)", StringSplitOptions.None).Length == 4 &&
              layoutWindowCode.Contains("new ProgramStarterWindow(_programStarterStore, _programStarterEntries, _themeController", StringComparison.Ordinal) &&
              windowThemeBinding.Contains("SetResourceReference(Control.BackgroundProperty", StringComparison.Ordinal) &&
              windowThemeBinding.Contains("SetResourceReference(Control.ForegroundProperty", StringComparison.Ordinal) &&
              windowThemeBinding.Contains("themeController.ThemeChanged += themeChanged", StringComparison.Ordinal) &&
              windowThemeBinding.Contains("themeController.ThemeChanged -= themeChanged", StringComparison.Ordinal) &&
              windowThemeBinding.Contains("NativeWindowFrameTheme.Apply", StringComparison.Ordinal),
            "program starter, editor, and import preview windows follow live application theme changes");
        Check(layoutWindowCode.Contains("ApplicationThemeWindowBinding.Attach(dialog, _themeController)", StringComparison.Ordinal),
            "the code-created view-preset dialog follows the live application theme");
        Check(layoutWindowCode.Contains("ExplorerDirs.SelectionBackgroundBrush", StringComparison.Ordinal) &&
              layoutGridCode.Contains("ExplorerDirs.BorderBrush", StringComparison.Ordinal) &&
              layoutHostCode.Contains("ExplorerDirs.BorderBrush", StringComparison.Ordinal),
            "layout selector and pane splitters consume semantic selection and border roles");

        Console.WriteLine("Pane theme markup smoke: PASS");

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "PaneControl.xaml")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Pane theme markup: " + description);
            Console.WriteLine("PASS " + description);
        }
    }
}
