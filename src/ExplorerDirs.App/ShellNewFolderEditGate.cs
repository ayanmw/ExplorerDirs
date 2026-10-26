namespace ExplorerDirs.App;

internal sealed class ShellNewFolderEditGate
{
    private bool _editAccepted;

    internal void MarkEditAccepted() => _editAccepted = true;
    internal bool IsComplete(bool renameEditorFocused) => renameEditorFocused;
    internal bool ShouldWaitForEditor(bool renameEditorFocused) => _editAccepted && !renameEditorFocused;
}
