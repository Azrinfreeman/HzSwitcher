# HzSwitcher: reliability and feature implementation plan

Status: first increment approved and implemented in source; manual/device acceptance remains outstanding. Later increments remain proposals.
Prepared: 2026-10-29.
Baseline: .NET 10 / Windows Forms, version 1.7.3, plus the current uncommitted UI redesign.

## 1. Scope and priorities

Deliver these improvements in small stages while retaining the redesigned four-page interface and existing display safety checks.

| Priority | Work | Outcome |
| --- | --- | --- |
| 0 | Finish real-device/UI verification | Establish a trustworthy baseline before more state-management changes |
| 1 | Temporary automation pause | Pause saver and app-profile changes together without losing preferences |
| 2 | Diagnostics view | Explain current behavior and failures without making users inspect raw logs |
| 3 | Settings export/import | Move and recover configuration safely |
| 4 | Signed release pipeline and graceful installer shutdown | Produce verifiable release artifacts and preserve normal cleanup |
| Later | Separate battery and plugged-in policies | Adapt saver settings to laptop power source, disabled by default |

The first feature increment should contain pause and diagnostics. Export/import follows after those state transitions are verified. Signing is a release workstream; it does not require adding a distribution page to the app. Battery policies are a separate later increment.

Keep C# / WinForms and existing dependencies. No automatic updater, cloud sync, telemetry, account system, independent multi-monitor automation, or additional display metrics are included.

## 2. Existing behavior to reuse

- `AutoSaverService` already has deterministic `Tick()`, an injectable clock/activity source, saver restoration, lock/resume handling, and a shared automatic-switch cooldown.
- `ProfileService` already tracks the pre-profile rate, manual overrides, foreground debounce, and delayed restoration. It defers to saver ownership and suspension.
- `SafetyNetService` verifies real rate changes and handles fallback/crash recovery. Pause must not bypass it.
- `TrayApp` coordinates brightness restoration through saver start/end events. Manual brightness changes during saver already influence the restoration target.
- `SettingsStore` reads JSON, migrates older settings, and preserves unreadable input as `.bad`. Its current `Save()` catches failures and returns no result; this is insufficient for reporting a successful import.
- `BrightnessService` knows its internal DDC/CI, WMI, or gamma method, but the public brightness mode combines DDC/CI and WMI as Hardware. Diagnostics must expose a cached backend description explicitly.
- The app controls one selected monitor. Monitor device names and brightness calibration are machine-specific.
- The current installer uses `/F` process termination on uninstall. This can bypass brightness and pinned-window cleanup.
- The previous implementation run passed 99 tests. That is historical evidence, not a new verification run for this plan. Final redesigned-screen, DPI, Narrator, and hardware checks remain outstanding.

Preserve the existing uncommitted work. Do not revert, overwrite, or publish it as part of preparing this plan.

## 3. Temporary automation pause

### User experience

Add one tray submenu, **Pause automation**, containing:

- 15 minutes
- 1 hour
- Until restart

While paused, expose **Resume automation** directly and mark the selected pause option. Choosing a duration again replaces the deadline starting from that action; it does not stack pauses.

Automation shows a compact pause row above its existing groups, with **Resume** and a readable deadline or “Until restart”. Display uses its existing conditional status area. Tray tooltip includes paused status within its length limit. No new navigation page or persistent countdown animation.

Manual refresh-rate, preset, brightness, and pin/unpin actions continue to work. Pause never changes the saved Auto power-saver or App profiles switches. If a user changes either switch during a pause, the new preference determines what resumes.

### Recommended pause semantics

**Pause restores the pre-automation state once, then blocks new automatic choices.** This avoids leaving a monitor stuck at the saver rate merely because automation was paused.

1. Set the shared automation gate first so neither service can start another automatic operation.
2. Clear pending profile candidates and pending idle/video/activity qualification windows.
3. If the saver owns the current state, restore it through its existing exit path. Preserve its brightness-restoration event handling and manual overrides.
4. If a profile still owns the resulting rate, release it to its pre-profile rate, respecting the existing override/current-rate checks. Saver release precedes profile release because saver may have captured a profile's rate as its return target.
5. Keep pause active until explicit resume, expiry, or process restart.

Releases apply only to the monitor and state actually owned by the relevant service. Capture/revalidate monitor identity; a disconnected or changed monitor must not receive a stale restoration target. Repeated Pause or Resume calls must be idempotent.

