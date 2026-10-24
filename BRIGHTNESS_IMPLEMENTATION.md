# Monitor brightness control: implementation plan

Baseline: HzSwitcher 1.3.1, C# / .NET 10 Windows Forms.
Status: proposal only. No code, settings, or packaging has been changed.

## Goal

Let the user change the brightness of the controlled monitor quickly from the tray menu and from the main window. It should follow the app's existing monitor selection, theme, and settings conventions.

## Decisions (from your answers)

| Question | Decision | Effect on the plan |
| --- | --- | --- |
| Tray control | Draggable slider only | The 25/50/75/100% submenu is dropped (2.4). |
| Per-monitor memory | Yes | Brightness is stored per monitor (2.3). |
| Hotkeys | None | Section 2.6 is removed. |
| Power-saver lowers brightness | Yes | New saver integration (2.8). |
| Software-dimming fallback | Yes | Added as a fallback path (1.1, 2.9). |
| Test hardware | Your current setup | Hardware checks are run on it only (4). |

## 1. How Windows exposes brightness

There is no single API. Two paths cover nearly all hardware:

| Monitor type | API | Notes |
| --- | --- | --- |
| External monitor (HDMI / DisplayPort / DVI) | DDC/CI through `dxva2.dll`: `GetPhysicalMonitorsFromHMONITOR`, `GetMonitorBrightness`, `SetMonitorBrightness`, `DestroyPhysicalMonitors` | The monitor must have DDC/CI enabled in its on-screen menu. Calls are slow (about 50-300 ms) and some monitors reject them. |
| Laptop internal panel | WMI, namespace `root\WMI`: `WmiMonitorBrightness` (read) and `WmiMonitorBrightnessMethods.WmiSetBrightness(timeout, level)` (write) | Level is 0-100. Only works for the built-in panel. |

The app currently uses `System.Windows.Forms.Screen` device names (`\\.\DISPLAY1`) to pick a monitor. Both brightness paths need a `HMONITOR`. We can get it from the device name with `EnumDisplayMonitors` and `GetMonitorInfo`, matching on `szDevice`. WMI instances are matched to a monitor through `InstanceName` and the PnP ID from `EnumDisplayDevices(..., EDD_GET_DEVICE_INTERFACE_NAME)`. For a first version, the WMI path applies only when the selected monitor is the internal panel. Detect that with `GetPhysicalMonitorsFromHMONITOR` failing or reporting no capabilities, plus a WMI instance existing.

### 1.1 Software-dimming fallback

