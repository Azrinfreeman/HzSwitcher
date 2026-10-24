# HzSwitcher

A small Windows tray app for switching a monitor's refresh rate and brightness, with an automatic power-saver that drops the rate when you are idle or watching video.

Version 1.5.0. Windows 10 or 11, x64. Built with C# on .NET 10 (Windows Forms). The release exe is self-contained, so no .NET install is needed.

## What it does

- **Refresh rate:** one-click switching from the tray menu or the main window. The tray icon shows the current rate. Rates below 60 Hz and near-duplicates such as 59 or 119 are hidden unless "Show all rates" is on.
- **Presets:** named one-click rates. Defaults are Gaming (highest), Balanced (120 Hz) and Battery saver (60 Hz). If the monitor doesn't support a rate, the nearest one is used.
- **Auto power-saver:** after the configured idle time, or after video has been playing with no input, it drops to the saver rate. Your previous rate returns after a stretch of continuous activity. It never throttles while a game from the game list is in front, and it pauses while the screen is locked.
- **App profiles:** pick a refresh rate for specific apps. It applies while the app is in front and goes back after the app has been out of focus for a few seconds. Changing the rate yourself overrides the profile.
- **Brightness:** a slider in the tray menu and on the Display page. Each monitor remembers its own level, and the power-saver can lower brightness too (see below).
- **Hotkeys:** global shortcuts. Defaults: Ctrl+Alt+1, 2 and 3 apply the presets, Ctrl+Alt+0 toggles the auto power-saver.
- **Multiple monitors:** choose which monitor is controlled.
- **Safety net:** rate changes are checked, and if the display ends up at an unexpected rate it is put back on the fallback rate (240 Hz by default, or the highest supported). If the app dies in the middle of a change, the fallback is applied at the next start.

## Brightness

HzSwitcher tries three methods, in this order:

1. **DDC/CI** (external monitors). Real backlight control. The monitor must have DDC/CI turned on in its own on-screen menu. Some monitors, docks and cables don't support it.
2. **WMI** (laptop built-in panel).
3. **Software dimming** (any monitor). Only darkens the picture; it does not change the backlight, cannot go brighter than the monitor's own setting, and is limited to 20% at the low end. It can conflict with Night light or HDR. The app labels this mode so you can tell.

Settings related to brightness:

- *Saver brightness* (Automation page): level used while the power-saver is active, or "Unchanged". Your own level comes back when the saver ends. If you change brightness during the saver, that new level is the one restored. If the app is closed or crashes mid-saver, the old level is restored at the next start.
- *Restore each monitor's brightness at startup* (General page): off by default. The app never changes brightness at startup unless you turn this on.

## Using it

- Left-click the tray icon to open the window, right-click for the menu. Closing the window keeps the app running in the tray; use **Exit app** (General page or tray menu) to quit.
- The window has four pages: **Display** (rate, presets, brightness), **Automation** (power-saver, app profiles), **Shortcuts** (hotkeys) and **General** (notifications, startup, advanced settings, log, exit).
- The window is dark, uses your Windows accent colour, and has a fixed size that scales with display DPI.

## Files and settings

Everything is stored per user in `%APPDATA%\HzSwitcher`:

| File | Purpose |
| --- | --- |
| `settings.json` | All settings. Edit it only while the app is closed. A file that can't be read is kept as `settings.json.bad` and defaults are used. |
| `log.txt` | Plain-text log of switches, monitor detection and errors. Truncated after about 512 KB. |

"Start with Windows" adds a per-user entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; turning it off removes it.

## Building

Requires the .NET 10 SDK.

```powershell
# debug build
dotnet build -c Debug -o $env:TEMP\hzbuild

# tests
dotnet test tests\HzSwitcher.Tests

# self-contained single-file release
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o artifacts\1.5.0
```

The installer is built with [Inno Setup 6](https://jrsoftware.org/isinfo.php) from the published exe:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=1.5.0 artifacts\installer\HzSwitcher.iss
```

It writes `artifacts\installer\HzSwitcher-Setup-<version>.exe`. The installer is unsigned, so Windows SmartScreen may warn about it.

## Project layout

| Folder | Contents |
| --- | --- |
| `Native/` | Windows API declarations (display modes, DDC/CI and gamma, input, shell) |
| `Services/` | Display, brightness, auto power-saver, profiles, safety net, settings, logging |
| `UI/` | Main window, dialogs, theme, tray menu renderer |
| `UI/Controls/` | Custom dark controls (cards, pill buttons, toggle, slider, number box, nav rail) |
| `tests/HzSwitcher.Tests/` | xUnit tests for the logic and the simple controls |

More background: `IMPLEMENTATION_PLAN.md` (earlier improvement plan), `FLICKER_REDUCTION.md`, `BRIGHTNESS_IMPLEMENTATION.md` and `UI_REVAMP_IMPLEMENTATION.md` (design notes for the 1.4.0 and 1.5.0 work).

## Known limits

- Brightness over DDC/CI can be slow on some monitors (tens to hundreds of milliseconds per change); the app applies only the newest requested value while you drag.
- The refresh-rate list comes from the monitor's current resolution, so it can differ after a resolution change.
- The installer and exe are not code-signed.
