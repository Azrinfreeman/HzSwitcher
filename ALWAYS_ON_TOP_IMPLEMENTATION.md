# Always on top — implementation proposal

Status: implemented after user approval on 2026-10-26. Build and automated test results are recorded below.

## Intended behavior

Add a PowerToys-style **Always on top** feature to HzSwitcher. The user enables it on the **General** page, then presses a global shortcut to pin or unpin the active application window above ordinary windows. Multiple windows can be pinned independently.

This proposal interprets the request as controlling other application windows, as PowerToys does. Keeping only HzSwitcher's own window on top would be a smaller, separate feature using WinForms `Form.TopMost`.

Windows exposes per-window topmost APIs; it does not provide a global Windows Settings switch for this behavior. HzSwitcher will implement the feature directly without requiring PowerToys, changing PowerToys settings, or installing a system component.

## Settings and user experience

- Add an **Always on top** section to General with an **Enable always on top** toggle, off by default.
- Helper text: “Use the shortcut to pin or unpin the active window above other windows. HzSwitcher must stay running.”
- Show the current shortcut and an **Edit hotkeys…** button, using the existing shortcut editor.
- Add **Toggle active window always on top** to the shortcut action list. Propose `Ctrl+Alt+T` as the initial shortcut; avoid PowerToys' `Win+Ctrl+T` default. Registration can still fail if another app uses the combination.
- On first enable, add the proposed binding only if no binding for this action exists. Preserve user edits and do not duplicate it on subsequent enables. If the user removes all bindings, show “No shortcut assigned” with the edit button; leave the feature enabled but inactive until assigned.
- Show operational status separately from the saved enabled preference: disabled, ready, no shortcut assigned, or shortcut unavailable. Do not claim readiness if registration failed.
- Notify on successful pin/unpin and explain failures briefly. These messages must be independent of the existing auto-switch notification preference. Keep errors in the log as well.
- Add an **Unpin all** action in the section for recovery when a pinned window cannot be focused. It remains available while cleanup is pending.
- Turning the feature off unregisters its shortcuts and restores windows changed by HzSwitcher. Hiding to the tray keeps the feature active; exiting restores managed windows.

The General page already scrolls. Reuse its `Section`, `Stack`, `Toggle`, theme and `_loading` event guard; verify the additional section at common DPI settings.

## Current code integration

| File | Planned change |
| --- | --- |
| `Services/SettingsStore.cs` | Add `AlwaysOnTopEnabled`, default `false`; update action documentation. Reuse `HotkeyBinding` for shortcut persistence. |
| `Native/WindowApi.cs` (new) | Wrap foreground/root-window lookup, validation, topmost inspection and `SetWindowPos`. Use pointer-sized handles and correct x86/x64 entry points for window styles. |
| `Services/AlwaysOnTopService.cs` (new) | Own pin/unpin policy, managed-window records, restoration, cleanup and structured results. |
| `Services/Abstractions.cs` | Add a small injectable window API abstraction so service behavior can be tested without changing real windows. |
| `TrayApp.cs` | Own the service; dispatch the new shortcut action, gate registration by feature enablement, expose status/actions to UI, restore on shutdown. |
| `UI/MainForm.cs` | Add the General section, preference wiring, shortcut summary, registration status and Unpin all. Extend `HotkeyText` for the action. |
| `UI/HotkeysForm.cs` | Add the new action without a parameter. Validate duplicate combinations by parsed modifiers/key, rather than text alone. |
| `UI/HotkeyWindow.cs` | Tighten parsing as necessary and expose normalized combinations for duplicate detection. Preserve existing shortcuts and `MOD_NOREPEAT`. |
| `tests/HzSwitcher.Tests/` | Add fake window API and focused service/settings/hotkey tests. |
| `README.md`, `CHANGELOG.md` | Document usage, cleanup behavior and limitations after implementation. |

An additive boolean with its initializer is enough for older settings files to load with the feature disabled. No settings-version bump is required solely for this field. Never serialize window handles or pinned-window state.

## Native operation and ownership

1. Handle the shortcut on the existing UI thread. Capture the foreground window immediately, before showing a notification or any UI that can change focus, and resolve its root window.
2. Reject zero, destroyed, invisible or unsuitable targets, including the desktop, taskbar and shell surfaces. Do not use window titles as identity. Normal HzSwitcher windows may also be pinned when explicitly focused.
3. Read current topmost state through `WS_EX_TOPMOST`. A valid unowned, non-topmost target is pinned with `SetWindowPos(HWND_TOPMOST)` and `SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE`. Check the return value before recording success.
4. Record only windows successfully changed by HzSwitcher, including HWND, process ID and a stronger lifetime marker where available. Track destruction through `SetWinEventHook(EVENT_OBJECT_DESTROY)` with an out-of-context callback; keep the delegate alive, filter for window destruction and marshal updates to the UI thread. This prevents stale handles from being applied to replacement windows. Revalidate identity immediately before every mutation.
5. A second shortcut on a managed window restores its original non-topmost state with `HWND_NOTOPMOST`, preserving position, size and activation. Remove the record only on success or confirmed destruction/identity mismatch.
6. If a window was already topmost before HzSwitcher touched it, leave it unchanged and report that it is already on top. Do not take ownership or later clear another application's topmost setting.
7. Unpin all, disable and graceful exit attempt restoration for every still-valid managed window. Continue after individual failures, report/log a summary, and retain failed records while the process remains alive so cleanup can be retried. Disable prevents new pins even if cleanup has failures. Unhook destruction events during disposal after cleanup.

