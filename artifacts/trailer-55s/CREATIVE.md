# HzSwitcher — Every hertz. On your terms.

## Brief and execution decisions

Mode: **custom autonomous**, selected by the user. The creative director owns storyboard, music, visual choices and production through delivery. The requested outputs are 55 seconds, exactly 1,650 frames at 30 fps, H.264 MP4 at 1920×1080 and 1080×1920, plus editable Remotion source. Local output directory: `artifacts/trailer-55s/`. The existing 57-second trailer is retained.

Audience: Windows users who choose refresh rates for work, video and games. Core promise: clear, accessible control of a selected display, with optional automation. No FPS, latency, compatibility, battery-life or energy benchmarks. Newly implemented pause/diagnostics are source-build features; footage is not represented as the installed release.

| Requirement | Execution |
|---|---|
| Apple-inspired presentation | One focal object, dimensional framing, precise typography and deliberately quiet holds. Keep the product's Windows identity. |
| Real product imagery | Render unchanged production WinForms views with deterministic demo service adapters; no recreated HTML UI. Include `UI DEMO · simulated device state` during product scenes. |
| Data safety | Fictional monitor labels and application names, isolated settings/log directory, no personal desktop capture or real display/brightness writes. |
| Both formats | Recompose titles and camera crops for portrait. Do not crop the landscape export. |
| Sound | Original 120 BPM electronic score composed to the edit, sparse licensed physical clicks and cinematic sweeps. No voiceover. Readable headlines work with audio off. |
| Editing | Frame-driven React, centralized tokens and scene data, `bgm` input prop, separate music and SFX stems, workbench manifest, local media and license record. |

## Visual direction

Considered: (1) charcoal instrument, precise white typography and native blue; (2) white gallery, which weakens continuity with the dark utility; (3) saturated neon, which makes a practical tool feel like speculative gaming hardware. Selected **charcoal instrument**.

Brand evidence: `UI/Theme.cs`, `UI/MainForm.cs`, `Assets/app.ico`. Background #202124; surface #292B2F; raised #35383D; text #F1F3F4; secondary #B5BAC1; blue fallback #4F8CFF; saver green #72D491. Capture records the actual accent. Use Segoe UI Variable/Segoe UI, medium-to-semibold headings, negative tracking, generous spacing, original icon. Stage may deepen charcoal to #111214 for separation; this is a restrained extension, not a replacement palette.

1920 landscape safe inset 96; 1080 portrait safe inset 72 and 170 px top/bottom allowance. Main headlines 88–112 px landscape, 94–112 px portrait; secondary/CTA ≥40 px. Product labels that must be read are enlarged with camera crops. Incidental form text is background texture, never the sole explanation. Rounded screenshot framing 18–24 px; controlled shadows, one broad soft light, no grids, particles, glow floods, random shake or flashing.

Motion tokens: measured, exact, tactile. Camera bezier(0.2,0,0.25,1) for the opening; guided cursor bezier(0.55,0,0.18,1); quint-out type reveals, 4-frame word staggers, 22–30-frame reveal windows. Camera moves settle before reading. Static brand holds ≥30f and final CTA ≥60f. Blur only in moving typography/brief fast camera phases, zero at rest. All values are pure functions of frame. No per-beat camera pumping.

## Gallery mapping and adaptations

Scanned all shot-card frontmatter and validated selected names/styles against `gallery/api/library.json`. Exact source/demo/reference paths are archived in `project/references/selection.json`.

| Feature / purpose | Shot recipe | Deliberate adaptation |
|---|---|---|
| Detail → product reveal | crane-rise-reveal · crane-rise-reveal | Keep 24f detail hold + 102f linked focal-point/scale move + ≥30f settle. Use real rate-control texture; remove fictitious queue/KPI and row washes. |
| Rate selection | cursor-flyover · cursor-flyover | One adjacent two-state rate interaction rather than four unrelated modules. Keep cursor lead, 20f eased camera arrival, click → response +2f and ≥24f readable hold. Re-measure targets per format. |
| Opening headline | blur-slide · blur-slide | Keep shared opacity/Y/blur progress and 4f word stagger. Restyle to native sans and blue; no paper, serif or red. |
| Final identity | logo-shrink-wordmark-lockup · logo-shrink-wordmark-lockup | Keep 5.4→1 contraction, 6% brake, icon yield/wordmark stagger ratios. Replace neon ring with the actual app icon silhouette; omit glow, keep logo upright, append a long readable CTA hold. |
| Other features | Custom PageCam/table-driven crops and match cuts | Original motions chosen for each feature: shortcut key press, horizontal profile reveal, brightness drag, time-compressed saver state, pause freeze, pin depth layering, diagnostic inspection. No claim of exact Gallery replication for these. |

