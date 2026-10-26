# Always on top: outline, sound and mouse menu

Status: implemented after user approval. The original design below is retained for reference. Automated build and regression checks cover service transitions, preferences, mouse button pairing, geometry, native overlay behavior and hook lifetime. Manual multi-monitor, application compatibility, audio and session checks remain pending. No version bump or installer rebuild was performed.

## Follow-up: frame menu and quiet success (2026-10-27)

Implemented ordinary right-click on standard non-client title bars/frames. A UI-thread hit test validates the frame after bounded geometry-only capture in the mouse hook. HzSwitcher displays a separate popup populated from the target's system menu plus a checked Always on top action, and forwards selected window commands after identity revalidation. The original menu remains accessible. Client content clicks pass through; custom title bars can use Shift+right-click. No cross-process injection or persistent menu mutation is used.

Successful pin/unpin and Unpin all operations no longer show balloons. Outline and optional sound remain; failures and already-topmost explanations still notify. The design below records the original 1.7.0 proposal and its earlier gesture scope.

## Goal

Extend the existing Always on top feature with a visible outline around pinned windows, a short sound when pinning/unpinning, and a mouse menu for selecting Always on top. Keep the existing shortcut, ownership rules, Unpin all and graceful-exit cleanup.

## Proposed experience

1. Enable Always on top on General as today.
2. Focus a window and use the existing shortcut, or **Shift+right-click anywhere on the target window** to open a small HzSwitcher menu containing **Always on top**.
3. After a successful pin, an accent-colored outline appears and a short pin sound plays. The menu item is checked for a window managed by HzSwitcher.
4. Repeat the shortcut/menu command to unpin; the outline disappears and a different short sound plays.
5. A normal right-click on the visible outline also opens this menu. Include **Unpin this window**, **Unpin all**, and **Always on top settings** where appropriate.

Mouse actions target the clicked window, even if another window was previously active. Opening the menu must never cause the menu itself to become the pin target.

## Right-click behavior: scope decision

The exact request for an ordinary right-click item inside every application's own menu cannot be implemented reliably through a simple Windows API. Applications own their client context menus, and many draw custom title bars. Adding an item to another process's system menu does not make HzSwitcher receive its command; that process handles `WM_SYSCOMMAND`.

**Recommended first implementation:** Shift+right-click opens a separate HzSwitcher menu on any eligible window; normal right-click on the outline opens the same menu. Existing application context menus and native title-bar menus keep their normal behavior. This is a proposed adjustment to the requested gesture, not a claim that the exact ordinary-right-click behavior is implemented.

If ordinary right-click on an unpinned window's title bar is mandatory, design that as a separate compatibility project before coding. Options involve intercepting/replacing native menus or a native bridge running in target processes, with elevated-process, architecture, custom-title-bar and application compatibility concerns. Do not add a nonfunctional system-menu item, modify Explorer shell menus, or silently inject into other applications. PowerToys-style outline and sound do not require that machinery.

## General settings

Extend the existing Always on top card with:

| Setting | Default | Behavior |
| --- | --- | --- |
| Show outline around pinned windows | On | Show/hide outlines without changing pins. |
| Outline thickness | 3 logical pixels | Allow 1–8; scale for the target monitor's DPI. |
| Outline color | Windows accent | Follow the existing theme/accent change event. Custom color can follow later. |
| Play sound when pinning or unpinning | On | Independent of auto-switch notifications and Windows notification sounds. |
| Enable Shift+right-click menu | On while feature enabled | Helper text states the gesture explicitly. |

Use additive settings properties with initializers so older settings files receive these defaults. Do not reset existing Always on top preferences or shortcuts. Disabling the main feature disables new pin actions and its mouse hook; pending failed-restoration records remain accessible through Unpin all.

## Service events and a common command path

`AlwaysOnTopService` currently returns strings and publishes only a general `Changed` event. Refactor it to return structured results with status, target identity, user-facing message and optional native error. Introduce target-specific `Toggle(WindowIdentity)` alongside `ToggleForeground()` and expose a read-only snapshot of managed identities.

Publish explicit successful pin/unpin/removal events. All entry points—shortcut, mouse menu and Unpin all—use the same policy and target revalidation. Feedback must follow confirmed state transitions, never string matching or an optimistic click handler.

