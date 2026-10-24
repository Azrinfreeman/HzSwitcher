# Reducing unnecessary display flashes

HzSwitcher still changes the real display refresh rate. A necessary mode change can briefly blank the screen while the monitor/driver resynchronizes; these changes do not promise seamless switching or enable Windows Dynamic Refresh Rate.

Implemented behavior:

- Requests for the already-active rate skip the native mode change, including fallback paths.
- A rejected request keeps the previous rate when it is still active, instead of switching to the configured fallback unnecessarily.
- New requests replace older delayed verification. Changing the selected monitor cancels verification for the previous selection; verification also checks the actual device identity before falling back.
- Saver changes use the same safety service as manual and profile changes, preventing an older verification target from undoing a newer saver change.
- Profiles and saver share the existing `MinSwitchSeconds` cooldown (30 seconds by default). During that interval, only the latest stable foreground profile is considered; intermediate candidates do not cause mode changes.
- Automatic profile restoration waits for both the profile leave delay and the cooldown. Manually disabling profiles restores immediately. Manual refresh-rate commands and saver restoration remain immediate (saver restoration still follows the existing activity delay).
- Advanced settings exposes **Automatic switch cooldown (seconds)**, from 0 to 300. Increase it to switch less often, or set 0 to retain only the existing foreground/leave delays. The cooldown delays automatic choices after manual choices too.
- Returning to the currently active profile resets a pending candidate's foreground delay, and profiles pause while the workstation is locked.
- Explicitly choosing the current profile rate preserves that manual choice when the profile ends, instead of producing an unwanted restore switch. A failed profile request keeps the previous profile state and waits for the cooldown before retrying.

No default refresh rates, idle timeout, activity restoration delay, or Windows startup preferences were changed. The running app and physical display were not used for validation; verification uses fake display/input/clock services and a build.

Validation completed: all 81 unit tests passed, including 17 new regression tests. A self-contained Windows x64 executable was published successfully to `artifacts/flicker-reduction/HzSwitcher.exe`. The existing running copy was not restarted or replaced. Exit the existing tray app before starting this executable; the single-instance behavior otherwise reopens the old running copy.
