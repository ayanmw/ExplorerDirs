namespace ExplorerDirs.App;

internal static class ProgramStarterEntryCommit
{
    internal static void Commit(IEnumerable<ProgramStarterEntry> proposed,
        Action<IEnumerable<ProgramStarterEntry>> persist,
        Action<IReadOnlyList<ProgramStarterEntry>> apply)
    {
        var snapshot = proposed.ToArray();
        persist(snapshot);
        apply(snapshot);
    }
}