Restoration is a deliberate part of the pause command. Safety-net verification and recovery may still run while paused. New saver drops, profile entries, and profile leave timers must not issue unrelated automatic writes.

If saver restoration fails, stop the chained release rather than asking the profile to restore against uncertain state. Keep pause active, expose the failure and **Retry restore**, and preserve recovery information. Do not retry in a rapid timer loop. Manual controls and the existing safety net remain available.

If someone has already changed the rate externally, release ownership without applying an obsolete target. Any saved brightness restoration must still respect whether the saver owns that brightness state; do not restore another monitor's level.

### Service design

- Add `AutomationPauseService` with an explicit state (`Running`, `Timed`, `UntilRestart`), remaining time, and a `Changed` event.
- Keep time/state tracking separate from the release orchestration in `TrayApp`; avoid a second display-changing controller.
- Pass the same pause state/gate to both automation services. Add explicit lifecycle methods for resetting qualification and releasing owned state; do not simulate pause by toggling persisted enable flags or parsing status strings.
- Add bounded restoration results so the coordinator can distinguish success, superseded ownership, and failure. Preserve normal leave/restore behavior outside pause.
- Reuse the existing UI timer/coordinator for expiry checks; do not create overlapping background service loops. Consume transitions on the UI thread so expiry, manual changes, and service ticks remain ordered.
- Use injectable `TimeProvider` for new pause duration accounting: elapsed timestamps determine expiry; UTC/local time is for presentation. It is built into .NET 10, so no additional package is needed. Existing services can retain their `IClock` contracts. [TimeProvider documentation](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)
- Finite pause includes time spent asleep. Recheck on wake and before accepting a new automatic choice. Verify elapsed-time behavior on Windows; a clock/time-zone adjustment must not unexpectedly extend the pause.
- Pause is session-only, including timed pause. Restart clears it; no pause fields enter normal settings, exports, or crash-recovery records.

### Resume behavior

- Resume removes the pause gate and starts qualification windows afresh.
- Profiles must meet their foreground debounce again. Saver idle/video qualification begins at resume, accounting for the resume boundary rather than immediately consuming an entire pre-pause idle interval.
- Continue respecting the existing automatic-switch cooldown, enabled flags, lock state, game exclusions, and foreground rules.
- Do not replay operations queued before the pause. Manual choices made during pause become the new baseline for subsequent automation.
- If timed expiry happens while locked, clear the pause when due but retain lock suspension. Resume does not imply permission to change a locked display.

### Focused verification

Test active/no-profile, saver-only, profile-only, and profile-then-saver chains; manual override; external rate change; failed restoration/retry; repeated commands; expiry; restart; lock/sleep/resume; monitor changes; and enable flags edited during pause. Assert applied operations and ordering, not just displayed status text. Use fake time without real sleeps or hardware.

## 4. Diagnostics

### User experience

Add **Diagnostics…** under General > Maintenance. Use an owned read-only dialog, matching `DialogShell` styling but with **Close**, **Refresh**, and **Copy diagnostics** rather than Save/Cancel.

Show short labeled rows grouped by Display, Brightness, Automation, and Recent failure:

- App version, OS version/build, process architecture, and capture timestamp.
- Selected monitor and resolution, actual reported Hz, supported rates, configured and resolved fallback rate.
- Brightness backend: DDC/CI, WMI, Software dimming, Unsupported, or Not checked. Distinguish requested/last-known values from confirmed hardware reads.
- Whether saver and profiles are enabled; pause state/deadline; current saver/profile ownership; relevant cooldown or suspension reason.
- Last failed operation, its target, time, and concise result. “No failure recorded this session” when appropriate.

Only show facts the app measures. Do not infer HDR/VRR support, GPU load, energy savings, physical backlight level, or hotkey availability from unrelated data.

### Data and refresh design

- Add a structured `DiagnosticsSnapshot` assembled from existing services and a small session-only last-failure record. Keep statuses structured at their source; do not reverse-parse `Status`, balloon text, or the log file.
- Expose the cached brightness backend without synchronously probing DDC/CI when the dialog opens. Refresh can request the existing background probe and update the snapshot when it completes.
- Record failures from actual operation results, including delayed safety-net fallback and brightness native-call rejection. Recording a requested value or queuing a brightness write does not prove success.
- Use a bounded, sanitized failure summary rather than retaining full exception dumps indefinitely. Correlate failures with device/operation so late completion from a previous monitor is not shown as a new failure on the current monitor.
- Opening diagnostics does not apply a rate, change brightness, enable hooks, or force recovery. Refresh is read-only hardware inspection; repeated Refresh must not enqueue concurrent probes.
- A device removed mid-refresh produces an unavailable/stale label, not an exception or a substituted monitor's data. Retain the snapshot timestamp so stale values are recognizable.
- Preserve **Open log** as an advanced escape route; do not embed the entire log by default.

