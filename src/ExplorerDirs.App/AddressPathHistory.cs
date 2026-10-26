namespace ExplorerDirs.App;

internal sealed class AddressPathHistory
{
    private const int MaximumEntries = 20;
    private readonly List<string> _paths = [];

    internal IReadOnlyList<string> Paths => _paths;

    internal void Record(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _paths.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        _paths.Insert(0, path);
        if (_paths.Count > MaximumEntries) _paths.RemoveRange(MaximumEntries, _paths.Count - MaximumEntries);
    }

    internal AddressPathHistory Copy()
    {
        var copy = new AddressPathHistory();
        copy._paths.AddRange(_paths);
        return copy;
    }
}
