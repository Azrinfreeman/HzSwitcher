# HzSwitcher 1.8.0-beta.1: first automation milestone

Date: 28 October 2026. Local branch: `feat/explainable-automation`.

## Scope

The user authorized implementation of the differentiation proposal. This beta delivers its recommended first milestone, plus monitor identity and compatibility-feedback foundations. The original baseline is commit `8d202bef9d0b6b0734951ac1227db0bd0dabf413` (1.7.1).

Implemented:

- One live coordinator for the selected monitor; legacy saver/profile classes remain for regression reference but are no longer instantiated by `TrayApp`.
- Pure rule selection with game protection, explicit app-profile precedence, migrated saver behavior, debounce, activity restoration and monotonic cooldown.
- Explanations, preview mode, manual-choice holds, timed/indefinite pauses, recording-session pause and external-drift yielding.
- Bounded local rate-transition history, directed-transition feedback blocks, deletion and an explicit redacted export preview.
- Read-only CCD source/target discovery, detection of mirrored/ambiguous paths, explicit target checking and no disconnected-target fallback.
- A durable rate-change journal, delayed readback, explicit recovery and archival of unreadable/unavailable records. No automatic rollback to a guessed primary monitor or arbitrary 240 Hz target.
- Atomic settings migration and backups; newer schemas remain preserved and cannot be overwritten through the normal save path.
- Brightness request/readback separation, target/generation checks, cancellation of pending automatic work, target-bound restoration and explicit review of interrupted brightness records.

The main UI retains Display, Automation, Shortcuts and General. **Why this rate?** opens the controls/history window. New installations start in Preview; upgrades retain active automation. Manual controls are explicit hardware actions even in Preview. A temporary rate is retained when Preview is entered after the user confirms keeping it.

## Important behavior changes

- Manual rate selection creates a temporary hold (15 minutes by default) even if that rate is already active.
- New profiles default to blocking the saver. Migrated profiles have **Allow saver** enabled to retain previous behavior.
- A detected external rate change pauses automation until Resume. The source of the change is not inferred.
- Restart does not automatically apply a global fallback rate. Matching recovery is explicit; an unmatchable journal can be archived without claiming that its original monitor was restored.
- Exit retains the current rate rather than issuing an unverified last-second rate change. A pending journal remains available at restart. Existing software-dimming cleanup is still attempted only for matching monitor identities.
- Recording-session state ends at process exit; persistent pauses survive. Resume does not disable Preview.
- Deleting history also deletes its feedback blocks. Retention expiry removes old observations and their blocks; this is not a permanent hardware blacklist.

## Validation

The clean baseline passed 78 tests. Final local verification:

- Release compilation succeeded with no warnings or errors.
- `dotnet test tests/HzSwitcher.Tests/HzSwitcher.Tests.csproj -c Release`: **118 passed, 0 failed, 0 skipped**.
- The isolated synthetic UI harness passed its explanation/grid checks; the [inspected screenshot](automation-details-synthetic.png) contains fictional monitor data.
- The separate read-only catalog check confirmed the connected display's identity (`DISPLAY1`). This was an enumeration check, not hardware mode-change acceptance.
- `git diff --check` passed.
- Self-contained Windows x64 single-file publish succeeded. The actual tray application was not launched.

Local artifact: `artifacts/1.8.0-beta.1/HzSwitcher.exe`, **49,614,518 bytes**. SHA-256:

```text
152CCB1154AFD38CCC4C33180DC96EF40018BF88FA0990F1819CAABCF71A1C09
```

The executable reports `1.8.0-beta.1+8d202bef9d0b6b0734951ac1227db0bd0dabf413`; the suffix identifies the checkout's baseline commit, not a commit containing these uncommitted changes. The SHA-256 above identifies this exact beta artifact.

Checks cover preview write suppression, precedence, focus delay, saver restoration, manual hold/expiry, timed pause, recording pause, lock/suspend, external drift, missing targets, in-flight operations, journal failures, interrupted recovery, feedback direction/scope, schema migration and preservation, redaction/deletion, CCD ABI sizes and clone-path classification.

The isolated `tools/HzSwitcher.Preview` executable creates the real details form using fake display/activity/media adapters. Its display writer throws if invoked. It checks readable preview text, a populated history grid and minimum usable grid dimensions, and renders an offscreen screenshot. It does not launch the actual tray app, enumerate monitors, set display modes or change brightness.

Its optional `--catalog` command is a separate read-only integration check and does enumerate connected displays. It does not create the synthetic form or write hardware settings.

An earlier in-process UI smoke attempt had an incorrect one-minute idle fixture and caused WinForms window-class registration collisions in later native-window tests. The fixture was corrected and UI QA was moved into its own message-loop process; it is not silently skipped inside the unit suite. Full native-window regression tests subsequently pass in the regular suite.

## Hardware and later milestones still open

- No real rate switch, DDC/CI/WMI write, flicker/blackout measurement, unplug/dock cycle or lock/resume acceptance was performed for this change.
- No full 100/150/200% DPI or physical accessibility acceptance is claimed. The synthetic window render is a desktop-layout check.
- Independent simultaneous per-monitor policies and foreground-window monitor ownership are **not implemented**. Monitor identity discovery is groundwork for that milestone, not a claim that it is delivered.
- Automatic compatibility recommendations, guided calibration, persistent compatibility profiles separate from recent history, local activity summaries and fractional-Hz media matching remain later work.
- No power-savings, battery-life, CPU, memory or wakeup benchmark is claimed.
- Brightness restoration is asynchronous and may remain pending when a read or write cannot be confirmed. Readback confirms a reported level, while software gamma acceptance does not confirm physical backlight brightness. Native requests already in flight cannot always be cancelled.
- CCD connection identity can change after a driver/dock/reboot. In that case the journal is retained for explicit review, not automatically remapped by model name. Driver identity may be unavailable and is labeled `unknown-driver` internally.
- The executable is an unsigned local beta. No installation, startup registration, commit, push, PR, GitHub release or deployment was performed.

## Reproduce

```powershell
dotnet build HzSwitcher.csproj -c Release
dotnet test tests/HzSwitcher.Tests/HzSwitcher.Tests.csproj -c Release
dotnet run --project tools/HzSwitcher.Preview/HzSwitcher.Preview.csproj -c Release -- artifacts/qa
dotnet publish HzSwitcher.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o artifacts/1.8.0-beta.1
```

Before a public release, run the proposal's physical-monitor matrix and resolve discovered issues. Keep simulated feedback and synthetic screenshots labeled as such.
