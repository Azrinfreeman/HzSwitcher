# HzSwitcher

A small Windows tray app for switching a monitor's refresh rate and brightness, with an automatic power-saver that drops the rate when you are idle or watching video.

Version **1.8.0-beta.1** (local development beta). Windows 10 or 11, x64. Built with C# on .NET 10 (Windows Forms). A self-contained publish requires no .NET installation.

This implements the first milestone of the [differentiation proposal](docs/DIFFERENTIATION_PROPOSAL.md). See [delivery status and validation limits](docs/AUTOMATION_1_8_DELIVERY.md). Independent simultaneous monitor policies remain future work. A local beta installer is in `artifacts/installer/HzSwitcher-Setup-1.8.0-beta.1.exe`; no GitHub release is implied.

## What it does

- **Refresh rate:** one-click switching from the tray menu or the main window. The tray icon shows the current rate. Rates below 60 Hz and near-duplicates such as 59 or 119 are hidden unless "Show all rates" is on.
- **Presets:** named one-click rates. Defaults are Gaming (highest), Balanced (120 Hz) and Battery saver (60 Hz). If the monitor doesn't support a rate, the nearest one is used.
- **Auto power-saver:** after the configured idle time, or after video has been playing with no input, it drops to the saver rate. Your previous rate returns after a stretch of continuous activity. It never throttles while a game from the game list is in front, and it pauses while the screen is locked.
- **App profiles:** pick a refresh rate for specific apps. New profiles take precedence over the saver unless **Allow saver** is checked; migrated profiles retain their previous saver priority. Game protection blocks profile/saver lowering.
- **Why this rate?:** view the winning rule, suggested rate, waiting state and local transition history from Display or the tray.
- **Preview:** new installations begin by suggesting rates without automatic rate or brightness changes. Existing installations keep their previous automation mode. Explicit manual controls remain available.
- **Temporary control:** manual rate choices hold for 15 minutes by default (editable). Pause automation for 15 minutes, an hour or until resumed; recording/presentation sessions pause changes until ended or the app restarts. Resume releases holds and pauses; it does not turn Preview into Active mode.
- **Compatibility feedback:** report flicker/blackouts and explicitly block that directed automatic transition in the recorded monitor context. Manual retry is still available. History is local, bounded to 500 events / 30 days, and can be deleted or exported with monitor identities and reasons omitted.
- **Brightness:** a slider in the tray menu and on the Display page. Each monitor remembers its own level, and the power-saver can lower brightness too (see below).
- **Hotkeys:** global shortcuts. Defaults: Ctrl+Alt+1, 2 and 3 apply the presets, Ctrl+Alt+0 toggles the auto power-saver.
- **Multiple monitors:** choose which monitor is controlled. A missing target no longer falls back to the primary display. Unconfirmed or mirrored display paths are blocked in the new rate-control path.
- **Verification and recovery:** changes have a durable monitor-bound journal and a three-second readback check. An interrupted or uncertain change requires explicit review in **Why this rate?**; the app does not blindly switch to 240 Hz at startup. Restore the recorded previous rate, keep the current rate, or explicitly archive a record whose monitor cannot be matched.
- **External changes:** if the rate changes outside HzSwitcher, automation yields until Resume instead of repeatedly fighting the other setting.

## Brightness

HzSwitcher tries three methods, in this order:

1. **DDC/CI** (external monitors). Real backlight control. The monitor must have DDC/CI turned on in its own on-screen menu. Some monitors, docks and cables don't support it.
2. **WMI** (laptop built-in panel).
3. **Software dimming** (any monitor). Only darkens the picture; it does not change the backlight, cannot go brighter than the monitor's own setting, and is limited to 20% at the low end. It can conflict with Night light or HDR. The app labels this mode so you can tell.

Settings related to brightness:

- *Saver brightness* (Automation page): level used while the power-saver is active, or "Unchanged". Restoration checks the target and observed level and yields to an external change. If an unfinished brightness record remains after restart, review it explicitly; an old record without monitor identity is never restored onto an assumed target.
- *Restore each monitor's brightness at startup* (General page): off by default. The app never changes brightness at startup unless you turn this on.

## Using it

