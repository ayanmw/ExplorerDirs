namespace ExplorerDirs.App;

/// <summary>
/// Lightweight, copyable location history for a tab. Native Shell travel logs
/// are browser-owned and cannot be transferred to a duplicated ExplorerBrowser.
/// </summary>
internal sealed class ShellTabNavigationHistory
{
    private const int MaximumStops = 100;
    internal sealed record Stop(string Location, IReadOnlyList<string> SelectedItems);

    private readonly List<Stop> _locations;
    private int _index;
    private int? _pendingIndex;

    internal ShellTabNavigationHistory(string initialLocation)
    {
        _locations = [new(initialLocation, [])];
    }

    private ShellTabNavigationHistory(IReadOnlyList<Stop> locations, int index)
    {
        if (locations.Count == 0) throw new ArgumentException("Navigation history cannot be empty.", nameof(locations));
        if ((uint)index >= (uint)locations.Count) throw new ArgumentOutOfRangeException(nameof(index));
        // Stops and their selection lists are immutable snapshots; copy the history
        // container while sharing selection storage with the cloned view.
        _locations = locations.ToList();
        _index = index;
    }

    internal string Current => _locations[_index].Location;
    internal IReadOnlyList<string> CurrentSelection => _locations[_index].SelectedItems;
    internal int Index => _index;
    internal bool HasPendingMove => _pendingIndex is not null;
    internal bool CanGoBack => _pendingIndex is null && _index > 0;
    internal bool CanGoForward => _pendingIndex is null && _index < _locations.Count - 1;

    internal ShellTabNavigationHistory Clone() => new(_locations, _index);

    internal IReadOnlyList<Stop> Snapshot() => _locations.ToArray();

    internal void UpdateCurrentSelection(IEnumerable<string> selectedItems)
    {
        var selected = selectedItems.Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _locations[_index] = _locations[_index] with { SelectedItems = Array.AsReadOnly(selected) };
    }

    internal void Record(string location)
    {
        if (_pendingIndex is { } pending && SameLocation(_locations[pending].Location, location))
        {
            _index = pending;
            _pendingIndex = null;
            return;
        }
        _pendingIndex = null;
        if (SameLocation(Current, location)) return;
        if (_index < _locations.Count - 1)
            _locations.RemoveRange(_index + 1, _locations.Count - _index - 1);
        _locations.Add(new(location, []));
        _index = _locations.Count - 1;
        if (_locations.Count > MaximumStops)
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
        location = _locations[target].Location;
        return true;
    }

    internal bool CommitLocation(string location)
    {
        if (_pendingIndex is not { } pending || !SameLocation(_locations[pending].Location, location))
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
