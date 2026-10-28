# Fullscreen gameplay: prevent Always on top mouse interception

Status: implemented after approval. Build and automated regression verification are recorded below; manual acceptance in the reported game remains pending. No version bump, installer rebuild, commit or push is included.

## Implementation notes

- The guard refreshes classification outside the hook every 250 ms and on relevant window events. Its HWND/process snapshot expires after 750 ms; unknown, changed or stale state leaves input with the application. Process names are reread during refresh to avoid persisting classification across process reuse.
- Fast hook checks use native window/client/monitor bounds. Client coverage distinguishes fullscreen from ordinary maximized windows with invisible resize margins; captionless frame coverage supplements it. No DWM or cross-process messaging runs inside the hook.
- Menu actions revalidate the guard; protection changes cancel pending gestures, close the pin popup and hide outlines without changing pins. Monitoring pauses when the feature is disabled and no failed cleanup pins remain.

## Problem and findings

While a fullscreen game is active, right-click can open the HzSwitcher Always on top menu and consume the game's click.

Confirmed in the current source:

- `Native/WindowMouseHook.cs` intercepts physical right-button down when `(shift || frame) && eligible`. Eligibility excludes shell windows but does not exclude games or fullscreen applications.
- Holding Shift during gameplay therefore satisfies the global menu gesture anywhere on an eligible window, even when the cursor is inside game content.
- Ordinary frame detection uses the window/client rectangles. It has no fullscreen exclusion and can classify edge coordinates as a frame candidate before the later UI-thread hit test.
- `TrayApp.cs` checks frame hit tests only after down/up have already been swallowed. Dropping the menu at that stage cannot restore the original game input.
- Outline windows have their own right-click handler, outside the low-level hook's interception decision.
- Existing game/fullscreen detection in `MediaDetector`/`ShellApi` protects the power saver only; the pin mouse hook does not consult it.

The missing guard is confirmed. The exact trigger in the reported game (Shift held, an edge classified as frame, or a pinned outline) has not been reproduced in that game. The fix must cover all three paths.

## Intended behavior

1. While the foreground window is fullscreen, pass right-button down/up through unchanged, with or without Shift. Support exclusive and borderless fullscreen, including games not present in the configured game list.
2. Also suspend mouse pin gestures while a configured game is foreground in windowed mode. Reuse `Settings.GameProcesses` and its existing wildcard matching; this avoids the same Shift conflict when a game changes display mode.
3. Suspend both ordinary frame right-click and Shift+right-click, without opening the pin menu, original native menu, balloon or sound.
4. Hide HzSwitcher pin outlines while this protection is active so their HWNDs cannot intercept gameplay clicks. Preserve pin ownership/topmost state, then restore eligible outlines when protection ends.
5. Close any HzSwitcher pin menu when entering the protected state. Recheck before opening a queued menu or running a menu action.
6. Restore normal title-bar/frame, Shift+right-click and outline menus after Alt+Tab to an ordinary desktop app. Preserve the existing explicit keyboard pin shortcut and General > Unpin all.

Fullscreen protection intentionally applies to other fullscreen apps, such as browser video, as well as games. Mouse pin menus resume when the app exits fullscreen. While a protected foreground app is active, mouse pin menus and outlines are suspended globally, including on secondary monitors; this favors uninterrupted gameplay and avoids targeting desktop windows underneath an exclusive game cursor. No new setting is needed for this bug fix.

## Implementation

### Shared interaction guard

Add a small `WindowPinInteractionGuard` owned by `TrayApp`, used by the hook, menu routing and outline presenter.

- Read the foreground root HWND and its owning process ID.
- Detect fullscreen against the actual monitor bounds, not the work area. Handle negative coordinates, mixed DPI and multiple monitors. Account for DWM visible frame bounds and maximized window state so an ordinary maximized desktop window is not automatically classified as fullscreen because of invisible resize margins. Retain geometry detection for borderless fullscreen.
- Reuse the shell exclusive-fullscreen signal as supplementary evidence. It is not sufficient on its own for borderless games.
- Reuse process-name lookup and `Match.Wildcard` for `GameProcesses`. Cache process classification by process identity, invalidate on foreground/process changes and refresh after settings edits. Do not invoke media audio checks.
- Refresh on foreground/location changes with a modest reconciliation timer while the mouse feature is enabled. Dispose subscriptions/timers with the feature.
- Publish an immutable, inexpensive snapshot for the hook. On a new foreground HWND/process or stale snapshot, pass the current click through and queue refresh; do not guess that interception is safe.
- At right-button down, use bounded native geometry checks for the current foreground window to catch entry into fullscreen on the same HWND before the cached refresh. If relevant state cannot be read, leave the click with the application. Process inspection, shell queries, synchronous cross-process hit tests, I/O and UI work stay outside the hook.

