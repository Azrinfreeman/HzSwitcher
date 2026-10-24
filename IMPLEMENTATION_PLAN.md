# HzSwitcher improvement implementation plan

Date: 12 October 2026  
Reviewed baseline: version 1.3.0, C# / .NET 10 Windows Forms  
Status: proposal only; implementation has not started.

## Recommendation

Make the existing switching behavior dependable and easy to understand first, then add conveniences. The best next release would combine consistent recovery, clear automation ownership, temporary pause, and a cleaner main window. Keep Windows Forms and the lightweight tray workflow; a framework rewrite would add cost without addressing the main gaps.

This plan comes from reviewing the current source, sibling test project, README, changelog, build script, and installer. It is a source review, not a live visual or hardware assessment. No application code, settings, display modes, startup entries, dependencies, or release files were changed. No app, build, installer, or tests were run for this review.

The existing parent `IMPLEMENTATION.md` concerns AppData cleanup, so this app-specific proposal is saved separately. The older `HZSWITCHER_PLAN.md` contains historical requirements that differ from the current implementation; the current source is the baseline here.

## 1. What is already present

- Tray controls, current-rate icon, and one-click refresh-rate buttons.
- Named presets, editable global hotkeys, and foreground app profiles.
- Idle/video power saver with delayed activity restoration and game exclusions.
- Monitor selection, Windows theme detection at launch, tooltips, and remembered window bounds.
- Mode preflight, delayed verification for manual/profile switches, and startup recovery.
- JSON settings migration, corrupt-file preservation, and a bounded text log.
- An xUnit test project with fake display/input/clock services, plus portable and installer packaging. The changelog reports 64 tests; their current pass status was not checked during this planning task.

These should be improved rather than introduced again as new features.

## 2. Prioritized backlog

Effort is relative: **S** = localized change; **M** = several connected components; **L** = architecture or hardware validation work. It is not a delivery estimate. P0 is correctness, P1 is the proposed next-release experience, P2 is optional expansion, and P3 is exploratory.

| ID | Priority | Improvement | User benefit | Effort |
| --- | --- | --- | --- | --- |
| R1 | P0 | One switching and recovery path | Every switch gets consistent validation and recovery | L |
| R2 | P0 | Explicit automation priority and manual ownership | Profiles, saver, and manual choices stop interfering | L |
| R3 | P0 | Monitor disconnect and resume handling | No stale restore targets or switches on an unintended screen | M |
| R4 | P0 | Validated, atomic settings saves | Preferences survive bad files and interrupted saves | M |
| U1 | P1 | Clear current-state header and switch feedback | Users understand the current rate and why it changed | M |
| U2 | P1 | Pause automation and restore now | Quick control during work, gaming, or presentations | M |
| U3 | P1 | DPI, theme, keyboard, and layout polish | A consistent, readable Windows experience | M |
| E1 | P1 | Better profile, preset, and hotkey editing | Less setup friction and fewer broken references | M |
| D1 | P1 | Local activity history and useful errors | Explain unexpected switches without opening a raw log | M |
| M1 | P2 | Saved settings for each monitor | Each display keeps suitable saver and recovery preferences | L |
| A1 | P2 | Battery and plugged-in rules | Laptop automation adapts to power source | M |
| A2 | P2 | Separate idle/video controls and better detection | Fewer unwanted drops while watching or listening | M |
| C1 | P2 | Settings import/export and reset | Easy backup, migration, and recovery | M |
| Q1 | P1 | Graceful shutdown and release cleanup | Reliable exit, upgrades, and packaging | M |
| F1 | P3 | Monitor bundles, CLI, fractional-rate support | More advanced workflows | L |

## 3. Reliability work

### R1. Use one switching and recovery path

**Observed:** `TrayApp.SetHz()` and profile callbacks use `SafetyNetService`, while `AutoSaverService` calls `IDisplay.TryApply()` directly. The safety service tracks an expected rate and a global pending flag, but does not capture the monitor or previous mode. Its verification timer reads whichever display is selected when it fires. Recovery defaults to a configured rate, or the highest supported rate, rather than the previous verified mode. A Windows-reported rate also cannot prove that the picture is visible to the user.

