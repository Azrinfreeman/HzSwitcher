# HzSwitcher: 57-second trailer implementation

Status: produced after approval. Delivered locally as artifacts/trailer/HzSwitcher-Trailer-57s.mp4, with captions, source bundle and QA records. Product scenes use real production UI rendered with simulated display/service state and are visibly labelled UI DEMO; no personal settings or monitor hardware were changed. Narration uses the installed Microsoft Zira Desktop synthesized voice. No public upload, app installation, commit or push was performed.

## Creative direction

**Concept:** Your screen. Your rules.

A polished product teaser that also shows how to use the app. Sell the convenience of controlling refresh rate, brightness and window priority from one small tray app. Lead with refresh-rate control; reveal automation and Always on top as supporting benefits.

Audience: Windows users with high-refresh monitors, gamers, laptop users and people who switch between work and entertainment.

Style: dark charcoal backgrounds, the app's blue/accent palette, crisp typography, large UI close-ups and deliberate cursor movements. Restrained motion graphics and quick cuts give it trailer energy without hiding the product. English narration with burned-in captions; useful with sound off.

**Runtime:** exactly 57.000 seconds, including the closing card. No extra intro or outro.

## Locked timeline and script

| Time | Duration | Visual and action | Main on-screen copy | Voiceover |
| --- | --- | --- | --- | --- |
| 00:00-00:04 | 4 s | Open on an original abstract display pulse. A cursor approaches the HzSwitcher tray icon; cut to the product name on the beat. | Your screen. Your rules. | "Your screen. Your rules." |
| 00:04-00:11 | 7 s | Show the real tray menu, then Display. Select a supported refresh-rate chip and hold on the updated rate. Crop closely enough to read the control. | Switch refresh rates in a click. | "Meet HzSwitcher. Change your refresh rate in a click, right from your Windows tray." |
| 00:11-00:18 | 7 s | Show Gaming, Balanced and Battery saver presets. Select one, then show a clean Ctrl+Alt+3 key overlay and the resulting Gaming/highest-supported selection. | Pick a preset. Or press a shortcut. | "Choose a preset for gaming, work, or winding down. Or switch instantly with a shortcut." |
| 00:18-00:26 | 8 s | Open Automation and App profiles. Show an already-prepared application-to-rate rule, switch to that app, then show the profile's active status. Keep only the relevant windows visible. | The right rate for each app. | "Set a refresh rate for each app, and let your screen adapt as you switch tasks." |
| 00:26-00:33 | 7 s | Return to the tray brightness slider. Move it gently once. A brief monitor-selector close-up communicates which display is being controlled. | Brightness, within reach. | "Adjust brightness without digging through menus. Choose the display you want to control." |
| 00:33-00:41 | 8 s | Show the idle power-saver setting, then a saver-active shot and a return-to-active shot. Use a visible "Time compressed" annotation across the shortened wait/restoration sequence. | Step away. Let it ease down. | "Step away, and the automatic power saver lowers the refresh rate. Return, and your previous setting comes back." |
| 00:41-00:49 | 8 s | General: enable Always on top. Cut to a standard app title-bar right-click; select Always on top. Hold on the accent outline while another window moves behind the pinned window. | Keep what matters on top. | "Keep what matters in view. Enable Always on top, then right-click a window's title bar to pin it." |
| 00:49-00:57 | 8 s | Pull the UI into a balanced hero composition. Show the app icon and name, then a static closing card for the final four seconds. | HzSwitcher / Your screen. Your rules. / Get HzSwitcher on GitHub / github.com/Azrinfreeman/HzSwitcher / Windows 10 & 11 · x64 | "HzSwitcher. Your screen. Your rules. Get it on GitHub." |

The eight durations total **57 seconds**. Record the narration to the timeline, leaving short pauses between benefits. If a line runs long, shorten its wording rather than speeding the voice unnaturally or extending the video.

## Footage and demonstration rules

- Use the actual HzSwitcher UI and actual behavior, with a clean desktop and neutral sample windows. Use the supplied app icon and existing visual identity.
- Record the shortest understandable path: tray icon to rate chip; preset or shortcut to result; profile to active status; title-bar menu to pin outline. Hold each result long enough to read it.
- Capture from a verified current source build so the native-menu fix and gameplay guard are included. The existing 1.7.2 installer predates the gameplay guard; do not represent that installer as containing the new guard. Match the downloadable release to the trailer before public publication.
- Use supported refresh rates on the capture monitor. If the suggested 120 Hz preset is unavailable, choose a supported rate; never fabricate capability or show unsupported changes as successful.
- Do not run a downloaded installer, publish a release, push files or alter the user's startup configuration as part of production. Use a separate demo settings location or disposable Windows profile to preserve personal settings.
- Capture refresh-rate changes only on a suitable test/secondary display when available. If recording the actual change would disrupt the working display, prepare the edit with clearly identified demonstration material and resolve the capture setup before performing changes.
- Screen-rate changes can blank a monitor briefly. Cut around capture interruptions without implying a measured performance improvement.
- Show brightness only on a display with a verified control method. If the capture uses software dimming, retain its mode label and do not imply it changes the hardware backlight.
- Use neutral desktop activity instead of third-party game footage. No private messages, account names, paths, notifications or personal desktop content in frame.
- Do not use PowerToys branding or imply affiliation. Present Always on top as a HzSwitcher feature.

