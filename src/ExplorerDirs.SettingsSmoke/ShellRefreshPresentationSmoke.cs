using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellRefreshPresentationSmoke
{
    internal static void Run()
    {
        var root = FindRepoRoot();
        var host = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));
        var redrawLease = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "ShellViewRedrawLease.cs"));
        var modeStart = host.IndexOf("public bool SetViewStyle(ViewStyle style)", StringComparison.Ordinal);
        var modeEnd = host.IndexOf("private void QueueViewStyleCapture", modeStart, StringComparison.Ordinal);
        var modePath = modeStart >= 0 && modeEnd > modeStart ? host[modeStart..modeEnd] : string.Empty;
        var sortStart = host.IndexOf("private void ScheduleInitialVirtualSort", StringComparison.Ordinal);
        var sortEnd = host.IndexOf("private bool HasPendingUnresolvedColumnValues", sortStart, StringComparison.Ordinal);
        var sortPath = sortStart >= 0 && sortEnd > sortStart ? host[sortStart..sortEnd] : string.Empty;
        var refreshStart = host.IndexOf("private void Refresh(Action? afterRefresh, bool traceAsUserRefresh", StringComparison.Ordinal);
        var refreshEnd = host.IndexOf("private (bool SelectionRestored", refreshStart, StringComparison.Ordinal);
        var refreshPath = refreshStart >= 0 && refreshEnd > refreshStart ? host[refreshStart..refreshEnd] : string.Empty;

        Check(modePath.Contains("ShellViewRedrawLease.TrySuppress(view)", StringComparison.Ordinal) &&
            modePath.IndexOf("ShellViewRedrawLease.TrySuppress(view)", StringComparison.Ordinal) <
                modePath.IndexOf("ApplyMode(", StringComparison.Ordinal) &&
            modePath.Contains("ApplyMode(", StringComparison.Ordinal) &&
            modePath.Contains("RedrawWindow(viewWindow", StringComparison.Ordinal) &&
            modePath.IndexOf("RedrawWindow(viewWindow", StringComparison.Ordinal) >
                modePath.IndexOf("ShellViewRedrawLease.TrySuppress(view)", StringComparison.Ordinal) &&
            !modePath.Contains("Refresh(", StringComparison.Ordinal) && !modePath.Contains("Navigate(", StringComparison.Ordinal),
            "view-style changes invalidate the existing native view after restoring redraw from the scoped mode setter");
        var sortLeaseCall = sortPath.IndexOf("ShellViewRedrawLease.TrySuppress(sortView)", StringComparison.Ordinal);
        var oppositeSortCall = sortPath.IndexOf("InvokeVirtualSortVerb(oppositeVerb, IsCurrent)", StringComparison.Ordinal);
        Check(sortLeaseCall >= 0 && oppositeSortCall > sortLeaseCall &&
            sortPath.Contains("ReadDirectoryMonitorItemCount() != readinessItemCount", StringComparison.Ordinal) &&
            sortPath.Contains("savedApplied", StringComparison.Ordinal) &&
            sortPath.Contains("ReleaseInitialVirtualSortRedrawLease", StringComparison.Ordinal) &&
            host.Contains("private void ReleaseInitialVirtualSortRedrawLease", StringComparison.Ordinal),
            "initial virtual sorting freezes native redraw across both Shell sort directions and releases after rows return");
        var disposeStart = host.IndexOf("private void DisposeBrowser()", StringComparison.Ordinal);
        var detachStart = host.IndexOf("private void DetachViewWindow()", StringComparison.Ordinal);
        Check(disposeStart >= 0 && host.IndexOf("ReleaseInitialVirtualSortRedrawLease(requestRedraw: false)", disposeStart, StringComparison.Ordinal) >= 0 &&
            detachStart >= 0 && host.IndexOf("ReleaseInitialVirtualSortRedrawLease(requestRedraw: false)", detachStart, StringComparison.Ordinal) >= 0,
            "closing or navigating away releases a pending initial-sort redraw lease");
        var sortVerbStart = host.IndexOf("private bool InvokeVirtualSortVerb", StringComparison.Ordinal);
        var sortVerbEnd = host.IndexOf("private sealed class NativeViewStateTarget", sortVerbStart, StringComparison.Ordinal);
        var sortVerbPath = sortVerbStart >= 0 && sortVerbEnd > sortVerbStart ? host[sortVerbStart..sortVerbEnd] : string.Empty;
        var viewProcStart = host.IndexOf("private IntPtr ViewWindowProc", StringComparison.Ordinal);
        var viewProcEnd = host.IndexOf("private static double TicksToMilliseconds", viewProcStart, StringComparison.Ordinal);
        var viewProcPath = viewProcStart >= 0 && viewProcEnd > viewProcStart ? host[viewProcStart..viewProcEnd] : string.Empty;
        Check(sortVerbPath.Contains("Interlocked.Increment(ref _shellSortCommandDispatchDepth)", StringComparison.Ordinal) &&
            sortVerbPath.Contains("Interlocked.Decrement(ref _shellSortCommandDispatchDepth)", StringComparison.Ordinal) &&
            viewProcPath.Contains("childCommandNotification", StringComparison.Ordinal) &&
            viewProcPath.Contains("programmaticSortCommand", StringComparison.Ordinal),
            "Shell child command notifications do not masquerade as user input and cancel paired sorting");
        Check(refreshPath.Contains("shellView.Refresh()", StringComparison.Ordinal) &&
            !refreshPath.Contains("TryNotifyDirectoryRefresh(", StringComparison.Ordinal) &&
            !refreshPath.Contains("ShellViewRedrawLease.TrySuppress(folderView)", StringComparison.Ordinal) &&
            !refreshPath.Contains("refreshRedrawLease", StringComparison.Ordinal) &&
            refreshPath.Contains("TimeSpan.FromMilliseconds(1500)", StringComparison.Ordinal),
            "F5 directly refreshes the current view without directory broadcast or redraw suppression; its bounded timer coordinates selection recovery");
        Check(redrawLease.Contains("activeLease.Count++", StringComparison.Ordinal) &&
            redrawLease.Contains("--activeLease.Count == 0", StringComparison.Ordinal),
            "overlapping redraw leases for the same native Shell view restore painting only after the last owner releases");
        Console.WriteLine("Shell refresh presentation smoke: PASS (source contract; no Shell view created)");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }

    private static void Check(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException("Shell refresh presentation smoke failed: " + behavior);
        Console.WriteLine("PASS " + behavior);
    }
}
