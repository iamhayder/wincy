# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it privately through GitHub's
[private vulnerability reporting](https://github.com/iamhayder/wincy/security/advisories/new),
which notifies the maintainer without disclosing anything publicly.

Expect an acknowledgement within a few days. If a fix is warranted it will ship in the
next release, and the advisory will be published once users have had a chance to update.

## What is in scope

Wincy stores everything you copy, so the parts worth scrutinising most are:

- **The privacy formats.** Wincy must never record a copy marked with
  `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`, or with
  `CanIncludeInClipboardHistory` / `CanUploadToCloudClipboard` set to zero. A way to make
  it record such a copy is a security bug, not a feature request.
- **The ignore rules.** Applications, patterns and formats configured under
  Preferences → Ignore must be honoured.
- **The database.** History lives in `%LOCALAPPDATA%\Wincy\history.db`, protected by
  ordinary NTFS permissions on your user profile. It is not encrypted; see below.
- **The installer.** Anything allowing an unprivileged user to influence what the MSI
  writes into Program Files.

## What is not a vulnerability

- **The history database is not encrypted.** Another process running as you, or anyone
  with your credentials, can read it. Encrypting at rest without a password prompt would
  only move the key next to the data. Use Preferences → Ignore, or pause recording, for
  anything that must not be stored.
- **Wincy cannot paste into elevated windows.** That is Windows blocking synthetic input
  across integrity levels, and it is working as designed.
- **Releases are not code-signed**, so SmartScreen warns on first run. Signing needs a
  certificate this project does not have.

## Scope

Only the latest release is supported.
