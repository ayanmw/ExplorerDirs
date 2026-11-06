using System.Diagnostics.CodeAnalysis;

namespace ExplorerDirs.NativeProbe;

internal enum ProjectedChangeKind { Created, Changed, Deleted, Renamed }

// Prototype coordination only: retain every event in order. Superseded work still
// needs reconciliation; freshness alone does not authorize dropping a Shell event.
internal sealed class ProjectedChangeLedger
{
    internal sealed record Work(ProjectedChangeLedger Owner, long Sequence,
        ProjectedChangeKind Kind, string Name, string? NewName);

    private readonly object _gate = new();
    private readonly Queue<Work> _pending = new();
    private readonly Dictionary<string, Work> _latest = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;
    private bool _invalidated;

    internal Work? Record(ProjectedChangeKind kind, string name, string? newName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (kind == ProjectedChangeKind.Renamed) ArgumentException.ThrowIfNullOrEmpty(newName);
        lock (_gate)
        {
            if (_invalidated) return null;
            var work = new Work(this, ++_sequence, kind, name, newName);
            _pending.Enqueue(work);
            _latest[name] = work;
            if (kind == ProjectedChangeKind.Renamed) _latest[newName!] = work;
            return work;
        }
    }

    internal bool TryTake([NotNullWhen(true)] out Work? work)
    {
        lock (_gate) return _pending.TryDequeue(out work);
    }

    internal bool IsCurrent(Work work)
    {
        lock (_gate)
            return !_invalidated && ReferenceEquals(work.Owner, this) && Matches(work.Name, work) &&
                (work.Kind != ProjectedChangeKind.Renamed || Matches(work.NewName!, work));
    }

    internal void Release(Work work)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(work.Owner, this)) return;
            // A chained rename can supersede only one endpoint. Retire the other
            // endpoint too, without erasing the successor's entry.
            if (Matches(work.Name, work)) _latest.Remove(work.Name);
            if (work.Kind == ProjectedChangeKind.Renamed && Matches(work.NewName!, work))
                _latest.Remove(work.NewName!);
        }
    }

    internal void Invalidate()
    {
        lock (_gate)
        {
            _invalidated = true;
            _pending.Clear();
            _latest.Clear();
        }
    }

    internal (int Queued, int TrackedPaths, bool Invalidated) Snapshot
    {
        get { lock (_gate) return (_pending.Count, _latest.Count, _invalidated); }
    }

    private bool Matches(string name, Work work) =>
        _latest.TryGetValue(name, out var current) && ReferenceEquals(current, work);
}