- Create an outline only after a confirmed pin.
- Remove it after confirmed unpin, destruction, identity mismatch or detection that the window is no longer topmost.
- A failed unpin retains ownership and its outline while the window remains topmost; expose retry through the menu and Unpin all.
- A window already topmost before HzSwitcher touched it remains untouched, gets no HzSwitcher outline and plays no success sound. Its menu item is disabled with explanatory text.
- If another app clears a managed window's topmost state, release the record and remove feedback; do not force it back on top.

## Outline implementation

Add a dedicated border presenter owned by the tray application's UI thread. Use four narrow border overlay windows per managed target, or an equivalent hollow window region. Do not paint onto another application's device context or change its styles to draw the outline.

Overlay requirements:

- Borderless tool windows, absent from the taskbar and Alt+Tab, shown without activation (`WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `SWP_NOACTIVATE`). Use layered drawing for color/opacity if needed.
- A genuinely empty center: clicks in the target's content must go directly to the target. Do not rely solely on `WS_EX_TRANSPARENT` or `HTTRANSPARENT` to guarantee cross-process click-through.
- Border strips receive right-click for the companion menu. Forward other pointer actions where practical or place strips outside the visible frame so they do not cover resize handles/content. Validate resizing carefully.
- Read visible frame bounds using `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`, with a documented `GetWindowRect` fallback. Work in physical screen coordinates, including negative monitor coordinates. Scale thickness using target DPI.
- Keep each outline immediately above its own target in the topmost band. Avoid activating the target or repeatedly bringing it above unrelated topmost windows. Resolve stacking on foreground and target order changes; test overlapping pinned windows explicitly.
- Hide outlines for minimized, invisible, DWM-cloaked or off-desktop targets. Hide them when appropriate on session lock. Reconcile on unlock, restore and virtual-desktop transitions.
- Follow move, resize, maximize, restore, display/DPI changes, theme changes and destruction. Clip strokes to monitor boundaries and avoid visible desktop artifacts around maximized windows.

Extend out-of-context WinEvent observation for relevant location/state/foreground changes. Filter notifications by tracked HWND and object/child identifiers; keep callbacks short and coalesce UI updates. Preserve a strong delegate reference and unhook on disposal.

Use a low-frequency reconciliation timer only while managed windows exist to detect missed events, cloaking and external topmost changes. This checks state and geometry; it must not repeatedly call `SetWindowPos(HWND_TOPMOST)` on target windows. Dispose unused overlays promptly. Border failures must not undo a valid pin: log the failure and show feedback that the window is pinned but its outline is unavailable.

## Sound implementation

Embed two short WAV assets, one for pin and one for unpin, with a clear source/license or generate simple project-owned tones during implementation. Play them asynchronously through `System.Media.SoundPlayer`; preload and reuse players. No new audio dependency is needed.

- Play once on confirmed user pin/unpin, from either shortcut or mouse menu.
- No sound for invalid targets, already-topmost windows, failed operations, destruction, external state changes, startup or shutdown cleanup.
- Unpin all may play a single unpin cue if at least one window was successfully restored; never one cue per window.
- Suppress cues during automatic disable cleanup. Honor the sound preference immediately and avoid overlapping repeated sounds.
- Sound playback failure must not block pinning, remove ownership, or show repeated error dialogs. Log once per failure condition.

Keep existing error feedback. Successful operations may continue to use existing notifications in the first iteration; sound and border are independent of `NotifyOnAutoSwitch`.

## Mouse menu implementation

Add an opt-in low-level mouse hook (`WH_MOUSE_LL`) active only when the main feature and mouse-menu preference are enabled.

1. On a physical Shift+right-button down, capture the screen point and resolve the root window under it. Ignore HzSwitcher menus/overlays, desktop, taskbar and other unsuitable targets; route outline clicks directly through their known target identity.
2. Keep the hook callback bounded: no process inspection, cross-process synchronous messages, UI construction, sound, I/O or geometry refresh. Always call the next hook for unrelated input and injected input.
3. For an intercepted gesture, suppress its matching right-button down/up pair, even if Shift is released between them. Queue the captured target to the UI thread and open the menu after release. If target validation fails after interception, show brief feedback rather than changing a different window.
4. Inspect and retain the target's full identity before showing the menu. Revalidate it when a menu item is selected; reject a destroyed/replaced window. Never fall back to whichever window now has focus.
5. Use a themed `ContextMenuStrip`; Escape and clicking elsewhere dismiss it. Exclude menu HWNDs from target detection and outline tracking.
6. Disabling/uninstalling the hook must finish or safely release any captured gesture without swallowing unrelated clicks. Unhook on shutdown and keep the callback delegate rooted.

Windows can remove a slow low-level hook. Minimize callback work, report initialization failures and do not display a misleading ready status. Elevated/protected apps and secure desktops remain limited; keep the shortcut and settings controls available as fallbacks.

## Planned files

| File | Change |
| --- | --- |
| `Services/AlwaysOnTopService.cs` | Structured outcomes, target-specific commands, snapshots and confirmed transition events. |
| `Native/WindowApi.cs` | Geometry, DPI, cloaking and additional lifecycle events; preserve native error handling. |
| `Services/AlwaysOnTopBorderService.cs` (new) | Reconcile managed targets with overlay lifetime, bounds and visibility. |
| `UI/AlwaysOnTopBorderWindow.cs` (new) | Nonactivating hollow border rendering and outline right-click. |
| `Services/AlwaysOnTopSoundService.cs` (new) | Reusable, asynchronous pin/unpin sound playback and preference gating. |
| `Native/WindowMouseHook.cs` (new) | Minimal Shift+right-click capture and correct mouse-pair handling. |
| `UI/WindowPinMenu.cs` (new) | Themed target-specific pin/unpin menu. |
| `Services/SettingsStore.cs` | Add outline, thickness, sound and mouse-menu preferences. |
| `TrayApp.cs` | Own presenters/hook/audio; route commands; order startup, disable and disposal. |
| `UI/MainForm.cs` | Extend General settings, gesture helper and operational status. |
| `Assets/`, `HzSwitcher.csproj` | Embedded sound resources. |
| `tests/HzSwitcher.Tests/` | Service, geometry, feedback and mouse-gesture tests. |
| `README.md`, `CHANGELOG.md` | Document the actual gesture and limits after implementation. |

## Implementation order

1. Refactor command outcomes and target-specific operations with regression tests.
2. Implement border overlays and lifecycle/geometry reconciliation.
3. Add sound assets, feedback policy and settings controls.
4. Add the mouse gesture, target-specific menu and hook status.
5. Validate interactions, update documentation and report remaining compatibility limits.

Do not bump the version, publish binaries or rebuild the installer until requested.

## Validation

Automated tests should cover confirmed feedback only, native failure, target identity changes, multiple windows, cleanup retries, external topmost removal, settings defaults/round-trip, thickness clamping and DPI/negative-coordinate geometry. Test mouse down/up pairing, Shift release, injected/unrelated input, stale menu targets and disable during an active gesture using fakes. Extend the native smoke tests to check overlay nonactivation and teardown where practical.

After code approval and implementation, run `dotnet build -c Debug` and `dotnet test tests\HzSwitcher.Tests`.

Manual acceptance checks:

- Pin/unpin Notepad, a browser and HzSwitcher through the shortcut and mouse menu; verify the clicked target is used without unwanted activation.
- Confirm ordinary right-click still opens each application's existing menu. Confirm Shift+right-click and outline right-click show the HzSwitcher menu.
- Verify outline alignment during rapid movement/resizing, snap layouts, maximize/restore, mixed-DPI monitor movement, negative coordinates and 100%/150%/200% scaling.
- Overlap two pinned windows and unrelated topmost windows; outlines must not draw across windows above their target.
- Test minimize/restore, hide/show, virtual desktops, lock/unlock, target destruction and handle replacement; check for orphan overlays and excess CPU.
- Verify sound on successful user operations only; test mute preference, rapid toggles, Unpin all, cleanup and sound-device failure.
- Confirm mouse click pairs remain normal after Escape, preference changes, hook failure and shutdown. Test custom title bars and elevated-app limits.
- Rebuild an installer only after requested separately; no installer run is part of this proposal.

Implementation followed the approved Shift+right-click approach. Validation: Debug build succeeded without warnings; all 56 tests passed. The manual acceptance checks above remain pending.
