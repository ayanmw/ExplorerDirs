namespace ExplorerDirs.App;

// Location publication and input completion are separate: an older visit can
// still describe the displayed view without completing a newer submitted input.
public readonly record struct ShellNavigationNotice(string Value, long? RequestVersion);
