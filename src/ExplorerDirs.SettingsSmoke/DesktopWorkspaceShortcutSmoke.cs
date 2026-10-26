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
        Console.WriteLine("Desktop workspace shortcut smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Desktop workspace shortcut smoke: " + message);
        Console.WriteLine("PASS " + message);
    }
}
