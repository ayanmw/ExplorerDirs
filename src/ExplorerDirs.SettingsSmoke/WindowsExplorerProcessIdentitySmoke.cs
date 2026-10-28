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

        Check(WindowsExplorerRestarter.IsCurrentShellProcess(100, () => 100),
            "the desktop Shell PID remains eligible when it is unchanged before termination");
        Check(!WindowsExplorerRestarter.IsCurrentShellProcess(100, () => 101),
            "a replacement desktop Shell prevents termination of a stale Explorer PID");
        Check(!WindowsExplorerRestarter.IsCurrentShellProcess(100, () => 0),
            "an unavailable desktop Shell PID prevents termination of the previously observed process");

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

        var reads = 0;
        Check(WindowsExplorerRestarter.WaitForExpectedReplacementShell(
                100, @"C:\Windows\explorer.exe",
                () => ++reads == 1
                    ? (100u, @"C:\Windows\explorer.exe")
                    : (101u, @"C:\Windows\explorer.exe"),
                maxAttempts: 3, _ => { }),
            "a Shell that appears after fallback launch is observed before reporting success");
        Check(reads == 2,
            "Shell recovery polling stops as soon as the replacement process is verified");

        reads = 0;
        Check(!WindowsExplorerRestarter.WaitForExpectedReplacementShell(
                100, @"C:\Windows\explorer.exe",
                () =>
                {
                    reads++;
                    return (100u, @"C:\Windows\explorer.exe");
                },
                maxAttempts: 3, _ => { }),
            "starting explorer.exe alone is not evidence that the desktop Shell recovered");
        Check(reads == 3,
            "the Shell is checked for the entire bounded recovery window");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Windows Explorer process identity: {label}");
    }
}
