using ExplorerDirs.App;

internal static class ShellNewFolderRefreshGateSmoke
{
    internal static void Run()
    {
        var focusCalls = 0;
        var currentRequest = false;
        if (ShellNewFolderRefreshGate.TryFocusForCommand(() => currentRequest, () =>
            {
                focusCalls++;
                return true;
            }) || focusCalls != 0)
            throw new Exception("A stale new-folder request must not move focus into the Shell view.");

        currentRequest = true;
        if (ShellNewFolderRefreshGate.TryFocusForCommand(() => currentRequest, () =>
            {
                focusCalls++;
                return false;
            }) || focusCalls != 1)
            throw new Exception("A failed Shell focus attempt must stop before invoking NewFolder.");

        if (ShellNewFolderRefreshGate.TryFocusForCommand(() => currentRequest, () =>
            {
                focusCalls++;
                currentRequest = false;
                return true;
            }) || focusCalls != 2)
            throw new Exception("A navigation change during Shell focus must stop the stale request.");

        var inputVersion = 4;
        var capturedInputVersion = inputVersion;
        if (ShellNewFolderRefreshGate.TryFocusForCommand(() => inputVersion == capturedInputVersion, () =>
            {
                focusCalls++;
                inputVersion++;
                return true;
            }) || focusCalls != 3)
            throw new Exception("User input during Shell focus must stop the stale request.");

        currentRequest = true;
        if (!ShellNewFolderRefreshGate.TryFocusForCommand(() => currentRequest, () =>
            {
                focusCalls++;
                return true;
            }) || focusCalls != 4)
            throw new Exception("A current request with a focused Shell view should be allowed to invoke NewFolder.");

        const string created = @"C:\parent\新建文件夹";
        var gate = new ShellNewFolderRefreshGate();
        var observed = new FolderCreationSnapshot([created], Failed: false);
        if (!gate.TryRequestRefresh(observed, @"C:\parent\initial.txt", operationCurrent: true, userInputUnchanged: true))
            throw new Exception("A uniquely observed folder missing from Shell focus should request one recovery refresh.");
        if (gate.TryRequestRefresh(observed, @"C:\parent\initial.txt", operationCurrent: true, userInputUnchanged: true))
            throw new Exception("A single new-folder operation must not request repeated recovery refreshes.");

        var focusedGate = new ShellNewFolderRefreshGate();
        if (focusedGate.TryRequestRefresh(observed, created, operationCurrent: true, userInputUnchanged: true))
            throw new Exception("A folder already focused by Shell should not be refreshed.");

        foreach (var (snapshot, current, unchanged) in new[]
        {
            (new FolderCreationSnapshot([], Failed: false), true, true),
            (new FolderCreationSnapshot([created, @"C:\parent\other"], Failed: false), true, true),
            (new FolderCreationSnapshot([created], Failed: true), true, true),
            (observed, false, true),
            (observed, true, false)
        })
        {
            if (new ShellNewFolderRefreshGate().TryRequestRefresh(snapshot, @"C:\parent\initial.txt", current, unchanged))
                throw new Exception("Ambiguous, failed, stale, or user-interrupted folder creation must not refresh the view.");
        }

        Console.WriteLine("New-folder refresh gate smoke: PASS");
    }
}
