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
        var warning = storeType.GetProperty("LoadWarning", BindingFlags.Instance | BindingFlags.Public);

        object Load() => load.Invoke(store, null) ?? throw new Exception("ThemePreferenceStore.Load returned null.");
        void Save(string value) => save.Invoke(store, [Enum.Parse(enumType, value)]);
        string Value(object value) => value.ToString() ?? "";

        Check(Value(Load()) == "System" && warning?.GetValue(store) is null,
            "missing theme preference defaults to System");
        Save("Dark");
        Check(Value(Load()) == "Dark", "Dark preference round-trips through the profile INI");
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

        File.WriteAllText(storePath, validContent.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal));
        Check(Value(Load()) == "System" && warning?.GetValue(store) is not null,
            "future theme preference format is protected with a warning");
        var futureFormatProtected = false;
        try { Save("Light"); }
        catch (TargetInvocationException ex) when (ex.InnerException is IOException) { futureFormatProtected = true; }
        Check(futureFormatProtected && File.ReadAllText(storePath).Contains("FormatVersion=2", StringComparison.Ordinal),
            "future theme preference format is not overwritten");

        File.WriteAllText(storePath, "[Theme]" + Environment.NewLine + "FormatVersion=1" + Environment.NewLine + "Mode=Unknown" + Environment.NewLine);
        Check(Value(Load()) == "System" && warning?.GetValue(store) is not null &&
              Directory.GetFiles(root, "Theme.ini.invalid-*.bak").Length == 1,
            "invalid theme preference is backed up before defaulting to System");
        Console.WriteLine("Theme preference smoke: PASS");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
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
        Check(resolvedType.IsEnum, "resolved theme uses a distinct state from the stored preference");        var paletteFactory = paletteType.GetMethod("For", BindingFlags.Static | BindingFlags.Public,
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
}
