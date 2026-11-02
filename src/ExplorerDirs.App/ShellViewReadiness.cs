namespace ExplorerDirs.App;

// Read availability is separate from the Shell-owned navigation history.
internal sealed class ShellViewReadiness
{
    private readonly Dictionary<string, int> _outstanding = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);
    private string? _latest;
    internal bool Pending { get; private set; } = true;
    internal long Generation { get; private set; }
    // Unlike the read generation, this does not change when an older request
    // settles. It identifies queued UI work belonging to the current visit.
    internal long NavigationVersion { get; private set; }
    internal bool HasOutstanding(string? identity) => _outstanding.ContainsKey(identity ?? "");
    internal bool RequiresLocationRead { get; private set; }
    internal bool IsCurrent(long version) => !Pending && version == NavigationVersion;

    internal void Begin(string? identity)
    {
        var key = identity ?? "";
        if (identity is null || _outstanding.ContainsKey(key)) _ambiguous.Add(key);
        _outstanding[key] = _outstanding.GetValueOrDefault(key) + 1;
        _latest = key;
        Pending = true;
        RequiresLocationRead = false;
        Generation++;
        NavigationVersion++;
    }

    internal bool Complete(string? identity) => End(identity);
    internal bool Fail(string? identity) => End(identity);

    private bool End(string? identity)
    {
        var key = identity ?? "";
        if (!_outstanding.TryGetValue(key, out var count)) return false;
        Generation++;
        if (count > 1) _outstanding[key] = count - 1;
        else _outstanding.Remove(key);
        var ambiguous = count == 1 && _ambiguous.Remove(key);
        // ExplorerBrowser callbacks carry a location, not a request token.
        // With repeated targets, wait for all matching callbacks rather than
        // guessing which request failed and exposing a still-changing view.
        if (_latest == key && count == 1)
        {
            Pending = false;
            _latest = null;
            RequiresLocationRead = ambiguous;
            return true;
        }
        return false;
    }

    internal void Reset()
    {
        _outstanding.Clear();
        _ambiguous.Clear();
        _latest = null;
        Pending = true;
        RequiresLocationRead = false;
        Generation++;
        NavigationVersion++;
    }
}
