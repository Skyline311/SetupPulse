# Contributing

Thanks for helping improve SetupPulse. English and German bug reports are welcome. Keep each issue or pull request focused on one problem or feature.

## Local setup

Use Windows 11 x64, Node.js 24, and the .NET 10 SDK selected by `global.json`. The Windows host also needs WebView2 Evergreen Runtime. No private credentials or personal device profiles are needed to build the project.

From the repository root:

```powershell
./scripts/build.ps1
cd ui
npx playwright install chromium
npm run test:e2e
```

The build script performs `npm ci`, builds the UI, collects production dependency licenses, runs the core tests, and publishes the Windows app. The EXE always goes to `artifacts/win-x64/SetupPulse.exe`. Close a running instance through **Tray → Beenden** before building; no alternate EXE folder is created. `-Dotnet 'C:\Tools\dotnet\dotnet.exe'` selects a local SDK executable. Local test reports and preview screenshots go to the ignored `dev/` directory.

For UI-only work, run `npm ci` and `npm run dev` inside `ui`. The browser bridge uses simulated devices. A browser preview cannot validate native Windows behavior.

Open `http://127.0.0.1:5173/?demoMonitors=3` to simulate three displays for live vertical alignment. This query affects only the browser simulation; it never changes native devices.

For native C# changes, open `SetupPulse.sln` in your IDE, or build the host directly after building the UI:

```powershell
cd ui
npm ci
npm run build
cd ..
dotnet build src/SetupPulse.App -c Debug
./src/SetupPulse.App/bin/Debug/net10.0-windows/win-x64/SetupPulse.exe --demo
```

The final command starts the Windows host with simulated hardware and a separate demo data folder. Rebuild the UI before rebuilding the host when frontend files change: the host embeds `ui/dist`. To attach a debugger, use this Debug build or set `--demo` as the startup argument for the app project in your IDE. Real hardware validation is a separate step.

## Demo and diagnostics

These optional developer and support commands are not needed for everyday use:

```powershell
./artifacts/win-x64/SetupPulse.exe --demo
./artifacts/win-x64/SetupPulse.exe --diagnose .\dev\diagnostics
./artifacts/win-x64/SetupPulse.exe --mixer-diagnose .\dev\mixer-diagnostics
./artifacts/win-x64/SetupPulse.exe --validate-current .\dev\validation
```

- `--demo` uses simulated devices and a separate data folder.
- `--diagnose` reads the current device inventory without changing it.
- `--mixer-diagnose` reads active audio endpoints, sessions, and app routing support without changing audio. It can include app paths and process identifiers.
- `--validate-current` additionally asks Windows to validate the current display plan and checks the DPI/audio compatibility adapters without applying a configuration.

Review diagnostic output before attaching it to an issue: it can include device identifiers and local paths. User-facing compatibility and recovery guidance is in [Troubleshooting](docs/TROUBLESHOOTING.md).

## Source layout and conventions

- `SetupPulse.Core` owns models, validation, storage, and transactions. Device access goes through `IHardware`.
- `SetupPulse.App` owns native Windows adapters, the watchdog, hotkeys, tray, and the WebView2 message bridge.
- `ui/src` contains React components and the typed bridge. Keep exposed bridge methods explicit and validate payloads in the host.
- `tests` contains core tests; `scripts` contains build and asset helpers; `docs` contains user help and release guidance.
- See [ARCHITECTURE.md](ARCHITECTURE.md) for the design of the Windows host and switching engine.
- Follow `.editorconfig`. Format changed frontend files with the installed Prettier, for example `npx prettier --write src/App.tsx` from `ui`.
- Keep dependency lockfiles and license notices current. Explain new dependencies and their purpose in the pull request.

The tray artwork source is `assets/branding/setuppulse-tray.svg`. After changing it, run `node scripts/export-tray-icon.mjs` with the UI Playwright browser installed to regenerate `src/SetupPulse.App/tray.ico`. The generated ICO is committed, so ordinary builds do not require a browser or a runtime SVG library.

## Validation

Core tests run against fake hardware and must not change real devices. Playwright tests use the browser simulation. Add a regression test for changed behavior where it can catch a real failure; documentation-only edits normally need link and wording checks rather than new tests.

For native changes, describe the actual Windows build, GPU/audio driver versions, devices involved, and what was manually tested. Do not describe simulated tests as hardware validation. Coordinate deliberate monitor, audio, or future Bluetooth changes with the person using the test computer.

Preserve these behaviors:

- Audio-only actions do not call display-changing functions.
- Device changes are coordinated; stale recovery processes cannot overwrite a later action.
- A new or changed monitor configuration requires confirmation.
- Starting the app or waking Windows does not automatically apply a setup.
- A missing or unsupported device produces a clear result rather than silently choosing another device.

## Issues and pull requests

Use the bug or feature template. Include reproducible steps, the app version, and expected versus actual behavior. For shortcut problems, distinguish number-row keys from number-pad keys.

Review attachments before sharing them. Diagnostics, exported profiles, and screenshots may contain device identifiers, user paths, or personal information. The repository's public example image uses simulated data.

Pull requests should explain the final behavior, relevant validation, and any compatibility limits. Update user-facing documentation when behavior changes. Keep builds, local settings, session notes, and private branding experiments out of the commit; `.gitignore` excludes the standard locations.

Project code is contributed under the [MIT License](LICENSE); dependencies retain their own license terms. Security reports are covered by [SECURITY.md](SECURITY.md).
