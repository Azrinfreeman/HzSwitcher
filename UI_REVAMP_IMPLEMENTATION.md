# UI revamp: layout and colour

Baseline: HzSwitcher 1.4.0, C# / .NET 10 Windows Forms.
Status: proposal only. No code has been changed.

This plan comes from reading the UI source (`MainForm`, `Theme`, `SaverBar`, the four dialogs, the tray menu in `TrayApp`). I tried to screenshot the running app but the capture failed, so nothing here is based on a visual review. Please check the observations in section 1 against what you see.

## Decisions (from your answers)

| Question | Decision | Effect on the plan |
| --- | --- | --- |
| Layout | Option A: nav rail and pages | Plan proceeds as written, with the changes below. |
| Accent colour | Follow the Windows accent | Accent is read from Windows and updates live (section 4). |
| Theme | Dark only | The light palette, the Light/Dark/System setting and light/dark live switching are dropped. Windows high-contrast mode is still honoured. |
| Window size | Fixed | No resizing: the rail does not collapse, the narrow-window and minimum-size work is dropped. The window is still scaled for display DPI. |
| Reference look | None | Visual choices are mine; section 2 and 4 stand as proposed. |

## 1. What is there now

| Area | Current state | Problem |
| --- | --- | --- |
| Main window | Two fixed 470 px columns of "cards" in a scrolling panel; 1,000+ px wide, 880 px tall by default | Taller than a 768 px laptop screen, so it scrolls; width forces a wrap to one column on small windows |
| Hierarchy | Every card has the same weight; only the big "240 Hz" and the blue selected button stand out | The most-used controls (rate, brightness) sit among rarely-used ones (hotkeys, profiles, startup) |
| Controls | Flat grey rectangular buttons, stock check boxes, stock `NumericUpDown`, stock `ComboBox`, stock `TrackBar` | Look dated next to Windows 11; dark theme leaves white or system-coloured parts on stock controls |
| Auto power-saver card | Absolute pixel positions (`Location(300, y)`, fixed `Height = 206`) | Breaks at 125/150% DPI and with longer text |
| Colour | One accent (`#2563EB`), green for saver, a dark/light palette picked once at launch | No live theme switching; accent ignores the Windows accent; status colours rely on colour alone |
| Dialogs | Settings, Presets, Profiles, Hotkeys are separate `FixedDialog` forms with default Windows chrome inside | Inconsistent with the main window; different sizes and spacing |
| Tray menu | Stock `ContextMenuStrip`; brightness slider hosted with `SystemColors.Menu` | Menu stays light in dark mode, so the slider looks like it belongs to another app |
| Tray icon | Blue (or green) circle with the Hz number | Small digits are hard to read at 16 px; colour is the only state cue |

## 2. Design direction

A calm, modern Windows 11 look that puts the two things people do most (change rate, change brightness) at the top and moves configuration behind navigation.

1. **Navigation rail + pages** instead of one long scroll. Four pages: **Display** (current rate, presets, rates, brightness), **Automation** (power-saver, app profiles), **Shortcuts** (hotkeys), **General** (notifications, startup, advanced, log). Fixed 780 × 560 (scaled with DPI), not resizable. Position is remembered; size is not.
2. **Rounded surfaces, quiet borders.** Cards get a 1 px border and a 8 px radius instead of a flat fill. The window uses the Windows 11 backdrop (Mica) when available.
3. **One clear primary action per area.** The selected rate is a filled accent chip; everything else is neutral.
4. **Status you can read without colour.** The saver state, profile state and errors get an icon and a text label as well as a colour.
5. **Dark, with the Windows accent.** One dark theme; the accent follows the Windows accent colour and updates live when it changes.

Alternative if you prefer less change: keep one scrolling page but single column (about 520 px wide), with the same visual styling. This is cheaper and lower risk; it is listed as option B in section 9.

## 3. Layout

