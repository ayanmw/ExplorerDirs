namespace ExplorerDirs.App;

internal static class ShellFocusRestoreGate
{
    internal static bool CanContinue(bool focusStillDesired, bool sameView, bool userInputUnchanged,
        bool withinRetryWindow) => focusStillDesired && sameView && userInputUnchanged && withinRetryWindow;

    internal static bool IsFileContentFocused(bool hasNativeFocus, bool focusWithinContentWindow,
        bool columnHeaderFocused, bool folderHasItems, bool folderItemFocused, bool renameEditorFocused = false) =>
        hasNativeFocus && !columnHeaderFocused && (renameEditorFocused ||
        (focusWithinContentWindow && (!folderHasItems || folderItemFocused)));

    internal static int ResolveFocusItemIndex(int focusedIndex, int selectionMarkedIndex, int itemCount)
    {
        if (itemCount <= 0) return -1;
        if ((uint)focusedIndex < (uint)itemCount) return focusedIndex;
        return (uint)selectionMarkedIndex < (uint)itemCount ? selectionMarkedIndex : -1;
    }

    internal static bool ShouldAttemptRestore(bool canContinue, bool windowActive,
        bool fileContentAlreadyFocused) => canContinue && windowActive && !fileContentAlreadyFocused;

    internal static long ResolveRetryWindowStartTimestamp(long startedAt, long now, bool windowActive) =>
        windowActive ? startedAt : now;

    internal static bool ShouldKeepPendingRestoreAfterNativeInput(bool inputInFileContent,
        bool inputInColumnHeader, bool sameView, bool focusStillDesired) =>
        inputInFileContent && !inputInColumnHeader && sameView && focusStillDesired;

    internal static bool ShouldFocusContentWindow(bool focusWithinContentWindow, bool columnHeaderFocused) =>
        !focusWithinContentWindow || columnHeaderFocused;

    internal static bool CanRestoreForeground(bool foregroundIsEmpty, bool foregroundOwnedByThisWindow) =>
        foregroundIsEmpty || foregroundOwnedByThisWindow;
}
