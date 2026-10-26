namespace ExplorerDirs.App;

internal sealed class ShellStatusCountRefreshGate
{
    private bool _pending;

    internal bool Request(bool isCurrentTab, bool isVisible)
    {
        if (!isCurrentTab || !isVisible) return false;
        _pending = true;
        return true;
    }

    internal bool TryConsume(bool isVisible)
    {
        if (!_pending) return false;
        _pending = false;
        return isVisible;
    }

    internal void Cancel() => _pending = false;
}
