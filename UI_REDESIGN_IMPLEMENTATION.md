# HzSwitcher: professional UI redesign

Status: approved and implemented in source. Automated verification is recorded below; final desktop and hardware QA remains pending.
Baseline: version 1.7.3, C# / .NET 10 / Windows Forms.
Prepared: 2026-10-29.

## 1. Recommendation

Make HzSwitcher feel like a carefully built Windows display utility: direct controls, stable alignment, compact settings, and precise feedback. Its identity should come from controlling a real monitor rather than from decorative dashboard components.

The main visual feature will be a discrete refresh-rate selector. Everything around it stays quiet. Brightness sits directly below it; presets remain convenient actions without becoming a second competing selection system.

Retain the existing four pages, dark theme, live Windows accent, native window chrome, tray workflow, and fixed default window. These are documented decisions in `UI_REVAMP_IMPLEMENTATION.md`. This proposal supersedes that document's visual treatment, not its historical decisions or progress notes.

No framework migration, new UI package, font download, backend rewrite, or settings-schema change is proposed.

## 2. Evidence and limits

Reviewed `MainForm`, `Theme`, the shared controls, `DialogShell`, the four editor dialogs, the tray construction, pin menu, relevant display/brightness services, and existing UI tests. This is a source-based review; the running application was not launched or visually inspected. Layout problems below are design risks inferred from code, not claims of reproduced clipping.

| Source observation | Why it weakens the interface | Proposed change |
| --- | --- | --- |
| `MainForm.Section()` wraps nearly every group in the same bordered `Card` | Primary controls and secondary settings get similar visual weight | Use open sections and aligned rows; reserve a filled surface for the display control area |
| Current rate appears in `_big`, in `CurrentModeText()`, and as selected preset/rate buttons | Repetition competes for attention; multiple presets can resolve to the same rate | Show one modest current-rate readout, resolution separately, and selected state only on the actual rate choice |
| Presets and rates both use `PillButton.Selected` | One-shot actions resemble persistent modes | Make presets neutral actions with names and resolved Hz values |
| `_monitor` disappears with one monitor | The controlled device loses visible identity | Always show the monitor name; show the dropdown affordance only when there is a choice |
| `_hotkeys` is a newline-separated label | Actions and key combinations have no stable columns or row-level navigation | Render a structured action/shortcut list |
| General includes behavior, detailed pinning preferences, tools, and a Quit card | A small utility becomes a long configuration page | Keep common rows; move detailed pinning preferences into an owned editor |
| Advanced settings repeats saver controls already on Automation | Two locations teach the same settings | Make Automation the home for ordinary saver controls; retain detection and specialist timing in Advanced |
| Fixed `CardWidth`, `BodyWidth`, and repeated min/max widths | Individual widths must be kept in sync manually | Derive the content width from the host; use measured table/flow layouts |
| `RefreshAll()` disposes and rebuilds rate/preset controls | Focus and selection can be disrupted during visible refreshes | Update existing controls; rebuild only when their underlying list changes |
| `NavRail` paints its items inside one accessible control | Labels exist, but per-item accessible navigation needs verification | Expose individual selectable accessible items or use child controls with matching keyboard behavior |

The previous redesign is already implemented. Recreating its cards, nav rail, sliders, or dialog shell would duplicate work. Reuse them selectively and change their roles and presentation.

## 3. Directions considered

### A. Display control panel — recommended

A narrow labeled rail, a persistent monitor header, one focused display surface, and flat settings rows. Left-aligned headings and labels; right-aligned values and secondary actions.

```text
+---------------------------------------------------------------+
| Native title bar: HzSwitcher                          _     X   |
|---------------+-----------------------------------------------|
| HzSwitcher    | Display                                       |
|               | [Monitor name                         v]      |
| Display       | 2560 x 1440                                   |
| Automation    |                                               |
| Shortcuts     | Refresh rate                         240 Hz   |
| General       | [60 Hz] [100 Hz] [120 Hz] [165 Hz] [240 Hz ✓]  |
|               |                              Show all rates   |
|               |                                               |
|               | Presets                                Edit…  |
|               | [Gaming 240 Hz] [Balanced 120 Hz] [Saver 60 Hz] |
|               |                                               |
|               | Brightness                              77%   |
|               | [−]  ----------------------o-------  [+]      |
|               |                                               |
| v1.7.3        | Saver active: 60 Hz; 240 Hz returns on activity |
+---------------+-----------------------------------------------+
```

