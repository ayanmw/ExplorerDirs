using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class WindowSessionRegistrySmoke
{
    public static void Run(string root)
    {
        var folder = Path.Combine(root, "window-sessions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var profile = Path.Combine(folder, "ExplorerDirs.ini");
        var registry = new WindowSessionRegistry(profile);
        var primary = registry.Load();
        Require(primary.SequenceEqual([profile]), "legacy profile starts as the primary window session");

        var stateA = State(@"C:\window-a");
        new SessionStore(profile, Path.Combine(folder, "missing.json")).Save(stateA);
        var second = registry.Create(State(@"C:\window-b"), [new PinnedTabEntry(0, 0, @"C:\window-b")]);
        Require(File.Exists(second) && !string.Equals(second, profile, StringComparison.OrdinalIgnoreCase),
            "new window receives an independent session file");
        Require(new WindowSessionRegistry(profile).Load().SequenceEqual([profile, second]),
            "window order is persisted and restored");
        Require(SessionStore.ParseComplete(File.ReadAllText(profile))!.Panes[0][0].Path == @"C:\window-a" &&
                SessionStore.ParseComplete(File.ReadAllText(second))!.Panes[0][0].Path == @"C:\window-b",
            "independent session snapshots do not overwrite one another");
        var pinsPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(second) + ".PinnedTabs.ini");
        Require(new PinnedTabsStore(pinsPath).Load().SequenceEqual([new PinnedTabEntry(0, 0, @"C:\window-b")]),
            "pinned-tab metadata follows its owning window session");

        registry.Remove(profile);
        var afterPrimaryClose = new WindowSessionRegistry(profile);
        Require(afterPrimaryClose.Load().SequenceEqual([second]),
            "closing the primary window persists the other window session for restart recovery");
        Require(!afterPrimaryClose.Remove(second) && new WindowSessionRegistry(profile).Load().SequenceEqual([second]),
            "the final session remains registered for restart recovery");

        var changedExternally = File.ReadAllText(registry.RegistryPath) + "; external change\n";
        File.WriteAllText(registry.RegistryPath, changedExternally);
        var conflicting = false;
        try { registry.Create(State(@"C:\window-c"), []); }
        catch (IOException) { conflicting = true; }
        Require(conflicting && File.ReadAllText(registry.RegistryPath) == changedExternally,
            "registry conflict rejects writes without replacing external changes");
        Require(Directory.GetFiles(folder, "ExplorerDirs.window-*.ini")
                .Count(path => !path.EndsWith(".PinnedTabs.ini", StringComparison.OrdinalIgnoreCase)) == 1,
            "failed registry writes clean up the unpublished session file");

        foreach (var futureVersion in new[] { "2", "2147483648", "999999999999999999999999999999999999999999",
            "1\nFormatVersion=2", "1\nFormatVersion=2147483648", "2\nFormatVersion=1" })
        {
            var futureFolder = Path.Combine(root, "window-sessions-future-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(futureFolder);
            var futureProfile = Path.Combine(futureFolder, "ExplorerDirs.ini");
            var futureRegistryPath = Path.Combine(futureFolder, "WindowSessions.ini");
            var futureRegistry = $"[WindowSessions]\nFormatVersion={futureVersion}\nCount=1\nSession0=ExplorerDirs.ini\n";
            File.WriteAllText(futureRegistryPath, futureRegistry);
            var future = new WindowSessionRegistry(futureProfile);
            Require(future.Load().SequenceEqual([futureProfile]) && future.LoadWarning is not null,
                "newer registry formats fall back to the readable primary session");
            var futureProtected = false;
            try { future.Create(State(@"C:\window-future"), []); }
            catch (IOException) { futureProtected = true; }
            Require(futureProtected && File.ReadAllText(futureRegistryPath) == futureRegistry,
                "newer registry formats stay untouched and reject writes");
        }

        var missingFolder = Path.Combine(root, "window-sessions-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(missingFolder);
        var missingProfile = Path.Combine(missingFolder, "ExplorerDirs.ini");
        new SessionStore(missingProfile, Path.Combine(missingFolder, "missing.json")).Save(State(@"C:\window-primary"));
        var missingRegistry = new WindowSessionRegistry(missingProfile);
        missingRegistry.Load();
        var missingSession = missingRegistry.Create(State(@"C:\window-secondary"), []);
        var missingRegistryText = File.ReadAllText(missingRegistry.RegistryPath);
        File.Delete(missingSession);
        var recovered = new WindowSessionRegistry(missingProfile);
        Require(recovered.Load().SequenceEqual([missingProfile]) && recovered.LoadWarning is not null,
            "missing secondary session falls back to the readable primary with a warning");
        var missingRegistryProtected = false;
        try { recovered.Create(State(@"C:\window-unrelated"), []); }
        catch (IOException) { missingRegistryProtected = true; }
        Require(missingRegistryProtected && File.ReadAllText(recovered.RegistryPath) == missingRegistryText,
            "missing session keeps its roster untouched and rejects further roster writes");
    }

    private static SessionState State(string path) => new(1, 4,
        [[new TabSettings(path, ViewState.Default())], [], [], []]);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception($"Window session registry smoke failed: {message}");
    }
}
