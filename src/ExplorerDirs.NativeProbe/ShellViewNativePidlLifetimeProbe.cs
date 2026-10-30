using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

// Exercises the IShellView prototype's retained travel history with real Shell PIDLs,
// while keeping the probe independent of any Shell view window or user desktop.
internal static class ShellViewNativePidlLifetimeProbe
{
    private const uint SbspBack = 0x4000;
    private const uint SbspForward = 0x8000;
    private const uint SbspCreateNoHistory = 0x00100000;
    private const int EFail = unchecked((int)0x80004005);
    private const int StopCount = 200;

    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-PidlLifetime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixture);
        var livePidls = new HashSet<nint>();
        var allocated = 0;
        var released = 0;
        nint rootPidl = 0;
        ShellViewNavigationHistory? history = null;
        string? expectedLocation = null;
        string? rejectedLocation = null;

        nint Track(nint pidl)
        {
            if (pidl == 0) throw new InvalidOperationException("The Shell returned a null PIDL allocation.");
            if (!livePidls.Add(pidl)) throw new InvalidOperationException("The Shell reused a still-live PIDL pointer.");
            allocated++;
            return pidl;
        }

        void Free(nint pidl)
        {
            if (pidl == 0) return;
            if (!livePidls.Remove(pidl)) throw new InvalidOperationException("The history released a PIDL it did not own, or released one twice.");
            released++;
            Marshal.FreeCoTaskMem(pidl);
        }

        nint Parse(string path)
        {
            Check(SHParseDisplayName(path, 0, out var pidl, 0, out _));
            return pidl;
        }

        try
        {
            var paths = new string[StopCount + 1];
            paths[0] = fixture;
            for (var index = 1; index < paths.Length; index++)
            {
                paths[index] = Path.Combine(fixture, $"location-{index:D3}");
                Directory.CreateDirectory(paths[index]);
            }
            var branchPath = Path.Combine(fixture, "branch");
            var noHistoryPath = Path.Combine(fixture, "no-history");
            var rejectedPath = Path.Combine(fixture, "rejected");
            Directory.CreateDirectory(branchPath);
            Directory.CreateDirectory(noHistoryPath);
            Directory.CreateDirectory(rejectedPath);

            rootPidl = Parse(paths[0]);
            history = new ShellViewNavigationHistory(rootPidl,
                source => Track(ILClone(source)), Free, ILIsEqual,
                source => Track(ILGetParent(source)),
                (parent, relative) => Track(ILCombine(parent, relative)),
                (target, _) =>
                {
                    var actual = GetFileSystemPath(target);
                    return string.Equals(actual, expectedLocation, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(actual, rejectedLocation, StringComparison.OrdinalIgnoreCase) ? 0 : EFail;
                });

            for (var index = 1; index < paths.Length; index++)
            {
                expectedLocation = paths[index];
                var target = Parse(paths[index]);
                int result;
                try { result = history.BrowseObject(target, 0); }
                finally { Marshal.FreeCoTaskMem(target); }
                if (result < 0 || history.Index != index || history.Count != index + 1 ||
                    !SamePath(GetFileSystemPath(history.CurrentPidl), paths[index]))
                    throw new InvalidOperationException($"Navigation stop {index} did not retain the expected current Shell PIDL.");
            }

            var retainedStops = history.Count;
            for (var index = StopCount - 1; index >= 0; index--)
            {
                expectedLocation = paths[index];
                Check(history.BrowseObject(0, SbspBack));
                if (history.Index != index || history.Count != retainedStops ||
                    !SamePath(GetFileSystemPath(history.CurrentPidl), paths[index]))
                    throw new InvalidOperationException($"Back at history index {index} restored the wrong Shell PIDL.");
            }
            for (var index = 1; index <= StopCount; index++)
            {
                expectedLocation = paths[index];
                Check(history.BrowseObject(0, SbspForward));
                if (history.Index != index || history.Count != retainedStops ||
                    !SamePath(GetFileSystemPath(history.CurrentPidl), paths[index]))
                    throw new InvalidOperationException($"Forward at history index {index} restored the wrong Shell PIDL.");
            }

            for (var index = 0; index < 50; index++)
            {
                expectedLocation = paths[StopCount - index - 1];
                Check(history.BrowseObject(0, SbspBack));
            }
            var branchIndex = history.Index;
            expectedLocation = branchPath;
            var branchPidl = Parse(branchPath);
            try { Check(history.BrowseObject(branchPidl, 0)); }
            finally { Marshal.FreeCoTaskMem(branchPidl); }
            if (history.Index != branchIndex + 1 || history.Count != branchIndex + 2 ||
                !SamePath(GetFileSystemPath(history.CurrentPidl), branchPath) || history.BrowseObject(0, SbspForward) >= 0)
                throw new InvalidOperationException("Branch navigation did not release and truncate the old forward PIDLs.");

            var countBeforeFailedNavigation = history.Count;
            var indexBeforeFailedNavigation = history.Index;
            var allocationsBeforeFailedNavigation = allocated;
            var releasesBeforeFailedNavigation = released;
            rejectedLocation = expectedLocation = rejectedPath;
            var rejectedPidl = Parse(rejectedPath);
            int failedResult;
            try { failedResult = history.BrowseObject(rejectedPidl, 0); }
            finally { Marshal.FreeCoTaskMem(rejectedPidl); }
            if (failedResult >= 0 || history.Count != countBeforeFailedNavigation || history.Index != indexBeforeFailedNavigation ||
                allocated != allocationsBeforeFailedNavigation + 1 || released != releasesBeforeFailedNavigation + 1 ||
                !SamePath(GetFileSystemPath(history.CurrentPidl), branchPath))
                throw new InvalidOperationException("A failed native navigation leaked its staged PIDL or changed history.");
            rejectedLocation = null;

            expectedLocation = noHistoryPath;
            var noHistoryPidl = Parse(noHistoryPath);
            try { Check(history.BrowseObject(noHistoryPidl, SbspCreateNoHistory)); }
            finally { Marshal.FreeCoTaskMem(noHistoryPidl); }
            if (history.Count != countBeforeFailedNavigation || history.Index != indexBeforeFailedNavigation ||
                !SamePath(GetFileSystemPath(history.CurrentPidl), noHistoryPath))
                throw new InvalidOperationException("SBSP_CREATENOHISTORY did not replace the active native PIDL in place.");

            Console.WriteLine($"ishellview-native-pidl-lifetime: stops={retainedStops}; back-forward={StopCount * 2}; branch-truncated=True; failed-navigation-balanced=True; no-history-replaced=True; owned-pidls-before-dispose={livePidls.Count}; allocated={allocated}; released={released}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ishellview-native-pidl-lifetime: failed={exception.GetType().Name}: {exception.Message}; owned={livePidls.Count}; allocated={allocated}; released={released}");
            return 1;
        }
        finally
        {
            history?.Dispose();
            if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
            try { Directory.Delete(fixture, recursive: true); } catch { }
            if (history is not null && livePidls.Count != 0)
                Console.Error.WriteLine($"ishellview-native-pidl-lifetime: cleanup-unbalanced={livePidls.Count}; allocated={allocated}; released={released}");
            else if (history is not null)
                Console.WriteLine($"ishellview-native-pidl-lifetime: dispose-balanced=True; allocated={allocated}; released={released}");
        }
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static string GetFileSystemPath(nint pidl)
    {
        Check(SHGetNameFromIDList(pidl, 0x80058000, out var text)); // SIGDN_FILESYSPATH
        try { return Marshal.PtrToStringUni(text) ?? throw new InvalidOperationException("The Shell returned an empty filesystem path."); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    private static void Check(int hresult) => Marshal.ThrowExceptionForHR(hresult);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll")] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll")] private static extern nint ILGetParent(nint pidl);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint parent, nint relative);
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint first, nint second);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint name);
}
