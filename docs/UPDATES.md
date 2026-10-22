# Updates

Under **Einstellungen → Updates**, choose **Nach Updates suchen**. When a newer stable release is available, **Jetzt aktualisieren** downloads and verifies it, replaces the portable package in its existing folder, and restarts SetupPulse. Profiles and MQTT credentials stay in their separate Windows user data folder. No Windows installer or administrator prompt is required; the app folder must be writable.

Optional automatic checks run once per app start, at most once per 24 hours, after a short startup delay. They only notify you. Downloads and installation require your click. You can turn automatic checks off and check manually instead.

An active switch or monitor confirmation blocks installation. Switches remain blocked during download and handoff. A temporary copy of the running EXE performs the replacement after the old app exits. It retains the replaced files until the new app has successfully loaded its interface and answered its first state request. If that startup fails, it attempts to restore and restart the previous version. The updater does not switch monitors or audio devices.

Downloads use HTTPS and the official `Skyline311/SetupPulse` GitHub repository. ZIP and individual file SHA-256 checksums, package contents, Windows x64 architecture, product and version are checked before installation. Checksums detect corruption and inconsistent assets; they are not a publisher code signature. The portable preview remains unsigned.

Only public, published, stable GitHub Releases are offered. A private repository or a repository with no published stable release shows **Noch kein öffentliches Release verfügbar**. The app contains no GitHub access token. Source commits, tags without a release, draft releases and prereleases are not app updates. Repository visibility and release publication are separate settings.

## Preparing a release

1. Set the app and UI version to the same `X.Y.Z` version and commit the changes.
2. Create a `vX.Y.Z` tag for that commit and push the tag. The release workflow builds, tests and prepares a **draft** GitHub Release. It does not publish it automatically.
3. Review the draft and its Windows x64 ZIP and `.zip.sha256` assets. The ZIP must contain the standard seven package files, including `SHA256SUMS.txt`.
4. Publish the stable release when ready. Public users can then discover it from the app.

Asset names must be `SetupPulse-X.Y.Z-win-x64.zip` and `SetupPulse-X.Y.Z-win-x64.zip.sha256`. Keep the version/tag/package consistent. Published release assets should remain unchanged; issue a new version for corrections. The first build containing the updater must be installed manually once.

If the portable EXE has been renamed, is in a read-only folder, or an update cannot be applied, download and extract the latest ZIP manually into the existing app folder after exiting SetupPulse. Keep the name `SetupPulse.exe` for in-app updates. Settings are retained. A failed recovery preserves the temporary backup in the app folder and reports the failure; do not remove that backup before recovering the app.

## Deutsch

Unter **Einstellungen → Updates** nach neuen Versionen suchen und bei Bedarf **Jetzt aktualisieren** anklicken. Die optionale Prüfung beim Start läuft höchstens einmal täglich; installiert wird ausschließlich nach deinem Klick. Die App startet kurz neu, Setups und Zugangsdaten bleiben erhalten. Während eines Monitorwechsels oder einer offenen Bestätigung wird kein Update gestartet.

Die Update-Dateien kommen aus veröffentlichten stabilen GitHub Releases. Solange das Repository privat ist oder noch kein solches Release vorliegt, ist keine öffentliche Aktualisierung verfügbar. Ein Quellcode-Push veröffentlicht noch kein App-Update. Der Release-Workflow erstellt zunächst einen Entwurf zum Prüfen und manuellen Veröffentlichen. Diese erste Version mit Updater muss einmal manuell installiert werden.

References: [GitHub Releases API](https://docs.github.com/en/rest/releases/releases), [release asset downloads](https://docs.github.com/en/rest/releases/assets).