**Implementation:**

1. Introduce a `DisplayChangeCoordinator` used by manual controls, hotkeys, profiles, saver entry/exit, and recovery.
2. Represent a request with monitor identity, requested rate, reason, and transaction ID. Capture the previous mode and display topology before applying.
3. Preflight the mode, apply it, verify immediately, and verify again after a short delay. Serialize changes per monitor; cancel or supersede old verification callbacks so they cannot undo a newer change.
4. Return a structured result: applied, unchanged, unsupported, unavailable, superseded, or failed. Preserve the native result code for diagnostics.
5. Persist a recovery journal before applying. Prefer the previous verified compatible mode, then a compatible last-known-good mode, then an explicitly configured supported fallback. Do not assume 60 Hz or the maximum rate is always safe.
6. If rollback fails, retain the unresolved journal and show recovery failure. Do not clear pending recovery merely because an attempt was made. Use bounded retries.
7. Preserve instant switching by default. Offer an optional “Confirm unfamiliar modes” countdown for users who want it. Treat confirmed mode history as monitor-and-mode specific; automatic API verification is not visual confirmation.
8. Make verification timers and event subscriptions disposable. Publish the final result so the UI and automation state reflect rollback as well as success.

Microsoft documents `CDS_TEST` as a preflight and supplies distinct result codes for rejection and driver failures. Keep those distinctions in the result model. [ChangeDisplaySettingsEx documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexa)

**Acceptance:** manual, profile, saver, restore, and recovery requests use the same path; a delayed verification cannot affect a newer request or another monitor; failure does not announce success; interrupted recovery remains recoverable.

### R2. Define which rule owns the refresh rate

**Observed:** the saver and profile services maintain separate restore state. Manual overrides are inferred from a different observed rate, so an explicit choice equal to the profile rate may not be recognized. Profile matching uses the first matching row, and edits to the same active process are not reliably reevaluated. Turning auto off in advanced settings does not call the immediate saver-exit path used by the main window and tray. Profiles also lack the saver's explicit session-lock gate.

**Proposed policy, in evaluation order:**

| Condition | Behavior |
| --- | --- |
| Recovery or verification in progress | Finish or safely supersede the transaction before another change |
| Display unavailable, session locked, suspended, or display off | Defer automatic changes and invalidate stale candidates |
| User explicitly requests a rate | Apply it and record manual ownership, including same-rate requests |
| Automation paused or manual hold active | Keep the chosen rate; suppress automatic profile/saver changes |
| Enabled foreground profile matches | Apply its rate; its “Allow power saver” option controls saver eligibility |
| Game exclusion matches | Prevent saver entry; if already saving, request restoration when available |
| Idle/video rule qualifies | Apply the supported saver rate after the configured delay |
| No temporary rule remains | Return to the verified baseline if automation still owns the rate |

Game exclusions inhibit saver behavior; they do not override an explicit manual choice or a configured app profile.

**Implementation:**

1. Add an `AutomationController` that decides the desired rate and reason; existing detectors report facts rather than directly changing display modes.
2. Keep the coordinator responsible for applying changes and the controller responsible for policy. Migrate one caller at a time to avoid a large simultaneous rewrite.
3. Route all setting changes through shared commands so main-window, tray, hotkey, and dialog behavior agree.
4. Keep current manual-switch behavior as the migration default: clear saver state and restart its cooldown. Add a separate optional timed “Hold this rate” action.
5. A manual request suppresses the current profile until that app loses focus beyond the leave delay, even if the requested rate equals its rate. On subsequent profile exit, preserve the manual baseline.
6. Use stable profile IDs and a settings revision to reevaluate active edits. Resolve multiple matches by explicit priority, then exact match before wildcard, then visible list order.
7. Add “Allow power saver” to profiles. Migrate existing profiles to the current saver-allowed behavior; new game-oriented profiles can suggest protection.
8. Respect external Windows/driver changes by updating the baseline and releasing automatic ownership, unless an unresolved recovery transaction requires reconciliation.
9. Bound retries and expose cooldown/retry status. Avoid repeatedly trying a failed restore every timer tick.