Wireframe values are illustrative supported modes, not new defaults. The status line is conditional; no permanent “Everything is working” message fills the space. With software brightness, a short method warning appears below its slider.

### B. Compact tray companion — alternative

A narrow window with Display as the home view and configuration behind a Settings view.

```text
+------------------------------------+
| HzSwitcher                 Settings |
| [Monitor name                    v] |
| Refresh rate                 240 Hz |
| [60] [100] [120] [165] [240 ✓]      |
| Presets                            |
| [Gaming] [Balanced] [Saver]         |
| Brightness                     77% |
| [−]  ------------------o---  [+]    |
+------------------------------------+
```

This suits fast tray access but hides Automation and Shortcuts more deeply and replaces the previously chosen navigation model. Keep it as a future alternative rather than mixing both layouts.

## 4. Visual system

### Palette

Keep dark mode and Windows personalization. These six base values are proposed starting tokens; measure final rendered pairs before implementation is accepted.

| Token | Proposed value | Role |
| --- | --- | --- |
| `Bg` | `#202124` | Main canvas and navigation |
| `Surface` | `#292B2F` | Display control surface and popups |
| `SurfaceRaised` | `#35383D` | Inputs and interactive hover backgrounds |
| `Border` | `#50555D` | Quiet separators; not the sole control boundary |
| `Text` | `#F1F3F4` | Labels, values, headings |
| `TextMuted` | `#B5BAC1` | Secondary information that remains readable |

- Keep the existing Windows-derived `Accent` and readable `AccentText`; use accent for the selected rate, slider fill, rail indicator, and focus. No fixed brand-blue replacement.
- Retain existing semantic saver/warning/error roles, subject to contrast checks on the new surfaces. State also has text or a checkmark.
- Separate quiet dividers from meaningful input boundaries: a subtle separator need not define a control; an essential outline must reach 3:1 against adjacent colours.
- Target 4.5:1 for normal text, including secondary text and small button labels. Measure hover, selected, and pressed states too.
- High contrast uses system colours, not these hex values. Current `HighContrast` is sampled at startup; include refreshing system-colour tokens when Windows accessibility settings change.
- No gradients, glow, decorative textures, blurred panels, new Mica work, or shadows inside the main content.

### Typography and geometry

- Use one installed family: Segoe UI Variable Text when available, otherwise Segoe UI. This is a deliberate Windows utility choice: native legibility, reliable local availability, and consistent metrics without network fonts.
- Proposed sizes in points: body/control 10.5, secondary 9.5, section 12 semibold, page 18 semibold, current rate 22 semibold. Avoid a billboard-sized Hz number.
- Use normal sentence case. No tracked uppercase labels, decorative eyebrow text, mixed headline colours, or monospace metadata.
- Align labels to the left; values and secondary actions to the right. Keep shortcut combinations in a consistent right column.
- Logical spacing: 4, 8, 12, 16, 24. Content inset 24, section gap 24, row gap 8. Controls normally 36 logical pixels high; navigation rows 40. Provide 44-pixel hit areas for compact +/- controls when space permits.
- Corners: 4 logical pixels on inputs/buttons; 8 on the primary display surface. Switch thumbs remain round because of their function. Remove repeated pill silhouettes.
- Use one consistent Windows icon family via existing Fluent/MDL2 fallback logic. Labels remain sufficient if the font is missing. No emoji navigation.

### Window and scaling

- Keep a default 780 × 560 logical-pixel client area and native title bar. Reduce rail width from 180 to a proposed 152; confirm the longest navigation label fits with icons before finalizing.
- The nominal content width becomes 580 logical pixels: 780 − 152 − 48. Derive actual width from the content host, accounting for a visible scrollbar; do not copy that number into every child.
- Preserve the fixed, non-resizable user model. If the DPI-scaled window exceeds the active monitor's working area, constrain its bounds to that working area and allow vertical content scrolling. Keep the title bar, navigation, and dialog actions reachable.
- Use `TableLayoutPanel`, docking, automatic row heights, and measured text. Avoid fixed heights for explanatory text and hardcoded label coordinates.
- Apply scaling once. Preserve the current end-of-constructor `AutoScaleDimensions` / `AutoScaleMode.Dpi` ordering; use `Theme.Dp()` for runtime measurements without double-scaling existing controls.
- Test monitor moves as well as launch-time DPI. Restore position within the current working area when the saved display is gone or smaller.

