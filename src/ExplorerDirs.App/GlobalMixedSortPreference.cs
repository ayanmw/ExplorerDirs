using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ExplorerDirs.App;

// One portable preference per profile, shared by windows and reloaded across processes.
internal sealed class GlobalMixedSortPreference : IDisposable
{
    private readonly PortableBooleanPreference _store;
    private readonly FileSystemWatcher _watcher;
    private readonly Dispatcher _dispatcher;
    private DispatcherOperation? _reload;
    private bool _disposed;
    internal bool Value { get; private set; }
    internal event Action? Changed;
    internal static GlobalMixedSortPreference ForProfile(string profile, Dispatcher dispatcher)
    {
        var key = "ExplorerDirs.GlobalMixedSort:" + Path.GetFullPath(profile).ToUpperInvariant();
        if (Application.Current.Properties[key] is GlobalMixedSortPreference existing) return existing;
        var preference = new GlobalMixedSortPreference(profile, dispatcher);
        Application.Current.Properties[key] = preference;
        Application.Current.Exit += (_, _) => preference.Dispose();
        return preference;
    }
    private GlobalMixedSortPreference(string profile, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Directory.CreateDirectory(profile);
        _store = new PortableBooleanPreference(Path.Combine(profile, "Sorting.ini"), "Sorting", "Mixed", "混合排序");
        Value = _store.Load();
        _watcher = new FileSystemWatcher(profile, "Sorting.ini")
        { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        _watcher.Changed += (_, _) => QueueReload();
        _watcher.Created += (_, _) => QueueReload();
        _watcher.Deleted += (_, _) => QueueReload();
        _watcher.Renamed += (_, _) => QueueReload();
        _watcher.EnableRaisingEvents = true;
    }
    internal void Set(bool value)
    {
        _store.Save(value);
        if (Value == value) return;
        Value = value; Changed?.Invoke();
    }
    private void QueueReload()
    {
        if (_dispatcher.HasShutdownStarted || _disposed) return;
        try { _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_disposed || _reload is { Status: DispatcherOperationStatus.Pending }) return;
            _reload = _dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                _reload = null;
                if (_disposed) return;
                var value = _store.Load();
                if (_store.LoadWarning is not null || value == Value) return;
                Value = value; Changed?.Invoke();
            }));
        })); }
        catch (InvalidOperationException) { /* Dispatcher shutdown can race a watcher event. */ }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _reload?.Abort(); _watcher.Dispose();
    }
}
