using System.Windows.Media;

namespace ExplorerDirs.App;

// UI-thread icon reuse only. Eviction removes cache references, never disposes
// images still held by tab/menu controls; a miss uses the existing Shell loader.
internal sealed class ShellIconCache
{
    private const int MaximumEntries = 256;
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _recent = new();
    private sealed record Entry(string Key, ImageSource? Image);

    internal int Count => _entries.Count;

    internal ImageSource? this[string key]
    {
        set
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.Value = new Entry(existing.Value.Key, value);
                _recent.Remove(existing);
                _recent.AddFirst(existing);
                return;
            }
            _entries.Add(key, _recent.AddFirst(new Entry(key, value)));
            if (_entries.Count <= MaximumEntries) return;
            var oldest = _recent.Last!;
            _entries.Remove(oldest.Value.Key);
            _recent.RemoveLast();
        }
    }

    internal bool TryGetValue(string key, out ImageSource? image)
    {
        image = null;
        if (!_entries.TryGetValue(key, out var entry)) return false;
        _recent.Remove(entry);
        _recent.AddFirst(entry);
        image = entry.Value.Image;
        return true;
    }
}
