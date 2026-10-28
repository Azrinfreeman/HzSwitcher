# Changelog

## 1.8.0 - 2026-10-29

- Added session-only automation pause for 15 minutes, 1 hour, or until restart, with ordered saver/profile restoration, explicit retry after restoration failure, and fresh qualification on resume.
- Added read-only diagnostics with a sanitized clipboard report, cached brightness backend/read details, and monitor-scoped failures from refresh-rate verification and brightness writes.
- Reworked Display around monitor identity, discrete refresh-rate choices, neutral preset actions, and brightness controls on one surface.
- Replaced repeated settings cards with aligned sections, structured shortcut rows, and a compact General page. Detailed pinning preferences now open through Configure.
- Aligned editor layouts and Save/Cancel footers; moved specialist saver controls into Detection settings without duplicating ordinary automation preferences.
- Improved contrast, accessible selection/navigation, shortcut recorder keyboard escape routes, stable controls during refresh, and working-area constraints.
- Commit brightness after mouse-wheel and accessibility changes as well as dragging and keyboard input.

## 1.7.3 - 2026-10-28

- Suspend Always on top mouse menus and outlines while a fullscreen app or configured game is foreground, so gameplay right-clicks (including Shift+right-click) pass through.
- Cancel pending menu actions during gameplay transitions and preserve right-button down/up pairing. Keyboard pin shortcuts and General > Unpin all remain available.

## 1.7.2 - 2026-10-28

- Fixed Original window menu failing to open from resize edges and during the handoff from the HzSwitcher popup. The popup now closes before requesting the native Alt+Space menu.

## 1.7.1 - 2026-10-27

- Right-click a standard title bar or frame to open a window menu with Always on top alongside the app's window commands. Custom title bars retain the Shift+right-click fallback.
- Preserve checked/disabled system-menu commands and provide access to the original window menu for specialized entries.
- Removed notification popups for successful pin/unpin and Unpin all actions; errors and explanatory feedback remain available.

## 1.7.0 - 2026-10-26

- Added accent-colored outlines for pinned windows, with DPI-scaled thickness and visibility/position tracking.
- Added distinct pin/unpin sound cues and General settings for outlines, thickness, sound and the mouse menu.
- Added Shift+right-click on a window and right-click on its outline to open a target-specific pin menu.
- Revalidate menu targets before changing window state and release tracking when another app clears topmost.

## 1.6.0 - 2026-10-26

- Added an optional Always on top feature on General: Ctrl+Alt+T pins/unpins the active window, with an editable shortcut, readiness status and Unpin all.
- Windows pinned by HzSwitcher are restored on disable or graceful exit. Existing topmost windows are left unchanged; forced termination cannot guarantee cleanup.
- Shortcut validation rejects multiple primary keys and detects duplicate combinations regardless of modifier order or aliases.

## 1.5.0 - 2026-10-25

New look and layout.

- Dark theme that follows the Windows accent colour (updates live when the accent changes). Windows high-contrast mode uses system colours instead.
- The main window now has a navigation rail with four pages: Display, Automation, Shortcuts, General. Fixed size, scaled for display DPI (the old window did not scale at 125% / 150%).
- New controls: rounded cards, pill buttons, toggle switches, an accent slider, rate chips, status chips and a number box with − / + buttons (replaces the stock number boxes).
- Tray menu is dark and rounded, with a themed brightness slider. The tray icon is drawn at the real icon size, fits three-digit rates, and shows a corner dot for the saver (green) or an active profile (amber).
- Presets, App profiles, Hotkeys and Advanced settings dialogs share one dark layout with a Save / Cancel bar and are scaled for DPI.
- The saver and profile state appear as chips under the current rate instead of a line of text.
- Keyboard and accessibility: the rail is first in tab order, inputs have names, the slider and number box report their value.
- "Restore each monitor's brightness at startup" moved to the General page.
- Added unit tests (`tests/HzSwitcher.Tests`), this changelog and a README.
- The installer script now uses relative paths.

## 1.4.0 - 2026-10-25

Brightness control.

- Brightness slider in the tray menu and on the main window, with − / + buttons.
- Control methods in order: DDC/CI for external monitors, WMI for laptop panels, software dimming as a fallback (labelled as such, 20% minimum).
- Per-monitor remembered level, and an option to restore it at startup (off by default).
- Auto power-saver can set a lower brightness while active ("Saver brightness") and puts your level back afterwards. A level changed by hand during the saver becomes the one restored. An interrupted saver is recovered at the next start.
- Slider changes are applied on a background thread; only the newest value is written.

## 1.3.1 and earlier

Not documented here. See `IMPLEMENTATION_PLAN.md` and `FLICKER_REDUCTION.md` for the state of the app at 1.3.x.
