using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellFocusRestoreGateSmoke
{
    internal static void Run()
    {
        Check(ShellFocusRestoreGate.CanContinue(true, true, true, true), expected: true,
            "pending file-operation focus restore remains monitored while its original view stays eligible");
        Check(ShellFocusRestoreGate.CanContinue(false, true, true, true), expected: false,
            "moving focus to an application control cancels pending file-operation focus restore");
        Check(ShellFocusRestoreGate.CanContinue(true, false, true, true), expected: false,
            "navigation or tab change cancels pending file-operation focus restore");
        Check(ShellFocusRestoreGate.CanContinue(true, true, false, true), expected: false,
            "new native user input cancels pending file-operation focus restore");
        Check(ShellFocusRestoreGate.CanContinue(true, true, true, false), expected: false,
            "file-operation focus retries stop at their deadline");
        Check(ShellFocusRestoreGate.ShouldKeepPendingRestoreAfterNativeInput(
                inputInFileContent: true, inputInColumnHeader: false, sameView: true, focusStillDesired: true), expected: true,
            "keyboard input delivered to the current file content does not cancel pending focus recovery");
        Check(ShellFocusRestoreGate.ShouldKeepPendingRestoreAfterNativeInput(
                inputInFileContent: true, inputInColumnHeader: true, sameView: true, focusStillDesired: true), expected: false,
            "column-header input cancels file-item focus recovery so sorting keeps header focus");
        Check(ShellFocusRestoreGate.ShouldKeepPendingRestoreAfterNativeInput(
                inputInFileContent: false, inputInColumnHeader: false, sameView: true, focusStillDesired: true), expected: false,
            "input delivered to another Shell control cancels pending file-content focus recovery");
        Check(ShellFocusRestoreGate.ShouldKeepPendingRestoreAfterNativeInput(
                inputInFileContent: true, inputInColumnHeader: false, sameView: false, focusStillDesired: true), expected: false,
            "input cannot keep a stale tab or navigation focus request alive");
        Check(ShellFocusRestoreGate.ShouldKeepPendingRestoreAfterNativeInput(
                inputInFileContent: true, inputInColumnHeader: false, sameView: true, focusStillDesired: false), expected: false,
            "file-content input does not restore focus after the pane stops being active");
        Check(ShellFocusRestoreGate.IsFileContentFocused(hasNativeFocus: true,
            focusWithinContentWindow: true, columnHeaderFocused: false,
            folderHasItems: true, folderItemFocused: true), expected: true,
            "a focused file item inside the native content window is recognized as content focus");
        Check(ShellFocusRestoreGate.IsFileContentFocused(hasNativeFocus: true,
            focusWithinContentWindow: false, columnHeaderFocused: false,
            folderHasItems: true, folderItemFocused: false, renameEditorFocused: true), expected: true,
            "an active Shell rename editor completes file-content focus recovery even before the folder view reports its focused item");
        Check(ShellFocusRestoreGate.IsFileContentFocused(hasNativeFocus: false,
            focusWithinContentWindow: false, columnHeaderFocused: false,
            folderHasItems: true, folderItemFocused: false, renameEditorFocused: true), expected: false,
            "a rename editor in another pane cannot complete this view's focus recovery");
        Check(ShellFocusRestoreGate.IsFileContentFocused(hasNativeFocus: true,
            focusWithinContentWindow: true, columnHeaderFocused: false,
            folderHasItems: true, folderItemFocused: false), expected: false,
            "content HWND focus without a focused Shell item keeps file-operation recovery active");
        Check(ShellFocusRestoreGate.IsFileContentFocused(hasNativeFocus: true,
            focusWithinContentWindow: true, columnHeaderFocused: false,
            folderHasItems: true, folderItemFocused: true), expected: true,
            "a focused Shell item makes content focus recovery complete");
        Check(ShellFocusRestoreGate.IsFileContentFocused(hasNativeFocus: true,
            focusWithinContentWindow: true, columnHeaderFocused: false,
            folderHasItems: false, folderItemFocused: false), expected: true,
            "empty folders can retain keyboard focus in their content window");
        Check(ShellFocusRestoreGate.IsFileContentFocused(hasNativeFocus: true,
            focusWithinContentWindow: true, columnHeaderFocused: true,
            folderHasItems: true, folderItemFocused: false), expected: false,
            "column-header focus is not mistaken for file-content focus");
        Check(ShellFocusRestoreGate.IsFileContentFocused(hasNativeFocus: true,
            focusWithinContentWindow: false, columnHeaderFocused: false,
            folderHasItems: true, folderItemFocused: false), expected: false,
            "another native Shell child is not mistaken for file-content focus");
        Check(ShellFocusRestoreGate.ShouldFocusContentWindow(focusWithinContentWindow: true,
            columnHeaderFocused: true), expected: true,
            "FocusView redirects column-header focus into the file-content window");
        Check(ShellFocusRestoreGate.ShouldFocusContentWindow(focusWithinContentWindow: true,
            columnHeaderFocused: false), expected: false,
            "FocusView retains a focused file item inside the content window");
        Check(ShellFocusRestoreGate.ShouldAttemptRestore(canContinue: true, windowActive: false,
                fileContentAlreadyFocused: false), expected: false,
            "focus retry stays pending without taking focus while the app window is inactive");
        Check(ShellFocusRestoreGate.ShouldAttemptRestore(canContinue: true, windowActive: true,
                fileContentAlreadyFocused: false), expected: true,
            "focus retry restores the file-content target when a sibling Shell control has focus");
        Check(ShellFocusRestoreGate.ShouldAttemptRestore(canContinue: true, windowActive: true,
                fileContentAlreadyFocused: true), expected: false,
            "focus monitor skips redundant SetFocus when file content already owns focus");
        Check(ShellFocusRestoreGate.CanRestoreForeground(foregroundIsEmpty: true, foregroundOwnedByThisWindow: false), expected: true,
            "file-operation focus may recover when Windows has no foreground window");
        Check(ShellFocusRestoreGate.CanRestoreForeground(foregroundIsEmpty: false, foregroundOwnedByThisWindow: true), expected: true,
            "file-operation focus may recover from a dialog owned by the same window");
        Check(ShellFocusRestoreGate.CanRestoreForeground(foregroundIsEmpty: false, foregroundOwnedByThisWindow: false), expected: false,
            "file-operation focus never reclaims foreground from another app or window");

        Check(ShellFocusRestoreGate.ResolveFocusItemIndex(-1, 2, 5), expected: 2,
            "selection-marked item is a safe focus fallback when Shell reports no focused item");
        Check(ShellFocusRestoreGate.ResolveFocusItemIndex(3, 2, 5), expected: 3,
            "valid Shell focused item takes precedence over selection mark");
        Check(ShellFocusRestoreGate.ResolveFocusItemIndex(-1, 8, 5), expected: -1,
            "invalid selection mark is not used as a focus target");
    }

    private static void Check(bool actual, bool expected, string name)
    {
        if (actual != expected) throw new Exception($"Failed: {name}");
        Console.WriteLine($"PASS {name}");
    }

    private static void Check(int actual, int expected, string name)
    {
        if (actual != expected) throw new Exception($"Failed: {name}; expected {expected}, got {actual}");
        Console.WriteLine($"PASS {name}");
    }
}
