using ExplorerDirs.App;
using System.IO;

internal static class ShellProviderProfileStoreSmoke
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.ShellProviderProfile.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ShellNamespace.ini");
            var initialStore = new ShellNamespaceProfileStore(path);
            var initialId = initialStore.LoadOrCreate();
            Check(initialId.HasValue && initialId != Guid.Empty, "a new profile receives a nonempty provider CLSID");
            Check(File.ReadAllText(path).Contains($"ClassId={initialId:D}", StringComparison.Ordinal),
                "the provider CLSID is stored in the portable profile INI");
            Check(new ShellNamespaceProfileStore(path).LoadOrCreate() == initialId,
                "reopening a profile preserves its provider CLSID");

            var concurrentPath = Path.Combine(root, "concurrent", "ShellNamespace.ini");
            var concurrentIds = new System.Collections.Concurrent.ConcurrentBag<Guid?>();
            Parallel.For(0, 8, _ => concurrentIds.Add(new ShellNamespaceProfileStore(concurrentPath).LoadOrCreate()));
            Check(concurrentIds.All(id => id.HasValue) && concurrentIds.Select(id => id!.Value).Distinct().Count() == 1,
                "simultaneous first reads converge on the one atomically stored profile CLSID");

            var malformed = Path.Combine(root, "malformed.ini");
            const string malformedText = "[ShellNamespace]\r\nFormatVersion=1\r\nClassId=not-a-guid\r\n";
            File.WriteAllText(malformed, malformedText);
            var malformedStore = new ShellNamespaceProfileStore(malformed);
            Check(malformedStore.LoadOrCreate() is null && malformedStore.LoadWarning is not null &&
                File.ReadAllText(malformed) == malformedText,
                "a malformed identity is reported and preserved instead of silently rotating");

            var newer = Path.Combine(root, "newer.ini");
            const string newerText = "[ShellNamespace]\r\nFormatVersion=2\r\nClassId=b0f47a2f-1690-4888-a0d6-cfe4a4abf92f\r\nFutureField=keep\r\n";
            File.WriteAllText(newer, newerText);
            var newerStore = new ShellNamespaceProfileStore(newer);
            Check(newerStore.LoadOrCreate() is null && newerStore.LoadWarning is not null &&
                File.ReadAllText(newer) == newerText,
                "a newer identity format is not overwritten with a new CLSID");

            Console.WriteLine("Shell provider profile store smoke: PASS");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException("Shell provider profile store: " + message);
    }
}
