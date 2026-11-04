using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

// Reuse the existing real product HwndHost test on the never-switched Desktop.
// Loading HostProbe here keeps its WPF/App types out of the desktop-monitor thread.
internal static class ProductFourPaneHostProbe
{
    internal static int Run(string hostProbePath)
    {
        hostProbePath = Path.GetFullPath(hostProbePath);
        var directory = Path.GetDirectoryName(hostProbePath)!;
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name) =>
            name.Name == "ExplorerDirs.App"
                ? context.LoadFromAssemblyPath(Path.Combine(directory, "ExplorerDirs.App.dll")) : null;
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(hostProbePath);
            var type = assembly.GetType("ProfileOwnerMultiRootShellHostProbe", throwOnError: true)!;
            var run = type.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!;
            // Small fixture, actual product DLL, independent saved sorts in four hosts.
            return (int)run.Invoke(null, [false, 0, false, 8192, false])!;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }
}
