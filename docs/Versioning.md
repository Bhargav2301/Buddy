# Versioning and releases

[VERSION](../VERSION) is the shared `major.minor.patch` number. Both applications
and the Windows package builder read it. Use patch increments for fixes, minor
increments for new features, and major increments for incompatible changes.
The Windows manifest is checked against it by CI.

## Prepare a version

1. Work on a branch and make focused commits describing each change.
2. Run `python scripts/version.py bump 0.2.1`, substituting the next version.
   This updates VERSION and the Windows manifest, and inserts a changelog entry.
3. Replace the TODO in CHANGELOG.md with user-visible changes, fixes, and any
   validation limits. Run `python scripts/version.py check`.
4. Commit these changes with the implementation. Open a pull request and inspect
   the Windows and Android checks before merging into `main`.

Do not reuse a version for a different release. Do not move or delete a published
tag. Ordinary commits may keep the current version while the next change is in
development; use a new version when preparing another downloadable release.

## What GitHub builds

Every push and pull request checks the version and changelog. Windows runners
execute the service and desktop logic tests, publish a self-contained x64 WPF
app, and validate its runtime and native dependencies. Android runners build a
testing APK and run lint. Real microphone, pointer, installation, and cross-device
AI acceptance still require hardware; see [Validation](Validation.md).

Successful builds on `main` create an annotated `v<version>` tag and a **draft
prerelease** if that version has not already been tagged. The release contains:

- `Buddy-Windows-v<version>.zip`
- `Buddy-Android-v<version>.apk`
- `Buddy-Setup-Guide.md`, `CHANGELOG.md`, `Build-Info.json`, and `SHA256SUMS.txt`

The tag points to the exact source commit that was built. Existing tags are never
rewritten. Rerunning that same commit can refresh draft assets; published releases
are left unchanged. A commit with an already tagged version only produces CI
artifacts. Bump the version to create the next release.

Review the draft in [GitHub Releases](https://github.com/Bhargav2301/Buddy/releases)
and complete the device checks before publishing it. Draft assets are visible to
repository maintainers. CI downloads are also available under the corresponding
[Actions run](https://github.com/Bhargav2301/Buddy/actions).

The release job alone requests `contents: write` to create tags and drafts. It
uses GitHub's built-in workflow token; no personal token is embedded in source.

## Native version details

Windows assembly/file versions are `<version>.0`. Android `versionName` equals
VERSION and `versionCode` is `major * 1,000,000 + minor * 1,000 + patch + 1`.
The supported range is major 0–2000 and minor/patch 0–999. Version 0.2.0 therefore
uses Android code 2001, greater than the original testing build's code 1.

CI debug APKs use a temporary signing key. They may not install over the previously
delivered APK or another CI build. If Android reports a signature mismatch,
uninstall the previous testing app, install the new APK, and pair again. This
removes the phone's local setup; conversations stored on the PC remain there.
A stable production signing key and Windows code-signing certificate are separate
release prerequisites and must never be committed to this repository.

## Earlier deliveries

Before this repository import, multiple Windows packages reused the `0.1.0`
filename. The baseline import is the latest combined source, not a reconstruction
of unavailable earlier snapshots. [CHANGELOG.md](../CHANGELOG.md) records those
milestones without inventing historical Git tags.
