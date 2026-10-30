namespace ExplorerDirs.App;

internal sealed class ShellViewRedrawLeaseCoordinator
{
    private sealed class ActiveLease(ShellViewRedrawLease lease)
    {
        internal ShellViewRedrawLease Lease { get; } = lease;
        internal int ReferenceCount { get; set; } = 1;
    }

    private readonly Dictionary<long, ActiveLease> _active = [];

    // Refreshes and their completion timers run on the owning WPF dispatcher.
    internal IDisposable? Acquire(long viewGeneration, Func<ShellViewRedrawLease?> createLease)
    {
        ArgumentNullException.ThrowIfNull(createLease);
        if (_active.TryGetValue(viewGeneration, out var active))
        {
            active.ReferenceCount++;
            return new LeaseReference(this, viewGeneration, active);
        }

        var lease = createLease();
        if (lease is null) return null;
        active = new ActiveLease(lease);
        _active.Add(viewGeneration, active);
        return new LeaseReference(this, viewGeneration, active);
    }

    private void Release(long viewGeneration, ActiveLease active)
    {
        if (!_active.TryGetValue(viewGeneration, out var current) || !ReferenceEquals(current, active)) return;
        if (--active.ReferenceCount != 0) return;
        _active.Remove(viewGeneration);
        active.Lease.Dispose();
    }

    private sealed class LeaseReference(ShellViewRedrawLeaseCoordinator owner, long viewGeneration,
        ActiveLease active) : IDisposable
    {
        private ShellViewRedrawLeaseCoordinator? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(viewGeneration, active);
    }
}
