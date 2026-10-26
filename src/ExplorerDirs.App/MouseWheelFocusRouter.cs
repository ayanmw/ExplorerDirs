namespace ExplorerDirs.App;

internal static class MouseWheelFocusRouter
{
    internal static bool TryRetarget(uint message, nuint wParam, bool enabled,
        nint currentTarget, nint hoveredTarget, bool isShellTarget, out nint target)
    {
        target = currentTarget;
        if (!enabled || message != 0x020A || (wParam & 0x0008) != 0 ||
            currentTarget == 0 || hoveredTarget == 0 || currentTarget == hoveredTarget || !isShellTarget)
            return false;

        target = hoveredTarget;
        return true;
    }
}