### Copy behavior

Show exactly the sanitized report that will be copied. The default report excludes raw logs, window titles, app process lists, serial numbers, usernames, absolute user paths, and machine-specific monitor identifiers. Local diagnostic details may show the active profile name, but omit it from the default copied report.

Clipboard copy is user-triggered and local. No upload or telemetry. Handle a temporarily busy clipboard with a concise retryable error. Any future optional detailed export must preview its additional contents first.

### Verification

Test backend mapping, unavailable/pending values, delayed failures, stale-device completion, failure sanitization, deterministic copied output, and the default exclusions. Verify read-only refresh never invokes a display/brightness write. Check keyboard access, narrow layouts, large text, and clipboard failure behavior manually.

## 5. Settings export/import

### Portable format

Use a versioned JSON document with a dedicated allowlisted DTO, for example an `HzSwitcherSettings` format identifier, `FormatVersion: 1`, originating app version, and a `Preferences` object. Do not serialize the entire live `Settings` object.

| Include | Exclude |
| --- | --- |
| Presets, hotkeys, app profiles, profile restore delay | Pending rate/brightness recovery markers and captured restoration targets |
| Saver enablement, timings, rate, brightness policy, detection lists | Temporary pause, active profile/saver state, verification timers |
| Notifications, rate visibility, fallback preference | Window position, selected device name, per-monitor brightness calibration |
| Pinning enablement and outline/sound/mouse preferences | Pinned window identities and shortcut-initialization bookkeeping |
| Startup-brightness opt-in preference | Windows startup registry entry, logs, executable paths, signing material |

Copying the startup-brightness preference does not copy monitor levels. Do not change brightness on import merely because that preference is enabled. A restart uses only locally available saved monitor levels according to the existing startup rules.

### Export

- General > Maintenance > **Export settings…** opens a native Save dialog with a predictable JSON filename.
- Capture a consistent preferences snapshot on the UI thread, then serialize/write without blocking it unnecessarily.
- Explain once in the file dialog context that profiles and detection lists contain application names. Export contains the user's configuration, not a sanitized support report.
- Report completion only after a successful write. Cancel is a no-op; report permission/disk errors without changing current configuration.

### Import and validation

1. **Import settings…** reads a bounded file (proposed maximum: 1 MiB) into a detached DTO. Reject malformed JSON, duplicate object keys, wrong field types, null required collections, unsupported/future format versions, invalid enum/action identifiers, and out-of-range values before touching live settings.
2. Validate every preference against the actual editor/service constraints. This includes six presets, 24-character preset names, unique names, supported shortcut syntax/unique normalized combinations, valid preset references, timing ranges, brightness sentinel/ranges, outline thickness, and bounded list/string sizes. Avoid coercing invalid input silently through clamping property setters.
3. Positive rate preferences may be unsupported by this monitor. Preserve them as preferences and show the existing nearest-rate/fallback resolution in the preview. Do not treat hardware incompatibility as malformed JSON or apply a mode while previewing.
4. Show a compact review: changed groups, preset/profile/shortcut counts, compatibility warnings, and any feature enablement changes. Offer **Apply import** and **Cancel**. This single review is needed because one operation replaces several settings groups.
5. Recheck relevant state at Apply. If a rate verification or other transition is still in progress, keep the preview open and explain when it can be applied. Never import pending-operation metadata to bypass that transition.

The first version imports the complete portable configuration; no merge or dozens of per-field checkboxes. Omitted optional fields receive explicit format-version defaults, not arbitrary current-machine values. Required sections must be present. Document these defaults and test them.

### Applying an import

