using System.IO;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class DesktopWorkspaceShortcutSmoke
{
    internal static void Run(string root)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var shortcut = Path.Combine(root, "Desktop Workspace.lnk");
                const string executable = @"C:\Program Files\Explorer Dirs\ExplorerDirs.App.exe";
                const string qdr = @"C:\Users\marvin\Workspaces\研发 工作区.qdr";
                DesktopWorkspaceShortcut.Create(shortcut, executable, null, qdr, @"C:\Program Files\Explorer Dirs");
                var (target, arguments) = DesktopWorkspaceShortcut.ReadTargetAndArguments(shortcut);
                Require(target == executable && arguments == '"' + qdr + '"',
                    "desktop shortcut stores the application target and quoted Unicode Q-Dir state argument");

                var dotnetShortcut = Path.Combine(root, "Dotnet Workspace.lnk");
                const string assembly = @"D:\build output\ExplorerDirs.App.dll";
                DesktopWorkspaceShortcut.Create(dotnetShortcut, @"C:\Program Files\dotnet\dotnet.exe",
                    assembly, qdr, @"D:\build output");
                var (dotnetTarget, dotnetArguments) = DesktopWorkspaceShortcut.ReadTargetAndArguments(dotnetShortcut);
                Require(dotnetTarget == @"C:\Program Files\dotnet\dotnet.exe" &&
                        dotnetArguments == '"' + assembly + '"' + " \"" + qdr + '"',
                    "dotnet-hosted development shortcuts include both the assembly and Q-Dir state arguments");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Desktop workspace shortcut smoke failed.", failure);
        var backgroundLink = Path.Combine(root, "Background Workspace.lnk");
        var backgroundQdr = Path.Combine(root, "background-workspace.qdr");
        const string backgroundText = "Q-Dir Version=12.79\nDir1=C:\\background\n";
        Task.Run(() => DesktopWorkspaceShortcut.CreateWithWorkspace(backgroundLink,
            @"C:\ExplorerDirs.App.exe", null, backgroundQdr, root, backgroundText)).GetAwaiter().GetResult();
        Require(QdrImporter.ReadText(backgroundQdr) == backgroundText &&
                DesktopWorkspaceShortcut.ReadTargetAndArguments(backgroundLink).Arguments == '"' + backgroundQdr + '"',
            "ThreadPool worker writes a workspace and a matching native shortcut without a window");
        var originalLink = File.ReadAllBytes(backgroundLink);
        var failedQdr = Path.Combine(root, "failed-shortcut-workspace.qdr");
        var refused = false;
        try
        {
            Task.Run(() => DesktopWorkspaceShortcut.CreateWithWorkspace(backgroundLink,
                @"C:\ExplorerDirs.App.exe", null, failedQdr, root, backgroundText)).GetAwaiter().GetResult();
        }
        catch (IOException) { refused = true; }
        Require(refused && !File.Exists(failedQdr) && File.ReadAllBytes(backgroundLink).SequenceEqual(originalLink),
            "failed shortcut publication removes only its new workspace and preserves the existing shortcut");
        var replacementQdr = Path.Combine(root, "replacement-workspace.qdr");
        Task.Run(() => DesktopWorkspaceShortcut.CreateWithWorkspace(backgroundLink,
            @"C:\ExplorerDirs.App.exe", null, replacementQdr, root, backgroundText, originalLink)).GetAwaiter().GetResult();
        Require(DesktopWorkspaceShortcut.ReadTargetAndArguments(backgroundLink).Arguments == '"' + replacementQdr + '"',
            "confirmed replacement publishes the new workspace shortcut");        var confirmed = File.ReadAllBytes(backgroundLink);
        var external = confirmed.Concat(new byte[] { 42 }).ToArray();
        File.WriteAllBytes(backgroundLink, external);
        var conflictQdr = Path.Combine(root, "conflict-workspace.qdr");
        refused = false;
        try { Task.Run(() => DesktopWorkspaceShortcut.CreateWithWorkspace(backgroundLink,
            @"C:\ExplorerDirs.App.exe", null, conflictQdr, root, backgroundText, confirmed)).GetAwaiter().GetResult(); }
        catch (IOException) { refused = true; }
        Require(refused && File.ReadAllBytes(backgroundLink).SequenceEqual(external) && !File.Exists(conflictQdr),
            "changes after replacement confirmation are preserved and the unpublished new QDR is removed");
        Require(File.Exists(backgroundQdr) && File.Exists(replacementQdr) &&
                !Directory.EnumerateFiles(root, "*.tmp.lnk").Any(),
            "replacement retains prior workspace files and cleans temporary shortcut payloads");        var existingText = QdrImporter.ReadText(backgroundQdr);
        refused = false;
        try
        {
            Task.Run(() => DesktopWorkspaceShortcut.CreateWithWorkspace(Path.Combine(root, "must-not-exist.lnk"),
                @"C:\ExplorerDirs.App.exe", null, backgroundQdr, root, "different")).GetAwaiter().GetResult();
        }
        catch (IOException) { refused = true; }
        Require(refused && QdrImporter.ReadText(backgroundQdr) == existingText &&
                !File.Exists(Path.Combine(root, "must-not-exist.lnk")),
            "an existing workspace conflict is preserved and never publishes a shortcut");
        Console.WriteLine("Desktop workspace shortcut smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Desktop workspace shortcut smoke: " + message);
        Console.WriteLine("PASS " + message);
    }
}