## 5. Page and workflow specification

### Display

1. Page heading, then persistent monitor identity and resolution. Preserve actual monitor labels; truncate only with keyboard- and pointer-accessible full text. Multiple monitors use the existing device selector.
2. Refresh-rate heading and current Windows-reported Hz value on one line. Present supported rates as equal-sized discrete choices, ascending and wrapping cleanly. These are separate modes, not a continuous slider or an evenly spaced numeric scale.
3. Selected rate gets a checkmark plus accent treatment. Focus is visibly different from selection. Arrow keys move focus without changing the display; Enter/Space applies the focused mode.
4. Preserve Show all rates and all existing rate filtering. Place it as a compact secondary checkbox; the full list wraps without horizontal scrolling.
5. Presets sit below rates as neutral one-click buttons. Name and resolved rate are visible. If several presets resolve to the current Hz, none is presented as a uniquely active mode. Retain the six-preset capacity and 24-character names with wrapping or disclosure.
6. Brightness uses a stable value column, the existing slider, and +/- actions. Preserve five-point steps, asynchronous writes, newest-request behavior, per-monitor persistence, and the software floor of 20%.
7. Hardware brightness needs no routine explanation. Software mode retains “Software dimming: does not change the backlight.” Offer secondary details for DDC/CI and possible Night light/HDR conflicts. Unsupported control shows a specific explanation and disabled inputs.
8. One conditional status area communicates saver or profile activity using existing state. Do not invent GPU load, energy savings, frame rate, HDR capability, or a refresh history chart.

### Automation

Use two open groups: Auto power-saver and App profiles.

- Put each master switch at the right of its group heading, with a full accessible label.
- Saver fields: “Idle or video timeout” with minutes, “Restore after activity” with seconds, “Saver refresh rate” with Hz, and “Saver brightness” with percent or Unchanged. Unit labels sit beside the inputs.
- Preserve the current shared idle/video timeout semantics. Do not imply independent timers that the service does not support.
- Show the existing saver status as a single text row. Show `SaverBar` only when the existing progress has a useful meaning; never animate a fake countdown or estimate.
- Disabled automation preserves its configured values and makes its dependent fields unavailable. Help remains readable.
- Profiles show enabled count and a compact preview of up to three app/rate pairs, then “Manage profiles…”. Empty state: “No app profiles” with “Add app…” leading to the existing editor.
- “Detection settings…” opens the advanced editor. Preserve game exclusion, video detection, activity restoration, and lock handling unchanged.

### Shortcuts

Replace the multiline label with aligned rows: action on the left, key combination on the right. Use restrained keycap rendering, not colourful badges. Include the pin/unpin action alongside display actions.

Keep “Edit shortcuts…” as the editor entry point; do not build a second recorder into the overview. Preserve the underlying hotkey action identifiers and configuration format. Existing empty state leads to the editor.

### General

Use short setting rows for Notifications, Start with Windows, and Restore monitor brightness at startup. Keep essential startup-brightness behavior explicit: this is opt-in.

Replace the expanded pinning block with an Always on top switch, current availability/status, “Configure…”, and “Unpin all”. Detailed outline, sound, mouse gesture, and thickness preferences move to a new owned `AlwaysOnTopSettingsForm` built on `DialogShell`.

- Keep Configure available while the feature is off so users can prepare settings.
- Preserve visible recovery actions and shortcut-conflict feedback. Disabling the feature and Unpin all retain existing cleanup behavior.
- Keep concise gesture instructions in the editor; do not hide them exclusively in a hover tooltip.
- `ShowAlwaysOnTopSettings()` must navigate to General and open/focus that editor so the pin menu still reaches the correct destination.
- Place Open log and Open settings file in a small maintenance group. Keep detection settings discoverable from Automation; remove duplicate generic Advanced entry points from General.
- Move Hide to tray and Exit app to a quiet page footer. Exit is a secondary action with normal readable text; a large red Quit card is unnecessary. Preserve close-to-tray behavior and its first-use notification.

