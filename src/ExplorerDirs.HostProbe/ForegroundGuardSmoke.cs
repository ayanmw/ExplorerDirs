internal static class ForegroundGuardSmoke
{
    internal static int Run()
    {
        HostProbeForegroundGuard.EnsureUnchanged((nint)123, (nint)123, "smoke");
        try
        {
            HostProbeForegroundGuard.EnsureUnchanged((nint)123, (nint)456, "smoke");
            throw new InvalidOperationException("A changed foreground handle was accepted.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("foreground changed", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("foreground-guard unchanged=accepted changed=rejected");
            return 0;
        }
    }
}