```
+--------------------------------------------------------------+
| [rail]  |  Display                                           |
|         |  +----------------------------------------------+  |
| Display |  |  240 Hz     3840x2160     [ Monitor v ]       |  |
| Auto    |  |  (chip) Saver off   (chip) Profile: none      |  |
| Keys    |  +----------------------------------------------+  |
| General |  Presets   [Gaming 240] [Balanced 120] [Saver 60]  |
|         |  Refresh rate  ( 60 | 100 | 120 | 240 )          |
|         |  +----------------------------------------------+  |
|  v1.4.0 |  |  Brightness  [sun]  ---------o------  77%     |  |
|         |  +----------------------------------------------+  |
+--------------------------------------------------------------+
```

- **Rail:** fixed 180 px wide (icon + label). The selected page has an accent bar on the left edge.
- **No scrolling by design:** each page is sized to fit 780 × 560. A page's panel keeps `AutoScroll` only as a safety net for large text scaling.
- **Display page:** hero card (current rate, mode, monitor selector, status chips), presets as pill buttons, rates as a segmented chip group (wraps), brightness card with a custom slider, +/− buttons and the control-method hint.
- **Automation page:** power-saver card (toggle, progress bar, three fields in a `TableLayoutPanel`), app profiles card with the list summary and "Manage…" button.
- **Shortcuts page:** hotkey list as rows (key cap + description) and "Edit hotkeys…".
- **General page:** notification and startup toggles, Advanced settings, Open log, Exit app (danger style, separated at the bottom).
- Spacing grid of 8 px; card padding 16 px; section gap 12 px; every size goes through a `Dp()` helper so it scales with DPI.

## 4. Colour

Define semantic tokens in `Theme` so no control uses a raw colour. Dark only. Values are starting points; contrast must be checked against the final surfaces (target 4.5:1 for text, 3:1 for UI edges) before shipping.

| Token | Value | Use |
| --- | --- | --- |
| `Bg` | `#16181D` | Window backdrop (fallback when Mica is unavailable) |
| `Surface` | `#1F2229` | Cards |
| `SurfaceRaised` | `#2A2E37` | Inputs, neutral buttons, rail hover |
| `Border` | `#343945` | Card and input outlines |
| `Text` | `#ECEEF3` | Primary text |
| `TextMuted` | `#9AA1B2` | Hints, labels |
| `Accent` | Windows accent, fallback `#4F8CFF` | Selected rate, primary buttons, slider fill, focus ring |
| `AccentText` | black or white by luminance | Text on accent |
| `Saver` | `#3FB950` | Saver active |
| `Warn` | `#E3B341` | Notices, fallback modes (e.g. software dimming) |
| `Danger` | `#F85149` | Exit, errors |

Rules:
- Read the Windows accent from `HKCU\Software\Microsoft\Windows\DWM\AccentColor` (fallback to the table); derive hover/pressed with the existing `Shift()` helper; derive `AccentText` from luminance so a light accent does not get white text. If the accent is too dark to see on `Surface` (below 3:1), lighten it for fills and focus rings.
- Subscribe to `SystemEvents.UserPreferenceChanged` so an accent change repaints the open window; `Accent` becomes a property rather than a `static readonly` field. Everything else in the palette stays constant.
- If `SystemInformation.HighContrast` is true, bypass the palette and use `SystemColors` so Windows high-contrast themes keep working.

## 5. Components

New files under `UI/Controls/`. Each is a small owner-drawn control with double buffering and an `AccessibleName` / `AccessibleRole`.

| Control | Replaces | Notes |
| --- | --- | --- |
| `Card` | `FlowLayoutPanel` card from `Section()` | Rounded rectangle, border, optional title and hint |
| `PillButton` | `FlatButton` | Variants: Neutral, Primary, Danger; hover, pressed, focus ring, disabled |
| `ChipGroup` | Rate button panel | Single-select, keyboard arrows, wraps; selected = accent fill |
| `ThemedSlider` | `TrackBar` in main window and tray | Track, accent fill, round thumb, wheel and arrow keys, `Scroll` event. Extends the existing `SaverBar` drawing. |
| `Toggle` | `CheckBox` | Switch style with label; same `CheckedChanged` event |
| `StatusChip` | Plain labels | Icon + text + colour; used for saver, profile, brightness method |
| `NavRail` | none | Icon + label buttons, selected indicator, collapses |
| `ThemedCombo`, `ThemedNumeric` | Stock combo / numeric | Only if the native dark styling (`DarkMode_CFD` / `DarkMode_Explorer`) proves inadequate in testing; otherwise keep stock controls with the existing native theming to limit work |