### Editing dialogs

Reuse `DialogShell` and preserve Save/Cancel semantics for existing editors. Maintain an uncommitted draft; Cancel or Escape discards edits. Main-page toggles remain immediate changes.

- Shared layout: concise heading/context, labeled body, fixed action footer. Save on the right, Cancel immediately before it, Add actions at the left.
- Presets: aligned name/rate columns, existing Highest/unsupported-rate behavior and nearest-rate resolution. Explain the resolution rule once where users edit rates.
- Profiles: aligned app/rate/enabled/remove rows. Retain process selection and restore-delay behavior. Long process names must not displace Remove or the selected rate.
- Shortcuts: consistent key/action/parameter/remove columns. Keep recording instructions, modifier requirement, and Backspace/Delete clearing behavior. Show validation errors near the relevant input and focus it.
- Advanced: remove duplicate auto-enabled, ordinary idle/restore, and saver-Hz controls; retain watching-quiet time, cooldown, title keywords, player processes, and game exclusions. Those settings continue mapping to the same stored fields.
- Always on top: provide all existing preferences, shortcut entry point, availability and pinned-window status. Use immediate Unpin all through the existing app callback; Save applies preference edits. Keep recovery usable regardless of unsaved changes.
- Clamp oversized dialogs to the available working area; scroll their bodies, never their Save/Cancel footer. Existing 600 × 620 Advanced sizing needs particular attention on scaled laptop displays.

### Tray menu and window pin menu

Keep these as native `ContextMenuStrip` surfaces with `ThemedMenuRenderer`. Apply the same typography, spacing, colours, and state discipline. Retain existing actions and submenu grouping; do not turn the tray into a custom dashboard.

Keep monitor identity, actual rate, preset actions, all rates, brightness, and checked automation state understandable. Preserve mouse/keyboard brightness commits. Keep startup, Advanced settings, Open, and Exit reachable even if secondary grouping changes.

The pin menu retains system-window commands, Original window menu, checked pin state, Unpin all, and settings access. Restyling must preserve clicked-window identity, fullscreen/game suppression, and all existing ownership/security guards.

## 6. Interaction feel

Apply game-designer principles through staging, timing, and truthful feedback. A utility should feel immediate at the thousandth use.

| Interaction | Feedback | Timing / correctness rule |
| --- | --- | --- |
| Press a rate or preset | Immediate pressed treatment; “Applying 120 Hz…” only if the operation remains pending | Press feedback within 100 ms; never delay the display API to finish animation |
| Rate succeeds | Actual Hz readout and selected check update together | Confirm from the existing display path; no celebratory overlay or number tween |
| Rate fails / fallback occurs | Retain or restore actual selection; inline failure message with actual result | Preserve safety-net logic and existing failure notification; no false success |
| Drag brightness | Thumb and requested percent track input immediately | Keep latest-request coalescing; do not label the optimistic value as hardware-confirmed |
| Saver/profile changes | Update a stable conditional status row | No shifting rate controls, pulsing badge, or repeated toast |
| Toggle a preference | Direct checked-state response | Optional 100–120 ms thumb movement; no overshoot |
| Hover / press | Small fill change without moving bounds | Optional 80–120 ms hover; pressed state immediate |
| Change page | Show the destination immediately | No slide, staggered entrance, or loading skeleton |

Display changes are currently synchronous. Start with truthful immediate press feedback; do not promise a visible progress animation while the UI thread is blocked. If measurements show a slow call, scope any asynchronous coordination separately and preserve the existing serialization/safety path.

Use a single short-lived UI timer only if interpolation is justified. Stop it when idle or hidden. Respect Windows client-area animation settings and high contrast; immediate state changes remain a supported fallback. No idle animation, screen shake, bounce, new sounds, or artificial waits. Existing optional pin/unpin sounds remain unchanged.

## 7. Implementation map

The implementation follows this file map. See section 11 for completed verification and remaining checks.

