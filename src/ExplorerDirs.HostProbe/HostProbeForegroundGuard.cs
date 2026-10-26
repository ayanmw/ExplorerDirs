internal static class HostProbeForegroundGuard
{
    internal static void EnsureUnchanged(nint before, nint after, string stage)
    {
        if (before != after)
            throw new InvalidOperationException(
                $"Foreground changed during {stage}: before=0x{before:X}, after=0x{after:X}; probe stopped.");
    }
}
