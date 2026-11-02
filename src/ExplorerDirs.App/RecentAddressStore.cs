using System.IO;

namespace ExplorerDirs.App;

internal sealed class RecentAddressStore
{
    private readonly FavoritesStore _store;
    private IReadOnlyList<string> _paths;
    internal IReadOnlyList<string> Paths => _paths;
    internal string? Warning { get; private set; }

    internal RecentAddressStore(string path)
    {
        _store = new FavoritesStore(path);
        _paths = _store.Load().Take(20).ToArray();
        Warning = _store.LoadWarning;
    }

    internal void Record(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            // Reload before merging histories from cooperating windows; the writer still rejects a later race.
            var latest = _store.Load();
            // Another window may have changed recency since our cached first item was read.
            // Skip rewriting only after checking the shared snapshot, and refresh our local menu too.
            if (latest.Count is > 0 and <= 20 && FavoritesStore.SamePath(latest[0], path))
            {
                _paths = latest.ToArray();
                Warning = _store.LoadWarning;
                return;
            }
            var proposed = new[] { path }.Concat(latest.Where(item => !FavoritesStore.SamePath(item, path))).Take(20).ToArray();
            _store.Save(proposed);
            _paths = proposed;
            Warning = null;
        }
        catch (IOException error) { Warning = error.Message; }
        catch (UnauthorizedAccessException error) { Warning = error.Message; }
    }
}
