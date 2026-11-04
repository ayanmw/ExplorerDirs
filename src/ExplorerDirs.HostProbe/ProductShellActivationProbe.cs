using System.Diagnostics;
using System.IO;
using System.Reflection;

internal static partial class FolderNavigationCallbacksProbe
{
    private static void CheckProviderRootComparison(string identity)
    {
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(SHParseDisplayName(identity, 0, out var pidl, 0, out _));
        nint folder = 0;
        try
        {
            var iid = new Guid("000214E6-0000-0000-C000-000000000046");
            System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(SHBindToParent(pidl, ref iid, out folder, out var child));
            var compare = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<ViewCallback>(
                System.Runtime.InteropServices.Marshal.ReadIntPtr(System.Runtime.InteropServices.Marshal.ReadIntPtr(folder), 7 * nint.Size));
            foreach (var flags in new uint[] { 0, 0x10000000 })
            {
                var result = compare(folder, flags, child, child);
                Ensure(result == 0, $"provider root marker must compare equal to itself; flags={flags:X}; hr={result:X8}");
            }
            System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(SHParseDisplayName(identity + "\\selected.txt", 0, out var leaf, 0, out _));
            try
            {
                var direct = ILIsParent(pidl, leaf, true);
                var recursive = ILIsParent(pidl, leaf, false);
                Console.WriteLine($"Provider encoded-root ancestry: direct={direct}; recursive={recursive}; no Shell view.");
                Ensure(direct && recursive, "encoded root must contain its own direct child for native notification matching");
            }
            finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(leaf); }
        }
        finally
        {
            if (folder != 0) System.Runtime.InteropServices.Marshal.Release(folder);
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pidl);
        }
        Console.WriteLine("Provider root marker comparison: PASS (name/canonical identity; no Shell view).");
    }

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
