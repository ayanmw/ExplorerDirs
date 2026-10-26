using ExplorerDirs.App;

internal static class ShellFileCommandsSmoke
{
    internal static void Run()
    {
        foreach (var key in new[] { 0x25, 0x26, 0x27, 0x28 })
            Check(ShellArrowKeyRouting.ShouldForward(0x0100, key, shellResult: 1,
                    fileContentFocused: true, columnHeaderFocused: false, editFocused: false),
                $"an untranslated arrow key 0x{key:X2} stays in native file content");
        Check(!ShellArrowKeyRouting.ShouldForward(0x0100, 0x25, shellResult: 0,
                fileContentFocused: true, columnHeaderFocused: false, editFocused: false) &&
              !ShellArrowKeyRouting.ShouldForward(0x0100, 0x25, shellResult: unchecked((int)0x80004005),
                fileContentFocused: true, columnHeaderFocused: false, editFocused: false),
            "only S_FALSE, never success or a Shell error, enables arrow fallback");
        Check(!ShellArrowKeyRouting.ShouldForward(0x0101, 0x25, 1, true, false, false) &&
              !ShellArrowKeyRouting.ShouldForward(0x0104, 0x25, 1, true, false, false) &&
              !ShellArrowKeyRouting.ShouldForward(0x0100, 0x41, 1, true, false, false),
            "key-up, system-key and non-arrow messages stay on their existing routes");
        Check(!ShellArrowKeyRouting.ShouldForward(0x0100, 0x25, 1, false, false, false) &&
              !ShellArrowKeyRouting.ShouldForward(0x0100, 0x25, 1, true, true, false) &&
              !ShellArrowKeyRouting.ShouldForward(0x0100, 0x25, 1, true, false, true),
            "arrows do not bypass WPF navigation outside file content, in headers or in editors");

        Check(ShellFileKeyboardActions.Resolve(0x41, 2) == ShellFileKeyboardAction.SelectAll &&
            ShellFileKeyboardActions.Resolve(0x58, 2) == ShellFileKeyboardAction.Cut,
            "Ctrl+A and Ctrl+X stay on the native Shell file-view command path");
        Check(KeyboardShortcuts.Resolve(0x2E, 4) == ExplorerShortcut.None,
            "Shift+Delete is not consumed by the application shortcut table");
        Check(ShellFileKeyboardActions.Resolve(0x43, 2) == ShellFileKeyboardAction.Copy &&
            ShellFileKeyboardActions.Resolve(0x2D, 2) == ShellFileKeyboardAction.Copy &&
            ShellFileKeyboardActions.Resolve(0x56, 2) == ShellFileKeyboardAction.Paste &&
            ShellFileKeyboardActions.Resolve(0x2D, 4) == ShellFileKeyboardAction.Paste &&
            ShellFileKeyboardActions.Resolve(0x2D, 6) == ShellFileKeyboardAction.None,
            "Ctrl+C/Insert copy and Ctrl+V/Shift+Insert paste without claiming modified Insert keys");
        Check(ShellFileKeyboardActions.Resolve(0x5A, 2) == ShellFileKeyboardAction.Undo &&
            ShellFileKeyboardActions.Resolve(0x5A, 0) == ShellFileKeyboardAction.None &&
            ShellFileKeyboardActions.Resolve(0x5A, 6) == ShellFileKeyboardAction.None,
            "Ctrl+Z maps to Shell Undo without claiming plain Z or Ctrl+Shift+Z");
        Check(ShellFileKeyboardActions.Resolve(0x59, 2).ToString() == "Redo" &&
            ShellFileKeyboardActions.Resolve(0x59, 0) == ShellFileKeyboardAction.None &&
            ShellFileKeyboardActions.Resolve(0x59, 6) == ShellFileKeyboardAction.None,
            "Ctrl+Y maps to Shell Redo without claiming plain Y or Ctrl+Shift+Y");
        var menus = new List<FakeMenu>();
        var canCopy = true;
        IShellCommandMenu Open(bool background)
        {
            var menu = new FakeMenu(background ? ["paste", "undo", "redo", "NewFolder"] : canCopy ? ["copy", "properties"] : []);
            menus.Add(menu);
            return menu;
        }
        var states = ShellFileCommands.Query(Open);
        Check(states[ShellFileCommand.Copy] && states[ShellFileCommand.Paste] &&
            states[ShellFileCommand.Undo] && states[ShellFileCommand.Redo] &&
            states[ShellFileCommand.NewFolder] && !states[ShellFileCommand.Delete], "native availability is honored");
        Check(menus.Count == 2 && menus.All(m => m.Disposed), "query opens and releases each target once");
        canCopy = false;
        Check(ShellFileCommands.Invoke(ShellFileCommand.Copy, Open) is null &&
            menus.All(m => m.Invocations == 0), "fresh selection overrides a previously enabled button");
        Check(ShellFileCommands.Invoke(ShellFileCommand.Paste, Open) == 0 &&
            menus[^1].Invocations == 1 && menus[^1].Disposed, "enabled paste invokes and releases background menu");
        Check(ShellFileCommands.Invoke(ShellFileCommand.Redo, Open) == 0 &&
            menus[^1].Invocations == 1 && menus[^1].Disposed, "enabled redo invokes and releases background menu");
        var disabledRedo = new FakeMenu(["undo"]);
        Check(ShellFileCommands.Invoke(ShellFileCommand.Redo, _ => disabledRedo) is null &&
            disabledRedo.Invocations == 0 && disabledRedo.Disposed,
            "redo is not invoked when the current Shell background menu reports it disabled");
        var staleRedo = new FakeMenu(["redo"]);
        Check(ShellFileCommands.Invoke(ShellFileCommand.Redo,
                background => background ? staleRedo : throw new Exception("Redo queried the selected-item menu."),
                () => false) is null && staleRedo.Invocations == 0 && staleRedo.Disposed,
            "a stale pane or navigation context cancels Redo before native invocation");
        Check(ShellFileCommands.Query(_ => null).Values.All(enabled => !enabled), "unavailable view disables commands");
        var contextCurrent = true;
        var queryMenus = new List<FakeMenu>();
        var invalidStates = ShellFileCommands.Query(background =>
        {
            var menu = new FakeMenu(background ? ["paste"] : ["copy"]);
            queryMenus.Add(menu);
            if (background) contextCurrent = false; // Native menu construction can reenter the host.
            return menu;
        }, () => contextCurrent);
        Check(invalidStates.Values.All(enabled => !enabled) && queryMenus.All(menu => menu.Disposed),
            "selection or tab changes during query discard the entire snapshot");
        var staleDuringOpen = true;
        var backgroundOpenedAfterStale = false;
        var selectionMenu = new FakeMenu(["copy"]);
        var stoppedDuringOpen = ShellFileCommands.Query(background =>
        {
            if (background)
            {
                backgroundOpenedAfterStale = !staleDuringOpen;
                return new FakeMenu(["paste"]);
            }
            staleDuringOpen = false;
            return selectionMenu;
        }, () => staleDuringOpen);
        Check(stoppedDuringOpen.Values.All(enabled => !enabled) && !backgroundOpenedAfterStale && selectionMenu.Disposed,
            "a stale context during selection-menu preparation prevents opening the background menu");
        var staleDuringStateQuery = true;
        var backgroundOpenedAfterStateQuery = false;
        var reentrantSelectionMenu = new FakeMenu(["copy"]) { OnIsEnabled = () => staleDuringStateQuery = false };
        var stoppedDuringStateQuery = ShellFileCommands.Query(background =>
        {
            if (background)
            {
                backgroundOpenedAfterStateQuery = !staleDuringStateQuery;
                return new FakeMenu(["paste"]);
            }
            return reentrantSelectionMenu;
        }, () => staleDuringStateQuery);
        Check(stoppedDuringStateQuery.Values.All(enabled => !enabled) && !backgroundOpenedAfterStateQuery &&
            reentrantSelectionMenu.Disposed,
            "a stale context during native command-state lookup prevents later menus");
        var invalidated = new FakeMenu(["delete"]);
        Check(ShellFileCommands.Invoke(ShellFileCommand.Delete, _ => invalidated, () => false) is null &&
            invalidated.Invocations == 0 && invalidated.Disposed, "changed pane or selection cancels before invocation");
        var newFolder = new FakeMenu(["NewFolder"]);
        Check(ShellFileCommands.Invoke(ShellFileCommand.NewFolder, background =>
            background ? newFolder : throw new Exception("NewFolder queried the selection."), () => false) is null &&
            newFolder.Invocations == 0 && newFolder.Disposed, "new folder cancels after reentrant background menu preparation");
        var rejected = new FakeMenu(["delete"]) { Result = unchecked((int)0x80004005) };
        Check(ShellFileCommands.Invoke(ShellFileCommand.Delete, _ => rejected) < 0 && rejected.Disposed,
            "invocation failure is returned and resources released");
        var throwing = new FakeMenu(["rename"]) { ThrowOnInvoke = true };
        try { ShellFileCommands.Invoke(ShellFileCommand.Rename, _ => throwing); throw new Exception("missing exception"); }
        catch (InvalidOperationException) { Check(throwing.Disposed, "exception releases native context"); }
    }

    private sealed class FakeMenu(IEnumerable<string> verbs) : IShellCommandMenu
    {
        public bool Disposed;
        public int Invocations;
        public int Result;
        public bool ThrowOnInvoke;
        public Action? OnIsEnabled;
        public bool IsEnabled(string verb)
        {
            OnIsEnabled?.Invoke();
            return verbs.Contains(verb);
        }
        public int Invoke(string verb)
        {
            if (!IsEnabled(verb)) throw new Exception("disabled command invoked");
            Invocations++;
            if (ThrowOnInvoke) throw new InvalidOperationException();
            return Result;
        }
        public void Dispose() => Disposed = true;
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Shell commands: {label}");
    }
}
