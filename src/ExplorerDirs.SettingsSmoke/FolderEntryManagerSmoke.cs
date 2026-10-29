using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class FolderEntryManagerSmoke
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.FolderEntryManager.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var tools = Path.Combine(root, "tools");
            Directory.CreateDirectory(tools);
            File.WriteAllText(Path.Combine(tools, "folder-verb.ps1"), "fixture");
            File.WriteAllText(Path.Combine(tools, "FolderVerb.psm1"), "fixture");
            var normalExe = Path.Combine(root, "ExplorerDirs.App.exe");
            File.WriteAllText(normalExe, "fixture");
            Check(FolderEntryManager.FindPackagedScript(root) == Path.Combine(tools, "folder-verb.ps1"),
                "normal release package resolves its adjacent management script");

            var isolatedApp = Path.Combine(root, "app");
            Directory.CreateDirectory(isolatedApp);
            File.WriteAllText(Path.Combine(isolatedApp, "ExplorerDirs.App.exe"), "fixture");
            Check(FolderEntryManager.FindPackagedScript(isolatedApp) == Path.Combine(tools, "folder-verb.ps1"),
                "isolated release package resolves tools from the release root");

            var debug = Path.Combine(root, "src", "bin", "Debug");
            Directory.CreateDirectory(debug);
            Check(FolderEntryManager.FindPackagedScript(debug) is null,
                "source/debug layout without packaged tools cannot enable system registration");

            var states = FolderEntryManager.ParseStates("[{\"Path\":\"HKCU\\\\Directory\",\"State\":\"missing\"},{\"Path\":\"HKCU\\\\Drive\",\"State\":\"owned\"}]");
            Check(states.Count == 2 && states[0].DisplayState == "未启用" && states[1].DisplayState == "已启用",
                "machine-readable state rows deserialize and map to user-facing labels");

            var preview = FolderEntryManager.ParsePreview("{\"Executable\":\"C:\\\\release\\\\ExplorerDirs.App.exe\",\"Sha256\":\"0123456789ABCDEF\",\"DirectoryArgumentDeclared\":true,\"DirectoryVerb\":\"Directory\",\"DriveVerb\":\"Drive\",\"Command\":\"command\",\"CurrentState\":[]}");
            Check(preview.DirectoryArgumentDeclared && preview.Sha256 == "0123456789ABCDEF" && preview.CurrentState.Count == 0,
                "machine-readable release preview deserializes its capability and current state");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Folder entry manager smoke failed: {message}");
    }
}
