#if EXPLORERDIRS_PROJECTED_NOTIFY_EXPERIMENT
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace ExplorerDirs.App;

// One observer per Dispatcher, shared by the experimental pane monitors only.
// Inclusive operation duration may contain nested operations and native message loops.
internal sealed class ProjectedDispatcherTimingExperiment
{
    private static readonly ConditionalWeakTable<Dispatcher, ProjectedDispatcherTimingExperiment> Observers = new();
    private static readonly FieldInfo? MethodField = typeof(DispatcherOperation).GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<DispatcherOperation, (long Started, int Depth, string Method)> _active = new();
    private int _leases;
    private int _records;

    private ProjectedDispatcherTimingExperiment(Dispatcher dispatcher) => _dispatcher = dispatcher;

    internal static IDisposable Acquire(Dispatcher dispatcher)
    {
        dispatcher.VerifyAccess();
        var observer = Observers.GetValue(dispatcher, value => new(value));
        if (observer._leases++ == 0)
        {
            observer._records = 0;
            dispatcher.Hooks.OperationStarted += observer.Started;
            dispatcher.Hooks.OperationCompleted += observer.Completed;
            Write("attached");
        }
        return new Lease(observer);
    }

    private void Started(object? sender, DispatcherHookEventArgs args)
    {
        var method = MethodField?.GetValue(args.Operation) as Delegate;
        var label = method is null ? "unavailable" : $"{method.Method.DeclaringType?.FullName}.{method.Method.Name}";
        _active[args.Operation] = (Stopwatch.GetTimestamp(), _active.Count, label);
    }

    private void Completed(object? sender, DispatcherHookEventArgs args)
    {
        if (!_active.Remove(args.Operation, out var sample)) return;
        var elapsed = Stopwatch.GetElapsedTime(sample.Started).TotalMilliseconds;
        if (elapsed < 50 || _records >= 256) return;
        _records++;
        Write($"operation;ms={elapsed:F1};priority={args.Operation.Priority};depth={sample.Depth};method={sample.Method}");
    }

    private void Release()
    {
        _dispatcher.VerifyAccess();
        if (--_leases != 0) return;
        _dispatcher.Hooks.OperationStarted -= Started;
        _dispatcher.Hooks.OperationCompleted -= Completed;
        _active.Clear();
        Write($"detached;records={_records}");
    }

    private static void Write(string value)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "dispatcher-experiment.log"), $"{Environment.TickCount64} {value}\n"); }
        catch (IOException) { }
    }

    private sealed class Lease(ProjectedDispatcherTimingExperiment observer) : IDisposable
    {
        private ProjectedDispatcherTimingExperiment? _observer = observer;
        public void Dispose() => Interlocked.Exchange(ref _observer, null)?.Release();
    }
}
#endif
