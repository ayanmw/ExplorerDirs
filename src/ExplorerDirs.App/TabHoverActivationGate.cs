namespace ExplorerDirs.App;

internal sealed class TabHoverActivationGate
{
    private object? _target;

    internal bool HasPendingTarget => _target is not null;

    internal bool Begin(object target, bool isOwnTabDrag, bool hasShellLocationData, int delayMilliseconds)
    {
        if (isOwnTabDrag || !hasShellLocationData || delayMilliseconds == 0)
        {
            _target = null;
            return false;
        }
        if (ReferenceEquals(_target, target)) return false;
        _target = target;
        return true;
    }

    internal bool Leave(object target)
    {
        if (!ReferenceEquals(_target, target)) return false;
        _target = null;
        return true;
    }

    internal object? TakeTarget()
    {
        var target = _target;
        _target = null;
        return target;
    }

    internal void Cancel() => _target = null;
}