- Left-click the tray icon to open the window, right-click for the menu. Closing the window keeps the app running in the tray; use **Exit app** (General page or tray menu) to quit.
- The window has four pages: **Display** (rate, presets, brightness), **Automation** (power-saver, app profiles), **Shortcuts** (hotkeys) and **General** (notifications, startup, advanced settings, log, exit).
- The window is dark, uses your Windows accent colour, and has a fixed size that scales with display DPI.

## Always on top

On **General**, turn on **Enable always on top**, then focus another app and press **Ctrl+Alt+T** to pin or unpin its window above ordinary windows. Edit the shortcut through **Edit hotkeys?**. The feature is off by default and needs HzSwitcher running; hiding to the tray keeps it active. The status shows whether the shortcut is available.

Pinned windows show an outline in the Windows accent color and play distinct short sounds when pinned or unpinned. General settings let you switch the outline, sounds and mouse gesture on/off, and choose outline thickness (1-8 logical pixels, scaled for the monitor).

**Right-click a title bar** (or press **Alt+Space**) and choose **Always on top (HzSwitcher)**, which HzSwitcher adds to each window's own Windows system menu, just above Close. It is checked while the window is pinned; choose it again to unpin. This also works for apps with custom title bars when they open the standard Windows menu. The entry is greyed out for windows that another tool already made topmost. HzSwitcher removes its entries when the feature is turned off or the app exits; after a forced termination an inert entry can remain until the target app restarts. Apps running as administrator may not accept the entry or the pin while HzSwitcher is not elevated.

**Shift+right-click** anywhere on a window opens the compact HzSwitcher menu, including for custom title bars that are drawn inside the app's content. A normal **right-click on the pinned outline** opens the same compact menu. Use it for apps whose title bar shows their own non-Windows menu. General has separate switches for the title-bar menu entry and for Shift+right-click. Actions use the clicked window even if another window has focus. Ordinary right-click inside app content continues to use that app's context menu. With the title-bar entry switched off, right-clicking a standard frame opens the older HzSwitcher replacement popup instead.

You can pin several windows. **Unpin all**, turning the feature off, or **Exit app** restores windows pinned by HzSwitcher. Windows already on top are left unchanged. Some protected or elevated windows cannot be controlled, and other topmost windows or exclusive fullscreen apps can cover a pinned window.

Cleanup is best effort. If HzSwitcher crashes or is force-closed, a pinned app may stay on top; close and reopen that app to recover. Pinned windows are not restored across restarts. If the shortcut conflicts with PowerToys or another app, choose another combination. Successful pin/unpin actions use the outline and optional sound without a notification popup. Errors and explanations (such as a window already pinned outside HzSwitcher) still display feedback.

## Files and settings

Everything is stored per user in `%APPDATA%\HzSwitcher`:

| File | Purpose |
| --- | --- |
| `settings.json` | All settings. Edit it only while the app is closed. A file that can't be read is kept as `settings.json.bad` and defaults are used. |
| `log.txt` | Plain-text log of switches, monitor detection and errors. Truncated after about 512 KB. |
| `transitions.json` | Recent rate transitions and explicit compatibility feedback. No window titles, URLs or screenshots. Deleting history also removes its transition blocks. |
| `pending-transition.json` | Durable record of an in-flight or interrupted rate change. Reviewed explicitly before another change. |
| `settings.json.v2-backup`, `settings.json.bak` | Migration backup and previous atomic settings version. A newer unknown settings schema is preserved rather than overwritten. |

Archived unreadable/unavailable recovery journals remain local for inspection. They are not included in redacted history export. History deletion does not delete settings backups or recovery archives.

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
  -p:EnableCompressionInSingleFile=true -o artifacts\1.8.0-beta.1
```

Then build the installer with Inno Setup: `ISCC.exe /DAppVersion=1.8.0-beta.1 artifacts\installer\HzSwitcher.iss`. Executables are unsigned.

A separate synthetic UI harness exercises the details window without accessing display settings or brightness:

```powershell
dotnet run --project tools/HzSwitcher.Preview/HzSwitcher.Preview.csproj -c Release -- artifacts/qa
```

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
- This beta has no new physical-monitor acceptance result, full DPI matrix or measured power/performance result. Readback is not proof of flicker-free operation. A native call already in flight cannot necessarily be cancelled.
- Simultaneous per-monitor automation, automatic compatibility recommendations, exact fractional-Hz cinema matching and activity summaries are not included in this first milestone.
