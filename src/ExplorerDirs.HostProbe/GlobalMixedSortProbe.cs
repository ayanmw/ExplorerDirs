using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

internal static class GlobalMixedSortProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static int Run()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var assembly = Assembly.Load("ExplorerDirs.App");
        var type = assembly.GetType("ExplorerDirs.App.GlobalMixedSortPreference", true)!;
        var browserType = assembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", true)!;
        var viewType = assembly.GetType("ExplorerDirs.App.ViewState", true)!;
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-GlobalSort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var factory = type.GetMethod("ForProfile", BindingFlags.Static | BindingFlags.NonPublic)!;
        var preference = factory.Invoke(null, [root, Dispatcher.CurrentDispatcher])!;
        var same = factory.Invoke(null, [root, Dispatcher.CurrentDispatcher])!;
        var peer = type.GetConstructors(Instance).Single().Invoke([root, Dispatcher.CurrentDispatcher]);
        var hosts = new List<object>();
        bool Value(object value) => (bool)type.GetProperty("Value", Instance)!.GetValue(value)!;
        void Set(bool value) => type.GetMethod("Set", Instance)!.Invoke(preference, [value]);
        void Apply()
        {
            foreach (var host in hosts) browserType.GetMethod("ConfigureGlobalMixedSort", Instance)!.Invoke(host, [Value(preference)]);
        }
        void Add(bool historical, uint mode)
        {
            var state = viewType.GetMethod("Default", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [mode, 16])!;
            viewType.GetProperty("MixedSizeSort")!.SetValue(state, historical);
            var constructor = browserType.GetConstructor(Instance, null, [typeof(string), viewType, typeof(bool)], null)!;
            var host = constructor.Invoke([root, state, false]); hosts.Add(host); Apply();
        }
        bool Cached(object host)
        {
            var state = browserType.GetMethod("CachedViewState", Instance)!.Invoke(host, null)!;
            return (bool)viewType.GetProperty("MixedSizeSort")!.GetValue(state)!;
        }
        Action changed = Apply;
        type.GetEvent("Changed", Instance)!.GetAddMethod(true)!.Invoke(preference, [changed]);
        try
        {
            if (!ReferenceEquals(preference, same) || Value(preference)) throw new Exception("Profile singleton/default failed.");
            Add(true, 3); Add(false, 4);
            if (hosts.Any(Cached)) throw new Exception("Historical tab value overrode global default.");
            Set(true);
            if (hosts.Any(host => !Cached(host))) throw new Exception("Existing panes did not apply global true.");
            Add(false, 4);
            if (!Cached(hosts[^1])) throw new Exception("New tab did not inherit global policy.");
            PumpUntil(() => Value(peer));
            if (!File.ReadAllText(Path.Combine(root, "Sorting.ini")).Contains("Mixed=true", StringComparison.OrdinalIgnoreCase)) throw new Exception("Global INI missing.");
            Set(false);
            PumpUntil(() => !Value(peer));
            if (hosts.Any(Cached)) throw new Exception("Global false did not apply to all hosts.");
            var restarted = type.GetConstructors(Instance).Single().Invoke([root, Dispatcher.CurrentDispatcher]);
            try { if (Value(restarted)) throw new Exception("Restart did not preserve false."); }
            finally { ((IDisposable)restarted).Dispose(); }
            Console.WriteLine("Global mixed sorting: PASS (one profile service, old/new uncreated production hosts, default, both directions, actual INI, watcher peer and restart; no visible window).");
            return 0;
        }
        finally
        {
            type.GetEvent("Changed", Instance)!.GetRemoveMethod(true)!.Invoke(preference, [changed]);
            ((IDisposable)peer).Dispose(); ((IDisposable)preference).Dispose();
            foreach (var host in hosts) ((IDisposable)host).Dispose();
            app.Shutdown();
        }
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(20);
        }
        if (!condition()) throw new Exception("Profile watcher did not propagate the global setting.");
    }
}