**Acceptance:** disabling auto from any entry point exits saver consistently; same-rate manual overrides survive profile exit; profile-to-profile transitions retain the correct baseline; lock and pause suppress both services; active profile edits take effect without an app restart.

### R3. Handle monitor changes and resume deliberately

**Observed:** monitor selection is supported, but unavailable selections silently resolve to the primary screen. `SelectMonitor()` moves on after attempting saver restoration even if that restoration failed. Display events refresh the UI without reconciling pending transactions, profile state, or restore targets.

**Implementation:**

- Add a display snapshot containing identity, current mode, supported rates, and availability. Bind switch requests to that identity instead of a dynamically resolved selection.
- Distinguish “Follow primary display” from “Use this specific display.” If a specifically selected monitor disappears, pause its automation and explain the issue instead of applying its policy to another screen.
- Debounce topology events and refresh after resume, reconnect, resolution change, and docking. Invalidate cached modes and stale transactions before reevaluating rules.
- Attempt restoration on the old monitor before selection changes. If it fails, retain recovery information scoped to that monitor and report it.
- Discover device path information for saved associations. Handle ambiguous or changed identities explicitly; do not promise persistence across every dock, port, or driver change.

Windows provides display path information and additional device information through `QueryDisplayConfig` and `DisplayConfigGetDeviceInfo`; use these as the basis for discovery, with a compatibility fallback where needed. [QueryDisplayConfig documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig)

**Acceptance:** disconnecting a selected secondary display cannot trigger its saved rate or rollback on the primary display; reconnect/resume rereads the supported modes; no supported rates produces an informative disabled state rather than “0 Hz.”

### R4. Validate and save settings safely

**Observed:** parsing accepts semantically invalid values and explicit null collections. Saving directly overwrites the file and only logs failure, so the UI can imply that preferences were saved when they were not. Version migration is currently based on searching the JSON text for a property name.

**Implementation:**

- Validate parsed settings: non-null collections and items, safe numeric ranges, recognized actions, unique IDs, usable names, and valid references.
- Implement ordered schema migrations using the parsed version. For a newer unsupported version, preserve the file and avoid overwriting it with an older schema.
- Write to a temporary file in the same directory, flush, then replace the settings file while keeping a last-good backup. Handle initial file creation separately.
- Return save success/failure to callers. Show “Could not save preferences” with retry; do not close an editor as successfully saved on failure.
- Edit a draft copy in dialogs, validate, then commit. Cancel must leave live settings unchanged.
- Debounce rapid numeric-setting saves. Keep recovery-journal writes immediate and separate from general preferences so they cannot be delayed behind UI edits.
- Preserve the existing unreadable-file backup behavior and add a visible recovery notice.

**Acceptance:** malformed and null-filled JSON cannot crash the app; a simulated interrupted write preserves the previous file; unknown future versions are not overwritten; save failure is visible; cancelling an editor changes nothing.

## 4. Interface and daily-use polish

### U1. Make the main window easier to scan

Keep the current two-column layout where space allows, but put the immediate actions first:

```text
Selected monitor                         240 Hz
3840 x 2160          Controlled by: Gaming profile

[Gaming 240 Hz] [Balanced 120 Hz] [Saver 60 Hz]
[Other rates...] [Pause automation...] [Restore now]

Power saver                            App profiles
Enabled | Idle for 2m / 10m            Current app + matching rule
Target: 60 Hz                         Manage profiles...

Hotkeys | Settings | Recent activity | Hide to tray
```

Implementation details:

