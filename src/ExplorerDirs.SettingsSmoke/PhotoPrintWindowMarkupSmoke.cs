using System.IO;
using System.Xml.Linq;

namespace ExplorerDirs.SettingsSmoke;

internal static class PhotoPrintWindowMarkupSmoke
{
    internal static void Run()
    {
        var repoRoot = FindRepoRoot();
        var xamlPath = Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PhotoPrintWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var window = document.Root ?? throw new InvalidOperationException("Photo print window XAML has no root element.");
        Check((string?)window.Attribute("Background") == "{DynamicResource ExplorerDirs.WindowBackgroundBrush}" &&
              (string?)window.Attribute("Foreground") == "{DynamicResource ExplorerDirs.WindowTextBrush}",
            "photo print window follows semantic surface and text colors");
        var rootGrid = document.Root?.Element(presentation + "Grid")
            ?? throw new InvalidOperationException("Photo print window has no root Grid.");
        var rows = rootGrid.Element(presentation + "Grid.RowDefinitions")?
            .Elements(presentation + "RowDefinition").ToArray() ?? [];
        Check(rows.Length == 2,
            "photo preview and action buttons occupy separate root Grid rows");

        var preview = rootGrid.Elements(presentation + "Border").SingleOrDefault(element =>
            (string?)element.Attribute("Grid.Column") == "2");
        var actions = rootGrid.Elements(presentation + "StackPanel").SingleOrDefault(element =>
            (string?)element.Attribute("Grid.Column") == "2");
        Check(preview is not null && (string?)preview.Attribute("Grid.Row") == "0" &&
              actions is not null && (string?)actions.Attribute("Grid.Row") == "1",
            "photo preview and print/cancel actions do not overlap");
        Check(preview is not null &&
              (string?)preview.Attribute("BorderBrush") == "{DynamicResource ExplorerDirs.BorderBrush}" &&
              (string?)preview.Attribute("Background") == "{DynamicResource ExplorerDirs.WindowBackgroundBrush}",
            "photo preview frame uses semantic border and surface colors");
        Check(rootGrid.Descendants(presentation + "TextBlock").Any(element =>
                (string?)element.Attribute(xaml + "Name") == "SettingsError" &&
                (string?)element.Attribute("Foreground") == "{DynamicResource ExplorerDirs.WindowTextBrush}"),
            "photo print errors remain readable through the semantic text role");

        var controls = rootGrid.Descendants().Where(element =>
            element.Name == presentation + "ComboBox" ||
            element.Name == presentation + "TextBox" ||
            element.Name == presentation + "ListBox" ||
            element.Name == presentation + "DocumentViewer");
        foreach (var control in controls)
        {
            Check(control.Attribute("AutomationProperties.Name") is not null ||
                  control.Attribute("AutomationProperties.LabeledBy") is not null,
                $"photo print control {control.Name.LocalName} has an accessible name or label");
        }

        Console.WriteLine("Photo print window markup smoke: PASS");

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "PhotoPrintWindow.xaml")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Photo print window markup: " + label);
        }
    }
}
