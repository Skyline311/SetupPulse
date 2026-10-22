# Troubleshooting

SetupPulse is an unsigned early preview. Its simulated tests do not replace testing on actual displays and audio hardware.

## Audio and display compatibility

- An application with a fixed audio-device selection may not follow the Windows default. Check the application's own audio settings.
- Global custom Windows scaling can prevent independent per-monitor scaling. SetupPulse does not edit DPI registry settings or sign you out.
- Recovery depends on Windows, drivers, and devices accepting the saved configuration. SetupPulse cannot guarantee a usable picture or repair a GPU-driver crash.
- The current version shows Bluetooth adapters but does not switch them.

## Bluetooth adapter status

Settings list attached physical Bluetooth adapters, including disabled adapters, and their Windows device status. Use the refresh button after connecting or changing a device; the list does not poll in the background. A started adapter is not proof that a particular mouse, keyboard, or headset is connected.

The Windows Bluetooth stack supports one radio at a time; see [Microsoft's Bluetooth FAQ](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-faq). Device errors such as code 31 are reported as returned by Windows. The code alone does not establish the cause. SetupPulse does not enable or disable adapters, change drivers, or alter pairing.

## Mixer and shortcuts

The mixer groups sessions from the same app on each output. Changing a group's volume or mute applies to its listed sessions. App output selection covers its currently listed processes; some apps restart audio or need restarting before changing outputs. Choosing **Windows-Standard** clears the app's explicit routing preference. An app's own explicit device selection can still take precedence.

App routing uses an isolated Windows compatibility API. If it is unavailable, the mixer reports this and keeps device/session volume controls available. A missing saved output remains visible rather than silently selecting a substitute.

Windows hotkey error 1409 means another Windows registration owns the combination. Check desktop shortcut properties as well as running apps. SetupPulse does not remove another program's binding. Save errors remain inside the editor so the draft can be corrected and retried.

## Display standby and driver recovery

SetupPulse does not apply a profile automatically when displays or Windows wake. It refreshes the device inventory; a still-pending monitor test can separately expire and restore its saved arrangement.

For black screens or flickering after standby, record the time and check the Windows System log for display-driver errors. GPU timeout recovery can reset the driver independently of SetupPulse; see [Microsoft's TDR explanation](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/timeout-detection-and-recovery). Compare a later wake with SetupPulse fully exited to help distinguish causes. A driver event alone does not establish what triggered it.

## Confirmation and recovery

A new or changed monitor setup must be confirmed within 90 seconds. Without confirmation, SetupPulse attempts to restore the state saved immediately before that switch. Successfully tested layouts switch directly on later uses.

Live vertical alignment keeps the original layout across repeated Y changes. Each accepted change renews the 90-second confirmation period. Cancel or expiry restores the original layout and leaves audio untouched.

Live alignment preserves the current display paths, resolution, rotation, refresh timings, and scaling while changing Y positions. It works on monitors currently active in Windows. Displays excluded from the edited setup remain locked and unchanged; enabling/disabling displays in the editor only changes the setup draft until that setup is activated. An aborted alignment returns to the preserved draft with a visible error.

A separate recovery process starts before the device change. Closing the app does not immediately end this process. Signing out or restarting the PC does end it; a pending test remains available for manual recovery when the app starts again.

The optional **Rettungs-Shortcut** is a separate manual recovery action. It restores the last saved safe configuration, once one exists. It does not guarantee that a driver or disconnected device will accept the configuration.

## Settings and imported profiles

Settings and transactions are stored under `%LOCALAPPDATA%\SetupSwitch`. SetupPulse was previously called SetupSwitch; the original data folder is retained to preserve existing profiles and shortcuts. The executable is portable, but these settings are kept in your Windows user profile rather than beside the EXE.

Saves use atomic replacement and backup files. Demo data uses the separate `Demo` subfolder.

Import adds profiles, clears imported shortcuts and monitor-test confirmations, and may require manual device reassignment on another PC. Review device bindings before applying an imported setup.

## Reporting a problem

Include the app version, Windows version, relevant devices, reproducible steps, and expected versus actual behavior. Distinguish number-row shortcuts such as `Ctrl+Alt+2` from number-pad shortcuts such as `Ctrl+Alt+Num2`.

Review screenshots, exported profiles, and diagnostics before attaching them to an issue: they can contain device identifiers and local paths. Diagnostic commands are documented in [CONTRIBUTING.md](../CONTRIBUTING.md#demo-and-diagnostics).

For a suspected vulnerability, follow [SECURITY.md](../SECURITY.md) rather than posting sensitive details in an issue.
