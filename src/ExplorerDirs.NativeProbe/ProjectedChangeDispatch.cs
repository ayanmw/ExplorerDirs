namespace ExplorerDirs.NativeProbe;

internal readonly record struct ProjectedDispatchResult(ulong Ticket, bool Reconciled = false,
    ProjectedChangeKind? ConfirmationKind = null);

// No scheduler or native ownership here: one Advance performs a bounded turn.
// Every external callback may pump Shell messages and retire this navigation.
internal sealed class ProjectedChangeDispatch(ProjectedChangeLedger queue, int maximumInFlight)
{
    private readonly List<(ProjectedChangeLedger.Work Work, ProjectedChangeLedger.Work Observation, ulong Ticket)> _pending = new();
    private bool _advancing;
    internal int PendingCount => _pending.Count;
    internal ProjectedChangeLedger.Work? LastUnconfirmed { get; private set; }

    internal void Advance(Func<bool> current, Func<ProjectedChangeLedger.Work, ProjectedDispatchResult> dispatch,
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
                LastUnconfirmed = null;
                return false;
            }
            if (!Active()) return;
            LastUnconfirmed = null;
            foreach (var (_, observation, _) in _pending)
            {
                var confirmed = ready(observation);
                if (!confirmed) LastUnconfirmed = observation;
                if (!Active() || !confirmed) return;
            }
            foreach (var (work, observation, ticket) in _pending)
            {
                if (!Active()) return;
                if (observation.Kind is ProjectedChangeKind.Deleted or ProjectedChangeKind.Renamed)
                {
                    // A rename's destination may already have a queued deletion.
                    // Path freshness is then false even though the old source ticket
                    // can retire safely. The callback must validate the native item
                    // generation, never erase facts by name alone.
                    acknowledge(observation, ticket);
                    if (!Active()) return;
                }
                queue.Release(work);
            }
            _pending.Clear();
            var sent = 0;
            while (sent++ < maximumInFlight && queue.TryTake(out var work))
            {
                if (!Active()) return;
                var result = dispatch(work);
                if (!Active()) return;
                if (result.Reconciled) queue.Release(work);
                else _pending.Add((work, result.ConfirmationKind is { } kind ? work with { Kind = kind } : work, result.Ticket));
            }
        }
        catch
        {
            // A partial batch may already contain accepted native acknowledgements.
            // Retire it rather than retrying those tickets after an external failure.
            queue.Invalidate();
            _pending.Clear();
            LastUnconfirmed = null;
            throw;
        }
        finally { _advancing = false; }
    }
}
