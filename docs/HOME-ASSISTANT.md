# Home Assistant and Zigbee buttons

SetupPulse can appear as an MQTT device in Home Assistant. Its **Setup** selection activates saved setups. Separate **Audioausgabe**, **Mikrofon**, and **Audiofavorit** selections control audio without changing monitors; **Wechselstatus** and **Letzter Fehler** report the outcome. The feature is optional and disabled by default. The PC must be awake, the Windows user signed in, and SetupPulse running.

## Connect

1. Keep the MQTT integration in Home Assistant connected to your broker. If you already use Zigbee2MQTT, use that same broker.
2. In SetupPulse, open **Einstellungen → Home Assistant** and enable the MQTT integration.
3. Enter the broker hostname/IP, port, and MQTT username/password. This is the MQTT broker address, not the Home Assistant web address or Home Assistant account password. The usual TCP port is 1883; TLS commonly uses 8883. TLS certificates must be trusted by Windows.
4. Choose a device name and click **MQTT-Verbindung speichern**. The connection status changes to **Verbunden** when the subscription and initial discovery succeed.
5. In Home Assistant, open **Settings → Devices & services → MQTT** and find the device with the chosen name. Automatic discovery must be enabled. The discovery prefix normally remains `homeassistant`.

Monitor setups appear only after you activate and confirm their exact monitor layout once on the PC. Changing the layout requires another local confirmation. Audio-only setups need no monitor confirmation. Duplicate setup names receive an ID suffix in the selection so each option remains distinct.

## Control audio independently

- **Audioausgabe** lists available Windows output devices. Choose speakers or a headset directly; no saved favorite is needed.
- **Mikrofon** lists available Windows input devices. Changing input leaves output unchanged, and changing output leaves input unchanged. Direct selection preserves volume, mute, and the separate communications-device assignments.
- **Audiofavorit** lists saved audio favorites whose devices are available. Selecting one applies its saved output/input, volume, mute, and communications settings, as configured in SetupPulse.

All three selections use the existing audio-only transaction and recovery path. They never apply a monitor layout. Device connection changes update the available options, and the displayed choice follows the actual Windows default devices or matching favorite. A favorite can be unknown if none matches. Duplicate or reserved names receive an ID suffix. Audio and setup commands share one switch queue and cannot run simultaneously.

To assign a Zigbee-button press to audio, use the same **Select: Select option** action below, targeting **Audioausgabe**, **Mikrofon**, or **Audiofavorit** instead of **Setup**. Existing Setup automations keep their entity identity when upgrading.

## Assign a Zigbee2MQTT button

Create a Home Assistant automation. Select the event/action exposed by your Zigbee button as the trigger; available presses depend on its model. For the action, choose **Select: Select option** (`select.select_option`), target the SetupPulse **Setup** entity, and choose the desired option, for example `TV` or `Büro`. You can use different presses for different setups, or place the selection on a dashboard. No custom Home Assistant component or Windows script is needed.

## State and connection behavior

- For setups with monitors, the selection follows the actual monitor layout. Switching audio independently keeps that setup active; activating the setup again still applies its linked audio settings. Audio-only setups follow the actual matching audio state. A changed monitor layout or a running transaction can make the setup unknown; it does not merely repeat the last command.
- Switch status is `ready`, `switching`, or `error`. The error entity shows a short error; SetupPulse retains full switch/recovery details in its existing UI.
- Unconfirmed layouts, missing setups, missing devices, and simultaneous switches are rejected. The existing transaction mutex and recovery path also apply to MQTT commands.
- Closing SetupPulse completely makes it unavailable. Closing its main window to the tray keeps it running.
- Broker or Home Assistant restarts reconnect and republish discovery/state without applying a setup. Retained commands delivered during subscription are ignored. Commands must be published without retain; the discovered select is already configured this way.
- Disabling the connection leaves the discovered entities offline. Remove the device in Home Assistant if you no longer want it listed.
- Broker credentials are stored separately from profile exports. The password is encrypted for the current Windows user; enter it again when moving to another PC/user. Leaving the password field empty preserves an existing password; **Gespeichertes Passwort entfernen** clears it on the next save.

## Deutsch

Unter **Einstellungen → Home Assistant** die MQTT-Anbindung aktivieren und denselben Broker wie Zigbee2MQTT eintragen. MQTT-Host, Port und MQTT-Zugangsdaten verwenden, anschließend **MQTT-Verbindung speichern** anklicken. In Home Assistant erscheint das gewählte Gerät unter der MQTT-Integration mit einer **Setup**-Auswahl sowie Status und letztem Fehler.

**Audioausgabe**, **Mikrofon** und **Audiofavorit** lassen sich unabhängig davon steuern. Für Ausgabe und Mikrofon genügt ein verfügbares Windows-Gerät; ein Favorit ist nicht erforderlich. Die direkte Geräteauswahl lässt Lautstärke, Stummschaltung, die jeweils andere Richtung und Kommunikationsgeräte unverändert. Ein Audiofavorit wendet seine gespeicherten Audioeinstellungen an. Alle drei Auswahlen wechseln ausschließlich Audio, keine Monitore. In einer Taster-Automation einfach die entsprechende Audio-Auswahl als Ziel verwenden. Angezeigt wird der tatsächliche Windows-Zustand; ohne passenden Favoriten ist dessen Auswahl unbekannt.

Ein Monitor-Setup wie **Büro** bleibt dabei aktiv, solange die Monitoranordnung passt. Separates Audio-Wechseln entfernt dessen Aktiv-Markierung nicht. Beim erneuten Aktivieren des Setups werden weiterhin dessen hinterlegte Audioeinstellungen angewendet. Reine Audio-Setups und Audiofavoriten werden nach dem tatsächlichen Audiozustand markiert.

Eine Automation verbindet das vom Zigbee-Taster gemeldete Ereignis mit **Auswahl: Option auswählen** (`select.select_option`). Als Ziel die SetupPulse-Setup-Entität und als Option beispielsweise `TV` wählen. Der konkrete Tastendruck hängt vom Taster ab. Die Auswahl lässt sich auch auf einem Dashboard platzieren.

Monitoranordnungen vorher einmal am PC aktivieren und bestätigen. Ungetestete oder geänderte Anordnungen werden nicht fernsteuerbar angeboten. Der PC muss eingeschaltet, der Windows-Benutzer angemeldet und SetupPulse gestartet sein. Die Verbindung läuft vollständig in der EXE. Wiederverbindung und Aufwachen aktivieren kein Setup automatisch. Passwörter bleiben im lokalen, für deinen Windows-Benutzer geschützten Speicher und sind nicht Bestandteil von Profilexporten.

References: [Home Assistant MQTT discovery](https://www.home-assistant.io/integrations/mqtt/), [MQTT select](https://www.home-assistant.io/integrations/select.mqtt/), [MQTT sensor](https://www.home-assistant.io/integrations/sensor.mqtt/).