- Briefly gate new automatic choices using an internal configuration-transition guard, distinct from the user's pause preference.
- Finish/release any currently owned automation state against its original settings and monitor before changing the rules. Use the pause lifecycle helpers and preserve safety verification. On release failure, keep the old preferences and leave a clear recovery path; do not pretend hardware rollback is guaranteed.
- Build a full candidate from the current settings and the validated allowlist, preserving excluded local fields. Revalidate the candidate and create a local pre-import backup.
- Add an explicit successful/failed save API, using a same-directory temporary file and atomic replacement/move. Flush and complete persistence before reporting success; a failed write leaves the previous settings file intact. Keep the existing logging wrapper for callers that do not yet need a result.
- Apply approved fields to the existing `Settings` instance on the UI thread. `SafetyNetService` holds that object directly; replacing only a `TrayApp` reference would leave services with inconsistent settings.
- Reconcile hotkeys, pinning preferences/enablement, and normal UI refresh through their existing entry points. Runtime shortcut conflicts are reported as unavailable bindings; they are not reported as successful registrations or hidden by the import result.
- Preserve any user pause that already existed. Remove the temporary import guard only after reconciliation. Enabled automation then evaluates the imported rules normally; state this behavior in the review. Import does not explicitly apply an arbitrary preset or saved brightness value.
- If reconciliation fails after the durable save, surface the affected subsystem and provide restore-backup/retry guidance. Do not claim the whole operation is atomically reversible across Windows APIs.

The local backup retains settings only, never raw logs or pinned-window handles. Provide **Restore previous settings…** through the same validation/review path; users must not need to edit JSON manually. Distinguish this local full-settings backup from portable export, and never reapply stored pending-operation fields from it.

### Verification

Test portable round-trip, all exclusions, old/future format versions, duplicate JSON keys, malformed/null/oversized input, normalized shortcut conflicts, broken preset references, unsupported monitor rates, and every numeric boundary. Test Cancel and every failure before Apply leave live preferences unchanged.

Use temporary directories and injectable file-failure cases to verify backup creation and atomic persistence. Test that services retain the same settings reference, existing pause survives, enabled flags reconcile, and failed shortcut registration is visible. Never use the real `%APPDATA%` folder in automated tests.

## 6. Real-device and UI verification

This work starts before feature implementation and repeats for affected paths afterward. It is not replaced by unit tests. The previous Escape interruption remains recorded; follow the user's latest desktop stop/resume instructions when verification begins. No desktop actions are part of this planning request.

Record screenshots and a short pass/fail result for:

- All four pages, every editor, tray menu, pin menu, diagnostics, and import review/error states.
- 100%, 125%, 150%, and 200% scaling; moving between monitors with different scaling; enlarged text; constrained laptop working areas; long monitor/preset/profile names and empty lists.
- Keyboard-only navigation, visible focus, shortcut recording escape routes, Narrator names/states/values, and high contrast at launch and while open.
- Real rate changes, rejected/unstable modes, delayed verification, manual override, safety fallback, and an interrupted-change recovery using a controlled test environment.
- DDC/CI, laptop WMI, software dimming, unsupported brightness, rapid changes, saver restoration, and per-monitor persistence.
- Sleep/wake, lock/unlock, disconnect/reconnect, resolution changes, and selected-monitor disappearance during an operation.
- Pin/unpin, original window commands, Unpin all, graceful exit, fullscreen/game suppression, and settings reached from the pin menu.
- Pause/restore/resume and import while services are enabled, disabled, paused, locked, or recovering from an error.

Use a VM or separate test configuration for crash recovery, installer, and destructive failure simulation. Keep ordinary desktop validation reversible; restore original monitor settings and configuration afterward. Hardware-specific modes that cannot be exercised must be marked untested, with the missing equipment recorded.

## 7. Release signing and installer lifecycle

### Graceful shutdown first

Add a bounded, idempotent `--exit` command that signals the existing instance to run `ExitApp()` through its UI thread. If no instance exists, `--exit` exits without starting the tray app. Keep it separate from the existing second-launch Show signal.

Have installer upgrade/uninstall request graceful exit and wait for process/file release. On timeout, show a retryable explanation rather than silently falling back to `/F`. Remove the current unconditional force-kill uninstall action. Preserve brightness restoration, pending-change handling, hotkey/hook disposal, and Unpin all semantics. Test both per-user and elevated installation paths, including a running app whose integrity level differs from the installer.

### Signing pipeline

Retain the existing Inno Setup EXE distribution initially. Prepare a release script that:

1. Builds/tests the intended source revision and reads one version from the project.
2. Publishes the self-contained Windows x64 app.
3. Signs and timestamps the final app executable with the approved publisher identity.
4. Builds Inno Setup with its configured signing tool so Setup, temporary copies, and the uninstaller receive the appropriate signatures.
5. Verifies the embedded app, installer, and uninstaller signatures and publisher; fails a signing-required release if verification fails.
6. Generates SHA-256 checksums from final signed artifacts and records source revision, version, tool versions, and verification results.

