# Q-Dir Soft-Refresh

## Goal

Add Q-Dir's Soft-Refresh behavior to Explorer Dirs so Shell refreshes can keep the current item order instead of re-enumerating the view.

## Confirmed behavior and platform contract

- Q-Dir describes Soft-Refresh as avoiding re-sorting/re-listing after drops, copies, and refreshes; its newer label is “Disable auto sorting when rename or copy items”.
- Windows `FOLDERFLAGS.FWF_NOENUMREFRESH` means the view does not re-enumerate or drop its current contents when refreshed.
- Use the native flag on the existing ExplorerBrowser `IFolderView2`; do not replace the file view or intercept Shell file operations.

## Product behavior

- Add a global View-menu toggle, initially off, saved in its own portable profile INI.
- When enabled, set only `FWF_NOENUMREFRESH` on every existing and newly created Shell view. When disabled, clear only that flag. Preserve all unrelated folder flags.
- Menu text and tooltip must disclose that refresh can leave changed/new items unlisted until the option is disabled or the view is navigated/reloaded.
- Unsupported views or native errors must be reported through the existing status/error path; do not claim the setting applied if readback differs.

## Out of scope

- No custom row sorting, hidden file-operation implementation, per-tab setting, or `IShellView` migration in this change.
- No automatic timer or additional refresh command.
- Real Q-Dir parity, especially exact new-item visibility, remains subject to visible acceptance.
