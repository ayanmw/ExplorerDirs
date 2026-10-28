using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using ExplorerDirs.App;

internal static class DefaultViewSmoke
{
    internal static void VerifyOlderReader(string assemblyPath, string root, bool proportionsOnly = false)
    {
        // Load only the old settings type, never its App/window or portable profile.
        var oldAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        if (!proportionsOnly)
        {
        var storeType = oldAssembly.GetType("ExplorerDirs.App.SessionStore", throwOnError: true)!;
        var session = new SessionState(11, 1,
            Enumerable.Range(0, 4).Select(_ => new List<TabSettings>
                { new(@"C:\fixture", ViewState.Default()) }).ToList(), DefaultIconSize: 256);
        var text = SessionStore.Render(session);
        var path = Path.Combine(root, "older-reader-v9-" + Guid.NewGuid().ToString("N") + ".ini");
        File.WriteAllText(path, text);
        var reader = Activator.CreateInstance(storeType, path, path + ".missing-legacy")!;
        Check(storeType.GetMethod("Load")!.Invoke(reader, null) is null &&
            storeType.GetProperty("LoadWarning")!.GetValue(reader) is string,
            "actual older assembly rejects the newer profile");
        var oldText = text.Replace("FormatVersion=11", "FormatVersion=2")
            .Replace("Layout=11", "Layout=4")
            .Replace("DefaultIconSize=256" + Environment.NewLine, "");
        var oldState = storeType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [oldText]) ?? throw new Exception("Old reader could not parse its supported format.");
        try
        {
            storeType.GetMethod("Save")!.Invoke(reader, [oldState]);
            throw new Exception("Old reader overwrote a v9 profile.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
        Check(File.ReadAllText(path) == text, "actual older assembly leaves v9 profile unchanged");
        Console.WriteLine("Older settings reader: v9 rejected; overwrite rejected; fixture unchanged.");
        }
        if (oldAssembly.GetType("ExplorerDirs.App.LayoutProportionsStore") is { } oldProportionsType)
        {
            var proportionsPath = Path.Combine(root, "older-proportions-" + Guid.NewGuid().ToString("N") + ".ini");
            var current = new LayoutProportionsStore(proportionsPath);
            current.Load();
            current.Save(new Dictionary<int, LayoutProportions> { [9] = new(0.5, 0.5, new(2, 3, 4)), [12] = new(0.5, 0.5, new(1, 2, 3, 4)) });
            var exact = File.ReadAllText(proportionsPath);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var oldProportions = oldProportionsType.GetConstructor(flags, null, [typeof(string)], null)!.Invoke([proportionsPath]);
            var recovered = oldProportionsType.GetMethod("Load", flags)!.Invoke(oldProportions, null);
            Check(oldProportionsType.GetProperty("LoadWarning", flags)!.GetValue(oldProportions) is string,
                "actual older proportions reader rejects v4");
            try
            {
                oldProportionsType.GetMethod("Save", flags)!.Invoke(oldProportions, [recovered]);
                throw new Exception("Old proportions reader overwrote v4.");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
            Check(File.ReadAllText(proportionsPath) == exact, "old proportions reader preserves new layout ratios");
            Console.WriteLine("Older proportions reader: v4 rejected; overwrite rejected; fixture unchanged.");
        }
    }

    internal static void Run(string root)
    {
        var folder = Path.Combine(root, "default-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var state = new SessionState(4, 4,
            Enumerable.Range(0, 4).Select(_ => new List<TabSettings>
                { new(@"C:\fixture", ViewState.Default()) }).ToList(), 0, [0, 0, 0, 0]);
        foreach (var style in ViewStyle.All)
        {
            var original = state with { DefaultViewMode = style.Mode, DefaultIconSize = style.IconSize };
            var ini = Path.Combine(folder, $"default-{style.Mode}-{style.IconSize}.ini");
            var writer = new SessionStore(ini, Path.Combine(folder, "no-legacy.json"));
            writer.Load();
            writer.Save(original);
            var reader = new SessionStore(ini, Path.Combine(folder, "no-legacy.json"));
            var loaded = reader.Load() ?? throw new Exception("Default view did not survive reopen.");
            Check(reader.LoadWarning is null && loaded.DefaultViewMode == style.Mode &&
                loaded.DefaultIconSize == style.IconSize, "exact default survives a new store instance");
            var newTab = ViewState.Default(loaded.DefaultViewMode, loaded.DefaultIconSize);
            Check(newTab.Mode == style.Mode && newTab.IconSize == style.IconSize &&
                newTab.SortColumns.Count == 0 && newTab.GroupKey is null,
                "new tab uses exact default without copying unrelated existing-tab settings");
            var imported = QdrImporter.Parse(QdrExporter.Render(original, []), (value, _) => value);
            Check(imported.ExtendedState?.DefaultViewMode == style.Mode &&
                imported.ExtendedState.DefaultIconSize == style.IconSize,
                "workspace extension preserves the exact default");
        }

        var legacy = SessionStore.Render(state).Replace("FormatVersion=11", "FormatVersion=2")
            .Replace("DefaultIconSize=-1" + Environment.NewLine, "");
        var legacyPath = Path.Combine(folder, "old-v2.ini");
        File.WriteAllText(legacyPath, legacy);
        var migrating = new SessionStore(legacyPath, Path.Combine(folder, "no-legacy.json"));
        var old = migrating.Load() ?? throw new Exception("V2 session lost.");
        Check(migrating.LoadWarning is null && old.DefaultIconSize == -1, "v2 keeps native default semantics");
        migrating.Save(old with { DefaultViewMode = 1, DefaultIconSize = 256 });
        migrating.Save(old with { DefaultViewMode = 1, DefaultIconSize = 96 });
        var backups = Directory.GetFiles(folder, "old-v2.ini.before-v11-*.bak");
        Check(backups.Length == 1 && File.ReadAllText(backups[0]) == legacy,
            "v2 upgrade keeps one exact original backup");
        var v3Text = SessionStore.Render(state).Replace("FormatVersion=11", "FormatVersion=3");
        var v3Path = Path.Combine(folder, "old-v3.ini");
        File.WriteAllText(v3Path, v3Text);
        var v3Store = new SessionStore(v3Path, Path.Combine(folder, "no-legacy.json"));
        var v3State = v3Store.Load() ?? throw new Exception("V3 session lost.");
        Check(v3Store.LoadWarning is null, "v3 remains readable without a damage warning");
        v3Store.Save(v3State with { Layout = 7 });
        v3Store.Save(v3State with { Layout = 8 });
        var v3Backups = Directory.GetFiles(folder, "old-v3.ini.before-v11-*.bak");
        Check(v3Backups.Length == 1 && File.ReadAllText(v3Backups[0]) == v3Text &&
            new SessionStore(v3Path, Path.Combine(folder, "no-legacy.json")).Load()?.Layout == 8,
            "v3 upgrade backs up once and restores the new layout on restart");
        Check(SessionStore.ParseComplete(SessionStore.Render(state with { Layout = 7 }).Replace("FormatVersion=11", "FormatVersion=3")) is null,
            "new layout IDs require v4, not a mislabeled v3 snapshot");
        var v4Text = SessionStore.Render(state with { Layout = 8 }).Replace("FormatVersion=11", "FormatVersion=4");
        var v4Path = Path.Combine(folder, "old-v4.ini");
        File.WriteAllText(v4Path, v4Text);
        var v4Store = new SessionStore(v4Path, Path.Combine(folder, "no-legacy.json"));
        var v4State = v4Store.Load() ?? throw new Exception("V4 session lost.");
        Check(v4Store.LoadWarning is null && v4State.Layout == 8, "v4 keeps mirror layout");
        v4Store.Save(v4State with { Layout = 11 }); v4Store.Save(v4State with { Layout = 12 });
        var v4Backups = Directory.GetFiles(folder, "old-v4.ini.before-v11-*.bak");
        Check(v4Backups.Length == 1 && File.ReadAllText(v4Backups[0]) == v4Text &&
            new SessionStore(v4Path, Path.Combine(folder, "no-legacy.json")).Load()?.Layout == 12,
            "v4 upgrade backs up once and restores four rows");
        Check(SessionStore.ParseComplete(SessionStore.Render(state with { Layout = 9 }).Replace("FormatVersion=11", "FormatVersion=4")) is null,
            "strip IDs require v5, not mislabeled v4");
        foreach (var invalid in new[] { "", "0", "-2", "1025", "text" })
        {
            var text = SessionStore.Render(state).Replace("DefaultIconSize=-1", "DefaultIconSize=" + invalid);
            Check(SessionStore.ParseComplete(text) is null &&
                SessionStore.Parse(text)?.Panes[0].Count == 1,
                "damaged default rejects complete snapshot without dropping readable tabs");
        }
        var arbitrary = state with { DefaultViewMode = 1, DefaultIconSize = 72 };
        var preserved = QdrImporter.Parse(QdrExporter.Render(arbitrary, []), (value, _) => value);
        Check(preserved.ExtendedState?.DefaultIconSize == 72, "non-preset default is preserved, not rounded");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Default view smoke: " + message);
    }
}

