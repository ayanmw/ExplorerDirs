namespace ExplorerDirs.NativeProbe;

// No scheduler or native ownership here: one Advance performs a bounded turn.
// Every external callback may pump Shell messages and retire this navigation.
internal sealed class ProjectedChangeDispatch(ProjectedChangeLedger queue, int maximumInFlight)
{
    private readonly List<(ProjectedChangeLedger.Work Work, ulong Ticket)> _pending = new();
    private bool _advancing;
    internal int PendingCount => _pending.Count;

    internal void Advance(Func<bool> current, Func<ProjectedChangeLedger.Work, ulong> dispatch,
        Func<ProjectedChangeLedger.Work, bool> ready, Action<ProjectedChangeLedger.Work, ulong> acknowledge)
    {
        if (_advancing) return;
        _advancing = true;
        try
        {
            bool Active()
            {
                if (!queue.Snapshot.Invalidated && current() && !queue.Snapshot.Invalidated) return true;
                queue.Invalidate();
                _pending.Clear();
                return false;
            }
            if (!Active()) return;
            foreach (var (work, _) in _pending)
            {
                var confirmed = ready(work);
                if (!Active() || !confirmed) return;
            }
            foreach (var (work, ticket) in _pending)
            {
                if (!Active()) return;
                if (work.Kind is ProjectedChangeKind.Deleted or ProjectedChangeKind.Renamed)
                {
                    if (!queue.IsCurrent(work)) throw new InvalidOperationException("Structural work requires reconciliation before acknowledgement.");
                    acknowledge(work, ticket);
                    if (!Active()) return;
                }
                queue.Release(work);
            }
            _pending.Clear();
            while (_pending.Count < maximumInFlight && queue.TryTake(out var work))
            {
                if (!Active()) return;
                var ticket = dispatch(work);
                if (!Active()) return;
                _pending.Add((work, ticket));
            }
        }
        catch
        {
            // A partial batch may already contain accepted native acknowledgements.
            // Retire it rather than retrying those tickets after an external failure.
            queue.Invalidate();
            _pending.Clear();
            throw;
        }
        finally { _advancing = false; }
    }
}