## Claims and microcopy

Allowed: one-click rate selection, presets, configurable shortcuts, app profiles, brightness control, automatic idle rate reduction, monitor selection and pinned windows.

Avoid: higher FPS, zero latency, measured battery-life improvements, universal monitor compatibility, guaranteed power savings or immunity to game-input conflicts. These have not been established by a trailer recording. A 60 fps export also cannot prove how a 120/144/240 Hz display feels.

Keep the saver demonstration honest: restoration follows configured sustained activity, rather than necessarily the first mouse movement. Show the compressed-time label rather than implying the whole automatic sequence happened in eight real seconds.

Use one short headline at a time. Captions carry narration; do not cover the clicked control or repeat the same large headline as another helper paragraph. Keep the final URL readable for at least four seconds. No price or "free forever" claim.

## Audio and motion

- Narration: confident, friendly English voice at a natural pace. Produce an original recording or use an available, appropriately licensed voice tool after approval. Check voice-tool availability first; do not silently substitute unreadable rapid speech or claim narration exists before it is generated.
- Music: a light electronic pulse with a small lift at the product reveal and final card. Use original or licensed music and retain its license/source. No commercial songs or borrowed trailer audio.
- Effects: sparse clicks and soft transition sounds. Use the app's own original pin cue for the pin moment; duck the music under narration.
- Target approximately -14 LUFS integrated, true peak at or below -1 dBTP. Avoid loud transients and abrupt audio endings.
- Use short position/scale eases and occasional match cuts. No flashing, exaggerated shaking or constant zooming. Product readability takes priority over animation.

## Production workflow after approval

1. Check local capture, narration and rendering tools; reuse installed tools. Inspect the icon and prepare the clean capture setup. Confirm any missing capability before promising a final format or installing tools.
2. Prepare reversible demo settings and supported monitor values. Verify the feature paths used in the script, including actual pinning and profile activation.
3. Record clean feature clips, allowing handles before and after each action. Capture saver states with real settings and retain the time-compression annotation in the edit.
4. Create the intro, key overlay, headlines, captions and end card. Prefer code/vector motion graphics for typography and UI framing; no generated raster artwork is required for this concept.
5. Record narration and assemble licensed/original audio. Place all eight scenes on the exact timeline and mix against the voice.
6. Render an initial MP4 using the available editor/renderer (FFmpeg where suitable), inspect representative frames and play the entire export. Correct timing, legibility, audio and capture issues before delivery.
7. Deliver the finished 57-second trailer and supporting files locally. Public upload, a GitHub release, repository commits and installation require a separate request.

## Deliverables

Suggested local folder: `artifacts/trailer/`.

- `HzSwitcher-Trailer-57s.mp4`: 1920 x 1080, 60 fps, H.264, AAC 48 kHz stereo. Exactly **3,420 video frames**; audio aligned to the same 57-second endpoint.
- `HzSwitcher-Trailer-57s.srt`: captions matching the final narration.
- Editable timeline/composition source and referenced local assets sufficient to rerender the trailer.
- Short asset/license record covering music, voice, fonts and any externally sourced material.

The approved scope is one horizontal trailer. Vertical/social variations, extra thumbnails and alternate language versions can follow separately.

## Acceptance checks

- Verify duration with a media probe: 57.000 seconds at the video-stream level and 3,420 frames at constant 60 fps. Inspect container/audio timing too, allowing only codec padding rather than an extra visible scene or audio tail.
- Watch the complete exported file with audio and once muted. Check opening impact, readable captions, narration sync, transitions, clean ending and the four-second URL hold.
- Check full-size and phone-sized playback. Cursor actions and resulting UI states must remain recognizable.
- Confirm claims match the captured build; no staged animation is presented as a benchmark or live functionality.
- Check private data, desktop notifications, unsupported rate values, sound clipping, encoding glitches and missing asset licenses.
- Restore any demo settings/display values changed during approved capture. Do not claim game compatibility or manual gameplay validation from this trailer.


## Production verification

Final video: 1920 x 1080 H.264, constant 60 fps, exactly 3,420 frames and 57.000 seconds presentation duration. Full file decode passed. Scene keyframes/contact sheet were inspected. Audio measurements and codec timing are recorded in artifacts/trailer/qa/verification.json. AAC media timing includes normal encoder priming; the MP4 presentation remains exactly 57 seconds. The soundtrack and transitions are original synthesis; captions are burned in and also supplied as SRT.
