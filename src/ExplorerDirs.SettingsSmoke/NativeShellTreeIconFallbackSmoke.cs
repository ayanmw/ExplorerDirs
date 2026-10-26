using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class NativeShellTreeIconFallbackSmoke
{
    internal static void Run()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "NativeShellTree.cs"));
        var probe = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.NativeProbe", "StandaloneTreeProbe.cs"));
        Check(UsesDefaultIconFallback(source),
            "product namespace-tree icon lookup returns E_NOTIMPL so Shell uses its default icon");
        Check(UsesDefaultIconFallback(probe),
            "standalone tree probe uses the same default icon fallback as the product");
        Console.WriteLine("Native Shell tree icon fallback smoke: PASS");

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "NativeShellTree.cs")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static bool UsesDefaultIconFallback(string source) =>
            source.Contains("OnGetDefaultIconIndex(nint item, out int normal, out int open)\n    {\n        normal = open = -1;\n        return unchecked((int)0x80004001);", StringComparison.Ordinal);
    }
}