- Show one clear ownership label: Manual, App profile, Idle saver, Video saver, Paused, or Recovery needed.
- Show the previous rate, intended rate, and outcome while switching. Avoid long animations around a display mode change.
- Mark the current rate with text or a checkmark as well as color. Make selected presets distinguishable when multiple presets resolve to the same rate.
- Show requested and resolved rates when they differ, for example “Balanced: 120 Hz requested; 100 Hz available.” Keep existing nearest-rate behavior visible rather than silent.
- Display unavailable/unsupported modes and explain disabled controls. The fallback hint must show the resolved fallback, not a stale hard-coded setting.
- Replace “Open settings file (hotkeys)” with “Open settings file”; hotkeys already have an editor.
- Keep detailed timings and detection lists in advanced settings. Add explanatory empty states and an unobtrusive first-run guide to tray behavior.
- Update controls incrementally. `RefreshAll()` currently clears and recreates rate/preset controls, which can lose keyboard focus and creates avoidable allocations.
- Cache a display snapshot and rendered tray icon; refresh on actual state or topology changes instead of reenumerating modes repeatedly during one UI refresh.

**Acceptance:** the current rate, selected display, controlling rule, and restore target are understandable without opening settings; failed switches leave accurate feedback; refreshing does not move keyboard focus.

### U2. Add pause, hold, and restore actions

- “Pause automation” offers 15 minutes, 1 hour, and until resumed. It pauses profiles and saver together without changing their saved enable switches.
- Pausing while saver is active first requests restoration. If restoration fails, still pause future automatic changes and show the failure.
- Show remaining pause duration in the main window and tray, with one-click resume. Evaluate expiry on resume from sleep as well as normal ticks.
- “Restore now” ends saver immediately and restarts the cooldown.
- “Hold this rate” is a separate timed manual override, useful during a meeting or game. Offer a matching hotkey action.
- Default pause/hold state to session-only. A restart resumes the user's saved automation settings; make this behavior clear in the menu/help.

**Acceptance:** pause preserves the saved enabled settings, covers both automation services, and expires predictably; restore now uses the verified switching path; a hold cannot be silently overridden by a profile.

### U3. Improve layout, theme, and accessibility

**Observed:** several controls and dialogs use fixed pixel widths/heights. The main form does not explicitly set the same DPI scaling baseline as the dialogs. Theme colors are static values detected once at launch. Active state is largely conveyed through color.

- Set a consistent DPI scaling policy and logical layout measurements across all forms; preserve the existing PerMonitorV2 setup.
- Replace fragile fixed-position rows with appropriately sized layout containers and scrolling. Keep the footer within the available width when columns wrap.
- Clamp restored bounds to the current working area, including smaller screens and changed scale factors.
- Add System / Light / Dark selection, live theme refresh, and a high-contrast palette using system colors. Restyle existing controls and tray menus when the theme changes.
- Give icon-only/compact controls accessible names, logical tab order, keyboard shortcuts, and visible focus. Expose progress/status accessibly without announcing every timer tick.
- Dispose removed controls, tooltips, generated icons, fonts owned by the app, and subscriptions. Explicitly unsubscribe the display-settings event during shutdown.
- Render a legible tray icon at the relevant sizes; provide a simpler symbol option if three-digit rates are too small.

Validate at 100%, 125%, 150%, and 200% scaling, with mixed-DPI monitors and a small working area. Automatic scaling helps with resolution and font differences, but custom layouts still need verification. [WinForms automatic scaling](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/forms/autoscale)

Keyboard navigation, visible focus, accessible names, and high contrast should be part of the acceptance checks. [Microsoft accessibility walkthrough](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/walkthrough-creating-an-accessible-windows-based-application)

**Acceptance:** no clipped actions or horizontal overflow at supported sizes/scales; all main actions work with a keyboard; current state is identifiable without color; changing the Windows theme updates open windows in System mode.

### E1. Make editors more forgiving

