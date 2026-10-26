# Photo Print Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Q-Dir-style printing for the active file view's selected images, with page layout, print-size settings, printer selection, and preview.

**Architecture:** Keep the existing Shell view as the source of selected file identities. Decode selected filesystem files with WIC/WPF, create preview and print documents from the same ordered layout (bounded preview images and full-resolution print images), then submit only through an explicit action with the captured queue and validated print ticket.

**Tech Stack:** .NET 10, WPF `BitmapDecoder`, `FixedDocument`/`DocumentViewer`, `PrintDialog`, existing SettingsSmoke harness.

**Spec:** `docs/superpowers/specs/2026-10-25-photo-print-design.md`

## Global Constraints

- Read only selected items from the active pane; never enumerate the directory or hidden panes.
- Keep “print current file view” separate from photo printing.
- Do not call Shell canonical `Print` as a substitute for the app's layout/preview controls.
- Do not add dependencies, write registry values, or launch an external image application.
- Closing or cancelling the preview window without submitting a print job; settings changes invalidate the previous preview.

## Review Focus

- Empty selection or selection containing folders/virtual Shell items: show a useful message and never decode a non-file path.
- Unsupported/corrupt images mixed with valid images: report each rejected item and keep the valid subset usable.
- Portrait/landscape images and custom physical sizes: preserve aspect ratio for Fit; crop only when Fill is explicitly selected.
- Cancel at either stage: no print job is submitted.
- Large source files: decode and create preview off the UI thread, release file handles after decode, and bound preview bitmap dimensions.

### Task 1: Capture Selected Photos

- [x] Add SettingsSmoke for active selection filtering: regular image paths pass, non-image, folder, and virtual selections are rejected, and source order is preserved.
- [x] Run the smoke before implementation and confirm compilation fails because `PhotoPrintSelection` is missing.
- [x] Implement pure `PhotoPrintSelection.Filter` results with accepted paths and rejected display names/reasons.
- [x] Connect the filter to the active pane's native Shell selection without directory enumeration; source path is `ActivePane -> PaneControl.Current -> ExplorerBrowserHost` and the app Release build passes.

### Task 2: Paginated Photo Document

- [x] Add failing SettingsSmoke for one/two/four/six/nine images per page, Fit vs Fill geometry, portrait/landscape source ratios, page count and invalid custom dimensions.
- [x] Run the layout smoke before implementation and confirm compilation fails because `PhotoPrintLayout` is missing.
- [x] Implement `PhotoPrintLayout.Arrange` for ordered pagination, Fit/Fill crops, and centimeter dimensions; implement WIC decode with `BitmapCacheOption.OnLoad`, frozen print sources, released input streams, and preview bitmaps capped at 1600 pixels.
- [x] Build `PhotoPrintDocument.Create` fixed pages from captured ordered image assets; SettingsSmoke verifies page count and mixed valid/corrupt decoding.
- [x] Add cancellation/dispatch gate smoke; connect the owned preview window with settings-version invalidation.

### Task 3: Preview, Printer Selection, and Integration

- [x] Add selection-state smoke for empty/blocked-only selection and a candidate selection; image-loader smoke separately proves decodable-image availability.
- [x] Add “打印所选照片…” beside the existing current-view print command; show an owned preview/settings window and submit only from its explicit Print button.
- [x] Keep window close/cancel as a no-submit path and report submitted photo/page counts in the existing status bar.
- [ ] Complete visible UI rendering/accessibility review and verify the selected queue/ticket survive printer changes and submit as captured. Source review fixed the preview/action overlap and adds a markup smoke for separate layout rows and accessible control names; actual window and printer behavior remain pending.
- [x] Run full Release `SettingsSmoke`, App Release build, and `git diff --check` (rechecked 2026-10-26).
- [x] Update `docs/roadmap.md` and `docs/shell-parity.md`; actual rendering, focus, layout and print quality remain pending visible acceptance.

## Execution Gate

This plan is part of the user's ongoing authorization to bring Q-Dir capabilities into Explorer Dirs. Do not publish a release or claim Q-Dir-equivalent photo-print acceptance before the isolated visible and print-output checks pass.
