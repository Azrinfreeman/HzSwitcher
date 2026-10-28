# HzSwitcher - 57-second trailer

Deliverable: HzSwitcher-Trailer-57s.mp4
1920 x 1080, 60 fps, H.264 + stereo AAC; English synthesized narration and burned-in English captions.

The trailer uses real production UI renders with clearly labelled simulated device states. This avoids changing physical display settings. The idle sequence is labelled Time compressed. No live-game compatibility or power benchmark is implied.

## Edit and rerender

Requires Python 3, Pillow, imageio-ffmpeg and Windows Segoe UI fonts. The original voice takes and synthesized music are included; rerendering does not require an online service.

1. Edit source/timeline.json and source/render.py for timing, copy and motion. If replacing narration, update the matching audio/voice-N.wav and timeline text together.
2. Run python source/render.py to produce video-silent.mp4 and the matching SRT.
3. Run python source/mix.py to mix the voice takes and original music.
4. Run python source/finish.py to mux, validate and write QA output.

Optional regeneration:
- source/music.py composes the original music bed.
- source/narrate.ps1 regenerates the local Windows Zira narration.
- source/capture.py regenerates UI snapshots from the HzSwitcher checkout using Capture.cs.txt. It builds an isolated temporary harness with fake display/services; it does not launch TrayApp or modify real monitor settings. The existing assets are sufficient to rerender without this step.

The timeline is exactly 57 seconds: 4 / 7 / 7 / 8 / 7 / 8 / 8 / 8. Video must contain exactly 3,420 frames. The closing URL is held throughout the closing scene; the principal end card settles for the last four seconds.

See ASSET_RECORD.md for provenance and qa/verification.json for measured output checks.
