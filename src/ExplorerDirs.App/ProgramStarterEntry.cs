namespace ExplorerDirs.App;

internal sealed record ProgramStarterEntry(
    Guid Id,
    string Name,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory);
