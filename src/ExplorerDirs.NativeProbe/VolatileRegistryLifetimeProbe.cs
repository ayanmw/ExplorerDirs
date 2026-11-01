using System.Diagnostics;
using Microsoft.Win32;

namespace ExplorerDirs.NativeProbe;

internal static class VolatileRegistryLifetimeProbe
{
    internal static int Run()
    {
        var classId = Guid.NewGuid();
        var classPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespacePath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        var readyPath = Path.Combine(Path.GetTempPath(), "ExplorerDirs-volatile-registry-" + classId.ToString("N") + ".ready");
        Process? child = null;
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add(typeof(VolatileRegistryLifetimeProbe).Assembly.Location);
            start.ArgumentList.Add("--volatile-registry-owner-crash-child");
            start.ArgumentList.Add(classId.ToString("D"));
            start.ArgumentList.Add(readyPath);
            child = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the volatile-registry child process.");

            var readyDeadline = Environment.TickCount64 + 10_000;
            while (!File.Exists(readyPath) && !child.HasExited && Environment.TickCount64 < readyDeadline)
                Thread.Sleep(20);
            if (!File.Exists(readyPath))
                throw new TimeoutException($"Volatile-registry child did not publish its ready marker: {child.StandardOutput.ReadToEnd()}{child.StandardError.ReadToEnd()}");

            // Terminate without running managed cleanup, matching an abrupt app-process exit.
            child.Kill(entireProcessTree: true);
            if (!child.WaitForExit(10_000))
                throw new TimeoutException("The volatile-registry child did not terminate after Kill.");

            var classKeySurvived = Registry.CurrentUser.OpenSubKey(classPath) is not null;
            var namespaceKeySurvived = Registry.CurrentUser.OpenSubKey(namespacePath) is not null;
            Console.WriteLine($"volatile-registry-owner-crash: child-exited={child.HasExited}; exit-code={child.ExitCode}; clsid-key-survived={classKeySurvived}; namespace-key-survived={namespaceKeySurvived}");
            if (!classKeySurvived || !namespaceKeySurvived)
                throw new InvalidOperationException("Volatile Shell registration did not survive creator-process termination while the user hive remained loaded.");
            return 0;
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited)
                {
                    try { child.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    child.WaitForExit();
                }
                child.Dispose();
            }
            Registry.CurrentUser.DeleteSubKeyTree(namespacePath, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(classPath, throwOnMissingSubKey: false);
            if (File.Exists(readyPath)) File.Delete(readyPath);
        }
    }

    internal static int RunChild(string classIdText, string readyPath)
    {
        var classId = Guid.Parse(classIdText);
        var classPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespacePath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        using (var classKey = Registry.CurrentUser.CreateSubKey(classPath,
                   RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
               ?? throw new InvalidOperationException("Could not create volatile CLSID key."))
        {
            classKey.SetValue(null, "ExplorerDirs volatile owner crash probe", RegistryValueKind.String);
            using var inproc = classKey.CreateSubKey("InprocServer32", RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryOptions.Volatile) ?? throw new InvalidOperationException("Could not create volatile InprocServer32 key.");
            inproc.SetValue(null, typeof(VolatileRegistryLifetimeProbe).Assembly.Location, RegistryValueKind.String);
        }
        using (var namespaceKey = Registry.CurrentUser.CreateSubKey(namespacePath,
                   RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
               ?? throw new InvalidOperationException("Could not create volatile Desktop NameSpace key."))
            namespaceKey.SetValue(null, "ExplorerDirs volatile owner crash probe", RegistryValueKind.String);

        File.WriteAllText(readyPath, "registered");
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }
}