When a monitor has neither DDC/CI nor WMI, the app falls back to a gamma-ramp dim (`SetDeviceGammaRamp` on a DC created with `CreateDC` for the monitor's device name). This is not a real backlight change: it only darkens the picture, so it can reduce black level and contrast, and it cannot go brighter than the monitor's own setting. Known conflicts: Windows Night light, HDR, and some games or drivers reset the ramp. Mitigations:

- The UI labels the mode ("Software dimming") so the user knows which kind of control they have.
- Clamp the lowest level to about 20% so the screen cannot be made unreadable (Windows rejects very dark ramps anyway).
- Reset the ramp on exit, on monitor change, and when the display topology changes.

The order of preference is: DDC/CI, then WMI (internal panel), then software dimming.

## 2. Design

### 2.1 New files

| File | Purpose |
| --- | --- |
| `Native/BrightnessApi.cs` | P/Invoke declarations for `dxva2.dll` (`PHYSICAL_MONITOR` struct, the four calls above) and `user32` `EnumDisplayMonitors` / `GetMonitorInfo`. Matches the style of `Native/DisplayApi.cs`. |
| `Services/BrightnessService.cs` | The `IBrightness` implementation. Public surface below. |
| `Services/Abstractions.cs` (edit) | Add `IBrightness` so the service can be faked in tests, like `IDisplay`. |

```csharp
public interface IBrightness
{
    /// <summary>True when the selected monitor can be controlled by any method.</summary>
    bool IsSupported { get; }
    /// <summary>Hardware (DDC/CI or WMI) or Software (gamma-ramp dimming).</summary>
    BrightnessMode Mode { get; }
    /// <summary>Current brightness as 0-100, or -1 if unreadable.</summary>
    int Get();
    /// <summary>Sets brightness 0-100 (clamped). Returns false if the monitor rejected it.</summary>
    bool Set(int percent);
}
```

`BrightnessService` takes the same `Func<string>` that `DisplayService` takes, so it always tracks the currently selected monitor. Internally it:

1. Resolves the device name to an `HMONITOR` and opens the physical monitor handle(s).
2. Reads the min / current / max once, caches the range, and maps the monitor's raw range to a 0-100 percentage. Many monitors use a raw range of 0-100, but some use others.
3. Falls back to WMI when the monitor is the internal panel.
4. Releases the handles (`DestroyPhysicalMonitors`) after each operation, or caches them and releases when the monitor changes and on dispose. Prefer cache-and-release, to avoid re-enumerating on every slider tick.
5. Catches all failures, logs through `Log.Write`, and returns "unsupported" rather than throwing.

### 2.2 Threading and throttling

DDC/CI calls block for tens to hundreds of milliseconds, so they must not run on the UI thread while the slider is dragged.

- Add a small coalescing writer inside `BrightnessService`: the UI calls `RequestSet(percent)`, which stores the latest value and wakes one background worker. The worker applies only the newest value, so a fast drag collapses into a few writes.
- Raise a `Changed` event on the UI synchronization context after a write completes, so labels reflect the confirmed value.
- Read the current brightness lazily when the window opens or the tray menu opens, not on a timer. This avoids constant DDC traffic, which can cause flicker or lag on some monitors.

### 2.3 Settings

Add to `Settings` in `Services/SettingsStore.cs`. All have safe defaults, so no `SettingsVersion` migration is needed: missing properties deserialize to the defaults.

```csharp
public bool ShowBrightness { get; set; } = true;
public int MinBrightness { get; set; } = 0;                       // floor for hardware control; software mode uses at least 20
/// <summary>Last level per monitor, keyed by a stable monitor ID (see below), 0-100.</summary>
public Dictionary<string, int> MonitorBrightness { get; set; } = new();
public bool RestoreBrightnessOnStart { get; set; }                // off by default
/// <summary>Level used while the power-saver is active; -1 = do not change brightness.</summary>
public int SaverBrightness { get; set; } = -1;
```

`BrightnessStep` is no longer needed because there are no hotkeys.

Per-monitor memory: `\\.\DISPLAYn` names can change after a reboot or replug, so the key is the monitor's PnP device ID (`DeviceID` from `EnumDisplayDevices` on the monitor child device), falling back to the device name. Levels are saved when the user stops dragging (not on every tick). A saved level is applied only when `RestoreBrightnessOnStart` is on, or when the user switches monitors in the app (so each monitor shows its own last level).

### 2.4 Tray menu

`TrayApp.BuildMenu` rebuilds the menu on every `Opening`, which suits this feature, since brightness can be read fresh each time.

- Add a **Brightness** entry after the refresh-rate items and before the "Auto power-saver" separator.
- The entry is a `ToolStripControlHost` hosting a themed `TrackBar` (0-100) inside a panel with a small "Brightness 70%" label. This lets the user drag directly inside the right-click menu.
- Keep the menu open while dragging. `ContextMenuStrip` stays open for hosted controls, but set `AutoClose` handling carefully: clicking the trackbar must not dismiss the menu.
- Slider only, as requested. Because the control is a hosted `TrackBar`, arrow keys and Home/End work while it has focus. If the dark theme makes it unusable, switch to a custom-drawn slider (reusing the `SaverBar` drawing style) rather than adding menu items.
- If `IsSupported` is false, show a disabled "Brightness (not supported on this monitor)" item rather than hiding it, so the user knows why.
- Mouse wheel over the tray icon: `NotifyIcon` has no wheel event, so this is not planned. Hotkeys cover the quick-adjust need.

### 2.5 Main window

In `UI/MainForm.cs`, add a **Brightness** card using the existing `Section(...)` helper, placed in the left column below "Refresh rate" so it sits near the current monitor selector.

Controls inside the card:

- A `TrackBar` (0-100, `TickFrequency` 10, width `BodyWidth`), styled through `Theme`.
- A large percentage label ("70%") matching the `_mode` / `_big` fonts.
- Small step buttons `−` and `+` using `FlatButton`, stepping by `BrightnessStep`.
- A hint line naming the control method in use ("Monitor backlight" or "Software dimming"). In software mode it adds: "Enable DDC/CI in the monitor's menu for real brightness control."
- When unsupported, disable the controls and show that hint in place of the slider.

Wiring follows the existing `_loading` guard pattern: `RefreshAll()` sets the slider value inside `_loading = true`; the `Scroll` / `ValueChanged` handler returns early when `_loading` is set, otherwise calls `RequestSet`. `Theme.Apply` already walks child controls, so `TrackBar` colors come from `Theme.Style`; add a `TrackBar` case there (back color `Theme.Card`).

### 2.6 Hotkeys

Removed: no brightness hotkeys, so `HotkeyBinding`, `HotkeysForm` and `OnHotkey` are untouched.

### 2.7 TrayApp integration

```csharp
private readonly BrightnessService _brightness;       // created next to _display
internal BrightnessService Brightness => _brightness;
```

- Construct it right after `_display`, passing `() => _settings.MonitorDevice`.
- Call `_brightness.Invalidate()` in `SelectMonitor(...)` and on `SystemEvents.DisplaySettingsChanged` so cached handles are dropped.
- Dispose it in `ExitApp()`.
- Optionally restore `LastBrightness` at startup when `RestoreBrightnessOnStart` is on.

### 2.8 Power-saver integration

`AutoSaverService` currently takes `apply: _safety.Apply` for refresh rate only. Add optional callbacks (`enterBrightness`, `exitBrightness`) passed from `TrayApp`:

- On entering the saver: if `SaverBrightness >= 0`, remember the current level, then set the saver level.
- On leaving the saver (activity, manual change, auto disabled, exit): restore the remembered level.
- If the user changes brightness manually while the saver is active, treat that as the new level to restore, so their adjustment is never overwritten.
- Add a "Saver brightness" field (with an "unchanged" option) to the Auto power-saver card, next to "Saver refresh rate".
- Crash safety: persist the pre-saver level in settings while the saver is active, so a crash cannot leave the screen dim on next start (the same idea as `PendingChange` for refresh rate).

### 2.9 Software-dimming implementation

- `Native/BrightnessApi.cs` adds `CreateDC`, `DeleteDC`, `SetDeviceGammaRamp`, `GetDeviceGammaRamp` (GDI32).
- Save the original ramp when first dimming a monitor and restore it exactly, instead of assuming a linear ramp.
- Build the dimmed ramp by scaling the original values by `level / 100`.

## 3. Interaction with existing features

- **Auto power-saver / profiles:** unchanged. Brightness is independent of refresh rate. A later enhancement could dim to a level while the saver is active, but that is not in this plan.
- **Safety net:** the refresh-rate safety net should not be touched. Brightness writes are non-destructive and reversible; the only protection is the optional `MinBrightness` floor.
- **Multiple monitors:** controls follow the selected monitor. Changing the monitor refreshes the slider.
- **Flicker:** `FLICKER_REDUCTION.md` shows the project is sensitive to display resyncs. Brightness over DDC/CI does not trigger a mode change, but throttling in 2.2 still matters.

## 4. Testing

The repo has an xUnit test project (`HzSwitcher.Tests`, internals visible). It is not present in the current folder, so its location needs confirming before adding tests.

Unit tests (with a fake `IBrightness` and fake clock):

- Percentage mapping from a raw min/max range, including a non-0-100 range and `min == max`.
- Clamping to 0-100 and to `MinBrightness`.
- Coalescing: a burst of 20 `RequestSet` calls results in at most a few writes, and the last write is the final requested value.
- Per-monitor level storage and lookup by monitor ID.
- Saver enter/exit restores the original level, and a manual change during the saver becomes the new restore level.
- Settings: a JSON file without the new properties loads with defaults.

Manual hardware checks (cannot be automated):

1. Your current monitor setup: DDC/CI path first, then the software fallback by forcing it on.
2. Switching monitors if you have more than one, to check per-monitor levels.
3. Power-saver entering and leaving, including a manual brightness change while the saver is active.
4. Dragging the tray slider with the dark and light Windows themes.

## 5. Delivery steps

1. `Native/BrightnessApi.cs` and `BrightnessService` (DDC/CI only), with the `IBrightness` interface and tests for the pure logic.
2. Main window card.
3. Tray slider.
4. WMI path for laptop panels and the software-dimming fallback.
5. Per-monitor memory, settings, and power-saver brightness integration.
6. Update `README` / changelog, bump `Version` in `HzSwitcher.csproj` (suggest 1.4.0), rebuild the installer.

Steps 1-3 give a working feature on external monitors; step 4 can ship separately if laptop testing is not available.

## 6. Risks

| Risk | Mitigation |
| --- | --- |
| Monitor has DDC/CI disabled or unsupported (common on cheap panels and some docks) | Detect, show a clear hint, never throw. |
| Slow DDC/CI calls freeze the UI | Background coalescing worker (2.2). |
| Hosted `TrackBar` in a `ContextMenuStrip` is awkward, especially with dark theming | Quick-step submenu as a fallback; test early in step 3. |
| Some monitors misreport range, so 100% looks wrong | Cache and normalize the range; floor setting; log raw values. |
| Brightness persisted by the monitor itself, so a bad level survives restart | Never write on startup unless the user opted into restore. |
| Laptop and external both present: WMI controls only the internal panel | Match by device and only use WMI for the internal one. |

## 7. Implementation notes (as built)

Differences from the plan above:

- `ShowBrightness` and `MinBrightness` were not added: nothing in the UI would have exposed them. The floor is fixed (0 for hardware, 20 for software dimming).
- Switching monitors shows that monitor's real level. The saved per-monitor level is used for "restore at startup" and, in software mode (which cannot be read back), as the current level.
- `IBrightness.RequestSet` replaces `Set`: it never blocks, and `BrightnessService.Changed` fires on the UI thread when a read or write finishes.
- Exit calls `Flush()` so a level restored by the saver is written before the process ends. In software mode, exit also resets the gamma ramp.
- Version bumped to 1.4.0, and `System.Management` 10.0.0 was added for the WMI path.
- Not done: unit tests (the test project is not in this folder), README/changelog, and a rebuilt installer.
