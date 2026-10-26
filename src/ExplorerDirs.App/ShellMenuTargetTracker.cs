namespace ExplorerDirs.App;

using System.Windows.Automation;

internal enum ShellMenuTarget { Unknown, Background, Item, KeyboardSelection }

internal readonly record struct ShellMenuPoint(int X, int Y);

internal static class ShellMenuHitTrace
{
    internal static bool IsEnabled(string? path) => !string.IsNullOrEmpty(path);

    internal static string Format(ShellMenuPoint point, ShellMenuTarget target, IReadOnlyList<ControlType> controls) =>
        $"context-menu-hit x={point.X} y={point.Y} target={target} controls={string.Join('/', controls.Select(control => control.ProgrammaticName))}";
}

// Keep the click's target separate from the Shell selection. A background click
// can leave an item selected, so selection alone cannot identify the menu target.
internal sealed class ShellMenuTargetTracker
{
    private const uint LvhtExOnContents = 0x04000000; // Vista: hit item icon/text content, excluding background.
    private readonly Func<long> _clock;
    private ShellMenuPoint? _mousePoint;
    private Task<ShellMenuTarget>? _mouseProbe;
    private long _sourceStartedAt;
    private bool _keyboard;
    private bool _allowCustomCommands;
    internal bool CanInjectCustomCommands => _allowCustomCommands;

    public ShellMenuTargetTracker(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    public void MouseDown(ShellMenuPoint? point, Func<ShellMenuPoint, Task<ShellMenuTarget>> probe,
        bool allowCustomCommands = true)
    {
        Reset();
        _allowCustomCommands = allowCustomCommands;
        if (point is not { } location) return;
        _keyboard = false;
        _mousePoint = location;
        _sourceStartedAt = _clock();
        try { _mouseProbe = probe(location); }
        catch { _mouseProbe = Task.FromResult(ShellMenuTarget.Unknown); }
    }

    public void ContextMenu(nint lParam, Func<ShellMenuPoint, Task<ShellMenuTarget>> probe,
        bool allowCustomCommands = true)
    {
        if (IsKeyboardContextMenu(lParam))
        {
            KeyboardMenuRequested(allowCustomCommands);
            return;
        }
        var point = DecodeContextPoint(lParam);
        if (_keyboard || _mousePoint != point || _mouseProbe is null)
            MouseDown(point, probe, allowCustomCommands);
    }

    public void KeyboardMenuRequested(bool allowCustomCommands = true)
    {
        _allowCustomCommands = allowCustomCommands;
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
        _allowCustomCommands = false;
        _keyboard = false;
        _mousePoint = null;
        _mouseProbe = null;
    }

    internal static bool IsNativeListItemHit(int itemIndex, uint hitFlags) =>
        itemIndex >= 0 && (hitFlags & (0x000E | LvhtExOnContents)) != 0;

    internal static bool CanUseSelectionForCommands(ShellMenuTarget target) =>
        target is ShellMenuTarget.Item or ShellMenuTarget.KeyboardSelection;

    internal static ShellMenuTarget ResolveNativeListViewHitTest(bool available, int itemIndex, uint hitFlags)
    {
        if (!available) return ShellMenuTarget.Unknown;
        return IsNativeListItemHit(itemIndex, hitFlags) ? ShellMenuTarget.Item : ShellMenuTarget.Background;
    }

    internal static ShellMenuPoint DecodeContextPoint(nint lParam)
    {
        var packed = lParam.ToInt64();
        return new ShellMenuPoint(unchecked((short)(packed & 0xffff)),
            unchecked((short)((packed >> 16) & 0xffff)));
    }

    private static bool IsKeyboardContextMenu(nint lParam) =>
        unchecked((uint)lParam.ToInt64()) == uint.MaxValue;
}