- **Profiles:** add “Use current foreground app,” refresh/search running apps, show exact/wildcard match explanations, and surface overlapping rules. Remember the previous non-HzSwitcher foreground app so the picker does not select this settings window. Keep manual process entry.
- **Presets:** use stable IDs for hotkey references; renaming preserves bindings. Report duplicates inline instead of silently dropping them. Make deletion effects visible and provide undo before committing. Replace the fixed six-row editor with an add/remove list if more presets are useful.
- **Hotkeys:** validate the entire combination and required parameters, show conflicts on the affected row, and preserve the previous working registration if a replacement cannot register. On apply failure, restore the previous registration set and report any restoration failure accurately.
- Migrate name-based preset bindings to IDs deterministically. Flag missing or ambiguous references instead of binding them to a guessed preset.
- Show unsupported saved rates with the actual resolved rate for the selected monitor. Use the same rate-choice component in all editors.
- Dispose temporary `Process` objects in the running-app enumeration and handle protected/exited processes without losing the entire list.

**Acceptance:** renaming a preset does not break its hotkey; duplicate/invalid rules are explained before Save; a conflicting new hotkey does not silently remove an existing working one; running-app refresh survives processes exiting.

### D1. Explain switching with a local activity history

- Add a bounded “Recent activity” view: timestamp, monitor, source, previous rate, target rate, and final result. Include skipped changes with a meaningful reason when useful.
- Surface actionable errors such as “Display disconnected” or “Windows rejected 120 Hz at this resolution,” while keeping native details expandable.
- Offer local diagnostic export with a preview and selected fields. Exclude full window titles, executable paths, and personal identifiers by default; do not upload automatically.
- Add notification preferences: errors/recovery only, automatic changes, or quiet. Suppress repeated notices for the same unresolved failure.
- Rotate logs instead of discarding all previous entries at the size limit. Define retention and clear-history controls.
- Add a small About view with version, settings/log locations, and manual help. A release-page link can be added once a real release URL is configured.

**Acceptance:** a user can explain the last automatic switch from the history; repeated failures do not produce notification spam; exported diagnostics contain only the previewed fields.

## 5. Optional feature expansion

### M1. Remember settings for each monitor

Build on R3's identity and topology work. Store saver rate, fallback/last-good mode, preferred baseline, and optional monitor-specific preset targets. Start with one selected monitor under control, as today. Group changes across several displays belong in a later feature because partial success requires explicit recovery behavior.

**Acceptance:** switching between monitors restores their own preferences; an unsupported saved rate is visibly resolved or requires selection; a disconnected monitor retains settings without applying them elsewhere.

### A1. Add battery and plugged-in policies

Add optional power-source rules such as “Use the saver rate on battery.” Keep these disabled by default, and treat the power source as a policy input below manual hold and explicit app profiles. Handle desktops and unavailable battery information without presenting misleading controls. Reevaluate on power-source change and resume.

**Acceptance:** unplugging a laptop causes at most one validated transition after the policy settles; reconnecting does not override a manual hold; desktops keep existing behavior.

### A2. Give idle and video detection separate controls

Allow idle-only, video-only, both, or neither, with separate timeouts. Keep the existing continuous-activity restoration behavior as the default, with optional instant restoration on click/key input as a later preference.

The current media detector combines foreground title/player/fullscreen evidence with audio from any allowed browser/player on the default output endpoint. Those may belong to different applications, so it can infer video incorrectly or miss audio routed elsewhere.

First correlate audio with the relevant foreground app/process family, handle output-device changes, add a short silence grace period, and explain “Video suspected” as heuristic evidence. Allow users to inspect the matched rule without retaining full window titles. Explore Windows media-session data only as a separately validated enhancement; do not claim it detects all browser video or frame rates.

**Acceptance:** background browser audio plus an unrelated fullscreen window does not qualify as video; short silence does not reset a long viewing session unnecessarily; detection failure leaves plain idle behavior available.

### C1. Add import/export and reset

Export user preferences and stable rule IDs, excluding pending recovery, window placement, logs, and temporary pause/hold state. Import into a validated draft, preview unavailable monitor/rate references, and back up current preferences before replacement. Offer section-level reset before “Reset all.” Imported startup preferences must not silently write Windows startup registration.

**Acceptance:** an exported configuration round-trips; invalid/newer schemas are rejected without changing live preferences; import cannot replay a recovery operation or silently enable Windows startup.

