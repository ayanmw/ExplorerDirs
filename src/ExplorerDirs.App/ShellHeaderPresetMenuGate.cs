namespace ExplorerDirs.App;

internal sealed class ShellHeaderPresetMenuGate
{
    private bool _pending;

    internal bool BeginRightClick(nuint keyFlags, string? windowClass)
    {
        Reset();
        _pending = ShellHeaderPresetShortcut.IsControlRightClick(keyFlags, windowClass);
        return _pending;
    }

    internal bool TryConsume(ShellMenuPoint contextPoint, out ShellMenuPoint menuPoint)
    {
        menuPoint = default;
        if (!_pending) return false;
        _pending = false;
        if (contextPoint == new ShellMenuPoint(-1, -1)) return false;
        menuPoint = contextPoint;
        return true;
    }

    internal void Reset() => _pending = false;
}
