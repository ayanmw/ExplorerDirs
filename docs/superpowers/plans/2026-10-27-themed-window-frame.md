# Themed Window Frame Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task-by-task. Keep this batch limited to native non-client frame colors.

**Goal:** Keep Windows 11 title bars and borders aligned with the selected Explorer Dirs theme.

**Architecture:** Derive DWM COLORREF values from the existing semantic theme palette and apply them to the main and photo-print windows when native handles are created and when the palette changes. Reset DWM colors to system defaults in high contrast. Unsupported DWM attributes on older Windows versions are ignored; WPF client-area resources remain the fallback.

**Tech Stack:** .NET 10 WPF, `DwmSetWindowAttribute`, `ExplorerDirs.SettingsSmoke`.

**Spec:** [Theme and icons design](../specs/2026-10-26-theme-and-icons-design.md).

## Constraints and Review Focus

- Use official DWM color attributes for border, caption, and caption text; these require Windows 11 build 22000 or newer.
- Use `DWMWA_COLOR_DEFAULT` to restore system control in high contrast.
- Do not set the undocumented immersive dark-mode attribute or change non-client metrics.
- Do not change shell views, file operations, or WPF control templates.
- Visible titlebar colors, activation states, and supported Windows versions remain a GUI acceptance item.

## Task 1: Apply palette colors to app window frames

**Files:**
- Create: `src/ExplorerDirs.App/NativeWindowFrameTheme.cs`
- Modify: `src/ExplorerDirs.App/ApplicationThemeController.cs`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Modify: `src/ExplorerDirs.App/PhotoPrintWindow.xaml.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/WindowFrameThemeSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`
- Modify: `docs/theme-icon-roadmap.md`
- Modify: `docs/roadmap.md`

- [x] Add a failing pure smoke for COLORREF packing, high-contrast reset, and both window integration paths.
- [x] Expose the current resolved palette separately from high-contrast system fallback.
- [x] Apply DWM colors at window creation and every later theme update.
- [x] Run focused theme smoke, complete Release `SettingsSmoke`, App Release build, and `git diff --check`.
- [x] Record unsupported-version and GUI acceptance boundaries; commit the batch.
