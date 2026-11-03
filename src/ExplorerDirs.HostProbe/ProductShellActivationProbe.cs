using System.Diagnostics;
using System.IO;
using System.Reflection;

internal static partial class FolderNavigationCallbacksProbe
{
    private static void CheckUnconfiguredShellActivation(Guid classId, string adapterPath, string root)
    {
        // Unlike older enumeration probes, this child must not configure or preload the DLL.
        // Shell's notification infrastructure cannot inherit the application's native globals.
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location,
                     "--shell-provider-enumeration-child-unconfigured", classId.ToString("D"),
                     Path.GetFullPath(adapterPath), root, root }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Activation child did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(15000))
        {
            child.Kill(entireProcessTree: true);
            if (!child.WaitForExit(5000))
                throw new TimeoutException("Activation probe child did not exit within five seconds after termination.");
            throw new TimeoutException("Unconfigured Shell activation exceeded 15 seconds; owned probe child terminated.");
        }
        var expected = Directory.GetDirectories(root).Length;
        var text = output.GetAwaiter().GetResult();
        Ensure(child.ExitCode == 0 && text.Contains($"provider-enumeration-child: count={expected};", StringComparison.Ordinal),
            $"unconfigured process must activate and enumerate the registered provider; exit={child.ExitCode}; output={text}; error={errors.GetAwaiter().GetResult()}");
        Console.WriteLine($"Unconfigured product activation: PASS; folders={expected}; no application configuration in child.");
    }
}