Do not modify binaries after signing. Distinguish an unsigned local developer build from a distributable signed release. No implicit unsigned fallback in the public-release path. Inno's signing integration is preferred over signing only the outer installer afterward. [Inno Setup SignTool reference](https://jrsoftware.org/ishelp/topic_setup_signtool.htm)

Signing requires an external publisher identity/certificate or signing service. Select the provider only after checking its eligibility and the publisher's needs; do not assume availability from the machine's region/time zone. Do not embed private keys, tokens, certificate passwords, or signing credentials in source. A later signing setup should use private secret entry and the provider's supported key storage. [Microsoft signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)

A valid signature identifies the publisher; a new signed download can still receive SmartScreen reputation warnings. Do not promise that signing removes them. [Microsoft SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)

Keep account enrollment, paid certificate purchase, public upload, and Store submission outside implementation approval. Prepare and verify local artifacts first; publication remains a separate explicit action. No signing account or secret is needed to review this plan or implement pause/diagnostics/import.

### Verification

Test fresh install, upgrade with the app running, canceled upgrade/uninstall, no-instance exit, graceful shutdown timeout, uninstall cleanup, startup task preference, data preservation, signature/timestamp validation, and final checksum matching. Test release scripts with signing unavailable so they fail clearly and do not label an unsigned artifact as signed.

## 8. Later: battery and plugged-in policies

Keep this disabled by default and deliver separately after the first features are stable.

- Add an optional **Use different saver settings on battery** switch on Automation. When off, preserve the existing single policy exactly.
- Initially vary only saver idle/video timeout, saver Hz, and saver brightness. Keep app profiles, pinning, hotkeys, and detection lists shared. Avoid a second profile system or automatic application of “Gaming” whenever AC connects.
- Obtain power source through an injectable provider. Treat unknown as the existing/default policy; do not equate “no battery” with a low battery or infer source from a preset name.
- Power notifications update the effective policy through the same coordinator and pause/import gates. Coalesce rapid notifications; no duplicate display writes.
- A power-source change must not overwrite a manually chosen rate, recapture an already-dimmed level as the original brightness, or replace a valid saver return target. If saver owns the display, reconcile its target through the existing ownership/safety path; otherwise use the new thresholds for future qualification.
- While paused or locked, update the pending policy without applying it; reevaluate once the relevant suspension ends.
- Add a settings migration only when these new persisted fields ship. Initialize both policy variants from current settings so an upgrade preserves behavior. Extend the portable format through an explicit compatible version/migration.
- Test AC/battery/unknown transitions, desktops without batteries, rapid cable changes, saver active/inactive, pause/lock, manual override, and profile interaction.

## 9. Proposed implementation map

Names for new files are provisional; keep types small and reuse existing native/control helpers.

| Area | Planned changes |
| --- | --- |
| `Services/AutomationPauseService.cs` (new) | Pause state, injected elapsed time, expiry, notifications |
| `Services/AutoSaverService.cs`, `ProfileService.cs` | Shared gate, explicit release/reset lifecycle, ownership results and structured diagnostic reasons |
| `Services/DiagnosticsService.cs` (new) | Read-only snapshots, bounded failure record, sanitized report |
| `Services/BrightnessService.cs`, `SafetyNetService.cs` | Cached diagnostic details and authoritative operation/failure notifications |
| `Services/SettingsTransferService.cs` (new) | Versioned portable DTO, validation, preview, backup and restore |
| `Services/SettingsStore.cs` | Explicit save result and atomic persistence helper; retain existing callers |
| `Services/Abstractions.cs`, test fakes | Only the new pause/time, diagnostics, persistence and later power contracts actually needed |
| `TrayApp.cs` | Ordered pause/import orchestration, tray actions, status, editor entry points and shutdown signal |
| `Program.cs` | Parse `--exit` and signal a running instance without launching another tray app |
| `UI/MainForm.cs` | Compact pause row; Maintenance actions for diagnostics/export/import/restore |
| `UI/DiagnosticsForm.cs` (new) | Read-only details and copy/refresh/close actions |
| `UI/SettingsImportForm.cs` (new) | Changes preview, compatibility warnings, Apply/Cancel and actionable errors |
| `UI/DialogShell.cs` | Add read-only action composition only if needed; preserve editor behavior |
| `artifacts/installer/HzSwitcher.iss` | Graceful lifecycle and signing integration |
| `scripts/release.ps1` (new) | Reproducible publish/sign/package/verify/checksum sequence |
| `tests/HzSwitcher.Tests` | Focused service, transfer, persistence, presentation and shutdown-command tests |
| `README.md`, `CHANGELOG.md` | Explain shipped behavior, portable exclusions, pause semantics, and signed-release limitations |

