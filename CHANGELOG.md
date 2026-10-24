# Changelog

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
