SetupPulse - portable Windows preview
Author and creator / Autor und Ersteller: Skyline311
https://github.com/Skyline311/SetupPulse

ENGLISH

This package contains an unsigned early preview of SetupPulse for Windows 11 x64.
It switches saved monitor configurations and audio favorites. Its mixer controls
device/app volume, mute, and app outputs. Bluetooth-adapter switching is not included.

1. Extract this ZIP to a folder you can keep.
2. Install Microsoft Edge WebView2 Evergreen Runtime if it is missing:
   https://developer.microsoft.com/en-us/microsoft-edge/webview2/
3. Start SetupPulse.exe. The .NET runtime is included; no SDK is needed.
4. Create monitor setups and select audio devices directly; favorites are optional.
5. Confirm a new monitor setup with Enter within 90 seconds. If it is not confirmed,
   a separate process attempts to restore the previous state. Drivers can still
   refuse a restoration; preview builds need hardware testing.

Closing the window normally leaves SetupPulse in the system tray. Use the tray
menu's "Beenden" item to exit before replacing the EXE. Settings are stored in
%LOCALAPPDATA%\SetupSwitch, not next to the executable. The UI is currently German.
The legacy SetupSwitch folder is retained so existing profiles and shortcuts work.

Under Einstellungen -> Bearbeiten, enable Mit Windows starten and choose
Beim Windows-Start minimiert starten to launch only in the notification area.
Manual launches open the main window. The tray uses white exchange arrows.

Check for public stable GitHub Releases under Einstellungen -> Updates. Optional
startup checks only notify you; click Jetzt aktualisieren to download, verify and
restart into a newer version. Profiles and credentials are kept. The app folder
must be writable. A private repository or no published release offers no update.

Number-pad shortcuts use names such as Ctrl+Alt+Num2. They are distinct from
Ctrl+Alt+2 on the number row. Audio favorites can switch independently of monitors.
Applications with a fixed audio device may not follow the Windows default.
Open Audio-Mixer for the mixer. A single left click on the tray icon opens its quick
view after the double-click interval; a double-click opens the main window. Tray-wheel
volume and additional shortcuts are optional in Einstellungen. Some apps adopt
a different output only after their audio playback restarts.

Optional Home Assistant / MQTT: configure your broker under Einstellungen ->
Home Assistant. Confirm monitor layouts locally first, then select them from
Home Assistant or a Zigbee-button automation. The integration starts disabled.
Guide: https://github.com/Skyline311/SetupPulse/blob/main/docs/HOME-ASSISTANT.md

The MIT license and dependency notices are included. SHA256SUMS.txt lists hashes
for the package files. Use the source repository's documentation for development,
diagnostics, and current limitations. Diagnostic output can contain local device
identifiers and should be reviewed before sharing.

DEUTSCH

Dieses Paket enthält eine nicht signierte frühe Vorschau für Windows 11 x64.
ZIP entpacken und SetupPulse.exe starten. Falls erforderlich, die oben verlinkte
WebView2 Evergreen Runtime installieren. Die .NET-Laufzeit ist enthalten.

Monitor-Setups mit direkter Audioauswahl speichern; Favoriten sind optional. Neue oder geänderte
Monitoranordnungen innerhalb von 90 Sekunden mit Enter bestätigen. Ohne Bestätigung
versucht der Rücksetzungsprozess die Wiederherstellung; sie hängt auch vom Treiber ab.

Strg+Alt+Num2 verwendet den Nummernblock, Strg+Alt+2 die obere Ziffernreihe.
Audiofavoriten funktionieren unabhängig von den Monitoren. Bluetooth-Umschaltung
ist noch nicht enthalten. Vollständig beenden über Tray -> Beenden.
Einstellungen: %LOCALAPPDATA%\SetupSwitch. Lizenztexte und Prüfsummen liegen bei.
Der bisherige Datenordner bleibt erhalten; vorhandene Setups bleiben verfügbar.
Unter Einstellungen -> Updates nach öffentlichen stabilen GitHub Releases suchen.
Die optionale Prüfung beim Start installiert nichts automatisch. Jetzt aktualisieren
lädt und prüft das Update und startet die App neu; Profile und Zugangsdaten bleiben
erhalten. Der App-Ordner muss dafür beschreibbar sein.
Unter Audio-Mixer findest du Geräte-/App-Lautstärke, Stummschaltung und App-Ausgaben.
Ein einzelner Linksklick auf das Tray-Icon öffnet nach der Doppelklick-Frist den
kleinen Schnellmixer am Tray; ein Doppelklick öffnet das Hauptfenster.
Escape oder ein Klick außerhalb schließt ihn. Voller Mixer öffnet Audio-Mixer.
Tray-Mausrad und zusätzliche
Shortcuts lassen sich in den Einstellungen aktivieren.
