# Always on top: right-click menu compatibility fix

Date: 28 October 2026  
Status: **Implemented in the 1.8.0-beta.1 working tree (28 October 2026)** — see section 11 for what was verified and what remains open. Sections 1–10 are the original proposal.  
Target: the local HzSwitcher 1.8.0-beta.1 working tree, based on commit `8d202bef9d0b6b0734951ac1227db0bd0dabf413`.

## 1. Problem and intended result

Some windows show Restore, Move, Size, Minimize, Maximize and Close when their title area is right-clicked, but no Always on top command. The supplied screenshot demonstrates the missing command; it does not identify the application, its privilege level, or the exact event path.

**Recommended fix: integrate Always on top into the actual Windows system menu for supported windows, and retain the compact HzSwitcher menu as an explicit fallback.**

Expected behavior:

- Normal title-bar right-click on a supported window opens its native menu with one **Always on top (HzSwitcher)** entry.
- The same native command is available through Alt+Space and the window icon when those routes open the same system menu.
- The entry reflects whether HzSwitcher owns the pin. Selecting it toggles only that window.
- Ordinary editor, browser, terminal, file-list and application-content context menus continue to behave normally.
- Shift+right-click and the existing hotkey remain available for custom menus and windows that cannot expose an accessible native system menu.

Do not promise that an entry can be added to every arbitrary application-owned context menu. A title area can use a true Windows system menu, a copied menu or a completely custom interface. Compatibility must be recorded by application and version.

## 2. Findings from the current source

### Confirmed routing limitation

`Native/WindowMouseHook.cs` considers an ordinary right-click eligible only when `WindowFrameApi.IsFrameCandidate()` returns true. `Native/WindowFrameApi.cs` implements that check as **inside the window rectangle but outside the client rectangle**.

Custom or extended title bars can occupy client coordinates. In that case, the hook passes the click through before the later `WM_NCHITTEST` check in `TrayApp` can run. A caption-like area inside the client rectangle never reaches the current frame-menu replacement path.

This is a concrete compatibility gap in the code. It is a likely explanation for the screenshot, not a confirmed reproduction of that particular application.

### The current menu is a replacement popup

`UI/WindowPinMenu.cs` builds a separate WinForms `ContextMenuStrip`, optionally copying standard menu commands through `WindowFrameApi.ReadCommands()`. It does not currently insert an entry into the target window's system menu.

Consequently, any path that opens the original native menu without passing through HzSwitcher's frame-click hook can omit the command. Keyboard invocation, custom title-bar routing and app-specific handling need explicit coverage.

### Existing strengths to retain

- `AlwaysOnTopService` owns pin state, checks window identity and avoids taking ownership of another tool's topmost window.
- Menu actions revalidate the captured window rather than simply pinning whichever window is foreground later.
- The low-level mouse hook avoids synchronously sending hit-test messages into another application.
- Shift+right-click, the hotkey, borders, sounds and cleanup already exist.

The fix should change menu discovery/integration rather than replace the pinning service.

## 3. Proposed design

### A. Native system-menu integration as the primary route

Add a `WindowSystemMenuService` that obtains eligible windows' current menus using `GetSystemMenu(hwnd, false)` and inserts one string command using `InsertMenuItemW`.

Use **Always on top (HzSwitcher)** as the proposed native label so it remains distinguishable when PowerToys or another utility is installed. Insert it before Close where possible; preserve all existing commands, order, state and custom submenus. Do not reset or reconstruct the application's menu.

