# HzSwitcher: differentiation and implementation proposal

> Historical proposal. The user subsequently authorized implementation with “proceed” on 28 October 2026. The first milestone is being delivered as 1.8.0-beta.1; see [actual delivery and remaining work](AUTOMATION_1_8_DELIVERY.md). The original planning-only language below records the authorization boundary when this proposal was written.

Date: 28 October 2026  
Status: PROPOSAL ONLY — no application changes or execution authorized  
Reviewed baseline: [`Azrinfreeman/HzSwitcher`](https://github.com/Azrinfreeman/HzSwitcher), `main`, commit `8d202bef9d0b6b0734951ac1227db0bd0dabf413`, application version `1.7.1`.

## 1. Recommended direction

**Make HzSwitcher a display automation assistant that explains every decision, respects manual choices, and remembers which transitions work well on each monitor.**

The strongest product story is a combination of:

1. **Explainable automation:** see why a monitor is using its current rate, what would change next, and which rule is preventing a change.
2. **Independent monitor behavior:** keep the display used by a game responsive while another display follows its own configured saver policy.
3. **Monitor compatibility memory:** record verified transitions and user-reported blackouts or discomfort, then avoid problematic automatic transitions.
4. **A preview mode:** evaluate the proposed behavior before permitting it to change any display settings.

Example: “Main monitor: 240 Hz because your game is active here. Side monitor: 60 Hz because its saver rule is active. Automatic switching is paused for your recording session.”

This is a defensible product direction, not a claim that no competitor has any of these features. Validate the combination with real users and hardware before advertising it as unique.

**Recommended first delivery:** a focused 1.8 milestone with one automation coordinator, explanations, preview mode, explicit temporary overrides, and transition history. Then add monitor-specific behavior and compatibility recommendations in a 2.0 milestone. Versions are proposed, not release commitments.

## 2. Competitive context

Current official documentation was reviewed on the date above. These are documented overlaps, not fresh installations or performance comparisons.

| Existing product | Documented overlap | Consequence for HzSwitcher |
| --- | --- | --- |
| [AutoActions](https://github.com/Codectory/AutoActions) | Application start/close/focus profiles; refresh rate, resolution and HDR actions | More application presets alone are a weak differentiator. |
| [Twinkle Tray](https://twinkletray.com/) | Multiple-monitor brightness, hotkeys and idle/time-based brightness | Brightness scheduling alone is a weak differentiator. |
| [DisplayFusion](https://www.displayfusion.com/HelpGuide/Triggers/) | Application, focus, idle and timer triggers | A general trigger editor would compete with a much broader suite. |
| [PowerToys Power Display](https://learn.microsoft.com/en-us/windows/powertoys/power-display) | Monitor controls, profiles, linked brightness and reliability handling | Additional sliders or a generic compatibility list are insufficient on their own. |
| [Vantage Display Manager](https://github.com/inakizamores/vantagedisplaymanager) | Complete display profiles and advertised verification/reversion | Verified application and rollback should be treated as reliability foundations. |

Position the product around understandable, monitor-specific refresh-rate decisions and a documented feedback loop. Avoid competing on feature count.

## 3. What already exists

Source inspection established the following baseline. The app and its tests were not run for this proposal.

| Area | Existing implementation | Change needed |
| --- | --- | --- |
| Display control | `DisplayService` operates on one selected GDI device, falling back to primary when unavailable | Explicit target operations and durable monitor mapping before independent automation |
| Saver | `AutoSaverService` has a 250 ms timer and handles idle/media heuristics, restore and cooldown | Keep its behavior while extracting decisions from hardware writes |
| App profiles | `ProfileService` has its own 1-second timer, activation delay, leave delay and manual override | Coordinate priorities and ownership with the saver |
| Recovery | `SafetyNetService` verifies after 3 seconds and uses a configurable fallback | Target-specific transaction records and clearer incomplete/failed outcomes |
| Media | Foreground/title/audio heuristics, cached for 3 seconds | Label this as a heuristic; it does not establish video frame rate |
| Brightness | DDC/CI, WMI and gamma fallback; coalesced asynchronous requests | Distinguish requested/readback state and bind requests/restoration to a monitor |
| Settings | Version 2 JSON; global pending rate/brightness fields; direct file writes | Versioned migration, atomic persistence and per-monitor pending records |
| UI | WinForms pages for Display, Automation, Shortcuts and General | Add concise explanations and progressive disclosure within the current design |
| Tests | xUnit fakes for display, clock, activity and foreground; service/UI checks | Extend to decision sequences, target identity and fault recovery |

Keep .NET 10, C#, Windows Forms, the existing theme, tray workflows and always-on-top feature. An interface framework rewrite is unnecessary for this direction.

## 4. Proposed features

### A. “Why this rate?” and automation preview — first milestone

Add a compact status on the Display page and tray menu:

- “120 Hz · Balanced preset.”
- “60 Hz · Idle for 10 minutes; restore after 5 seconds of activity.”
- “240 Hz · Game protection.”
- “Waiting 18 seconds before switching again.”
- “Automation paused until 15:30 · Your manual override.”
- “Suggested 60 Hz; unchanged because preview mode is on.”

An expandable details view shows the winning rule, competing rules, target monitor, requested rate, observed rate and any waiting or failure state. User-facing status should use plain language; raw native codes belong in diagnostics.

**Preview behavior:** compute decisions from current context but suppress every automation-owned refresh-rate and brightness write, including deferred writes. Existing explicit manual controls remain available and clearly labeled. Do not let the old service timers continue writing around preview mode. Entering preview while a temporary change is active must first finish or explicitly retain that change; show the choice rather than silently restoring hardware.

Offer preview as the recommended onboarding choice for new automation rules. Existing installations retain their enabled behavior during migration.

**Acceptance:** an extended simulated context sequence produces explanations and history with zero automation hardware writes in preview; toggling the mode cannot release stale queued requests.

### B. Explicit control over temporary automation — first milestone

Add tray actions: **Pause automation for 15 minutes**, **Pause for 1 hour**, **Pause until resumed**, and **Resume automation**. A manual rate selection holds that target until the user resumes or its selected duration expires; use 15 minutes as the proposed default and allow changing it.

Add an explicit **Recording / presentation session** action. It pauses automatic changes on the selected monitors without changing their existing settings. Exiting the session re-evaluates current context rather than replaying changes queued during the pause.

Automatic OBS/streaming detection can later offer a suggestion, but a running process is not proof that recording is active. Do not advertise automatic recording detection in this milestone.

**Acceptance:** game/profile/saver events cannot override an active manual hold; the UI always exposes its scope and expiry. Lock/suspend do not consume elapsed-active-use assumptions: timed holds use an explicit wall-clock expiry, and expiry is re-evaluated on resume. A pause-until-resumed setting survives restart; a recording session is cleared on restart with a visible status explaining that it ended.

### C. Transition history and compatibility memory — staged delivery

Record bounded local history of actual transitions separately from recommendations:

- monitor reference and connection-context revision;
- previous/requested/observed mode and brightness method;
- initiating action and winning reason;
- native result, verification result, rollback result and timestamps;
- optional user feedback: “Looked fine”, “Black screen”, “Visible flicker”, or “Other problem”.

Do not log an idle timer tick as a transition. Coalesce repeated blocked recommendations. Keep the most recent 500 events and no more than 30 days; allow immediate deletion. History stays local and uses process basenames only when relevant. Never persist window titles, browser URLs or screen images. Export is explicit, redacted by default and previewable.

**First milestone:** expose recent history and a “Report a problem with this switch” action. This provides useful debugging before any recommendation logic is trusted.

**Later milestone:** repeated automatic failures or explicit negative feedback quarantine the specific directed transition for the current connection context. Show a recommendation such as “Avoid automatic 240 → 60 Hz on this display; try 120 Hz instead.” Require confirmation before changing a stored policy. Never automatically cycle through modes to discover a working one.

Compatibility states: **Not tried**, **Verified by readback**, **User confirmed**, **Problem reported**, and **Disabled for automation**. Readback cannot prove absence of flicker, image correctness or panel health. Never collapse these states into a universal “safe” label.

**Acceptance:** a quarantined transition is not retried automatically; a manual retry is explicit and retains history. Display replacement, driver/context changes or uncertain identity invalidate confidence rather than transferring approval silently.

### D. Independent monitor policies — second milestone

Allow an opted-in policy per monitor: baseline preset, idle rate, optional saver brightness, app rule and excluded/protected state. Existing users start with their current selected monitor as the only automation-enabled target.

Resolve which monitor contains the foreground app using its window bounds and largest intersection. Add hysteresis before transferring ownership when a window crosses displays. For spanning games, a profile can protect all intersected displays. If app location is unknown, protect conservatively rather than lower an uncertain game display.

Use global keyboard/mouse idle initially. Per-monitor idle cannot be inferred reliably from pointer location alone and is deferred. A side monitor may use an explicitly configured lower baseline while the game monitor is protected; do not claim independent per-monitor human-attention detection.

Show each monitor’s current rate, policy, automation state and reason. An unavailable configured monitor stays unavailable: never redirect its queued command or restoration to the primary display. Clone/mirrored or coupled display paths are detected and excluded from independent control until support is verified.

**Acceptance:** two monitors can hold different policies; moving a game transfers protection without rapid switching; unplug/reconnect cannot apply the disconnected target’s rate or brightness to another display.

### E. Local activity summary — optional follow-up

Summarize time observed at each rate, time automation was paused, and successful/failed transitions. Stop accounting across sleep or observation gaps. Make this a practical view of what automation did.

Do not convert refresh-rate reduction into claimed watts, electricity savings, battery improvement or carbon reduction. Those require separate measurements. An energy experiment could later accept external meter readings, with monitor power separated from total-PC power.

## 5. Core decision rules

Introduce one coordinator as the only owner of automation writes. Rules return proposals; they do not call the display API. Manual controls also pass through its target validation and transition path.

Evaluate each target in this order:

1. **In-flight recovery:** complete or stop an existing transition before considering a new one; an emergency restore bypasses automation cooldown but never identity validation.
2. **Unavailable/uncertain target, lock, suspend or unsupported topology:** suppress new changes and cancel stale work.
3. **Explicit pause / recording session:** leave current settings alone.
4. **Manual hold:** preserve the user’s target until expiry or resume.
5. **Game protection:** restore or maintain the configured game rate; suppress saver/profile lowering on protected targets.
6. **Explicit app profile:** apply its allowed rate after stable focus; protected games cannot be lowered by ordinary app profiles.
7. **Idle/media saver:** apply a supported configured target only after its thresholds. Media remains heuristic and has no FPS inference.
8. **Baseline:** restore according to owned state and current user intent.

This proposed priority gives app profiles precedence over the saver, unlike the current saver-first coordination. Expose “Allow saver while this app is open” as a profile option. Migrate existing profiles with that option enabled to preserve their current saver behavior; new profiles default to explicit profile precedence. Record this distinction in migration tests and release notes.

Separate the winning intent from permission to execute: preview, cooldown, debounce and compatibility restrictions can defer or block an otherwise winning target. Explanations must show both.

Preserve existing 1.5-second profile activation stability, configured leave delay, saver restore window and 30-second default automatic cooldown unless a specific validated change requires otherwise. For example, protection-driven restoration may bypass cooldown, but lowering never does. Use a monotonic elapsed-time source for debounce/cooldown and UTC timestamps for history and hold expiry.

If another program changes a rate, record external drift and yield that monitor’s automation into a visible “Review external change / Resume” hold. Do not fight Windows Settings or a second utility by repeatedly reapplying HzSwitcher’s choice. Automatic drift detection cannot reliably identify who made the change; do not attribute it to a named program without evidence.

## 6. Implementation architecture

Proposed additions are illustrative file boundaries, not requirements to create an unnecessary class for every concept.

| Component | Responsibility |
| --- | --- |
| `Native/DisplayConfigApi.cs` | CCD path enumeration, source/target identity, current rational refresh data |
| `Services/MonitorCatalog.cs` | Immutable topology snapshots, stable references, mapping confidence, capability invalidation |
| `Services/AutomationContext.cs` | Timestamped foreground, activity, media, lock and monitor snapshots |
| `Services/AutomationPolicyEngine.cs` | Pure per-target evaluation, precedence, reason codes and blocked outcomes |
| `Services/AutomationCoordinator.cs` | Scheduling, hold ownership, generation checks and serialized target writes |
| `Services/DisplayTransitionService.cs` | Preflight, durable journal, application, verification and recovery |
| `Services/TransitionHistoryStore.cs` | Bounded records, retention, deletion and redacted export |
| `Services/MonitorCompatibilityStore.cs` | Context-scoped observations, feedback and transition quarantine |
| `UI/AutomationDetailsForm.cs` | Read-only decision details, preview, holds and recent transitions |

Refactor `AutoSaverService` and `ProfileService` incrementally into context/rule providers; remove their independent hardware-write paths before activating the coordinator. Keep their existing tests as regression evidence where behavior is retained, and update assertions for explicitly documented priority changes.

Reuse `WindowEventObserver` where its lifecycle supports foreground tracking; otherwise introduce a narrow foreground event adapter. Debounce display-change notifications and perform bounded background discovery. Keep a low-frequency reconciliation fallback and cached media checks. Do not claim zero CPU or event-only operation while timers remain.

### Monitor identity and mode representation

Use `QueryDisplayConfig` and `DisplayConfigGetDeviceInfo` to associate a current GDI source with a target device path and adapter/target information. Use active paths, retry buffer-size races within a bounded limit, and gate Windows 11-only flags by supported behavior.

Treat runtime adapter/target identifiers and GDI names as mappings, not universal persistent physical identities. Device paths and connection metadata can change across docks or drivers. Identical monitors without distinguishable identity require explicit remapping; no automatic model-name-only match.

The existing `DEVMODE` rate list uses integer Hz. Do not silently add 23.976/59.94 FPS matching on top of this representation. Keep integer mode selection for the first milestone. Before exact-rate support, define a rational mode representation and validate physical versus virtual rates on DRR-capable devices. Report VRR/DRR as unknown where not established; HzSwitcher is not a replacement for GPU VRR.

### Transition and brightness ownership

Each operation captures the target reference, topology generation, current mode, intent generation and previous owned brightness. Check these again before every write and before recovery. Serialize writes; a newer intent cancels older pending requests. Native calls already in progress may not be cancellable, so discard late results and revalidate observed state.

Persist a per-target pending record atomically before applying a temporary mode. Make persistence errors observable; if durable recovery state cannot be written, decline the change. Avoid the current pattern of logging a save failure while proceeding as if recovery data exists.

Use the previous verified mode as the first recovery candidate, and a supported configured fallback only when necessary. Record each attempt. Keep the pending record when application or recovery remains uncertain. Never clear it solely because a fallback call returned; confirm readback. On restart, offer target-specific recovery after remapping; never restore an absent monitor’s state onto another display.

Brightness is asynchronous and must expose requested, observed and unavailable/error states. A combined rate/brightness intent is not an atomic hardware transaction: show partial completion, stop later steps on target change, and restore only values still owned by HzSwitcher. External brightness changes supersede the saved automatic restore value. Preserve the software dimming floor and indicate its limitations; do not add HDR/gamma changes to this scope.

Reuse the existing verification path during migration, then move it behind the transition interface. A 3-second readback is a bounded check, not proof against a later driver reset. Never allow the old safety timer and the new coordinator to issue competing recoveries.

## 7. Persistence and upgrade behavior

Propose settings schema version 3 after final schema design. Back up the existing file once before migration; write via a same-directory temporary file and atomic replacement with a recoverable backup. Validate ranges, null collections, schema versions and target references.

- Preserve hotkeys, presets, always-on-top preferences, selected monitor and current automation settings.
- New independent-monitor automation and compatibility-driven policy changes start disabled.
- Keep the legacy selected-monitor path until its mapping is confirmed; do not activate additional targets.
- Convert global pending flags only if the original target can be established. Ambiguous recovery stays pending for user review.
- Keep history/compatibility separate from the frequently saved settings file and bound their size.
- A newer unknown schema must not be overwritten with defaults. Report the incompatibility and preserve the file.

## 8. UI changes

Keep the current visual style and four main pages for the first milestone.

**Display:** current Hz, selected monitor and existing presets; add one reason line, hold indicator, and **Why?** action.

**Automation:** a clear mode choice (**Active**, **Preview**, **Paused**), rule summaries, app profiles and recording-session controls. Detailed diagnostics stay in the expandable/form view.

**General:** history retention/deletion and explicit redacted export. Keep always-on-top configuration here without expanding it.

**Tray:** current target/status, pause/resume, recording session, and recent-switch details. Reuse current hotkey registration conflict feedback for new optional actions; do not reserve new shortcuts automatically.

For independent monitors, extend the Display page with compact monitor cards only after the targeting model is complete. Verify keyboard navigation, accessible names, 100/150/200% DPI and a small laptop viewport. Status cannot depend on color alone.

## 9. Delivery sequence and exit criteria

All phases below are future work and remain unstarted.

### Phase 0 — characterization and design confirmation

Inventory every rate/brightness write and restoration path. Record current regression results on Windows after implementation is authorized. Confirm transition ownership, migration fixtures and the behavior changes listed above. No new native mode-setting mechanism is required yet.

**Exit:** a complete write-path map, reproducible baseline, and an agreed migration contract. If the baseline fails, document and resolve relevant failures before claiming a regression-free update.

### Phase 1 — first milestone / proposed 1.8

Extract the pure decision engine and single coordinator for the currently selected monitor. Add preview, reasons, manual holds, recording-session pause, external-drift yielding and bounded transition history. Retain the existing mode-setting backend behind explicit target checks.

**Exit:** one automation writer, preview produces zero automatic writes, manual choices are respected, failed transitions remain visible, and existing workflows pass targeted regression checks.

### Phase 2 — monitor identity and independent policies / proposed 2.0 beta

Implement the monitor catalog, topology generations, explicit target operations and target-specific recovery. Introduce per-monitor policies and app-location protection. Skip unsupported clone/coupled setups with an explanation.

**Exit:** test on a laptop panel plus an external display and on a mixed-refresh desktop pair. Unplug, dock, primary-display change, lock, suspend and resume must not redirect writes or restores.

### Phase 3 — compatibility feedback / proposed 2.0

Add transition feedback, context-scoped quarantine, user-confirmed policy recommendations and readable monitor reports. Provide a guided single-transition check only as an explicit action, with a known restore target and clear limitations. Do not run an automatic mode sweep.

**Exit:** problematic directed transitions stop recurring automatically, resets invalidate stale confidence, and user feedback never masquerades as instrument-measured blackout duration.

### Phase 4 — optional follow-up

Add the local activity summary and refine scheduling based on measured CPU/wakeups. Reconsider cinema/FPS matching only after exact mode representation and a dependable, explicitly supported player integration are established.

## 10. Verification plan

These are proposed checks, not recorded passing results. Do not run them as part of this planning-only request.

**Decision and sequence tests:** precedence combinations; migration-preserved saver behavior; focus flutter; manual hold expiry; preview transitions; cooldown; game protection; external drift; late events; cancelled intents. Drive fake time and fake context; no physical display writes in unit tests.

**Recovery and identity tests:** unsupported rate, native rejection, delayed wrong readback, process restart with pending records, failed settings writes, failed rollback, target removal during verification, ambiguous identical monitors, display-index reassignment, topology changes and partial brightness success.

**Persistence tests:** v2 and unversioned fixtures; null/malformed/out-of-range input; interrupted atomic save; unknown newer version; bounded history; deletion; export redaction. Verify existing unrelated preferences survive migration.

**Windows hardware acceptance:** selected monitor, two independent mixed-Hz displays, laptop/dock, brightness DDC/CI and WMI, software fallback, a borderless game, configured app, media playback, recording-session pause, lock/unlock, sleep/resume, unplug/replug and external changes in Windows Settings. Test mirrored, HDR, VRR and DRR configurations as supported/blocked/unknown explicitly; never imply support solely from a unit test.

**Performance targets to measure:** under 0.5% average total-system CPU over ten minutes of steady desktop idle on a documented reference PC; no added 250 ms hardware enumeration loop; no unbounded growth after 100 topology/focus cycles; no automatic switch inside the configured cooldown except documented protection/recovery restores. These are acceptance targets, not current performance claims.

Future commands, after implementation approval:

```powershell
dotnet build HzSwitcher.csproj -c Release
dotnet test tests/HzSwitcher.Tests/HzSwitcher.Tests.csproj -c Release
```

Report hardware results separately from synthetic tests, including OS, GPU/driver, connection, monitor model, configuration and observed limitations. Do not claim measured power savings without measurements.

## 11. Features deliberately deferred

- Generic HDR/resolution/layout management: broad competitor territory and additional recovery complexity.
- Automatic FPS detection from window titles, audio or media playback state: insufficient evidence of actual frame rate.
- Browser extension, process injection or screen capture to classify content: disproportionate scope for this product direction.
- AI/cloud suggestions: deterministic local policies and explicit feedback are sufficient for the proposed experience.
- More always-on-top features, RGB controls or unrelated desktop utilities: they dilute the display automation story.
- Electricity-savings percentages and predicted battery life: require reliable measurements.

## 12. Portfolio demonstration

Build a 60–90 second demonstration after the relevant phases are complete:

1. Preview a rule and show the exact explanation without changing hardware.
2. Activate it, then show the actual rate and verification outcome.
3. Hold a manual choice and show automation yielding.
4. Demonstrate different policies on two monitors and a game protecting its own display.
5. Report a problematic transition and show the resulting explicit recommendation/quarantine.

Label simulated failures as simulated. Use a physical display/phone recording if demonstrating visible blackout behavior; a desktop capture may not show the monitor losing signal. Publish a short case study covering the original competing service timers, the new decision model, recovery boundaries and measured validation.

Suggested positioning once delivered: **“HzSwitcher gives each monitor a clear policy, explains automatic changes, and remembers transitions you prefer to avoid.”**

## 13. Source references

Repository references are pinned to the inspected commit so future implementation can distinguish the baseline from later changes:

- [Application wiring and manual controls](https://github.com/Azrinfreeman/HzSwitcher/blob/8d202bef9d0b6b0734951ac1227db0bd0dabf413/TrayApp.cs)
- [Saver state machine](https://github.com/Azrinfreeman/HzSwitcher/blob/8d202bef9d0b6b0734951ac1227db0bd0dabf413/Services/AutoSaverService.cs)
- [Application profile behavior](https://github.com/Azrinfreeman/HzSwitcher/blob/8d202bef9d0b6b0734951ac1227db0bd0dabf413/Services/ProfileService.cs)
- [Selected-monitor display operations](https://github.com/Azrinfreeman/HzSwitcher/blob/8d202bef9d0b6b0734951ac1227db0bd0dabf413/Services/DisplayService.cs)
- [Verification and recovery](https://github.com/Azrinfreeman/HzSwitcher/blob/8d202bef9d0b6b0734951ac1227db0bd0dabf413/Services/SafetyNetService.cs)
- [Settings and persistence](https://github.com/Azrinfreeman/HzSwitcher/blob/8d202bef9d0b6b0734951ac1227db0bd0dabf413/Services/SettingsStore.cs)
- [Media heuristic](https://github.com/Azrinfreeman/HzSwitcher/blob/8d202bef9d0b6b0734951ac1227db0bd0dabf413/Services/MediaDetector.cs)
- [Microsoft: QueryDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig)
- [Microsoft: target display identity fields](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name)

## 14. Authorization boundary

This document is the only deliverable authorized by the current request. It does not authorize source changes, builds, tests, installation, display changes, commits, pushes or publication. No implementation phase has begun. Review the proposed direction and scope before authorizing implementation.
