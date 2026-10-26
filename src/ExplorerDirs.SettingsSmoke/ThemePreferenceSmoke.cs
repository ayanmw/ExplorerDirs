using System.Reflection;
using System.IO;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ThemePreferenceSmoke
{
    public static void Run(string root)
    {
        var storeType = typeof(UiPreferencesStore).Assembly.GetType(
            "ExplorerDirs.App.ThemePreferenceStore", throwOnError: false);
        if (storeType is null)
            throw new Exception("ThemePreferenceStore must provide a portable System/Light/Dark profile preference.");

        var enumType = typeof(UiPreferencesStore).Assembly.GetType(
            "ExplorerDirs.App.ThemePreference", throwOnError: false);
        if (enumType is null || !enumType.IsEnum)
            throw new Exception("ThemePreference must define the supported preference values.");

        var storePath = Path.Combine(root, "Theme.ini");
        var store = Activator.CreateInstance(storeType, storePath)
            ?? throw new Exception("ThemePreferenceStore constructor returned null.");
        var load = storeType.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new Exception("ThemePreferenceStore.Load is required.");
        var save = storeType.GetMethod("Save", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new Exception("ThemePreferenceStore.Save is required.");
        var saveAccent = storeType.GetMethod("SaveAccentColor", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new Exception("ThemePreferenceStore.SaveAccentColor is required.");
        var customAccent = storeType.GetProperty("CustomAccentColor", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new Exception("ThemePreferenceStore.CustomAccentColor is required.");
        var warning = storeType.GetProperty("LoadWarning", BindingFlags.Instance | BindingFlags.Public);

        object Load() => load.Invoke(store, null) ?? throw new Exception("ThemePreferenceStore.Load returned null.");
        void Save(string value) => save.Invoke(store, [Enum.Parse(enumType, value)]);
        string Value(object value) => value.ToString() ?? "";

        Check(Value(Load()) == "System" && warning?.GetValue(store) is null,
            "missing theme preference defaults to System");
        var customColor = System.Windows.Media.Color.FromRgb(0x2a, 0x7b, 0xc4);
        saveAccent.Invoke(store, [customColor]);
        Check(Value(Load()) == "System" && Equals(customAccent.GetValue(store), customColor) &&
              File.ReadAllText(storePath).Contains("FormatVersion=2", StringComparison.Ordinal) &&
              File.ReadAllText(storePath).Contains("AccentColor=#2A7BC4", StringComparison.Ordinal),
            "custom accent color round-trips in Theme.ini format 2");
        saveAccent.Invoke(store, [null]);
        Check(customAccent.GetValue(store) is null && Value(Load()) == "System",
            "returning to Windows accent clears the custom color without changing theme mode");

        File.WriteAllText(storePath, "[Theme]" + Environment.NewLine +
            "FormatVersion=1" + Environment.NewLine + "Mode=Dark" + Environment.NewLine);
        Check(Value(Load()) == "Dark" && customAccent.GetValue(store) is null,
            "version 1 theme profiles retain their mode and default to Windows accent");
        Save("Dark");
        Check(Value(Load()) == "Dark", "Dark preference round-trips through the profile INI");
        saveAccent.Invoke(store, [customColor]);
        Check(Value(Load()) == "Dark" && Equals(customAccent.GetValue(store), customColor),
            "changing accent preserves the selected Light/Dark/System mode");
        Save("Light");
        Check(Value(Load()) == "Light", "Light preference round-trips through the profile INI");
        Save("System");
        Check(Value(Load()) == "System", "System preference round-trips through the profile INI");

        var validContent = File.ReadAllText(storePath);
        File.AppendAllText(storePath, "; external edit" + Environment.NewLine);
        var externalConflict = false;
        try { Save("Dark"); }
        catch (TargetInvocationException ex) when (ex.InnerException is IOException) { externalConflict = true; }
        Check(externalConflict && File.ReadAllText(storePath).Contains("external edit", StringComparison.Ordinal),
            "external theme preference edits are preserved");

        File.WriteAllText(storePath, validContent.Replace("FormatVersion=2", "FormatVersion=3", StringComparison.Ordinal));
        Check(Value(Load()) == "System" && warning?.GetValue(store) is not null,
            "future theme preference format is protected with a warning");
        var futureFormatProtected = false;
        try { Save("Light"); }
        catch (TargetInvocationException ex) when (ex.InnerException is IOException) { futureFormatProtected = true; }
        Check(futureFormatProtected && File.ReadAllText(storePath).Contains("FormatVersion=3", StringComparison.Ordinal),
            "future theme preference format is not overwritten");

        File.WriteAllText(storePath, "[Theme]" + Environment.NewLine + "FormatVersion=1" + Environment.NewLine + "Mode=Unknown" + Environment.NewLine);
        Check(Value(Load()) == "System" && warning?.GetValue(store) is not null &&
              Directory.GetFiles(root, "Theme.ini.invalid-*.bak").Length == 1,
            "invalid theme preference is backed up before defaulting to System");
        File.WriteAllText(storePath, "[Theme]" + Environment.NewLine +
            "FormatVersion=2" + Environment.NewLine + "Mode=Dark" + Environment.NewLine + "AccentColor=#12GG56" + Environment.NewLine);
        Check(Value(Load()) == "System" && warning?.GetValue(store) is not null &&
              Directory.GetFiles(root, "Theme.ini.invalid-*.bak").Length == 2,
            "invalid custom RGB is backed up and never partially applied");
        CheckCustomAccentUiWiring();
        Console.WriteLine("Theme preference smoke: PASS");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
    }

    private static void CheckCustomAccentUiWiring()
    {
        var repoRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (repoRoot is not null &&
               !(File.Exists(Path.Combine(repoRoot.FullName, "docs", "roadmap.md")) &&
                 File.Exists(Path.Combine(repoRoot.FullName, "src", "ExplorerDirs.App", "ApplicationThemeController.cs"))))
            repoRoot = repoRoot.Parent;
        var root = repoRoot?.FullName ?? throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        var menu = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var dialogMarkup = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "AccentColorDialog.xaml"));
        var dialogCode = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "AccentColorDialog.xaml.cs"));
        if (!(menu.Contains("Click=\"WindowsAccent_Click\"", StringComparison.Ordinal) &&
              menu.Contains("Click=\"CustomAccent_Click\"", StringComparison.Ordinal) &&
              window.Contains("new AccentColorDialog(current) { Owner = this }", StringComparison.Ordinal) &&
              window.Contains("SetCustomAccentColor(selected)", StringComparison.Ordinal) &&
              window.Contains("SetCustomAccentColor(null)", StringComparison.Ordinal)))
            throw new Exception("Theme menu must select custom or live Windows accent through the owned dialog.");
        Console.WriteLine("PASS theme menu selects custom or live Windows accent through the owned dialog");
        if (!(dialogMarkup.Contains("TextChanged=\"ColorText_TextChanged\"", StringComparison.Ordinal) &&
              dialogMarkup.Contains("IsDefault=\"True\"", StringComparison.Ordinal) &&
              dialogMarkup.Contains("IsCancel=\"True\"", StringComparison.Ordinal) &&
              dialogCode.Contains("TryParseColor(ColorText.Text", StringComparison.Ordinal) &&
              dialogCode.Contains("DialogResult = true", StringComparison.Ordinal)))
            throw new Exception("Custom accent dialog must validate input and apply only after confirmation.");
        Console.WriteLine("PASS custom accent dialog previews valid RGB input and applies only after confirmation");
        Console.WriteLine("Custom accent UI smoke: PASS");
    }

    public static void CheckResolution()
    {
        var assembly = typeof(UiPreferencesStore).Assembly;
        var preferenceType = assembly.GetType("ExplorerDirs.App.ThemePreference")
            ?? throw new Exception("ThemePreference must be available for system theme resolution.");
        var paletteType = assembly.GetType("ExplorerDirs.App.ThemePalette", throwOnError: false);
        if (paletteType is null)
            throw new Exception("ThemePalette must resolve System separately from explicit Light/Dark preferences.");
        var resolve = paletteType.GetMethod("Resolve", BindingFlags.Static | BindingFlags.Public)
            ?? throw new Exception("ThemePalette.Resolve is required.");
        var resolvedType = assembly.GetType("ExplorerDirs.App.ResolvedTheme")
            ?? throw new Exception("ResolvedTheme must distinguish the current palette from the stored preference.");
        object Resolve(string preference, bool systemDark) => resolve.Invoke(null,
            [Enum.Parse(preferenceType, preference), systemDark])
            ?? throw new Exception("ThemePalette.Resolve returned null.");

        Check(Resolve("System", true).ToString() == "Dark", "System theme resolves to Dark when Windows prefers dark colors");
        Check(Resolve("System", false).ToString() == "Light", "System theme resolves to Light when Windows prefers light colors");
        Check(Resolve("Light", true).ToString() == "Light", "explicit Light preference ignores the Windows color setting");
        Check(Resolve("Dark", false).ToString() == "Dark", "explicit Dark preference ignores the Windows color setting");
        Check(resolvedType.IsEnum, "resolved theme uses a distinct state from the stored preference");
        var paletteFactory = paletteType.GetMethod("For", BindingFlags.Static | BindingFlags.Public,
            binder: null, types: [resolvedType, typeof(System.Windows.Media.Color)], modifiers: null)
            ?? throw new Exception("ThemePalette.For(theme, accent) is required to derive colors from the system accent.");
        var accent = System.Windows.Media.Color.FromRgb(232, 64, 48);
        var lightColors = paletteFactory.Invoke(null, [Enum.Parse(resolvedType, "Light"), accent])
            ?? throw new Exception("Light accent palette returned null.");
        var darkColors = paletteFactory.Invoke(null, [Enum.Parse(resolvedType, "Dark"), accent])
            ?? throw new Exception("Dark accent palette returned null.");
        var selection = lightColors.GetType().GetProperty("SelectionBackground")
            ?? throw new Exception("ThemeColors.SelectionBackground is required.");
        var accentColor = lightColors.GetType().GetProperty("Accent")
            ?? throw new Exception("ThemeColors.Accent is required.");
        var selectionText = lightColors.GetType().GetProperty("SelectionText")
            ?? throw new Exception("ThemeColors.SelectionText is required.");
        Check(Equals(accentColor.GetValue(lightColors), accent),
            "system accent is retained as a semantic theme color");
        Check(!Equals(selection.GetValue(lightColors), selection.GetValue(ThemePalette.For(ResolvedTheme.Light))),
            "Windows accent changes the light selection palette");
        Check(!Equals(selection.GetValue(darkColors), selection.GetValue(ThemePalette.For(ResolvedTheme.Dark))),
            "Windows accent changes the dark selection palette");
        Check(selectionText.GetValue(lightColors) is System.Windows.Media.Color &&
              selectionText.GetValue(darkColors) is System.Windows.Media.Color,
            "accent selection palettes choose explicit readable foregrounds");
        var classify = paletteType.GetMethod("IsLightForeground", BindingFlags.Static | BindingFlags.Public)
            ?? throw new Exception("ThemePalette.IsLightForeground is required for system theme detection.");
        Check((bool)classify.Invoke(null, [byte.MaxValue, byte.MaxValue, byte.MaxValue])!,
            "light system foreground selects Dark mode");
        Check(!(bool)classify.Invoke(null, [byte.MinValue, byte.MinValue, byte.MinValue])!,
            "dark system foreground selects Light mode");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
    }
    public static void CheckActivePaneAccentWiring()
    {
        var repoRoot = FindRepoRoot();
        var paneMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var windowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        Check(paneMarkup.Contains("x:Name=\"RootBorder\"", StringComparison.Ordinal),
            "pane has a stable root border for active focus indication");
        Check(paneCode.Contains("SetActivePaneVisual", StringComparison.Ordinal) &&
              paneCode.Contains("ExplorerDirs.AccentBrush", StringComparison.Ordinal),
            "pane active state uses the dynamic accent brush");
        Check(windowCode.Contains("UpdateActivePaneVisuals", StringComparison.Ordinal) &&
              windowCode.Contains("SetActivePaneVisual(ReferenceEquals(pane, ActivePane))", StringComparison.Ordinal),
            "active pane changes update every pane visual");

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

        static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("Active pane theme wiring: " + label);
            Console.WriteLine("PASS " + label);
        }
    }

    public static void CheckSemanticBrushRoles()
    {
        var repoRoot = FindRepoRoot();
        var controller = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ApplicationThemeController.cs"));
        var setStart = controller.IndexOf("private void SetThemeResources", StringComparison.Ordinal);
        var clearStart = controller.IndexOf("private void ClearThemeResources", StringComparison.Ordinal);
        Check(setStart >= 0 && clearStart > setStart,
            "theme controller exposes separate resource apply and high-contrast clear paths");

        var apply = controller[setStart..clearStart];
        var clear = controller[clearStart..];
        var roles = new Dictionary<string, (string Color, string SystemFallback)>
        {
            ["ExplorerDirs.WindowBackgroundBrush"] = ("WindowBackground", "WindowBrush"),
            ["ExplorerDirs.WindowTextBrush"] = ("WindowText", "WindowTextBrush"),
            ["ExplorerDirs.ControlBackgroundBrush"] = ("ControlBackground", "ControlBrush"),
            ["ExplorerDirs.ControlTextBrush"] = ("ControlText", "ControlTextBrush"),
            ["ExplorerDirs.BorderBrush"] = ("Border", "ControlDarkBrush"),
            ["ExplorerDirs.MutedTextBrush"] = ("MutedText", "GrayTextBrush"),
            ["ExplorerDirs.AccentBrush"] = ("Accent", "HighlightBrush"),
            ["ExplorerDirs.AccentTextBrush"] = ("AccentText", "HighlightTextBrush"),
            ["ExplorerDirs.SelectionBackgroundBrush"] = ("SelectionBackground", "HighlightBrush"),
            ["ExplorerDirs.SelectionTextBrush"] = ("SelectionText", "HighlightTextBrush")
        };

        foreach (var (role, mapping) in roles)
        {
            Check(apply.Contains($"SetBrush(\"{role}\", colors.{mapping.Color})", StringComparison.Ordinal) &&
                  clear.Contains($"_application.Resources[\"{role}\"] = SystemColors.{mapping.SystemFallback};", StringComparison.Ordinal),
                $"semantic brush {role} follows the palette and falls back to Windows in high contrast");
        }

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "ApplicationThemeController.cs")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("Semantic theme brush wiring: " + label);
            Console.WriteLine("PASS " + label);
        }
    }
}