### F1. Keep these for a later release

- **Monitor bundles:** a named set of rates for several displays, with per-display outcomes and rollback after partial failure.
- **CLI commands:** apply a preset, pause automation, or report status by messaging the existing instance. Avoid a second process independently controlling the display.
- **Fractional refresh rates:** investigate rational timing values and driver-specific reporting before replacing the current integer-Hz model. This is separate from the existing “Show all rates” filter.
- **Usage statistics:** show time spent at each rate and switching frequency. Do not present estimated watts or battery savings as measured results without a calibrated measurement source.
- **Update checks:** only after a real release feed, signing, and update policy exist. A manual release-page link is a smaller first step.

These are lower priority than dependable switching and daily-use polish.

## 6. Proposed implementation structure

Paths below are relative to the app directory unless prefixed with `../`. Names are proposals, not files created in this task.

| Area | Existing files | Proposed additions or changes |
| --- | --- | --- |
| Validated switching | `Services/SafetyNetService.cs`, `Services/DisplayService.cs`, `Services/Abstractions.cs`, `Native/DisplayApi.cs` | `DisplayChangeCoordinator`, request/result records, recovery journal, injectable verification scheduler |
| Automation decisions | `Services/AutoSaverService.cs`, `Services/ProfileService.cs`, `Services/MediaDetector.cs`, `TrayApp.cs` | `AutomationController`, ownership state, shared commands, detector snapshots |
| Display identity/lifecycle | `Services/DisplayService.cs`, `TrayApp.cs`, `Native/DisplayApi.cs` | Display snapshots, topology reconciliation, optional display-config native wrapper |
| Preferences | `Services/SettingsStore.cs` | Ordered migrations, validator, atomic persistence, draft edits; schema fields introduced only with their feature |
| Main experience | `UI/MainForm.cs`, `UI/SaverBar.cs`, `UI/Theme.cs`, `TrayApp.cs` | Ownership header, pause/restore actions, incremental updates, live theme service |
| Editing | `UI/ProfilesForm.cs`, `UI/PresetsForm.cs`, `UI/HotkeysForm.cs`, `UI/RateChoice.cs`, `UI/SettingsForm.cs` | Stable IDs, validation, conflict handling, scalable layouts |
| Diagnostics | `Services/Log.cs` | Bounded activity store, result-aware notifications, recent-activity view |
| Verification/release | `../HzSwitcher.Tests`, `../build.ps1`, `../installer/HzSwitcher.iss`, `../README.md`, `../CHANGELOG.md` | Interaction regression tests, graceful packaging lifecycle, updated behavior documentation |

Do not add a dependency injection framework, database, background service, or web UI for this scope. Extend the existing small interfaces. Keep mode changes coordinated on the app's appropriate UI/native execution context; if discovery is moved off-thread, respect native/COM requirements and reconcile stale results before use.

## 7. Delivery sequence and review points

### Phase 1 — Correctness foundation

Implement R4 persistence/journaling foundations, then R1, R2, and R3 with regression coverage. Preserve current defaults except where existing behavior is incorrect. Review the ownership and fallback policies before UI expansion.

**Exit criteria:** one switching path; monitor-scoped recovery; coherent disable/manual/profile behavior; bounded failures; existing timing tests remain valid or are intentionally updated for documented behavior changes.

### Phase 2 — Recommended next-release experience

Implement U1, U2, U3, E1, D1, and Q1. Work through small reviewable changes: status/feedback, pause/restore, editor fixes, then theme/layout and history. Update README and changelog to match shipped behavior and replace stale planning references with a clearly historical label.

**Exit criteria:** clear rule ownership, keyboard-accessible controls, readable layouts across the validation matrix, understandable errors, and orderly shutdown/packaging.

### Phase 3 — Choose useful additions

Implement M1, A1, A2, and C1 according to the user's actual monitor and laptop workflows. Validate each separately. Do not bundle all optional features into the first polish release.

**Exit criteria:** each addition is independently configurable, backward compatible, and covered by meaningful interaction tests.