Icons: use the Segoe Fluent Icons font (Windows 11) with Segoe MDL2 Assets as fallback, and plain text labels if neither is present. No image files to ship.

## 6. Window chrome

- Rounded corners: `DwmSetWindowAttribute` `DWMWA_WINDOW_CORNER_PREFERENCE` (33) = round.
- Backdrop: `DWMWA_SYSTEMBACKDROP_TYPE` (38) = Mica when the OS supports it (Windows 11 22H2+); otherwise solid `Bg`. The window body is painted with transparency only where Mica should show through (rail and window background), not inside cards.
- Always use the dark title bar attribute (the app no longer reads the Windows light/dark setting, so `Theme.Dark` and `DetectDark()` are removed).
- Font: Segoe UI Variable Text when installed, else Segoe UI. Hero rate uses Segoe UI Variable Display / semibold at 44 px.

## 7. Tray menu and icon

- **Menu:** a `ToolStripProfessionalRenderer` subclass (`ThemedMenuRenderer`) with a custom `ProfessionalColorTable` using the tokens; rounded highlight, themed separators, themed check marks. The brightness item uses `ThemedSlider` and `Surface` colours.
- **Order:** Current rate header; presets; All refresh rates; Brightness; Monitor; Auto power-saver / App profiles toggles; Open / Exit. Same items as now, grouped with separators.
- **Icon:** rounded square badge instead of a circle, white bold Hz number with a dark outline for legibility, a small corner dot for state (green saver, amber profile). Render at the current DPI size (`SystemInformation.SmallIconSize`) rather than a fixed 32 px bitmap.

## 8. Dialogs

Create `DialogShell` (a base form: themed background, padding, title, button bar with Save and Cancel `PillButton`s, fixed size, scaled with DPI). Move Presets, Profiles, Hotkeys and Settings onto it, replacing absolute sizes with `TableLayoutPanel` layouts. Their logic stays unchanged; this is purely a layout and styling pass. Settings form's "Open settings file (hotkeys)" button moves to the General page.

## 9. Options

| Option | Scope | Risk |
| --- | --- | --- |
| **A. Full revamp** (chosen) | Nav rail, pages, custom controls, themed tray menu, dialogs | Highest effort; most owner-drawn code to test |
| **B. Restyle only** | Single scrolling column, `Card`/`PillButton`/`ChipGroup`/`ThemedSlider`, new palette, themed tray menu; no nav rail, dialogs get colour only | Medium |
| **C. Palette and spacing only** | New colours, spacing, fix DPI-fragile layout in the auto card; no new controls | Low |

Decision: **A**, delivered in the phases below so each phase is usable and B is a natural stopping point after phase 3.

## 10. Phases

1. **Theme foundation:** dark-only tokens, live Windows accent, high-contrast fallback, `Dp()` helper. Replace static palette uses and remove light-theme code. (Everything keeps working with the old layout.)
2. **Core controls:** `Card`, `PillButton`, `ChipGroup`, `ThemedSlider`, `Toggle`, `StatusChip`. Convert the existing main window to them in place.
3. **Tray:** themed menu renderer, themed brightness slider, new icon rendering.
4. **Navigation layout:** `NavRail`, four pages, window chrome (rounded corners, Mica), fixed window size, auto-card moved to `TableLayoutPanel`.
5. **Dialogs:** `DialogShell`, convert the four dialogs, move settings-file button.
6. **Polish:** keyboard focus order and visible focus, accessible names, version bump (suggest 1.5.0).

## 11. Testing

No UI test project exists, so verification is manual plus a screenshot pass:

- Changing the Windows accent colour while the app is open, including a very dark and a very light accent.
- Windows set to light mode: the app still shows its dark theme.
- 100%, 125%, 150% and 200% display scaling, and moving the window between two monitors with different scaling.
- Windows high-contrast theme.
- Keyboard only: Tab order, arrow keys in `ChipGroup` and `ThemedSlider`, Enter/Space on buttons.
- A 768 px-tall screen at 100% and 125% scaling: the fixed window fits and nothing is clipped.
- Windows 10 (no Mica or rounded corners) falls back cleanly.
- A screenshot of each page and dialog, saved for before/after comparison.
- Existing behaviour: rate switching, presets, saver, brightness, profiles and hotkeys all still work (no logic changes intended).

