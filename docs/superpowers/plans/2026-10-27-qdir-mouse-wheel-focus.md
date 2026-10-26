# Q-Dir Mouse Wheel Focus Implementation Plan

> **For agentic workers:** Execute inline. Each step uses test-first verification and a focused commit.

**Goal:** Add an opt-in portable setting that routes ordinary mouse-wheel messages to the visible Shell file view or directory tree under the pointer, matching Q-Dir's Mouse wheel focus behavior.

**Architecture:** A WPF thread message filter retargets only vertical `WM_MOUSEWHEEL` messages when the preference is enabled and the pointed HWND belongs to a visible Explorer Dirs Shell view/tree. It updates the logical active pane for per-pane views, leaves Ctrl+wheel untouched, and does not take keyboard focus. A dedicated portable INI stores the preference; the default is off.

**Tech Stack:** .NET 10 Windows Desktop WPF, `ComponentDispatcher.ThreadFilterMessage`, native HWND hit testing, SettingsSmoke.

**Spec:** `docs/shell-parity.md` Q-Dir mouse-wheel-focus parity, grounded in [Q-Dir FAQ 75](https://www.softwareok.com/?faq=75&seite=faq-Q-DIR).

## Global Constraints

- Do not start Explorer Dirs, ExplorerBrowser, or `IShellView`; only source edits and noninteractive checks are allowed.
- Default remains off so existing wheel routing is unchanged until enabled.
- Ctrl+wheel continues to cycle view styles through its existing Shell path.
- Retarget only messages whose pointer target is inside a visible native file view or tree.
- Preserve current Shell wheel handling; do not synthesize a second wheel message.

## Review Focus

- Disabled preference, Ctrl+wheel, and non-Shell targets must not be retargeted (covered by `MouseWheelFocusSmoke`).
- Per-pane tree/file targets must activate the corresponding logical pane without moving keyboard focus (covered by router-state smoke; real focus remains GUI acceptance).
- A shared-tree target must not change pane identity (covered by router-state smoke).
- Multiple main windows on the same WPF dispatcher must only retarget for the window that owns the pointed HWND (covered by ownership predicate tests).
- Stale/invalid HWNDs must fail closed and preserve the original message target (covered by native ownership helper tests).

### Task 1: Pure wheel-message decision and regression smoke

**Files:**
- Create: `src/ExplorerDirs.App/MouseWheelFocusRouter.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`
- Create: `src/ExplorerDirs.SettingsSmoke/MouseWheelFocusSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`

**Interfaces:** `MouseWheelFocusRouter.TryRetarget(uint message, nuint wParam, bool enabled, nint currentTarget, nint hoveredTarget, bool isShellTarget, out nint target)` returns whether to retarget. It only accepts ordinary `WM_MOUSEWHEEL` without `MK_CONTROL`.

- [x] Write smoke cases for enabled/disabled, Ctrl modifier, non-Shell target, unchanged target, and valid hovered Shell target.
- [x] Run the targeted smoke and confirm it fails because the router was not yet implemented.
- [x] Implement the minimal pure decision helper.
- [x] Run the targeted smoke and confirm it passes.
- [x] Commit the tested helper (`5d5de67`).

### Task 2: Portable preference, menu toggle, and WPF message routing

**Files:**
- Modify: `src/ExplorerDirs.App/MainWindow.xaml`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml.cs`
- Modify: `src/ExplorerDirs.App/ExplorerBrowserHost.cs`
- Modify: `src/ExplorerDirs.App/ShellTreeHost.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/MouseWheelFocusSmoke.cs`

**Interfaces:** `PaneControl.ContainsMouseWheelShellTarget(nint hwnd)` confirms the HWND belongs to its current file view or visible per-pane tree. `ExplorerBrowserHost.ContainsNativeWindow(nint hwnd)` and `ShellTreeHost.ContainsNativeWindow(nint hwnd)` use native parent/child identity. MainWindow's thread filter updates `MSG.hwnd` only after router approval and calls `SetActivePane` only for a matching per-pane target.

- [x] Write settings smoke for default-off INI load/save and the router decision cases; cross-window HWND ownership remains untested.
- [x] Run the preference smoke before wiring and confirm it fails because the setting store is not yet connected.
- [x] Add `MouseWheelFocus.ini` with default-off `FocusOnWheel`, a checkable View-menu item, and save-failure feedback.
- [x] Subscribe/unsubscribe `ThreadFilterMessage` with the main window lifetime; use `GetCursorPos`/`WindowFromPoint`, fail closed for invalid or non-Shell HWNDs, and retarget without synthesizing messages.
- [x] Run full Release SettingsSmoke and App Release build; actual Ctrl+wheel route is unchanged by source inspection and the existing router smoke.
- [x] Commit the integrated feature (`3ad1101`) and update the shell-parity status. Roadmap log entry remains to be added.