Wrap native failures in results such as `Pinned`, `Unpinned`, `AlreadyTopmost`, `InvalidTarget` and `Failed`, with a useful native error when the API supplies one. Never update UI state optimistically after failure. No polling loop or repeated forced re-pinning is needed.

## Shortcut lifecycle

Use the existing `HotkeyWindow` rather than a keyboard hook or another message window. Add `ToggleAlwaysOnTop` to `HotkeyBinding.Action` and the `TrayApp.OnHotkey` switch.

`ApplyHotkeys()` currently unregisters everything and registers bindings using list indexes as IDs. Preserve that index mapping when skipping always-on-top bindings while disabled; do not compact IDs. Check the enabled flag again during dispatch to ignore stale queued events.

Reapply shortcuts at startup, after editing them and when enabling/disabling this feature. Refresh its status after each registration pass. A conflict must leave other successfully registered actions working and provide the affected combination to the user; never silently replace an existing binding or launch PowerToys.

The parser currently permits multiple non-modifier keys by overwriting the previous key. Require exactly one supported non-modifier key, reject modifier-only combinations and normalize modifier order/aliases for duplicate comparisons. Keep support for existing valid shortcuts. This work does not require adding Windows-key capture to the editor.

## Boundaries and limitations

- First version includes global pin/unpin, multiple managed windows, status notifications and Unpin all. A colored border, sound, exclusion lists and automatic pinning rules are deferred.
- A topmost window stays above ordinary windows; another topmost window can cover it. Exclusive fullscreen and secure desktops are outside this guarantee.
- Some elevated or protected applications may reject the operation. Report failure without automatically elevating HzSwitcher or changing the installation's privilege level.
- Other software can change the same window's topmost state. There is no exclusive ownership API; document best-effort coexistence and avoid forcing the state back repeatedly.
- Graceful exit restores managed windows. A crash or forced termination can leave external windows topmost. Do not persist and replay raw handles across restarts; closing/reopening the affected application is the recovery path for this first version. Guaranteed crash cleanup would require a separately designed watchdog.

## Implementation sequence

1. Add settings and a native window adapter, with the service and explicit ownership/cleanup policy.
2. Add the shortcut action, registration gating, normalized validation and registration status.
3. Wire General UI controls, notifications, Unpin all and startup/exit lifecycle.
4. Verify service behavior with fakes, then perform the Windows checks below.
5. Update user documentation after behavior is verified. Do not change the release version or publish artifacts as part of this proposal.

## Validation and acceptance criteria

Automated coverage should verify successful pin/unpin, independent windows, already-topmost targets remaining untouched, invalid targets, native failures, failed restoration retry, destruction/handle reuse, disable/exit cleanup, older settings defaulting to disabled, settings round-trip, malformed/duplicate shortcuts and disabled-action dispatch/registration behavior.

After implementation, run:

```powershell
dotnet build -c Debug -o "$env:TEMP\hzbuild"
dotnet test tests\HzSwitcher.Tests
```

Manual checks on Windows 10/11:

- Enable, pin a normal app, focus another app and confirm the pinned app stays above it; toggle again to restore normal stacking without moving or resizing it.
- Pin two windows and exercise individual unpin, Unpin all, disable and Exit app. Closing HzSwitcher to the tray must keep pins active.
- Close a pinned target, open another window and confirm no stale record changes the replacement window.
- Exercise an already-topmost app, invalid shell targets, an elevated target and a shortcut conflict with another running application.
- Remove/reassign the shortcut, restart enabled/disabled and confirm persistence and accurate readiness status. Confirm refresh-rate shortcuts still work.
- Inspect General at 100%, 150% and 200% DPI, keyboard navigation and scrolling. Confirm pin notifications work when auto-switch notifications are off.
- Document observed forced-termination behavior; do not claim crash restoration.

## Implementation notes

Implemented the General section, shortcut action, native adapter, managed-window cleanup and focused tests. Added `AlwaysOnTopShortcutInitialized` so removing the shortcut stays respected after disabling and re-enabling. The window abstraction lives alongside the service rather than in the shared abstractions file.

Automated checks include a Windows native smoke test using an offscreen test-owned native window; it verifies topmost style changes without changing bounds. Interactive checks across Windows versions, elevated applications and DPI settings still require manual validation.

Verification: Debug build succeeded with no warnings; all 47 automated tests passed. Interactive DPI and cross-application checks remain manual.