## 12. Risks

| Risk | Mitigation |
| --- | --- |
| Owner-drawn controls flicker or look blurry at fractional DPI | Double buffering, draw with `Dp()` units, test at 125% and 150% |
| Accessibility regression from custom controls | Set `AccessibleName/Role`, keep keyboard behaviour equal to the stock control, test with Narrator |
| Stock `ComboBox` / `NumericUpDown` look wrong in dark mode | Keep native dark theming first; build themed versions only if testing shows it is needed |
| Mica and rounded corners differ by Windows build | Feature-detect, fall back to solid `Bg` |
| Large diff touches every UI file | Phased delivery; no logic changes; phases 1–3 are shippable on their own |
| Fixed size cannot adapt to large text scaling or a long monitor name | `AutoScroll` safety net on each page; truncate long labels with ellipsis and a tooltip |
| Light accent colours make white text unreadable | Choose `AccentText` by luminance |

## 13. Open questions

None outstanding; the five questions above are answered in the Decisions table.

## 14. Progress (updated 2026-10-25)

| Phase | Status | Notes |
| --- | --- | --- |
| 1. Theme foundation | Done | Dark-only tokens in `UI/Theme.cs`, live Windows accent, high-contrast fallback, `Theme.Dp()` |
| 2. Core controls | Done | `UI/Controls/`: `Card`, `PillButton`, `ChipGroup`, `ThemedSlider`, `Toggle`, `StatusChip`, `Gfx`; main window converted in place; auto-saver card uses a table layout |
| 3. Tray | Done | `UI/ThemedMenuRenderer.cs`, themed brightness slider in the menu, icon drawn at real size with state dot |
| 4. Navigation layout | Done | `UI/Controls/NavRail.cs`, `MainForm` rewritten as four pages, fixed 780 x 560 (scaled for DPI), rounded corners. Mica backdrop skipped: it needs a transparent client area, which is fragile in WinForms |
| 5. Dialogs | Done | `UI/DialogShell.cs` base for Settings, Presets, Profiles, Hotkeys; `UI/Controls/NumberBox.cs` replaces `NumericUpDown`; drop-downs fixed by dropping `FlatStyle.Flat` (no custom combo needed). Dialog rows are built in `OnLoad` after scaling |
| 6. Polish | Done | Rail first in tab order, accessible names and values for slider / number box / rail, version 1.5.0. Release build: `artifacts\1.5.0\HzSwitcher.exe` |

Phase 4 note: the form's `AutoScaleDimensions` / `AutoScaleMode.Dpi` must be set at the END of the constructor, otherwise the window and controls are not scaled for the display (found at 150%). `PillButton` measures its own preferred size for the same reason.

Also done earlier: brightness feature (see `BRIGHTNESS_IMPLEMENTATION.md`).

Build and test notes:
- .NET 10 SDK is installed (via winget). Build with `dotnet build -c Debug -o %TEMP%\hzbuild2`.
- The newest test build is `%TEMP%\hzbuild2\HzSwitcher.exe` (temp folders may be cleared by a restart; rebuild if missing).
- Release build of the brightness feature only: `artifacts\brightness\HzSwitcher.exe` (1.4.0). It does not include any of the UI revamp.
- The installer script `artifacts\installer\HzSwitcher.iss` still points at an old path and needs fixing before an installer can be built; Inno Setup is not installed.
- Not yet checked by hand: dragging the new sliders, toggles and chips, and the tray menu open on screen.

Remaining: build the installer (fix the stale path in `artifacts\installer\HzSwitcher.iss`, install Inno Setup), README / changelog (none exist), unit tests (no test project). Text boxes in dialogs are still stock (square border).

Installer: rtifacts\installer\HzSwitcher-Setup-1.5.0.exe built with Inno Setup 6 (winget JRSoftware.InnoSetup) from rtifacts\1.5.0\HzSwitcher.exe; the .iss paths were fixed to be relative. Not signed and not test-installed.
