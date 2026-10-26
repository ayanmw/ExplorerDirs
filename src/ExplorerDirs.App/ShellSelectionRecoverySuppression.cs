using System.Collections.Generic;
using System.Threading;

namespace ExplorerDirs.App;

internal sealed class ShellSelectionRecoverySuppression
{
    private readonly object _sync = new();
    private readonly Dictionary<long, int> _activeLeases = [];

    internal bool IsSuppressed(long navigationVersion)
    {
        lock (_sync) return _activeLeases.ContainsKey(navigationVersion);
    }

    internal IDisposable Acquire(long navigationVersion)
    {
        lock (_sync)
            _activeLeases[navigationVersion] = _activeLeases.GetValueOrDefault(navigationVersion) + 1;
        return new Lease(this, navigationVersion);
    }

    private void Release(long navigationVersion)
    {
        lock (_sync)
        {
            if (!_activeLeases.TryGetValue(navigationVersion, out var count)) return;
            if (count == 1) _activeLeases.Remove(navigationVersion);
            else _activeLeases[navigationVersion] = count - 1;
        }
    }

    private sealed class Lease(ShellSelectionRecoverySuppression owner, long navigationVersion) : IDisposable
    {
        private ShellSelectionRecoverySuppression? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(navigationVersion);
    }
}
