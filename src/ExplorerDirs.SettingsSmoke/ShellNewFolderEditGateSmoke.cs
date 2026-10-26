using ExplorerDirs.App;

internal static class ShellNewFolderEditGateSmoke
{
    internal static void Run()
    {
        var gate = new ShellNewFolderEditGate();
        Check(!gate.ShouldWaitForEditor(renameEditorFocused: false),
            "a not-yet-accepted SelectItem can be retried");
        gate.MarkEditAccepted();
        Check(!gate.IsComplete(renameEditorFocused: false),
            "Shell accepting SVSI_EDIT is not proof that the rename editor owns focus");
        Check(gate.ShouldWaitForEditor(renameEditorFocused: false),
            "accepted edit stays pending while the native edit control has not appeared");
        Check(gate.ShouldWaitForEditor(renameEditorFocused: false),
            "a pending accepted edit is not sent repeatedly while the editor is absent");
        Check(gate.IsComplete(renameEditorFocused: true) && !gate.ShouldWaitForEditor(renameEditorFocused: true),
            "the operation completes only after the native rename editor receives focus");

        var failed = new ShellNewFolderEditGate();
        Check(!failed.ShouldWaitForEditor(renameEditorFocused: false) &&
              !failed.IsComplete(renameEditorFocused: false),
            "a rejected SelectItem attempt does not enter the accepted-but-pending state");

        Console.WriteLine("New-folder edit gate smoke: PASS");
    }

    private static void Check(bool valid, string message)
    { if (!valid) throw new Exception("New-folder edit gate: " + message); }
}