Prefer extending existing native geometry helpers over copying fullscreen logic. Keep the power-saver's media policy unchanged; its video/game distinctions serve a different purpose.

### Mouse hook and paired input

Update `WindowMouseHook`/`MouseGesture` so protection is evaluated **before** accepting a new gesture.

- Protected, uncertain, unrelated or injected input goes to `CallNextHookEx` without creating a captured gesture.
- Preserve right-button pair ownership: if down passed through, its up must pass through even if protection ends or Shift changes before release.
- If HzSwitcher captured down before protection began, consume only that matching up and cancel the queued menu. Do not send an unmatched up to a game that never received down.
- Revalidate protection in the queued callback. Cancel stale menu requests without synthesizing gameplay input or invoking `ReplayFrameClick`/`ShowOriginalMenu`.
- Keep normal disable/unhook cleanup and injected-input handling intact.

### Menu and outline paths

- Route every mouse-driven menu entry through the guard, including outline right-click.
- On protection activation, close the pin popup and hide existing overlays. The hook must already pass gameplay clicks through; hiding overlays is an additional requirement, not a replacement for the pre-interception guard.
- Recheck before dispatching menu actions and before replaying native frame input. Never open the native menu as a fallback during protected gameplay.
- On leaving protection, reconcile visibility/geometry of surviving owned targets. Do not unpin, repin or force their z-order to restore outlines.
- Leave successful pin notification suppression and the native-menu handoff fix intact.

## Files to change after approval

| File | Work |
| --- | --- |
| `Services/WindowPinInteractionGuard.cs` (new) | Shared protected-state snapshot, classification and refresh lifetime. |
| `Native/ShellApi.cs`, `Native/WindowGeometry.cs` / `WindowFrameApi.cs` | Reuse/extend HWND-based monitor and fullscreen geometry queries. |
| `Native/WindowMouseHook.cs` | Guard before suppression and maintain down/up pair ownership. |
| `TrayApp.cs` | Own guard; gate queued menus/actions and native replay. |
| `Services/AlwaysOnTopBorderService.cs` | Hide/reconcile overlays during protected state. |
| `UI/WindowPinMenu.cs` | Reject protected menu actions through shared routing. |
| `tests/HzSwitcher.Tests/` | Policy, gesture-pair, routing and native geometry regressions. |
| `README.md`, `CHANGELOG.md` | Briefly document gameplay/fullscreen mouse-menu suspension. |

## Verification

Executed: `dotnet build -c Debug` succeeded with zero warnings/errors; `dotnet test tests/HzSwitcher.Tests --no-restore` passed all 86 tests, including eight gameplay-guard regressions and the original native-menu handoff check. `git diff --check` passed. Manual acceptance in the reported game is still pending.

The validation scope follows:

Focused automated checks first:

- Shift+right-click and ordinary edge right-click pass down/up through in protected state; no menu, sound, balloon or native-menu fallback.
- Cover known windowed games, unknown borderless fullscreen, the exclusive-fullscreen signal and lookup failures.
- Cover transitions between down/up, Shift release, foreground changes, same-HWND fullscreen transitions, stale classification, injected events and feature disable.
- Cover maximized versus fullscreen windows, negative coordinates, multiple monitors and DPI-scaled bounds.
- Cover queued menu cancellation, blocked outline/menu actions, hidden overlays and restoration without topmost mutations.
- Retain normal desktop frame menus, content right-click, keyboard pin, Unpin all and original-menu handoff regressions.

Then run `dotnet build -c Debug` and `dotnet test tests/HzSwitcher.Tests`.

Manual acceptance must include the reported game: hold Shift while right-clicking in gameplay, at every screen edge, with a visible/unlocked cursor and a captured/hidden cursor; repeat in exclusive, borderless and windowed modes. Test Alt+Tab, another monitor, fullscreen video and return to normal desktop menus. Automated geometry tests cannot establish compatibility with the actual game's input path.

No version bump, installer rebuild, installation, commit or push is part of this proposal.

## References

- [Microsoft: LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc) documents that returning nonzero can prevent delivery and that callbacks must remain fast.
- [Microsoft: SHQueryUserNotificationState](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate) describes the notification-state query and notes the absence of notifications when fullscreen apps start/stop; reconciliation is still needed.
