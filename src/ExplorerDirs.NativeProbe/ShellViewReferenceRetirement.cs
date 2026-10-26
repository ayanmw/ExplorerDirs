namespace ExplorerDirs.NativeProbe;

// Owns failed Shell-view window retirements until the parent HWND is gone.
internal sealed class ShellViewReferenceRetirement(
    Func<nint, int> destroyViewWindow,
    Action<nint> release,
    Action<string> report)
{
    private readonly List<(nint View, nint Folder)> _deferred = [];

    internal int DeferredCount => _deferred.Count;

    internal void RetireAfterCreateAttempt(nint view, nint folder, bool createViewWindowAttempted)
    {
        if (createViewWindowAttempted)
        {
            Retire(view, folder);
            return;
        }

        if (view != 0) release(view);
        if (folder != 0) release(folder);
    }

    internal void Retire(nint view, nint folder)
    {
        var destroyHr = 0;
        if (view != 0)
        {
            try { destroyHr = destroyViewWindow(view); }
            catch (Exception ex)
            {
                Defer(view, folder, $"DestroyViewWindow threw: {ex.Message}");
                return;
            }
            if (destroyHr < 0)
            {
                Defer(view, folder, $"DestroyViewWindow failed: {destroyHr:X8}");
                return;
            }
        }

        if (view != 0) release(view);
        if (folder != 0) release(folder);
    }

    // The caller must first destroy the parent HWND so failed child views cannot call a released site.
    internal void ReleaseDeferredAfterParentDestroyed()
    {
        foreach (var (view, folder) in _deferred)
        {
            if (view != 0) release(view);
            if (folder != 0) release(folder);
        }
        _deferred.Clear();
    }

    private void Defer(nint view, nint folder, string reason)
    {
        _deferred.Add((view, folder));
        report($"retaining view={view}; folder={folder}; {reason}");
    }
}