## 10. Delivery sequence and completion gates

1. **Baseline QA:** record existing redesign results and fix only confirmed regressions. Establish isolated settings/test paths.
2. **Pause:** implement the shared gate and ownership releases, then tray/UI controls and expiry. Complete the pause interaction matrix before shipping it.
3. **Diagnostics:** add structured read-only data and actual failure reporting, then dialog and sanitized clipboard output. Verify opening/refresh does not change display state.
4. **Export/import:** implement DTO validation and atomic saves first, then review/backup/restore and service reconciliation. Exercise failure paths before normal-path UI polish.
5. **Release readiness:** implement graceful shutdown, prepare the signing-capable pipeline, and perform installer tests. Signing execution depends on a separately configured publisher identity; feature implementation does not.
6. **Optional laptop increment:** implement and test battery policies only after the earlier stages are accepted.

For each implementation stage, run focused tests while editing and then the complete `dotnet test tests/HzSwitcher.Tests` suite and an appropriate `dotnet build`. Add meaningful state/failure tests rather than screenshots asserted through unit tests. Re-run manual checks only for changed or unverified paths. Record actual results and any missing hardware; do not mark a real-device check passed from mocks.

The recommended first approval covers stages 1–3: baseline verification, pause, and diagnostics. This keeps the first increment useful and reviewable. Later stages remain fully specified here, without requiring all features to land in one release.

## 11. Decisions proposed for review

- Pause restores automation-owned state once, then holds automatic changes; it does not merely freeze an active saver.
- Pauses are session-only; Resume restarts qualification and respects saved enable flags.
- Diagnostics copy is sanitized and local; logs are not included by default.
- Import replaces a complete portable configuration after one review, preserves local device/recovery data, and creates a recoverable backup.
- The initial distribution remains an Inno Setup EXE, with graceful shutdown and externally configured signing.
- Battery policies remain a later opt-in addition.

These were the recommendations at proposal time. The planning request initially added only this Markdown document; the user subsequently approved the first increment. The implementation record below describes the resulting work.

## 12. First-increment implementation record

Implemented the session-only pause clock/gate, tray duration choices and Resume/Retry actions, compact Automation controls, Display/tray status, and ordered saver-then-profile restoration. A rejected restoration retains ownership and blocks automatic expiry/resume until an explicit retry succeeds or a manual/external choice supersedes it. Resume resets qualification inside the services, preserves enabled preferences, and continues respecting lock suspension and the shared cooldown. Superseded saver ownership also invalidates the underlying profile's restoration claim, including an external choice that happens to equal the previous profile rate. Monitor identity is revalidated before releasing owned rates or restoring saver brightness.

Implemented the owned read-only Diagnostics dialog, background Refresh, and sanitized clipboard report. Snapshots use cached brightness state; hardware reads, requested levels, and pending work are distinct. Refreshes share/coalesce background probes and discard stale monitor or pre-write results. Typed refresh-rate and brightness failures are recorded per monitor in a bounded session-only history; raw paths, process names, monitor identifiers, and exception/log text do not enter copied reports. Successful copy/refresh clears an earlier inline error.

Added 38 automated cases covering duration/expiry and wall-clock changes, repeated commands, all saver/profile ownership combinations, restore failure/retry, manual and external overrides, monitor changes, resume qualification/cooldown/lock/enable flags, sanitized reports, brightness backend/read/write behavior, background probe coalescing and stale completion, delayed safety fallback, and read-only dialog construction. Tests use fake displays/brightness backends and isolated temporary log/settings paths. The existing 99 tests also passed: **137 total, zero failures or skips** on 2026-10-29.

The Debug review build at `%TEMP%\HzSwitcher-reliability\HzSwitcher.exe` succeeded with zero warnings and zero errors. `git diff --check` reported no whitespace errors. The review build was not launched.

Desktop automation remains stopped following the earlier Escape interruption. No installed application, real monitor mode/brightness, startup preference, installer, or release artifact was changed. Full visual/DPI, Narrator, clipboard contention, sleep/wake elapsed-time behavior on Windows, DDC/CI/WMI/software hardware, disconnect/reconnect, and real restoration/fallback checks remain unverified. Those manual acceptance gates are not replaced by the passing automated suite.

Export/import, graceful installer shutdown, signing, and battery policies were not part of this first increment and remain unimplemented.
