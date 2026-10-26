using System.IO;
using System.Reflection;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ProgramStarterStoreSmoke
{
    public static void Run(string root)
    {
        var assembly = typeof(UiPreferencesStore).Assembly;
        var storeType = assembly.GetType("ExplorerDirs.App.ProgramStarterStore", throwOnError: false)
            ?? throw new Exception("ProgramStarterStore must persist the independent portable ProgramStarter.ini.");
        var entryType = assembly.GetType("ExplorerDirs.App.ProgramStarterEntry", throwOnError: false)
            ?? throw new Exception("ProgramStarterEntry must provide stable IDs for configured commands.");
        var storePath = Path.Combine(root, "ProgramStarter.ini");
        Directory.CreateDirectory(root);
        try
        {
            var store = Activator.CreateInstance(storeType, storePath)
                ?? throw new Exception("ProgramStarterStore constructor returned null.");
            var load = storeType.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new Exception("ProgramStarterStore.Load is required.");
            var save = storeType.GetMethod("Save", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new Exception("ProgramStarterStore.Save is required.");
            var warning = storeType.GetProperty("LoadWarning", BindingFlags.Instance | BindingFlags.Public);
            Check(((System.Collections.IEnumerable)load.Invoke(store, null)!).Cast<object>().Count() == 0,
                "a missing ProgramStarter.ini loads as an empty command list");

            var entry = Activator.CreateInstance(entryType, Guid.NewGuid(), "工具", @"C:\工具\viewer.exe",
                "\"%curdir%\"", @"C:\工具") ?? throw new Exception("ProgramStarterEntry constructor returned null.");
            var entries = Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType))!;
            entries.GetType().GetMethod("Add")!.Invoke(entries, [entry]);
            var duplicateName = Activator.CreateInstance(entryType, Guid.NewGuid(), "工具", @"C:\工具\viewer2.exe",
                "", @"C:\工具") ?? throw new Exception("ProgramStarterEntry constructor returned null.");
            entries.GetType().GetMethod("Add")!.Invoke(entries, [duplicateName]);
            save.Invoke(store, [entries]);
            var savedText = File.ReadAllText(storePath);
            Check(savedText.Contains("[ProgramStarter]", StringComparison.Ordinal) &&
                  savedText.Contains("FormatVersion=1", StringComparison.Ordinal) &&
                  savedText.Contains("Item0=", StringComparison.Ordinal) &&
                  savedText.Contains("Item1=", StringComparison.Ordinal),
                "ProgramStarter.ini stores versioned records independently");
            var loaded = ((System.Collections.IEnumerable)load.Invoke(store, null)!).Cast<object>().ToArray();
            var loadedIds = loaded.Select(item => entryType.GetProperty("Id")!.GetValue(item)).ToArray();
            Check(loaded.Length == 2 && loadedIds.Distinct().Count() == 2 &&
                  loaded.Select(item => entryType.GetProperty("Name")!.GetValue(item)).All(name => name!.Equals("工具")),
                "duplicate command names keep distinct IDs and Unicode values through an INI round-trip");

            var external = savedText + "; external edit" + Environment.NewLine;
            File.WriteAllText(storePath, external);
            var conflict = false;
            try { save.Invoke(store, [entries]); }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException) { conflict = true; }
            Check(conflict && File.ReadAllText(storePath) == external,
                "external edits are preserved instead of overwritten");

            var future = savedText.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal);
            File.WriteAllText(storePath, future);
            _ = load.Invoke(store, null);
            var futureRejected = false;
            try { save.Invoke(store, [entries]); }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException) { futureRejected = true; }
            Check(futureRejected && File.ReadAllText(storePath) == future && warning?.GetValue(store) is not null,
                "a future format warns and refuses writes without changing the source");

            File.WriteAllText(storePath, "[ProgramStarter]\nFormatVersion=1\nCount=1\nItem0=broken\n");
            _ = load.Invoke(store, null);
            Check(Directory.GetFiles(root, "ProgramStarter.ini.invalid-*.bak").Length == 1,
                "malformed configuration is backed up before recovery");
            Console.WriteLine("Program Starter store smoke: PASS");
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            var expectedParent = Path.GetFullPath(AppContext.BaseDirectory);
            if (Path.GetDirectoryName(resolved) == Path.TrimEndingDirectorySeparator(expectedParent))
                Directory.Delete(resolved, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Program Starter store: " + message);
        Console.WriteLine("PASS " + message);
    }
}