| File / area | Intended work |
| --- | --- |
| `UI/Theme.cs` | Palette, font/spacing/radius roles; correct contrast across states; refresh high-contrast/system-colour behavior |
| `UI/MainForm.cs` | Content-driven widths, page hierarchy, persistent monitor identity, structured shortcuts, compact General, stable control updates and status |
| `UI/Controls/NavRail.cs` | Proposed 152-pixel rail, quieter identity, consistent selection/focus and per-item accessibility |
| `UI/Controls/PillButton.cs` | Retain existing button API/name for compatibility; reduce corner radius and add quiet secondary presentation only where needed |
| `UI/Controls/ChipGroup.cs` | Discrete rate-choice semantics, focus behavior, selected-state accessibility |
| `UI/Controls/Card.cs` | Retain for meaningful grouped surfaces; stop using it as the mandatory section wrapper |
| `UI/Controls/Toggle.cs`, `ThemedSlider.cs`, `NumberBox.cs` | Consistent metrics and focus; preserve existing value/event APIs; verify assistive-technology changes reach the application callbacks |
| `UI/Controls/StatusChip.cs`, `UI/SaverBar.cs` | Reuse only where needed; prefer a plain status row over chip proliferation |
| `UI/DialogShell.cs` | Consistent footer, optional intro, working-area constraints and focus return |
| `UI/PresetsForm.cs`, `ProfilesForm.cs`, `HotkeysForm.cs`, `SettingsForm.cs` | Aligned editor layouts, concise copy and validation; remove duplicated saver controls from Advanced |
| `UI/AlwaysOnTopSettingsForm.cs` (new) | Owned pinning-preferences editor using existing settings and callbacks |
| `TrayApp.cs` | Minimal editor opening/wiring and presentation changes; preserve operational behavior |
| `UI/ThemedMenuRenderer.cs`, `UI/WindowPinMenu.cs` | Consistent menu styling; retain commands and target guards |
| `tests/HzSwitcher.Tests/UiTests.cs` | Focused assertions for changed contrast and interaction contracts |
| `README.md` | Update navigation/control descriptions after the implemented UI is verified |

Prefer ordinary panels, tables, labels, and existing controls. Introduce a reusable setting-row helper only if several pages need the same behavior. Do not create a broad new component framework or rename the control library just for aesthetic reasons.

## 8. Delivery sequence after approval

1. **Baseline:** capture every page/dialog, tray menu, and pin menu in the existing app; record normal, saver/profile, software brightness, and unsupported states where available. Confirm the source-based diagnosis visually before removing anything.
2. **Foundation and Display:** implement semantic metrics and Display first. Review its screenshot at default size and 150% DPI before applying the treatment throughout the app.
3. **Configuration pages:** rebuild Automation, Shortcuts, and General with open groups; add the owned pinning editor and preserve direct settings navigation.
4. **Dialogs and menus:** align editors, simplify Advanced, and bring tray/pin-menu presentation into the same system.
5. **Interaction and accessibility:** preserve focus during updates, verify reported versus requested state, complete accessible roles/states and working-area handling. Add optional motion only after static behavior works.
6. **Validation and documentation:** execute the checks below, capture final screenshots, update README, and summarize the actual changes. Versioning/installer work is outside this visual proposal.

Each phase should leave a usable app. Avoid changing display/saver/profile algorithms while restyling them.

## 9. Acceptance criteria and verification plan

### Design acceptance

- Display communicates the target monitor, actual refresh rate, supported choices, and brightness in one glance.
- Default Display content fits without scrolling at 780 × 560 logical pixels for ordinary rates/presets. Overflow cases scroll vertically without hiding essential actions.
- The main content is not a stack of equally weighted bordered cards. No decorative metrics, generic onboarding copy, gradients, or repeated status badges are introduced.
- Only actual rate choices carry persistent rate selection. Presets remain commands even when several resolve to the same Hz.
- General exposes common preferences and recovery actions without showing every pinning detail at once.
- Labels stay readable with long monitor names, maximum preset names/count, many rates, empty lists, and several profiles/hotkeys.

### Behavior and accessibility

