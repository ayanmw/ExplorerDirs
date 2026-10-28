namespace ExplorerDirs.App;

internal static class MouseWheelFocusRouter
{
    internal static bool ShouldProcessGetMessage(int code, nuint retrievalMode) =>
        code >= 0 && retrievalMode == 1; // HC_ACTION and PM_REMOVE.

    internal static (int X, int Y) GetScreenPoint(nint lParam)
    {
        var value = lParam.ToInt64();
        return (unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
    }

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