Product capture and styleframe precede scene coding. The final standalone compositions and browser preview are inspected before complete exports. The selected autonomous workflow authorizes continuous production; no repeated creative approval checkpoints.

## Storyboard and frame timeline

Beat grid: 120 BPM, 15 frames per quarter-note. Scene boundaries are exact beats. One idea per scene; scene timing is generated from this table in `src/timeline.ts`.

| # | Time | Frames | Picture / actual state | Main copy | Motion / transition / sound |
|---|---|---|---|---|---|
| 1 | 0–6s | 0–179 | Refresh-rate macro opens to the real Display window; product identified by 4s | Every hertz. / On your terms. | Crane reveal, word-level focus; low opening pulse and reveal sweep |
| 2 | 6–11s | 180–329 | Actual rate controls: 240 Hz → 120 Hz | Change your refresh rate. | Cursor leads camera, click and state change; physical click, clean cut |
| 3 | 11–15s | 330–449 | Preset controls, then genuine shortcut settings with large Ctrl+Alt+2 overlay | A preset. / A shortcut. | Keycap depression and matched result; two tactile taps |
| 4 | 15–20s | 450–599 | Production profile editor with fictional `notepad` → 120 Hz, then active profile state | A rate for each app. | Lateral editor-to-result reveal; soft sweep |
| 5 | 20–25s | 600–749 | Real brightness slider in intermediate captured states, 70% → 40% | Brightness, within reach. | Single measured drag; lighting stays steady, small click |
| 6 | 25–31s | 750–929 | Automation settings → saver 60 Hz → restored 240 Hz | Step away. / Let it ease down. | Controlled time-compressed sequence, explicit annotation; restrained low percussion |
| 7 | 31–35s | 930–1049 | Actual Automation page paused, `Resume` visible | Pause on your terms. | Motion decelerates into a stable hold; dry mechanical stop |
| 8 | 35–40s | 1050–1199 | General pin controls + neutral notes window in a staged desktop composition | Keep what matters on top. | Depth relationship shown by another neutral surface passing behind the accented note; no third-party content |
| 9 | 40–45s | 1200–1349 | Actual Diagnostics dialog with sanitized display/brightness/automation facts | See what's happening. | Front-facing inspection crop, clear pause/readout; light inspection click |
| 10 | 45–49s | 1350–1469 | Display surface and extracted controls gather into a product portrait | Small app. / More control. | Distinct restrained assembly with ≥15f final rest; score lift |
| 11 | 49–55s | 1470–1649 | Original icon + HzSwitcher wordmark and GitHub CTA | HzSwitcher / Built for your Windows display. | Icon contracts to wordmark lockup, 6% brake; CTA holds, musical cadence and clean audio fade |

## Acceptance and intentional rule deviations

Check every scene at entry, action, settled state and both sides of every seam in both aspect ratios. Validate actual UI source, simulated-state disclosure, no private data, no invented features, clear feature names, ≥1s brand hold, sharp large labels, no text collisions, frame-deterministic motion, no black/empty frames. Probe exports for 55s/1650f/30fps, correct resolution, H.264/yuv420p and stereo AAC 48kHz. Measure loudness/true peak and verify audio/cut synchronization from decoded exports. Independently review before delivery.

Shotcraft Q8's particle-heavy highest-energy finale is deliberately softened to respect the user's controlled Apple-inspired direction; contrast, assembly and music provide the lift. C3's serif/highlighter treatment is replaced by the native sans/blue identity. C1 narration is supplied through short visual headlines; there is no synthetic voiceover. Diagnostic details are not claimed as benchmarks. BGM and SFX-only versions share identical video frames.