Windows documents system-menu modification and custom command IDs below `0xF000`. Microsoft PowerToys has also implemented native menu insertion plus out-of-context menu/invocation event handling. That is feasibility evidence, not a guarantee for every application. See [GetSystemMenu](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getsystemmenu), [InsertMenuItemW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-insertmenuitemw), and [PowerToys' implementation change](https://github.com/microsoft/PowerToys/pull/45773).

### B. Prepare menus before the click

On enable:

1. Register narrowly scoped out-of-context WinEvent hooks.
2. Prepare the current foreground window immediately.
3. Process existing eligible top-level windows in bounded batches so already-open and inactive windows are covered.
4. Refresh candidate windows on foreground/show events, and detect window/menu destruction or replacement.
5. Observe system-menu popup start/end to refresh checked state and recover from a recreated menu where the event timing permits it.

Do not modify thousands of windows synchronously during startup. Filter out shell surfaces, menu/tooltip windows, HzSwitcher's own menus and invalid targets; de-duplicate pending work by identity.

Popup-start timing alone may be too late for first-open rendering. The feasibility step must verify first-click behavior, including applications that rebuild their menu during opening. If this cannot be made dependable for an app, classify its native integration as unsupported and expose the fallback; do not claim successful first-click support based only on the next opening.

### C. Route selection to the original target

A system-menu selection normally sends `WM_SYSCOMMAND` to its owning application. Adding an item alone is not enough: HzSwitcher must also observe a reliable invocation event and route it to its own pin service.

Prototype `EVENT_OBJECT_INVOKED` alongside popup lifecycle events. Determine whether the accessible event's child identity is a command ID or position for each supported route; do not assume identical encoding across providers. Validate it against the exact menu item that HzSwitcher inserted.

Keep a menu-session record containing the original `WindowIdentity`, menu handle, allocated command ID, ownership marker and session generation. Correlate the event to that record, then revalidate the window and menu before calling `AlwaysOnTopService.Toggle(identity)`.

The existing identity includes process start time, but that alone cannot detect an HWND reused by the same process. Add an observed window-lifecycle generation to menu ownership and invalidate it on destruction/recreation; reject late events from an older generation.

**Never fall back to pinning the current foreground window when the original menu owner is ambiguous.** Closing or switching windows while a menu is open must not redirect a pin action. Keyboard and pointer activation must each invoke the toggle exactly once; deduplicate repeated accessibility events without blocking a later deliberate second activation.

If invocation observation is unreliable on a target app, do not leave a visible command that silently does nothing. Remove the owned item when appropriate and classify the native route as unavailable for that app.

### D. Prevent command collisions and accidental cleanup

Do not hard-code PowerToys' command ID. Other utilities and the application can already use it.

- Allocate an unused, appropriately aligned ID below `0xF000` for each menu, inspecting existing commands and relevant submenus.
- Track a process-session ownership marker in `MENUITEMINFO.dwItemData`, with an in-memory ownership table. Treat item data as an opaque value, never a pointer to dereference in another process.
- Validate marker representation with both 32-bit and 64-bit target applications before relying on it.
- On refresh, update an owned entry in place; do not append duplicates.
- If the menu handle or its marker changes, discard stale ownership and re-evaluate.
- On disable/exit, remove only entries whose window identity, menu, ID and marker still match.
- Never call `GetSystemMenu(hwnd, true)` to clean up: it resets the application's menu. Never destroy a menu owned by another application.

Unexpected app reuse of an ID must cause HzSwitcher to relinquish the entry, not overwrite the app command.

### E. Keep the mouse hook as a fallback

When native integration is enabled, allow ordinary right-clicks to reach the application. Do not show the existing replacement popup on top of the native popup.

Retain the mouse hook for Shift+right-click and clicks on HzSwitcher's own pin outline. Keep its callbacks short and post work to the UI thread. Microsoft documents timeout/removal risks for low-level hooks; moving synchronous cross-process probing into that callback is not the fix. See [LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc).

The old geometric frame interception may remain as an explicitly selected legacy compatibility mode during development. It should not run in parallel with native integration on the same click. Do not broaden it to capture an arbitrary top strip of every window: that would steal right-clicks from browser tabs, search fields and toolbars.

### F. Privilege and custom-window limits

Treat an elevated application, a denied menu write, an unavailable menu and a pinning failure as different results. Do not label a registered mouse hook as proof that native menu integration works.

Do not automatically elevate HzSwitcher, install a service or inject code into target processes for this fix. Identify the boundary in settings, keep existing fallback gestures where permitted, and report permission failures clearly. Elevated targets require explicit compatibility checks; they are not guaranteed even if an entry can be inserted.

For app-owned HTML/Qt/other custom menus that do not surface the real system menu, the fallback remains appropriate. App-specific integration is considered only after a reproducible case establishes what menu the app uses.

## 4. UI and settings

Keep the master **Enable always on top** switch. Separate the current combined mouse-menu preference into clear controls:

- **Show Always on top in window system menus** — native integration.
- **Enable Shift+right-click menu** — explicit fallback.
- **Enable pin-outline right-click menu** — retain existing outline access.

Suggested status text:

- “System-menu integration ready.”
- “Shift+right-click fallback ready.”
- “This app uses a custom menu; use Shift+right-click or Ctrl+Alt+T.”
- “Menu access was denied for this window.”

Keep detailed window class, error codes and event correlation in opt-in diagnostics. Do not show repeated notifications for normal right-clicks or successful pin/unpin actions.

Migration must preserve opt-outs: if the existing `AlwaysOnTopMouseMenu` setting is false, do not silently enable either native integration or the fallback. If it is true, migrate to the new native route and fallback once the feasibility gate passes. Master-disabled installations remain disabled. Preserve hotkeys, border/sound preferences and every automation setting from the existing beta.

Version the settings change deliberately and update the current schema reader's supported maximum; do not create settings that the same executable then rejects. Back up before migration and retain existing unknown-newer-schema preservation.

## 5. File-level changes proposed

| File | Intended change |
| --- | --- |
| `Native/WindowSystemMenuApi.cs` (new) | Narrow wrappers for item insertion/query/update/removal and ownership validation |
| `Native/WindowMenuEventObserver.cs` (new) | Out-of-context WinEvent registration and minimal queued event records |
| `Services/WindowSystemMenuService.cs` (new) | Eligibility, preparation, session ownership, ID allocation, invocation correlation, refresh and cleanup |
| `Native/WindowMouseHook.cs` | Explicit fallback routing; prevent duplicate native/replacement popup handling |
| `Native/WindowFrameApi.cs` | Retain appropriate legacy helpers; stop treating outside-client geometry as general custom-title-bar support |
| `UI/WindowPinMenu.cs` | Keep compact fallback actions; make legacy frame-copy mode explicit |
| `TrayApp.cs` | Initialize/dispose the new service, route results through existing feedback and synchronize checked state |
| `Services/SettingsStore.cs`, `UI/MainForm.cs` | Separate preferences, migration and accurate readiness status |
| `tests/HzSwitcher.Tests/WindowFrameTests.cs` and new menu-service tests | Ownership, collision, invocation, compatibility and cleanup coverage |
| Isolated native-window test helper | Real system menu and accessibility-event fixtures, including menu recreation |
| `README.md`, `CHANGELOG.md` | Explain supported native routes, fallback and remaining limitations after validation |

Keep the new service independent of display-rate automation. Do not alter the already implemented preview, pause, history or recovery behavior as part of this menu fix.

## 6. Implementation sequence and gates

### Phase 0 — reproduce and prove the native route

Record the screenshot app's executable/version and where the click occurs; the image alone is insufficient. Identify whether that popup is its actual `GetSystemMenu` menu or a separate application-owned popup. Reproduce using the current HzSwitcher build with the feature enabled.

Build an isolated native-window fixture to verify insertion, first-open visibility and pointer/keyboard invocation with out-of-context events. Include a custom-frame window whose client rectangle covers its caption, and an app that recreates its system menu.

**Gate:** native item visibility and exactly-once toggle must work in the fixture and the reported app if it exposes a supported native menu. A command that appears but cannot be invoked does not pass. If the reported app uses a custom popup, record that finding and validate its explicit fallback before claiming resolution.

### Phase 1 — ownership-aware service

Implement bounded preparation, ID/marker ownership, stale-event rejection, checked/disabled states and cleanup. Reuse the existing pin service for actual topmost changes. Add failure injection for denied access, changed menu handles and destroyed windows.

**Gate:** no duplicate entry, no command collision, no wrong-target pin and no deletion of app-owned menu entries.

### Phase 2 — route integration and settings

Make native menus primary, keep Shift/outline fallbacks, migrate preferences and expose separate readiness states. Handle partial hook-registration failure by unwinding hooks already installed. On disable, stop new work before cleaning up owned items.

**Gate:** ordinary content menus remain intact, native and replacement menus never appear together, and toggling the feature cannot leave stale active handlers.

### Phase 3 — compatibility validation and local review build

Run targeted automated checks and a manual Windows app matrix, record exact results and render menu screenshots. Only then build a versioned local beta. Publication is separate from this proposed fix.

## 7. Acceptance checks

### Automated

- Existing standard-frame behavior and all current pin/hotkey/border/sound regressions remain covered.
- Client-area caption fixture exposes the command through its real native menu.
- Native right-click, Alt+Space, keyboard navigation/Enter and mouse selection toggle exactly once.
- Menu open on window A followed by focus change to B never pins B.
- Recycled HWND, changed process start time, recreated HMENU and late events are rejected.
- App command IDs and another tool's entries remain intact; ownership markers survive the tested bitness boundary.
- Existing checked/disabled entries and custom submenus are preserved; an externally topmost window is not adopted by HzSwitcher.
- Disabling/exiting removes only HzSwitcher-owned entries; partial event-hook setup unwinds correctly.
- A hung/denied target cannot stall global mouse input or create an unbounded work queue.
- Migration preserves disabled settings and unrelated automation configuration.

### Manual Windows matrix

Test on Windows 10 and 11 where available, recording OS/app versions and privilege level:

- The actual app/window shown in the user's screenshot.
- A standard Win32 app, File Explorer and Notepad.
- Windows Terminal, Chrome/Edge and an Electron app such as VS Code.
- One custom-frame/Qt-style application if available.
- An elevated terminal or Task Manager to characterize permission limits.
- Coexistence with PowerToys Always On Top enabled.

For each: ordinary title-bar right-click, app-icon menu, Alt+Space, Shift+right-click, pin/unpin, maximize/restore, menus opened on inactive windows, app restart and menu recreation. Also test text selections, browser tabs, terminal panes, file context menus, caption buttons, negative monitor coordinates and mixed DPI. Verify both light/dark rendering and keyboard access.

Record **native supported**, **fallback supported**, **denied**, **not tested** or **unsupported**, with a reason. Do not replace these categories with a blanket “all windows work.”

No tests, fixtures, menu modifications, app launches or pin actions were performed while writing this proposal. The earlier 118-test result belongs to the previous milestone and does not validate this future fix.

## 8. Risks and boundaries

- Apps can recreate or ignore system menus. First-open and invocation reliability are explicit release gates.
- Accessibility events are notifications, not authority to act on a different window. Ambiguity must yield a no-op.
- Menu mutation can race app-owned updates; validate ownership before every action and cleanup.
- A forced crash may leave an inert inserted command in a target app until that app recreates its menu or restarts. Normal-exit cleanup is required, but crash cleanup cannot be guaranteed without additional lifecycle design.
- This proposal does not add generic entries to every browser/editor content context menu.
- It does not require administrator mode or process injection as a default workaround.

## 9. Additional technical references

- [Microsoft: custom DWM frames](https://learn.microsoft.com/en-us/windows/win32/dwm/customframe) — title/frame behavior can extend into client coordinates.
- [Microsoft: WM_NCHITTEST](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest) — distinguishes caption, client, buttons and borders.
- [Microsoft: accessibility event constants](https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants) — verify events actually emitted by target UI elements.
- [Microsoft: WM_SYSCOMMAND](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-syscommand) — native menu command delivery and reserved low bits.
- [Microsoft: SendMessageTimeout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw) — bounded cross-process queries outside low-level input callbacks.

## 10. Authorization boundary

The current request authorizes this Markdown plan only. No source/configuration changes, builds, tests, package updates, installation, menu mutation, pinning, commits or pushes are included in this planning turn. Implementation remains unstarted until the user authorizes it.

## 11. Implementation record

Implemented: `Native/WindowSystemMenuApi.cs`, `Native/WindowMenuEventObserver.cs`, `Services/WindowSystemMenuService.cs`, TrayApp wiring (100 ms preparation pump, observer lifecycle, status), `WindowMouseHook.FrameMenus`, a new `AlwaysOnTopSystemMenu` setting and General toggle, README/CHANGELOG.

Settings: no schema bump was needed. The new property defaults on; files without it inherit `AlwaysOnTopMouseMenu`, so existing opt-outs stay off. Older builds ignore the extra property.

Phase 0 findings (Windows 11 Pro 10.0.26300, isolated WinForms fixture in a separate process, synthetic Alt+Space, arrow/Enter and mouse click):

- Insertion before `SC_CLOSE` is visible on first open when prepared beforehand.
- Selection raises exactly one `EVENT_OBJECT_INVOKED` from the `#32768` popup with `OBJID_SYSMENU` and **child = command ID** (not position), before `EVENT_SYSTEM_MENUPOPUPEND`, for both keyboard and mouse.
- `GetGUIThreadInfo.hwndMenuOwner` identifies the real owner during invocation.
- The target also receives `WM_SYSCOMMAND` with the inserted ID; `DefWindowProc` ignores it.
- End-to-end with the production service/observer/`WindowApi`: pin → topmost + checked, unpin → cleared + unchecked, one toggle per selection, zero owned items after disable.

Automated: 129 tests pass (118 existing + 11 in `SystemMenuTests.cs`: ID collision avoidance, no duplicates, exactly-once toggle, owner vs foreground, foreign controls, recycled HWND, recreated menu, externally topmost, ownership-safe cleanup, denied writes, settings migration, real Win32 insert/update/remove).

Still open (manual matrix, section 7): the app from the screenshot, Explorer, Terminal, Chrome/Edge, VS Code/Electron, elevated targets, PowerToys coexistence, Windows 10, mixed DPI. Elevated targets currently show the entry if the write succeeds; the pin then fails with a reported error rather than being pre-classified.
