using ExplorerDirs.App;
using System.Windows.Automation;

internal static class ShellMenuTargetTrackerSmoke
{
    internal static void Run()
    {
        Check(ShellMenuTargetTracker.CanUseSelectionForCommands(ShellMenuTarget.Item),
            "a confirmed item hit may supply selection-specific custom commands");
        Check(ShellMenuTargetTracker.CanUseSelectionForCommands(ShellMenuTarget.KeyboardSelection),
            "a keyboard context menu may use the Shell selection");
        Check(!ShellMenuTargetTracker.CanUseSelectionForCommands(ShellMenuTarget.Background),
            "a confirmed background hit never supplies a stale Shell selection");
        Check(!ShellMenuTargetTracker.CanUseSelectionForCommands(ShellMenuTarget.Unknown),
            "an unknown mouse hit never supplies a stale Shell selection");

        var headerMenu = new ShellMenuTargetTracker();
        headerMenu.MouseDown(new ShellMenuPoint(12, 88), _ => Task.FromResult(ShellMenuTarget.Unknown),
            allowCustomCommands: false);
        Check(!headerMenu.CanInjectCustomCommands,
            "native column-header menus can suppress file-view tab commands independently of hit testing");
        headerMenu.ContextMenu((nint)((88 << 16) | 12), _ => Task.FromResult(ShellMenuTarget.Unknown));
        Check(!headerMenu.CanInjectCustomCommands,
            "a forwarded header context message preserves its original command-suppression decision");
        var contentMenu = new ShellMenuTargetTracker();
        contentMenu.MouseDown(new ShellMenuPoint(12, 88), _ => Task.FromResult(ShellMenuTarget.Unknown));
        Check(contentMenu.CanInjectCustomCommands,
            "unknown hits originating in file content retain target-independent tab commands");

        Check(!ShellMenuHitTrace.IsEnabled(null) && !ShellMenuHitTrace.IsEnabled(string.Empty) &&
            ShellMenuHitTrace.IsEnabled("trace.log"),
            "empty or missing trace paths disable diagnostic collection");
        var diagnostic = ShellMenuHitTrace.Format(new ShellMenuPoint(12, -8), ShellMenuTarget.Background,
            [ControlType.ListItem, ControlType.List]);
        Check(diagnostic == $"context-menu-hit x=12 y=-8 target=Background controls={ControlType.ListItem.ProgrammaticName}/{ControlType.List.ProgrammaticName}",
            "opt-in menu diagnostics retain signed coordinates and control types without item names");

        CheckEqual(ShellMenuTargetTracker.IsNativeListItemHit(itemIndex: 0, hitFlags: 0x4), expected: true,
            "native subitem hit confirms a click on file text");
        CheckEqual(ShellMenuTargetTracker.IsNativeListItemHit(itemIndex: 0, hitFlags: 0x04000000), expected: true,
            "the Vista extended on-contents flag confirms a native item-content hit");
        CheckEqual(ShellMenuTargetTracker.IsNativeListItemHit(itemIndex: 0, hitFlags: 0), expected: false,
            "a row index without an icon, label, or state-icon hit is blank-space background");
        CheckEqual(ShellMenuTargetTracker.IsNativeListItemHit(itemIndex: -1, hitFlags: 0x4), expected: false,
            "item hit flags without a valid row cannot target a file");
        Check(ShellMenuTargetTracker.ResolveNativeListViewHitTest(available: false, itemIndex: 0, hitFlags: 0x4) ==
              ShellMenuTarget.Unknown,
            "without native list hit testing, broad UI Automation row bounds cannot identify an item command target");

        var menuTargets = new ShellMenuTargetTracker();
        var backgroundPoint = new ShellMenuPoint(12, 88);
        var probeCalls = 0;
        Task<ShellMenuTarget> BackgroundProbe(ShellMenuPoint point)
        {
            probeCalls++;
            Check(point == backgroundPoint, "background probe receives the mouse location");
            return Task.FromResult(ShellMenuTarget.Background);
        }
        menuTargets.MouseDown(backgroundPoint, BackgroundProbe);
        menuTargets.ContextMenu((nint)((88 << 16) | 12), BackgroundProbe);
        Check(menuTargets.Resolve() == ShellMenuTarget.Background && probeCalls == 1,
            "background right-click ignores a still-selected Shell item and reuses its point probe");

        long menuClock = 100;
        var staleMenuTarget = new ShellMenuTargetTracker(() => menuClock);
        staleMenuTarget.MouseDown(backgroundPoint, _ => Task.FromResult(ShellMenuTarget.Item));
        menuClock += 5001;
        Check(staleMenuTarget.Resolve() == ShellMenuTarget.Unknown,
            "old right-button hit cannot leak into an unrelated later menu");

        menuTargets.MouseDown(new ShellMenuPoint(30, 90), _ => Task.FromResult(ShellMenuTarget.Item));
        menuTargets.ContextMenu((nint)((90 << 16) | 30),
            _ => throw new Exception("matching menu point started a second probe"));
        Check(menuTargets.Resolve() == ShellMenuTarget.Item,
            "item right-click can use the Shell selection after hit confirmation");

        var slowMenuProbe = new TaskCompletionSource<ShellMenuTarget>();
        menuTargets.MouseDown(new ShellMenuPoint(40, 90), _ => slowMenuProbe.Task);
        Check(menuTargets.Resolve() == ShellMenuTarget.Unknown,
            "unfinished mouse hit test cannot borrow the Shell selection");
        menuTargets.ContextMenu((nint)(-1), _ => throw new Exception("keyboard menu probed the mouse"));
        slowMenuProbe.SetResult(ShellMenuTarget.Item);
        Check(menuTargets.Resolve() == ShellMenuTarget.KeyboardSelection,
            "keyboard context menu uses selection rather than a stale mouse result");

        menuTargets.Reset();
        long keyboardMenuClock = 20;
        var keyboardMenuTarget = new ShellMenuTargetTracker(() => keyboardMenuClock);
        keyboardMenuTarget.KeyboardMenuRequested();
        Check(keyboardMenuTarget.Resolve() == ShellMenuTarget.KeyboardSelection,
            "Shell menu key can establish keyboard origin before WM_CONTEXTMENU arrives");
        keyboardMenuClock += 5001;
        Check(keyboardMenuTarget.Resolve() == ShellMenuTarget.Unknown,
            "unhandled Shell menu key does not leave a permanent selection target");

        var changedPoint = new ShellMenuPoint(55, 91);
        menuTargets.MouseDown(new ShellMenuPoint(54, 91), _ => Task.FromResult(ShellMenuTarget.Item));
        menuTargets.ContextMenu((nint)((91 << 16) | 55), point =>
            Task.FromResult(point == changedPoint ? ShellMenuTarget.Background : ShellMenuTarget.Item));
        Check(menuTargets.Resolve() == ShellMenuTarget.Background,
            "context message location replaces a stale right-button location");

        menuTargets.MouseDown(null, _ => Task.FromResult(ShellMenuTarget.Item));
        var probeAfterMissingCursor = 0;
        menuTargets.ContextMenu((nint)((88 << 16) | 12), _ =>
        {
            probeAfterMissingCursor++;
            return Task.FromResult(ShellMenuTarget.Background);
        });
        Check(probeAfterMissingCursor == 1 && menuTargets.Resolve() == ShellMenuTarget.Background,
            "a right-button down without a cursor coordinate cannot reuse an earlier hit result");

        var zeroExtendedKeyboardMenuTarget = new ShellMenuTargetTracker();
        zeroExtendedKeyboardMenuTarget.ContextMenu(unchecked((nint)0xFFFFFFFFUL),
            _ => throw new Exception("zero-extended keyboard invocation must not probe the mouse"));
        Check(zeroExtendedKeyboardMenuTarget.Resolve() == ShellMenuTarget.KeyboardSelection,
            "zero-extended 32-bit (-1,-1) context coordinates identify keyboard invocation on 64-bit LPARAM");

        var keyboardHeaderMenuTarget = new ShellMenuTargetTracker();
        keyboardHeaderMenuTarget.ContextMenu((nint)(-1),
            _ => throw new Exception("keyboard header invocation must not probe the mouse"),
            allowCustomCommands: false);
        Check(!keyboardHeaderMenuTarget.CanInjectCustomCommands &&
              keyboardHeaderMenuTarget.Resolve() == ShellMenuTarget.KeyboardSelection,
            "a keyboard context menu on a native header keeps file-view custom commands suppressed");

        var directKeyboardHeaderMenuTarget = new ShellMenuTargetTracker();
        directKeyboardHeaderMenuTarget.ContextMenu((nint)(-1),
            _ => throw new Exception("direct keyboard header invocation must not probe the mouse"),
            allowCustomCommands: false);
        Check(!directKeyboardHeaderMenuTarget.CanInjectCustomCommands &&
              directKeyboardHeaderMenuTarget.Resolve() == ShellMenuTarget.KeyboardSelection,
            "a keyboard context message carries its native-source command-suppression decision");

        var preannouncedKeyboardHeaderMenuTarget = new ShellMenuTargetTracker();
        preannouncedKeyboardHeaderMenuTarget.KeyboardMenuRequested(allowCustomCommands: false);
        preannouncedKeyboardHeaderMenuTarget.ContextMenu((nint)(-1),
            _ => throw new Exception("preannounced keyboard header invocation must not probe the mouse"),
            allowCustomCommands: false);
        Check(!preannouncedKeyboardHeaderMenuTarget.CanInjectCustomCommands &&
              preannouncedKeyboardHeaderMenuTarget.Resolve() == ShellMenuTarget.KeyboardSelection,
            "a Shift+F10 pre-notification preserves native header command suppression through menu dispatch");

        menuTargets.Reset();
        menuTargets.ContextMenu((nint)((91 << 16) | 55), _ =>
            Task.FromException<ShellMenuTarget>(new InvalidOperationException("probe unavailable")));
        Check(menuTargets.Resolve() == ShellMenuTarget.Unknown,
            "failed hit test cannot borrow a selected item");
        menuTargets.Reset();
        Check(menuTargets.Resolve() == ShellMenuTarget.Unknown &&
            ShellMenuTargetTracker.DecodeContextPoint(unchecked((nint)0xFFF8000C)) == new ShellMenuPoint(12, -8),
            "closed menus forget hit targets and signed screen coordinates decode correctly");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Shell menu target: {label}");
    }

    private static void CheckEqual(bool actual, bool expected, string label)
    {
        if (actual != expected) throw new InvalidOperationException($"Shell menu target: {label}");
    }
}
