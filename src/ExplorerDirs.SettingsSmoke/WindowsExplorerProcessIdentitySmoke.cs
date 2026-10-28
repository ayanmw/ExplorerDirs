using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class WindowsExplorerProcessIdentitySmoke
{
    internal static void Run()
    {
        Check(WindowsExplorerRestarter.MatchesExpectedExecutable(
                @"C:\Windows\explorer.exe", @"C:\Windows\explorer.exe"),
            "the registered Windows Explorer executable is allowed");
        Check(WindowsExplorerRestarter.MatchesExpectedExecutable(
                @"c:\WINDOWS\EXPLORER.EXE", @"C:\Windows\explorer.exe"),
            "Windows executable path comparison is case-insensitive");
        Check(!WindowsExplorerRestarter.MatchesExpectedExecutable(
                @"C:\CustomShell\explorer.exe", @"C:\Windows\explorer.exe"),
            "a same-named executable outside the Windows directory is rejected");
        Check(!WindowsExplorerRestarter.MatchesExpectedExecutable(
                @"C:\Windows\notepad.exe", @"C:\Windows\explorer.exe"),
            "a different shell executable is rejected");
        Check(!WindowsExplorerRestarter.MatchesExpectedExecutable(
                null, @"C:\Windows\explorer.exe"),
            "an unavailable process image path is rejected");
        Check(!WindowsExplorerRestarter.MatchesExpectedExecutable(
                @"C:\Windows\explorer.exe", ""),
            "an unavailable expected image path is rejected");

        Check(WindowsExplorerRestarter.IsExpectedReplacementShell(
                100, 101, @"C:\Windows\explorer.exe", @"C:\Windows\explorer.exe"),
            "a different PID running the Windows Explorer image is a restored shell");
        Check(!WindowsExplorerRestarter.IsExpectedReplacementShell(
                100, 100, @"C:\Windows\explorer.exe", @"C:\Windows\explorer.exe"),
            "the retired shell PID is not mistaken for its replacement");
        Check(!WindowsExplorerRestarter.IsExpectedReplacementShell(
                100, 101, @"C:\Tools\custom-shell.exe", @"C:\Windows\explorer.exe"),
            "a different process owning the shell window is not reported as Explorer recovery");
        Check(!WindowsExplorerRestarter.IsExpectedReplacementShell(
                0, 101, @"C:\Windows\explorer.exe", @"C:\Windows\explorer.exe"),
            "a missing original Shell PID cannot establish a replacement");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Windows Explorer process identity: {label}");
    }
}
