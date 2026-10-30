namespace ExplorerDirs.App;

/// <summary>
/// Lightweight, copyable location history for a tab. Native Shell travel logs
/// are browser-owned and cannot be transferred to a duplicated ExplorerBrowser.
/// </summary>
internal sealed class ShellTabNavigationHistory
{
    private const int MaximumLocations = 100;
    private readonly List<string> _locations;
    private int _index;
    private int? _pendingIndex;

    internal ShellTabNavigationHistory(string initialLocation)
    {
        _locations = [initialLocation];
    }

    private ShellTabNavigationHistory(IReadOnlyList<string> locations, int index)
    {
        if (locations.Count == 0) throw new ArgumentException("Navigation history cannot be empty.", nameof(locations));
        if ((uint)index >= (uint)locations.Count) throw new ArgumentOutOfRangeException(nameof(index));
        _locations = locations.ToList();
        _index = index;
    }

    internal string Current => _locations[_index];
    internal int Index => _index;
    internal bool CanGoBack => _pendingIndex is null && _index > 0;
    internal bool CanGoForward => _pendingIndex is null && _index < _locations.Count - 1;

    internal static ShellTabNavigationHistory FromSnapshot(IReadOnlyList<string> locations, int index) =>
        new(locations, index);

    internal IReadOnlyList<string> Snapshot() => _locations.ToArray();

    internal void Record(string location)
    {
        if (_pendingIndex is { } pending && SameLocation(_locations[pending], location))
        {
            _index = pending;
            _pendingIndex = null;
            return;
        }
        _pendingIndex = null;
        if (SameLocation(Current, location)) return;
        if (_index < _locations.Count - 1)
            _locations.RemoveRange(_index + 1, _locations.Count - _index - 1);
        _locations.Add(location);
        _index = _locations.Count - 1;
        if (_locations.Count > MaximumLocations)
        {
            _locations.RemoveAt(0);
            _index--;
        }
    }

    internal bool TryBeginMove(bool back, out string location)
    {
        var target = back ? _index - 1 : _index + 1;
        if (_pendingIndex is not null || target < 0 || target >= _locations.Count)
        {
            location = string.Empty;
            return false;
        }
        _pendingIndex = target;
        location = _locations[target];
        return true;
    }

    internal bool CommitLocation(string location)
    {
        if (_pendingIndex is not { } pending || !SameLocation(_locations[pending], location))
            return false;
        _index = pending;
        _pendingIndex = null;
        return true;
    }

    internal bool CancelPendingMove()
    {
        if (_pendingIndex is null) return false;
        _pendingIndex = null;
        return true;
    }

    private static bool SameLocation(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
