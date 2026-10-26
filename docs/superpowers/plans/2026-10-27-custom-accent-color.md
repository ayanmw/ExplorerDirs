# Custom Accent Color Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task-by-task. Keep the feature within application-owned theme colors.

**Goal:** Let users choose either the live Windows accent or a custom accent color, persisted in the portable profile.

**Architecture:** Extend `Theme.ini` to format version 2 while continuing to read version 1. The theme controller resolves the accent independently from System/Light/Dark mode, and the existing semantic palette derives readable selection colors from that accent. A small owned WPF dialog accepts a six-digit RGB value; applying or returning to Windows accent updates all open windows through the existing theme event.

**Tech Stack:** .NET 10 WPF, portable INI store, `System.Windows.Media.Color`.

**Spec:** [Theme and icons design](../../specs/2026-10-26-theme-and-icons-design.md), phase 3.

## Global Constraints

- Default accent remains the current Windows accent; custom accent never changes the Windows setting.
- Theme mode and accent source remain independent in `Theme.ini`.
- Read version 1 files as System mode plus Windows accent; do not overwrite unknown newer formats.
- High contrast keeps Windows color control and resets custom DWM frame colors.
- ExplorerBrowser file content and Q-Dir `.qdr` theme fields remain outside this feature.

## Review Focus

- Invalid or partial RGB input leaves the currently applied theme and INI unchanged.
- Canceling the color dialog does not write settings.
- Switching back to Windows accent immediately uses the newest OS accent, including after OS color notifications.
- A version 1 profile migrates only when the user changes a setting; merely opening the app does not rewrite it.

## Task 1: Persist and apply accent preference

**Files:** `ThemePreferenceStore.cs`, `ApplicationThemeController.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `AccentColorDialog.xaml`, `AccentColorDialog.xaml.cs`, `ThemePreferenceSmoke.cs`, `docs/theme-icon-roadmap.md`, `docs/roadmap.md`.

- [x] Add failing smoke coverage for version 1 load, version 2 custom/system accent round-trip, invalid color preservation, and newer-format protection.
- [x] Extend the store and controller with System/Custom accent source and validated RGB value.
- [x] Add an owned hex-input dialog and menu commands for selecting custom or Windows accent.
- [x] Verify theme changes reapply to all windows and keep the OS accent monitor active in custom mode through source wiring and controller event flow.
- [x] Run complete Release `SettingsSmoke`, App Release build, and `git diff --check`; record GUI appearance as pending.
- [x] Commit the complete batch.
