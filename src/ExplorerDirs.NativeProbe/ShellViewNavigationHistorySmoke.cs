using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

internal static class ShellViewNavigationHistorySmoke
{
    private const uint Parent = 0x2000;
    private const uint Relative = 0x1000;
    private const uint Back = 0x4000;
    private const uint Forward = 0x8000;
    private const uint CreateNoHistory = 0x00100000;
    private const uint SameBrowser = 0x0001;
    private const uint NewBrowser = 0x0002;
    private const uint HelpMode = 0x0040;
    private const uint ExploreMode = 0x0020;
    private const uint KeepSameTemplate = 0x00020000;

    internal static void Run()
    {
        var pool = new PidlPool();
        var root = pool.Create(1);
        var child = pool.Create(2);
        var branch = pool.Create(3);
        var failedTarget = pool.Create(4);
        try
        {
            using var history = new ShellViewNavigationHistory(
                root, pool.Clone, pool.Free, pool.Equal, pool.Parent, pool.Combine,
                (target, _) => pool.Read(target) == 4 ? unchecked((int)0x80004005) : 0);

            Check(history.BrowseObject(child, 0) == 0 && pool.Read(history.CurrentPidl) == 2,
                "a successful navigation appends the target to history");
            Check(history.BrowseObject(0, Parent) == 0 && history.Index == 2 &&
                  pool.Read(history.CurrentPidl) == 1,
                "parent navigation clones the target before releasing its temporary PIDL");
            Check(history.BrowseObject(0, Back) == 0 && pool.Read(history.CurrentPidl) == 2,
                "back navigation moves to the previous retained PIDL");
            Check(history.BrowseObject(0, Forward) == 0 && pool.Read(history.CurrentPidl) == 1,
                "forward navigation moves to the next retained PIDL");

            Check(history.BrowseObject(0, Back) == 0 && history.BrowseObject(branch, 0) == 0 &&
                  history.Index == 2 && history.Count == 3 &&
                  history.BrowseObject(0, Forward) < 0 && pool.Read(history.CurrentPidl) == 3,
                "a successful branch truncates forward history");
            var indexBeforeFailure = history.Index;
            var countBeforeFailure = history.Count;
            Check(history.BrowseObject(failedTarget, 0) < 0 && history.Index == indexBeforeFailure &&
                  history.Count == countBeforeFailure && pool.Read(history.CurrentPidl) == 3,
                "a failed navigation leaves the current history unchanged");

            ShellViewNavigationHistory? reentrant = null;
            var nestedResult = 0;
            var callbackCount = 0;
            using (reentrant = new ShellViewNavigationHistory(root, pool.Clone, pool.Free, pool.Equal,
                pool.Parent, pool.Combine, (_, _) =>
                {
                    if (++callbackCount == 1) nestedResult = reentrant!.BrowseObject(branch, 0);
                    return 0;
                }))
            {
                Check(reentrant.BrowseObject(child, 0) == 0 && nestedResult == unchecked((int)0x800700AA) &&
                    callbackCount == 1 && reentrant.Count == 2 && reentrant.Index == 1 &&
                    pool.Read(reentrant.CurrentPidl) == 2,
                    "nested navigation is rejected as busy without corrupting the outer history");
                Check(reentrant.BrowseObject(branch, 0) == 0 && reentrant.Count == 3 &&
                    callbackCount == 2 && pool.Read(reentrant.CurrentPidl) == 3,
                    "a completed outer navigation releases the busy gate for subsequent requests");
            }

            ShellViewNavigationHistory? disposedDuringBrowse = null;
            var closeDuringCallback = false;
            using (disposedDuringBrowse = new ShellViewNavigationHistory(root, pool.Clone, pool.Free, pool.Equal,
                pool.Parent, pool.Combine, (target, _) =>
                {
                    if (closeDuringCallback)
                    {
                        disposedDuringBrowse!.Dispose();
                        Check(pool.Read(target) == 1, "closing retains the borrowed back target until its callback returns");
                    }
                    return 0;
                }))
            {
                Check(disposedDuringBrowse.BrowseObject(child, 0) == 0, "disposal fixture has back history");
                closeDuringCallback = true;
                Check(disposedDuringBrowse.BrowseObject(0, Back) == unchecked((int)0x80004004) &&
                    disposedDuringBrowse.Count == 0 && disposedDuringBrowse.Index == 0,
                    "closing during navigation aborts history commit and releases retained locations");
                Check(disposedDuringBrowse.BrowseObject(branch, 0) == unchecked((int)0x80004004),
                    "a closed history rejects later navigation");
            }

            var throwingTarget = pool.Create(7);
            using var throwingHistory = new ShellViewNavigationHistory(
                root, pool.Clone, pool.Free, pool.Equal, pool.Parent, pool.Combine,
                (_, _) => throw new InvalidOperationException("fixture navigation failure"));
            var freesBeforeThrow = pool.FreedCount;
            var navigationThrew = false;
            try { throwingHistory.BrowseObject(throwingTarget, 0); }
            catch (InvalidOperationException) { navigationThrew = true; }
            Check(navigationThrew && throwingHistory.Index == 0 && throwingHistory.Count == 1 &&
                  pool.Read(throwingHistory.CurrentPidl) == 1 && pool.FreedCount == freesBeforeThrow + 1,
                "a throwing navigation releases its staged PIDL and preserves history");

            var currentLocationRequest = pool.Create(1);
            var sameLocationCallbacks = 0;
            using var samePathHistory = new ShellViewNavigationHistory(
                root, pool.Clone, pool.Free, pool.Equal, pool.Parent, pool.Combine,
                (_, _) => { sameLocationCallbacks++; return 0; });
            Check(samePathHistory.BrowseObject(currentLocationRequest, 0) == 0 &&
                  sameLocationCallbacks == 1 && samePathHistory.Index == 0 && samePathHistory.Count == 1 &&
                  pool.Read(samePathHistory.CurrentPidl) == 1,
                "same-location navigation reaches the host without duplicating its history entry");

            var newBrowserCallbacks = 0;
            using var browserModeHistory = new ShellViewNavigationHistory(
                root, pool.Clone, pool.Free, pool.Equal, pool.Parent, pool.Combine,
                (_, _) => { newBrowserCallbacks++; return 0; });
            var newBrowserTarget = pool.Create(9);
            Check(browserModeHistory.BrowseObject(newBrowserTarget, NewBrowser) == unchecked((int)0x80004001) &&
                  newBrowserCallbacks == 0 && browserModeHistory.Index == 0 && browserModeHistory.Count == 1 &&
                  pool.Read(browserModeHistory.CurrentPidl) == 1,
                "an unsupported new-browser request is rejected without navigating the current view");
            Check(browserModeHistory.BrowseObject(newBrowserTarget, HelpMode) == unchecked((int)0x80004001) &&
                  newBrowserCallbacks == 0 && browserModeHistory.Index == 0 && browserModeHistory.Count == 1 &&
                  pool.Read(browserModeHistory.CurrentPidl) == 1,
                "an unsupported help-mode request is rejected without navigating the current view");
            Check(browserModeHistory.BrowseObject(newBrowserTarget, KeepSameTemplate) == unchecked((int)0x80004001) &&
                  newBrowserCallbacks == 0 && browserModeHistory.Index == 0 && browserModeHistory.Count == 1 &&
                  pool.Read(browserModeHistory.CurrentPidl) == 1,
                "an unsupported same-template request is rejected without navigating the current view");
            Check(browserModeHistory.BrowseObject(newBrowserTarget, ExploreMode) == unchecked((int)0x80004001) &&
                  newBrowserCallbacks == 0 && browserModeHistory.Index == 0 && browserModeHistory.Count == 1 &&
                  pool.Read(browserModeHistory.CurrentPidl) == 1,
                "an explore-mode request is rejected when the host cannot create a tree browser");

            uint browserModeFlags = 0;
            using var sameBrowserHistory = new ShellViewNavigationHistory(
                root, pool.Clone, pool.Free, pool.Equal, pool.Parent, pool.Combine,
                (_, flags) => { browserModeFlags = flags; return 0; });
            var sameBrowserTarget = pool.Create(10);
            Check(sameBrowserHistory.BrowseObject(sameBrowserTarget, SameBrowser) == 0 &&
                  browserModeFlags == SameBrowser && sameBrowserHistory.Index == 1,
                "same-browser mode is retained while navigating in the current view");
            var sameBrowserExploreTarget = pool.Create(11);
            Check(sameBrowserHistory.BrowseObject(sameBrowserExploreTarget, SameBrowser | ExploreMode) == 0 &&
                  browserModeFlags == SameBrowser && sameBrowserHistory.Index == 2,
                "same-browser mode permits navigation while ignoring the explore-mode request");

            var relativeChild = pool.Create(5);
            uint callbackFlags = uint.MaxValue;
            using var relativeHistory = new ShellViewNavigationHistory(
                root, pool.Clone, pool.Free, pool.Equal, pool.Parent, pool.Combine,
                (target, flags) =>
                {
                    callbackFlags = flags;
                    return pool.Read(target) == 15 ? 0 : unchecked((int)0x80004005);
                });
            Check(relativeHistory.BrowseObject(relativeChild, Relative) == 0 &&
                  pool.Read(relativeHistory.CurrentPidl) == 15 &&
                  (callbackFlags & Relative) == 0,
                "a relative PIDL combines with the current absolute location before navigation");

            var relativeRefinement = pool.Create(8);
            uint refinementFlags = uint.MaxValue;
            using var refinementHistory = new ShellViewNavigationHistory(
                root, pool.Clone, pool.Free, pool.Equal, pool.Parent, pool.Combine,
                (target, flags) =>
                {
                    refinementFlags = flags;
                    return pool.Read(target) == 18 ? 0 : unchecked((int)0x80004005);
                });
            Check(refinementHistory.BrowseObject(relativeRefinement, Relative | CreateNoHistory) == 0 &&
                  pool.Read(refinementHistory.CurrentPidl) == 18 && refinementHistory.Index == 0 &&
                  refinementHistory.Count == 1 && refinementFlags == CreateNoHistory,
                "relative no-history navigation composes the target without adding a history entry");

            using var noHistory = new ShellViewNavigationHistory(
                root, pool.Clone, pool.Free, pool.Equal, pool.Parent, pool.Combine,
                (target, _) => pool.Read(target) == 4 ? unchecked((int)0x80004005) : 0);
            var noHistoryTarget = pool.Create(6);
            var failedNoHistoryTarget = pool.Create(4);
            Check(noHistory.BrowseObject(child, 0) == 0 && noHistory.BrowseObject(branch, 0) == 0 &&
                  noHistory.BrowseObject(0, Back) == 0 && noHistory.Index == 1 && noHistory.Count == 3,
                "the no-history fixture enters a location with a forward entry");
            Check(noHistory.BrowseObject(failedNoHistoryTarget, CreateNoHistory) < 0 &&
                  noHistory.Index == 1 && noHistory.Count == 3 && pool.Read(noHistory.CurrentPidl) == 2 &&
                  noHistory.BrowseObject(0, Forward) == 0 && pool.Read(noHistory.CurrentPidl) == 3 &&
                  noHistory.BrowseObject(0, Back) == 0,
                "a failed no-history navigation retains the old location and forward branch");
            Check(noHistory.BrowseObject(noHistoryTarget, CreateNoHistory) == 0 &&
                  noHistory.Index == 1 && noHistory.Count == 2 &&
                  pool.Read(noHistory.CurrentPidl) == 6 && noHistory.BrowseObject(0, Forward) < 0,
                "SBSP_CREATENOHISTORY replaces the current entry without adding a travel-log stop");
        }
        finally { pool.Dispose(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Shell navigation history failed: {message}.");
        Console.WriteLine($"PASS {message}");
    }

    internal sealed class PidlPool : IDisposable
    {
        private readonly List<nint> _allocations = [];
        private readonly HashSet<nint> _freed = [];

        internal int FreedCount => _freed.Count;

        internal nint Create(int id)
        {
            var pointer = Marshal.AllocHGlobal(sizeof(int));
            _allocations.Add(pointer);
            Marshal.WriteInt32(pointer, id);
            return pointer;
        }

        internal nint Clone(nint source)
        {
            if (_freed.Contains(source)) throw new InvalidOperationException("A released PIDL was cloned.");
            return Create(Marshal.ReadInt32(source));
        }

        internal nint Combine(nint absoluteParent, nint relativeChild) =>
            Create(Read(absoluteParent) * 10 + Read(relativeChild));

        internal void Free(nint pointer)
        {
            if (pointer == 0) return;
            _freed.Add(pointer);
            Marshal.WriteInt32(pointer, -1);
        }

        internal bool Equal(nint first, nint second)
        {
            if (_freed.Contains(first) || _freed.Contains(second))
                throw new InvalidOperationException("A released PIDL was read during history update.");
            return Marshal.ReadInt32(first) == Marshal.ReadInt32(second);
        }

        internal int Read(nint pointer)
        {
            if (_freed.Contains(pointer)) throw new InvalidOperationException("A released PIDL became current.");
            return Marshal.ReadInt32(pointer);
        }

        internal nint Parent(nint pointer)
        {
            var id = Read(pointer);
            return id == 1 ? 0 : Create(id - 1);
        }

        public void Dispose()
        {
            foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer);
            _allocations.Clear();
        }
    }
}