### Phase 4 — Advanced workflows

Evaluate F1 only after real usage shows a need and the switching foundation has passed hardware validation.

## 8. Validation plan for future implementation

No checks in this section were executed for this planning task.

### Automated checks

Extend the existing xUnit project and fakes rather than replacing them. Focus on observable outcomes and service interactions:

- Saver/profile/manual overlap, same-rate manual intent, active profile edits, and settings changes through different UI entry points.
- Superseded verification, failed rollback, failed journal write, restart recovery, unavailable monitor, and disconnect during an outstanding change.
- Pause expiry, sleep/resume, session lock, game entering while saver is active, and external rate changes.
- Corrupt/null settings, ordered migration, future-version preservation, interrupted save, stable preset references, and invalid import.
- Hotkey registration conflicts and restoration of the previous working set.
- Bounded retries and cooldowns using fake time; no real waits or real display switching in unit tests.

### Manual UI and hardware checks

| Scenario | Expected outcome |
| --- | --- |
| Preset/rate click, tray command, hotkey | Same validated behavior and final status |
| Small window; 100–200% DPI; mixed-DPI movement | Readable layout, reachable buttons, stable focus |
| Light/dark/high contrast; keyboard and Narrator | Visible state and meaningful accessible names |
| Idle/video drop and sustained-input restore | Configured timings, correct baseline, no repeated switching |
| Profile transitions, manual override, pause, disable | Documented priority and restore behavior |
| Secondary monitor removal, docking, resolution change | No policy or rollback redirected to another monitor |
| Lock, sleep, resume, display off/on | Deferred automation and refreshed mode information |
| Windows/driver changes rate externally | No unwanted fight with the new setting |
| HDR/VRR enabled and disabled where available | Behavior recorded on the tested hardware; no unsupported compatibility claims |
| Failed settings write or rejected mode | Accurate visible error; previous valid state retained |
| Long tray-only session and repeated editor opening | No sustained handle/memory growth or exception loop |

Use only enumerated supported modes on the user's hardware. Verify rejection/rollback edge cases with fakes before considering controlled hardware tests. Any display-changing manual checks remain future work requiring authorization; this document does not authorize running them.

## 9. Q1 — Shutdown and release quality

**Observed:** normal exit restores saver state but does not explicitly release an active profile. The safety verifier and the anonymous display-settings subscription are not disposed through the current exit path. The build script force-terminates running copies, and the installer uses forced termination during uninstall, bypassing orderly restoration.

Implementation:

1. Define a shutdown command: stop accepting automation, settle/cancel pending transactions, restore only modes still owned by the app, persist unresolved recovery, unsubscribe events, dispose timers/resources, and then exit.
2. Extend the existing single-instance messaging mechanism with a bounded graceful-exit request for packaging/update flows. On timeout, stop packaging with an actionable message rather than silently killing a running copy.
3. Review installer close/uninstall behavior to use orderly shutdown where possible and retain unresolved recovery information if forced OS/process termination occurs.
4. Build to a staging folder and promote successful outputs without deleting the previous good release first. Keep cleanup confined to verified build-output paths.
5. Run the existing unit suite, publish, and inspect portable/installer artifacts only after implementation is authorized. Check install/upgrade/uninstall startup behavior and settings preservation.
6. Retain the project version as the single source of truth. Update version/changelog only for an actual release, not for this proposal.

**Acceptance:** exiting while a profile or saver owns the rate restores the intended baseline; a user/driver-owned rate is preserved; pending verification cannot fire after disposal; packaging cannot silently bypass normal shutdown; a failed build leaves the last good artifact available.

## 10. Scope awaiting implementation approval

Recommended first scope: **Phase 1 plus Phase 2**. The most useful visible additions are **Pause automation**, **Restore now**, an ownership label, and clear switch outcomes. Optional power-source rules, per-monitor preferences, and import/export can follow after that foundation is stable.

All backlog items above are proposals. Approval to implement should identify the phase or item IDs; until then, only this planning document is delivered.
