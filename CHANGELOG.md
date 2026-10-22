# Changelog

These entries describe local development versions. They are not announcements of published GitHub releases.

## 0.4.3 - 2026-10-22

- Re-selecting a setup whose monitor settings already match Windows now skips native display validation, mode application and DPI changes. Linked audio still applies, and confirmation and audio rollback remain available.
- This avoids unnecessary display reconfiguration; a fix for reported monitor wake timeouts has not yet been confirmed.

## 0.4.2 - 2026-10-22

- Keep Home Assistant setup, device and favorite names selectable when they use any letter case of the reserved state "none".
- Refresh public demo screenshots, align release instructions with the tested draft workflow, and add automated dependency and secret checks with pinned workflow actions.

## 0.4.1 - 2026-10-22

- Identify detected audio devices already referenced by saved favorites, including communications roles. Show the saved names with a star and open the existing favorite for editing instead of offering to create a duplicate. Keep Windows default-device status independent.

## 0.4.0 - 2026-10-22

- Add in-app updates from public stable GitHub Releases under Settings, with optional startup checks at most once daily, manual checking, download progress, and explicit installation.
- Verify HTTPS download sources, ZIP and file SHA-256 checksums, package contents, Windows x64 architecture, product and version before replacing the portable package. Block installation during switches or monitor confirmation; keep existing profiles and credentials.
- Use a temporary copy of the current EXE to replace files after exit, check the new app's initial interface/state response, and restore the previous files if startup fails. No permanent update service, installer or GitHub credentials are required.
- Add a version-tag workflow that builds/tests the standard package and prepares a draft GitHub Release for manual review and publication.

## 0.3.3 - 2026-10-22

- Keep monitor setups active in the tray and Home Assistant when audio is switched independently. Active monitor setups follow their actual display layout, while audio favorites and audio-only setups continue to follow the actual audio state.
- Activating a setup still applies its linked audio settings. Separately changing audio does not rewrite the setup or its favorite, and changes to the actual display layout still remove the active mark.

## 0.3.2 - 2026-10-22

- Replace the monitor editor's Hz number field with refresh-rate presets from Windows' reported modes, filtered by the selected resolution and orientation. Mark the current rate and keep optional manual entry when needed.
- Preserve existing exact rates when opening or saving a setup, including near-integer rates and values missing from the current mode list. Editing the selection changes the setup draft; activation still uses the existing monitor test and confirmation.

## 0.3.1 - 2026-10-21

- Add independent Home Assistant selections for available audio output devices, microphones, and audio favorites. Direct device selection does not require a favorite and preserves volume, mute, communications assignments, and the other audio direction.
- Report actual default audio devices and matching favorites, update options when devices connect or disconnect, and use the existing audio-only transaction/recovery path without applying monitor layouts. Setup and audio commands share the same concurrency guard and reject retained startup commands.

## 0.3.0 - 2026-10-21

- Add optional Home Assistant integration over MQTT with automatic discovery, setup selection, actual matching setup state, switch status, and error reporting. Configure the broker under Settings; the feature is disabled by default.
- Store MQTT credentials separately from exported profiles and protect the password for the current Windows user. Support authenticated connections, optional TLS, offline availability, Home Assistant restarts, and bounded reconnect delays.
- Accept remote activation only for confirmed monitor layouts or audio-only setups, through the existing transaction and recovery path. Ignore retained commands on subscription and reject unknown or concurrent commands; reconnecting never applies a setup.

## 0.2.11 - 2026-10-20

- Give the audio mixer its own navigation entry below Audio. Keep favorites and detected devices on the Audio page, and open the separate mixer directly from its tray action, shortcut and quick-mixer link.
- Reset the main view to the top when changing pages. Mixer polling runs only in a visible mixer view.

## 0.2.10 - 2026-10-20

- Select detected output devices and microphones directly in a setup without creating an audio favorite. Add an optional display name or save the selection as an independent favorite.
- Add a Favorisieren action to recognized audio devices. Existing linked favorites, shortcuts and imported profiles remain supported.

## 0.2.9 - 2026-10-19

- Read actual desktop DPI with a temporary invisible per-monitor-aware window instead of legacy resource scale factors that can report140% for a175% display. Destroy the window immediately and restore the calling thread's DPI context.

## 0.2.8 - 2026-10-19

- Retry display/DPI readback briefly after a monitor switch before rejecting a transient mismatch. Permanent differences still fail and trigger recovery, with exact requested/actual values in the error.
- Preserve the original switch error through recovery and keep it visible after restoring the previous setup.

## 0.2.7 - 2026-10-18

- Style mixer volume ranges independently of text-field padding, with consistent empty/full tracks in dark and light themes.
- Defer the tray double-click window action until NotifyIcon finishes processing its event; cancel pending single clicks on mouse-down and suppress trailing double-click releases.

## 0.2.6 - 2026-10-18

