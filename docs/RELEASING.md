# Preparing a release

Use this process for [Skyline311/SetupPulse](https://github.com/Skyline311/SetupPulse). Building locally prepares files; the version-tag workflow prepares a GitHub release draft. Publication remains a separate, deliberate step.

## Build the package

Use a clean Windows checkout and the developer prerequisites in [CONTRIBUTING.md](../CONTRIBUTING.md#local-setup). Run:

```powershell
./scripts/build.ps1
cd ui
npx playwright install chromium
npm run test:e2e
```

The EXE always goes to `artifacts/win-x64/SetupPulse.exe`, with supporting license and quickstart files alongside it. Close a running instance through the tray before rebuilding. A versioned `SetupPulse-<version>-win-x64.zip` plus `.sha256` file goes to `artifacts/`. Local test reports, preview screenshots, and diagnostics belong in the ignored `dev/` directory. The ZIP includes the executable, MIT license, third-party licenses/notices, a standalone English/German quickstart (`README.txt`), the changelog, and a `SHA256SUMS.txt` file. It excludes symbols, old EXEs, settings, and diagnostics. The full GitHub README remains in the source repository, where its relative links and screenshot resolve correctly.

To verify a downloaded package, compare `Get-FileHash -Algorithm SHA256 <zip-file>` with its `.sha256` file. Checksums detect accidental differences; they do not substitute for a trusted download or a code signature.

## Validate the candidate

- Run the core and browser tests and record the results.
- Check the actual Windows host, tray, shortcut capture, and independent audio switching.
- Test monitor switching, scaling, delayed HDMI audio, confirmation timeout, and recovery on the supported hardware. Simulated tests alone are insufficient.
- Check start with Windows, app exit, sign-in/restart, and wake behavior without automatic setup application.
- Verify the package contains the license texts and uses no personal profiles or device identifiers. Confirm the README describes implemented features accurately.

The project is currently an unsigned early preview. State that in the release notes. Do not describe Bluetooth switching as available until it is implemented and tested.

## Prepare the release draft

1. Set the same `X.Y.Z` version in the app project, `ui/package.json`, and its lockfile. Update the changelog, README status, and demo screenshots when the visible interface changes. Commit the final source.
2. Create a `vX.Y.Z` tag on that commit and push the tag. Alternatively, run **Prepare release draft** for that existing tag.
3. The workflow validates the version and tag, builds the package, runs core and browser tests, and uploads the ZIP and checksum to a **draft** release. A failed check must not be bypassed for publication.
4. Download and check the exact draft package. Record which native and hardware checks above were actually completed, separately from simulated tests. Include outstanding compatibility limits in the release notes.

Do not replace the assets of a published release. Correct the source and publish a new version instead. The workflow refuses to overwrite a published release.

## Publish the reviewed draft

Review the release title, notes, ZIP, checksum, and test results, then publish the draft when ready. Use a stable GitHub release for versions that should be offered by the in-app updater. A stable GitHub release can still be clearly described as an unsigned early preview; this does not imply broad hardware certification. Drafts and GitHub prereleases are intentionally excluded from in-app updates. See [Updates](UPDATES.md).

Once the first download is published, replace the temporary no-download notice in both READMEs with the actual release link. Keep any unfinished fresh-Windows or hardware checks explicit; do not describe a browser simulation or a clean CI runner as a completed physical-monitor or standby test.

## Repository and privacy checks

SetupPulse is the selected app name. Keep visible names, icons, executable/package names, screenshots, and documentation consistent. The legacy SetupSwitch storage and process-coordination identifiers are intentionally retained for compatibility; do not rename them without a migration plan.

Before the first upload, review `git status --short` and the files Git would include. `.gitignore` does not remove files that were already committed. Retain source code, lockfiles, build configuration, public documentation, the app icon, and required license texts. Put private session notes, validation history, design drafts, and temporary working files in the ignored `dev/` directory. Generated build packages in `artifacts/` are also ignored.

Review all reachable history and branches before making a private development repository public, including older pull-request references and Actions logs. Removing an internal file from the current tree does not remove its old commits. Preserve private development history separately and use a reviewed public starting history when required. Never publish the private archive or local credentials.

When public, enable private vulnerability reporting so the route in [SECURITY.md](../SECURITY.md) is available, enable Dependabot alerts and secret scanning/push protection, and require successful Windows and security checks before merging into `main`. Scheduled dependency and secret scans are also included in the repository. No workflow automatically publishes releases or merges dependency updates.
