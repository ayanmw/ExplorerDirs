namespace ExplorerDirs.NativeProbe;

// Isolates PIDL ownership and SBSP history transitions from view-window creation.
internal sealed class ShellViewNavigationHistory : IDisposable
{
    private const uint SbspParent = 0x2000;
    private const uint SbspNavigateBack = 0x4000;
    private const uint SbspNavigateForward = 0x8000;
    private const uint SbspRelative = 0x1000;
    private const uint SbspSameBrowser = 0x0001;
    private const uint SbspNewBrowser = 0x0002;
    private const uint SbspOpenMode = 0x0010;
    private const uint SbspExploreMode = 0x0020;
    private const uint SbspHelpMode = 0x0040;
    private const uint SbspKeepSameTemplate = 0x00020000;
    private const uint UnsupportedBrowseFlags = SbspNewBrowser | SbspHelpMode | SbspKeepSameTemplate;
    private const uint SbspCreateNoHistory = 0x00100000;
    private const int ENotImpl = unchecked((int)0x80004001);

    private readonly Func<nint, nint> _clone;
    private readonly Action<nint> _free;
    private readonly Func<nint, nint, bool> _equals;
    private readonly Func<nint, nint> _getParent;
    private readonly Func<nint, nint, nint> _combine;
    private readonly Func<nint, uint, int> _navigate;
    private readonly List<nint> _locations = [];
    private int _index;

    internal ShellViewNavigationHistory(nint rootPidl, Func<nint, nint> clone,
        Action<nint> free, Func<nint, nint, bool> equals, Func<nint, nint> getParent,
        Func<nint, nint, nint> combine,
        Func<nint, uint, int> navigate)
    {
        _clone = clone;
        _free = free;
        _equals = equals;
        _getParent = getParent;
        _combine = combine;
        _navigate = navigate;
        var root = _clone(rootPidl);
        if (root == 0) throw new InvalidOperationException("Could not clone the Shell navigation root PIDL.");
        _locations.Add(root);
    }

    internal int Index => _index;
    internal int Count => _locations.Count;
    internal nint CurrentPidl => _locations[_index];

    internal int BrowseObject(nint pidl, uint flags)
    {
        // Never downgrade unsupported requests into navigation that replaces the
        // current view. New-browser hosting is absent; HELPMODE and KEEPSAMETEMPLATE
        // are explicitly unsupported by the Shell contract.
        if ((flags & UnsupportedBrowseFlags) != 0) return ENotImpl;
        if ((flags & SbspExploreMode) != 0 && (flags & SbspSameBrowser) == 0) return ENotImpl;

        nint target = pidl;
        nint ownedTarget = 0;
        if ((flags & SbspParent) != 0)
        {
            ownedTarget = _getParent(CurrentPidl);
            target = ownedTarget;
        }
        else if ((flags & SbspNavigateBack) != 0)
            target = _index > 0 ? _locations[_index - 1] : 0;
        else if ((flags & SbspNavigateForward) != 0)
            target = _index + 1 < _locations.Count ? _locations[_index + 1] : 0;
        else if ((flags & SbspRelative) != 0)
        {
            ownedTarget = pidl == 0 ? 0 : _combine(CurrentPidl, pidl);
            target = ownedTarget;
        }

        if (target == 0) return ENotImpl;
        nint stagedLocation = 0;
        try
        {
            // Keep an owned copy before the navigation callback; parent targets
            // are temporary PIDLs and must not be read after BrowseObject frees them.
            if ((flags & (SbspNavigateBack | SbspNavigateForward)) == 0 &&
                !_equals(CurrentPidl, target))
            {
                stagedLocation = _clone(target);
                if (stagedLocation == 0)
                    throw new InvalidOperationException("Could not retain the navigated Shell PIDL.");
            }

            // The callback receives an absolute PIDL, so clear the category flag
            // after composing the caller's relative PIDL with the current location.
            var navigateFlags = flags & ~SbspRelative;
            if ((flags & SbspSameBrowser) != 0)
                navigateFlags &= ~(SbspOpenMode | SbspExploreMode);
            var hr = _navigate(target, navigateFlags);
            if (hr < 0) return hr;
            RecordSuccessfulNavigation(flags, stagedLocation);
            stagedLocation = 0;
            return hr;
        }
        finally
        {
            if (stagedLocation != 0) _free(stagedLocation);
            if (ownedTarget != 0) _free(ownedTarget);
        }
    }

    private void RecordSuccessfulNavigation(uint flags, nint stagedLocation)
    {
        if ((flags & SbspNavigateBack) != 0)
        {
            _index--;
            return;
        }
        if ((flags & SbspNavigateForward) != 0)
        {
            _index++;
            return;
        }
        if (stagedLocation == 0) return;
        if ((flags & SbspCreateNoHistory) != 0)
        {
            // Keep the active stop current while avoiding a new travel-log entry.
            for (var index = _locations.Count - 1; index > _index; index--)
            {
                _free(_locations[index]);
                _locations.RemoveAt(index);
            }
            _free(_locations[_index]);
            _locations[_index] = stagedLocation;
            return;
        }
        for (var index = _locations.Count - 1; index > _index; index--)
        {
            _free(_locations[index]);
            _locations.RemoveAt(index);
        }
        _locations.Add(stagedLocation);
        _index++;
    }

    public void Dispose()
    {
        foreach (var location in _locations) _free(location);
        _locations.Clear();
        _index = 0;
    }
}
