using System.IO;
using System.Xml.Linq;

internal static class PaneThemeMarkupSmoke
{
    internal static void Run()
    {
        var repoRoot = FindRepoRoot();
        var document = XDocument.Load(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
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