- Open the quick mixer in a separate compact tray window. Reuse the existing browser surface and preserve main-window drafts.
- Dismiss the quick mixer on Escape, close, outside activation, or opening the main window. Keep monitor confirmations in the main window.

## 0.2.5 - 2026-10-18

- Remove the louder/quieter actions from the tray menu. Configured volume shortcuts and optional tray-wheel control remain available.

## 0.2.4 - 2026-10-18

- Validate the requested DPI scale against Windows' reported range instead of rejecting a stale current value or a wider maximum range.
- Clearly distinguish applying a monitor setup from confirming its result; ignore Enter while application is still busy and renew the confirmation deadline after application completes.
- Identify the affected audio favorite and device role when a saved device is missing, with guidance to check its binding or connection.

## 0.2.3 - 2026-10-18

- Apply and restore live monitor heights using the current display paths and timing data. Change only Y positions and bypass the full setup/DPI update that could abort alignment.
- Keep monitors excluded from the edited setup locked during alignment; label the setup's active/inactive selection explicitly.
- Keep alignment errors visible in the setup editor after an aborted session, preserving the draft.

## 0.2.2 - 2026-10-18

- Distinguish tray single-click and double-click: a single left click opens the quick mixer after the Windows double-click interval; a double left click opens the main window and cancels the mixer action. Opening the main window also dismisses an already open quick mixer.

## 0.2.1 - 2026-10-17

- Open the settings page correctly through its global shortcut.
- Block mixer controls immediately during monitor changes while allowing continuous volume adjustment.
- Combine full device refreshes after rapid tray-wheel or volume-shortcut changes to reduce repeated work.
- Show physical Bluetooth adapters and their Windows device status in Settings, with manual refresh. Adapter switching is not included.

## 0.2.0 - 2026-10-17

- Keep save errors visible inside the editor and preserve drafts for retry. Explain Windows hotkey registration failures accurately, including conflicts with desktop shortcuts.
- Add device/app volume, mute, level meters, grouped app sessions, and per-app output routing with a return to the Windows default.
- Add a tray quick mixer, optional tray-wheel volume control, configurable mixer/settings/volume shortcuts, and an optional logarithmic slider scale.
- Pause mixer polling outside the audio view and while hidden; serialize writes and reject stale reads. Keep mixer actions blocked during pending monitor changes.
- Allow atomic transaction replacement during reads and retry transient sharing conflicts instead of terminating the recovery watchdog.
- Credit EarTrumpet's MIT-licensed routing reference; no EarTrumpet executable or installation is required.

## 0.1.8 - 2026-10-17

- Move live height alignment exclusively into the setup editor; remove the duplicate Workspace button.

## 0.1.7 — 2026-10-17

- Open live monitor height alignment directly from the setup editor.
- Copy confirmed heights into the setup draft while preserving other edits; cancel and timeout return to the unchanged draft.

## 0.1.6 — 2026-10-17

- Live vertical monitor alignment with Y sliders, numeric positions, and one-pixel adjustments.
- Preserve the original layout across repeated live changes; cancel or revert automatically after 90 seconds without a change or confirmation.
- Keep audio, resolution, scaling, refresh rate, and horizontal positions unchanged during alignment.
- Recheck the latest transaction deadline under the watchdog lock before an automatic rollback.

## 0.1.5 — 2026-10-17

- Rename the app, projects, executable, and release packages from SetupSwitch to SetupPulse.
- Add the SetupPulse icon to the Windows executable, tray, and app interface.
- Dark tray menu with current-device checkmarks and visible saved shortcuts.
- Retain existing settings, shortcuts, and cross-process locks for compatibility with SetupSwitch.
- Update an enabled Windows autostart entry to the new executable when SetupPulse starts.

- Public README in English with a German translation and a simulated-device screenshot.
- Contribution and release guidance, security reporting policy, issue forms, and a pull request template.
- Expanded ignore rules for build output, local settings, diagnostics, session notes, and branding experiments.
- Release package generation with license files and checksums; Windows CI build and test artifacts.

## 0.1.4 — 2026-10-16

- Describe the planned Bluetooth-adapter feature independently of manufacturer and connection method.
- Bluetooth switching remains unimplemented.

## 0.1.3 — 2026-10-16

- Capture and register number-pad digits as separate shortcut keys, such as `Ctrl+Alt+Num2`.
- Preserve the captured key when saving and reopening an audio favorite.

## 0.1.2 — 2026-10-15

- Add resolution presets with aspect ratios, optional manual dimensions, and portrait handling.

## 0.1.1 — 2026-10-15

- Correct shortcut capture for physical digit keys on German keyboards.
- Create and link an audio favorite directly from the setup editor.
- Match the title bar and scrollbar to the selected theme.

## 0.1.0 — 2026-10-15

- Initial portable Windows host and Workspace UI.
- Monitor setups, independent audio favorites, global shortcuts, tray, optional autostart, profile import/export, and recovery transactions.
