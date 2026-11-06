using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

// Reuse the existing real product HwndHost test on the never-switched Desktop.
// Loading HostProbe here keeps its WPF/App types out of the desktop-monitor thread.
internal static class ProductFourPaneHostProbe
{
    internal static int Run(string hostProbePath, bool large = false, bool observeNotifications = true, bool forceSortDeadline = false, string? sortContextAdapter = null, bool inputDuringSort = false, bool commandDuringSort = false, bool renameDuringSort = false, bool reconcileUserSort = false, bool retrySort = false, uint userSortMode = 3, bool hideSizeColumn = false, string? persistedSessionPath = null, bool readPersistedSession = false, bool unavailableSizes = false, bool holdUnavailable = false, bool initialSizeGrouping = false, int groupedFolderCount = 1025, bool realEverything = false, bool sortPresentation = false, bool dropRegistration = false, bool groupedRecovery = false)
    {
        hostProbePath = Path.GetFullPath(hostProbePath);
        var directory = Path.GetDirectoryName(hostProbePath)!;
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name) =>
            name.Name == "ExplorerDirs.App"
                ? context.LoadFromAssemblyPath(Path.Combine(directory, "ExplorerDirs.App.dll")) : null;
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(hostProbePath);
            if (dropRegistration)
                return (int)assembly.GetType("DropRegistrationHostProbe", true)!.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [sortContextAdapter])!;
            if (sortPresentation || groupedRecovery)
                return (int)assembly.GetType("SortPresentationHostProbe", true)!.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [sortContextAdapter, groupedRecovery])!;
            var type = assembly.GetType("ProfileOwnerMultiRootShellHostProbe", throwOnError: true)!;
            var run = type.GetMethod(realEverything ? "RunEverythingHostsForProbe" : initialSizeGrouping && sortContextAdapter is not null ? "RunGroupedLargeForProbe" : persistedSessionPath is not null ? "RunPersistedSortForProbe" : sortContextAdapter is null ? "Run" : retrySort ? "RunSortRetryForProbe" : reconcileUserSort ? "RunUserSortReconciliationForProbe" : renameDuringSort ? "RunRenameDuringSortForProbe" : commandDuringSort ? "RunCommandDuringSortForProbe" : inputDuringSort ? "RunInputDuringSortForProbe" : "RunWithSortContextForProbe", BindingFlags.Static | BindingFlags.NonPublic)!;
            // The large case reproduces the previously failing four-root, 1,025-folders-per-root
            // workload, including notification identity observation and saved sorts.
            return (int)run.Invoke(null, realEverything ? [sortContextAdapter] : initialSizeGrouping && sortContextAdapter is not null ? [sortContextAdapter, groupedFolderCount] : persistedSessionPath is not null ? [sortContextAdapter, persistedSessionPath, readPersistedSession, unavailableSizes, holdUnavailable] : sortContextAdapter is null
                ? [false, large ? 1025 : 0, large, 8192, large && observeNotifications, forceSortDeadline, initialSizeGrouping]
                : reconcileUserSort ? [sortContextAdapter, userSortMode, hideSizeColumn] : [sortContextAdapter])!;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }
}
