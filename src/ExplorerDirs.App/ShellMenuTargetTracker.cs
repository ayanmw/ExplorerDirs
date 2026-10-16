namespace ExplorerDirs.App;

internal enum ShellMenuTarget { Unknown, Background, Item, KeyboardSelection }

internal readonly record struct ShellMenuPoint(int X, int Y);

// Keep the click's target separate from the Shell selection. A background click
// can leave an item selected, so selection alone cannot identify the menu target.
internal sealed class ShellMenuTargetTracker
{
    private readonly Func<long> _clock;
    private ShellMenuPoint? _mousePoint;
    private Task<ShellMenuTarget>? _mouseProbe;
    private long _sourceStartedAt;
    private bool _keyboard;

    public ShellMenuTargetTracker(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    public void MouseDown(ShellMenuPoint point, Func<ShellMenuPoint, Task<ShellMenuTarget>> probe)
    {
        _keyboard = false;
        _mousePoint = point;
        _sourceStartedAt = _clock();
        try { _mouseProbe = probe(point); }
        catch { _mouseProbe = Task.FromResult(ShellMenuTarget.Unknown); }
    }

    public void ContextMenu(nint lParam, Func<ShellMenuPoint, Task<ShellMenuTarget>> probe)
    {
        if (lParam == -1)
        {
            KeyboardMenuRequested();
            return;
        }
        var point = DecodeContextPoint(lParam);
        if (_keyboard || _mousePoint != point || _mouseProbe is null)
            MouseDown(point, probe);
    }

    public void KeyboardMenuRequested()
    {
        _keyboard = true;
        _sourceStartedAt = _clock();
        _mousePoint = null;
        _mouseProbe = null;
    }

    public ShellMenuTarget Resolve()
    {
        if (_clock() - _sourceStartedAt > 5000) return ShellMenuTarget.Unknown;
        if (_keyboard) return ShellMenuTarget.KeyboardSelection;
        if (_mousePoint is null) return ShellMenuTarget.Unknown;
        if (_mouseProbe is { IsFaulted: true })
        {
            _ = _mouseProbe.Exception;
            return ShellMenuTarget.Unknown;
        }
        if (_mouseProbe is not { IsCompletedSuccessfully: true }) return ShellMenuTarget.Unknown;
        return _mouseProbe.Result is ShellMenuTarget.Item or ShellMenuTarget.Background
            ? _mouseProbe.Result : ShellMenuTarget.Unknown;
    }

    public void Reset()
    {
        _keyboard = false;
        _mousePoint = null;
        _mouseProbe = null;
    }

    internal static ShellMenuPoint DecodeContextPoint(nint lParam)
    {
        var packed = lParam.ToInt64();
        return new ShellMenuPoint(unchecked((short)(packed & 0xffff)),
            unchecked((short)((packed >> 16) & 0xffff)));
    }
}