- Keyboard-only pass: navigation, Tab/Shift+Tab, rate arrows without applying, Enter/Space application, slider and number-box keys, dialog Save/Cancel, focus return, and menu operation.
- Narrator pass: individual navigation items, current/selected rate, switch state, slider name/value, visible validation, and disabled reasons. Read-only summaries must not replace accessible control state.
- Focus remains visible and stable when status, monitor topology, supported rates, or presets update. Reveal focused elements inside scroll regions.
- Verify 100%, 125%, 150%, and 200% DPI, mixed-DPI monitor moves, enlarged Windows text, a 1366 × 768 working area, and 1920 × 1080 at 200%. Check both main form and every dialog.
- Verify Windows 10 and 11 fallback rendering, live dark/light Windows accent extremes, and high contrast at launch and while open. The app remains dark outside high contrast.
- Measure normal/secondary/button text at 4.5:1 and essential non-text boundaries/focus at 3:1. Include all interaction states.
- Exercise real monitor rate success/failure/fallback, preset resolution, monitor changes, hardware/software/unsupported brightness, saver restore, app profiles, hotkeys, startup preferences, close-to-tray, and exit cleanup.
- Verify pin/unpin, Unpin all, outline/sound options, mouse menu targeting, original window commands, and fullscreen/game suppression after relocating settings.

### Automated scope

After implementation, run `dotnet build -c Debug` and `dotnet test tests/HzSwitcher.Tests`. Use focused tests first while editing. Keep existing service/pin tests as regression coverage; add tests only for changed interaction, state, or contrast behavior. Tighten the small accent-label contrast assertion from its current 3:1 threshold to 4.5:1 if those labels remain normal-sized text.

Do not use unit tests as proof of DPI, layout quality, Narrator support, real display switching, or physical monitor brightness. Those require the manual/visual checks above. No build, test, or hardware operation was run to prepare this proposal.

## 10. Design critique and skill application

- **Frontend design:** the first pass could have become another dark dashboard with a large Hz hero and rounded status cards. Revised it to a native control-panel layout whose distinctive element is a real set of discrete monitor modes. Typography and spacing carry hierarchy; decoration does not.
- **UI/UX Pro Max:** ran the design-system search for “display control desktop utility” and retried with “desktop utility settings”. Both returned a marketing-oriented Hero + Features + CTA pattern. That is not a verified match for this WinForms utility, so its page pattern, palette, web fonts, and animation stack were rejected and not persisted. The desktop rules above use the skill's general accessibility/interaction guidance plus the existing application's constraints. There is no WinForms-specific stack entry; no WPF/WinUI migration is implied.
- **Game designer:** use staging to make the active display controls clear, timing for immediate response, and visual truth for success/failure. Squash/stretch, exaggerated impacts, particles, and continuous animation do not serve this utility.

The plan deliberately preserves useful instructions: software-dimming limits, shortcut recording, pin gestures, unsupported states, and close-to-tray behavior. Concision must not remove information required to operate the app.

## 11. Implementation record

The user approved implementation after reviewing this proposal. Implemented the Display control surface, narrower navigation, open configuration sections, structured shortcut overview, dedicated Always on top editor, aligned dialogs, detection-only Advanced editor, and matching menu styling. Reused existing controls and service callbacks; added `LayoutStack` for width-driven vertical layouts. No dependencies, settings schema, or display/saver/profile algorithms were changed.

Also implemented live high-contrast restyling, accessible navigation children and rate selection, truthful rate-error feedback, stable controls on routine refresh, working-area constraints, and a unified brightness completion event. The shortcut recorder now permits Tab, Shift+Tab, Escape, and Enter to retain ordinary dialog navigation. Preset and shortcut validation shows an inline error and identifies the relevant field.

The baseline source build succeeded. The installed app (1.7.2) was inspected on Display, Automation, and General; the repository baseline is 1.7.3. Desktop automation was subsequently stopped with the physical Escape key, and no further desktop actions were performed. Full before/after captures, the remaining dialogs/menus, Narrator, mixed DPI, live high contrast, and real monitor/pinning checks have not been completed. The original verification matrix in section 9 remains the manual acceptance checklist.

Automated verification covers palette contrast, selection/navigation accessibility, width-constrained stacks, persistent editor actions, shortcut capture navigation, brightness accessibility commits, and the existing service/pinning regressions. Final Debug build passed with zero warnings and zero errors; the full test suite passed all 99 tests. `git diff --check` reported no whitespace errors. README and the Unreleased changelog describe the new layout. The installed application and release artifacts were not replaced. A review build is available at `%TEMP%\HzSwitcher-redesign\HzSwitcher.exe`.
