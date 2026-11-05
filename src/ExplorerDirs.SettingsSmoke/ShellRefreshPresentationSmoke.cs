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
        var sortEnd = host.IndexOf("private (bool SelectionRestored", sortStart, StringComparison.Ordinal);
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
        // Recovery now keeps the requested primary direction. Checking the deleted
        // opposite/saved-verb implementation would reward the oscillation reported by users.
        Check(!sortPath.Contains("InvokeVirtualSortVerb", StringComparison.Ordinal) &&
            sortPath.Contains("target.SetSorting(temporary)", StringComparison.Ordinal) &&
            sortPath.Contains("target.SetSorting(sorts)", StringComparison.Ordinal) &&
            sortPath.Contains("finally { EndInitialSortTransient(serial); }", StringComparison.Ordinal),
            "initial sorting preserves the primary direction and protects transient secondary keys");
        Check(sortPath.Contains("ShellSizeSortOrder.IsOrderedAsync", StringComparison.Ordinal) &&
            sortPath.Contains("++attempts < 3", StringComparison.Ordinal) &&
            sortPath.Contains("_viewRestoreFailed = true", StringComparison.Ordinal) &&
            sortPath.Contains("inputVersion == Interlocked.Read", StringComparison.Ordinal),
            "initial sorting verifies native row order, bounds retries and retires on user input");
        var paintStart = host.IndexOf("private void EndSortPresentation", StringComparison.Ordinal);
        var paintEnd = host.IndexOf("private void ClearNativeSortCommandSelection", paintStart, StringComparison.Ordinal);
        var paintPath = host[paintStart..paintEnd];
        Check(paintPath.Contains("ShellViewRedrawLease.TrySuppress(view)", StringComparison.Ordinal) &&
            paintPath.Contains("stable >= 2", StringComparison.Ordinal) &&
            paintPath.Contains("started.ElapsedMilliseconds >= 1500", StringComparison.Ordinal) &&
            paintPath.Contains("lease.Dispose()", StringComparison.Ordinal) &&
            !paintPath.Contains("RefreshWithPresentation", StringComparison.Ordinal),
            "column sorting holds the last frame with bounded release instead of refreshing the directory");
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
        // Refresh now passes the native call through the manual/automatic flag policy;
        // keep checking the presentation boundary rather than the old invocation spelling.
        Check(refreshPath.Contains("ShellRefreshPolicy.Invoke(traceAsUserRefresh || forceFullEnumeration, _softRefresh", StringComparison.Ordinal) &&
            refreshPath.Contains("shellView.Refresh, IsCurrentRefreshRequest", StringComparison.Ordinal) &&
            !refreshPath.Contains("TryNotifyDirectoryRefresh(", StringComparison.Ordinal) &&
            !refreshPath.Contains("ShellViewRedrawLease.TrySuppress(folderView)", StringComparison.Ordinal) &&
            !refreshPath.Contains("refreshRedrawLease", StringComparison.Ordinal) &&
            refreshPath.Contains("TimeSpan.FromMilliseconds(1500)", StringComparison.Ordinal),
            "F5 directly refreshes the current view without directory broadcast or redraw suppression; its bounded timer coordinates selection recovery");
        Check(redrawLease.Contains("activeLease.Count++", StringComparison.Ordinal) &&
            redrawLease.Contains("--activeLease.Count == 0", StringComparison.Ordinal),
            "overlapping redraw leases for the same native Shell view restore painting only after the last owner releases");
        Check(sortPath.Contains("if (grouped)", StringComparison.Ordinal) &&
            sortPath.Contains("RefreshWithPresentation(traceAsUserRefresh: false, forceFullEnumeration: true)", StringComparison.Ordinal),
            "saved startup groups bypass automatic soft enumeration without pretending user F5 input");
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
