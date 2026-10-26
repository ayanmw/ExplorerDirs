using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class NativeShellTreeIconFallbackSmoke
{
    internal static void Run()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "NativeShellTree.cs"));
        Check(UsesSystemShellIcons(source),
            "product namespace-tree callback supplies normal/open Windows system image-list indices");
        Console.WriteLine("Native Shell tree icon smoke: PASS");

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

        static bool UsesSystemShellIcons(string source) =>
            source.Contains("SHGetFileInfo", StringComparison.Ordinal) &&
            source.Contains("OnGetDefaultIconIndex(nint item, out int normal, out int open)", StringComparison.Ordinal) &&
            source.Contains("systemImageIndex | smallIcon", StringComparison.Ordinal) &&
            source.Contains("return 0;", StringComparison.Ordinal);
    }
}
