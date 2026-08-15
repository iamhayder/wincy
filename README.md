<div align="center">

# Wincy

**Clipboard history for Windows.** Lightweight, keyboard-first, and entirely local.

[![CI](https://github.com/iamhayder/wincy/actions/workflows/ci.yml/badge.svg)](https://github.com/iamhayder/wincy/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/iamhayder/wincy?sort=semver)](https://github.com/iamhayder/wincy/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/iamhayder/wincy/total)](https://github.com/iamhayder/wincy/releases)
[![License](https://img.shields.io/github/license/iamhayder/wincy)](LICENSE)

Press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd>, type a few letters, press <kbd>Enter</kbd>.

</div>

<!--
  A screenshot belongs here. Save one as docs/screenshot.png and uncomment:

  <p align="center">
    <img src="docs/screenshot.png" alt="The Wincy popup, showing clipboard history with a search field" width="480">
  </p>
-->

---

## Install

Download from the [latest release](https://github.com/iamhayder/wincy/releases/latest):

| File | Use it if |
|---|---|
| **`Wincy-x.y.z-x64.msi`** | You want a normal install — Start Menu entry, listed in Add or Remove Programs |
| **`Wincy-x.y.z-portable-x64.exe`** | You want a single file to run from anywhere, no install |

Both are self-contained: no .NET runtime to install. Wincy needs **Windows 10 1809 or
newer**, 64-bit.

> [!NOTE]
> Neither download is code-signed, so SmartScreen warns the first time you run it.
> Choose **More info**, then **Run anyway**.

## Features

- **Searchable history** — exact, fuzzy, regular expression, or all three in turn
- **Keeps formatting** — rich text, HTML, images and files come back exactly as copied,
  or paste as plain text on demand
- **Pinning** — keep an item at the top with a permanent letter shortcut
- **Keyboard-first** — the whole application is reachable without the mouse, and every
  row shows what your current modifiers will do
- **Native look** — acrylic backdrop, rounded corners, and light or dark following your
  system theme and accent colour
- **Private by design** — nothing leaves your machine, and copies from password managers
  are never recorded
- **Light on resources** — event-driven rather than polling, with images compressed and
  history loaded lazily

## Usage

<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> opens the popup, or click the tray icon.
Then type to search.

| Shortcut | Does |
|---|---|
| <kbd>Enter</kbd> | Copy the selected item |
| <kbd>Alt</kbd>+<kbd>Enter</kbd> | Copy **and paste** it |
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>Enter</kbd> | Paste without formatting |
| <kbd>Ctrl</kbd>+<kbd>1</kbd>…<kbd>9</kbd> | Copy the *n*-th item directly |
| <kbd>Alt</kbd>+<kbd>1</kbd>…<kbd>9</kbd> | Paste the *n*-th item directly |
| <kbd>↑</kbd> <kbd>↓</kbd> | Move the selection |
| <kbd>PgUp</kbd> / <kbd>PgDn</kbd> | First / last item |
| <kbd>Alt</kbd>+<kbd>P</kbd> | Pin or unpin |
| <kbd>Alt</kbd>+<kbd>Backspace</kbd> | Delete the item |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Backspace</kbd> | Clear unpinned items |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>Backspace</kbd> | Clear everything |
| <kbd>Alt</kbd>+<kbd>Space</kbd> | Toggle the preview pane |
| <kbd>Ctrl</kbd>+<kbd>,</kbd> | Preferences |
| <kbd>Esc</kbd> | Close |

You never have to memorise that table: the keycaps on the right of each row always show
what the modifiers you are currently holding will do.

**Pinning** moves an item to the top and gives it a permanent letter. From then on
<kbd>Ctrl</kbd>+<kbd>letter</kbd> copies it and <kbd>Alt</kbd>+<kbd>letter</kbd> pastes
it, wherever it is in the list.

**Cycling** is the fast path to the last few things you copied: hold
<kbd>Ctrl</kbd>+<kbd>Shift</kbd> and tap <kbd>V</kbd> repeatedly to step down the
history. Release the modifiers and the item you landed on is taken.

**Pausing**: <kbd>Alt</kbd>-click the tray icon to stop recording;
<kbd>Alt</kbd>+<kbd>Shift</kbd>-click to skip only the next copy.

Emacs-style navigation works too — <kbd>Ctrl</kbd>+<kbd>N</kbd>/<kbd>P</kbd>/<kbd>J</kbd>/<kbd>K</kbd>
to move, and <kbd>Ctrl</kbd>+<kbd>U</kbd>/<kbd>W</kbd>/<kbd>H</kbd> to edit the search.

## Preferences

<kbd>Ctrl</kbd>+<kbd>,</kbd>, or right-click the tray icon.

| Pane | Covers |
|---|---|
| **General** | Launch at login, shortcuts, search mode, whether Enter copies or pastes |
| **Storage** | Which kinds to save, how many items, sort order, database size |
| **Appearance** | Where the popup opens and on which screen, pin placement, highlight style, preview side and timing, tray icon, which chrome to show |
| **Pins** | Reassign or release pin letters |
| **Ignore** | Applications, patterns, clipboard formats |
| **Advanced** | Pause recording, clearing behaviour, clipboard settle delay, logs |

## Privacy

Your history stays on your machine. **There is no network code in this application.**

Wincy honours the standard Windows clipboard privacy formats, so copies from password
managers and private browsing sessions are never recorded:

- `ExcludeClipboardContentFromMonitorProcessing`
- `CanIncludeInClipboardHistory` set to zero
- `CanUploadToCloudClipboard` set to zero
- `Clipboard Viewer Ignore`

Beyond that, **Preferences → Ignore** excludes applications by executable name, discards
copies matching a regular expression, and takes further clipboard formats.

Everything lives in `%LOCALAPPDATA%\Wincy`. That folder is **not** encrypted and survives
uninstall — delete it if you want the history gone. See [SECURITY.md](SECURITY.md) for
the reasoning.

## FAQ

**Why not <kbd>Win</kbd>+<kbd>V</kbd>?**
Windows reserves it for its own clipboard history and will not hand it to another
application. Change Wincy's shortcut under Preferences → General.

**Nothing happens when I press the shortcut.**
Something else has claimed it. Wincy says so at startup if registration failed; pick a
different combination in Preferences → General.

**It copies but does not paste.**
Check "Paste automatically" in Preferences → General, and see the limitation below about
windows running as administrator.

**Where is my data?**
`%LOCALAPPDATA%\Wincy` — `history.db` and `settings.json`. The log next to them,
`wincy.log`, is the first place to look if something misbehaves.

**Can I sync between machines?**
No. That would mean sending your clipboard somewhere, which this application does not do.

## Known limitations

**Wincy cannot paste into windows running as administrator.** Windows blocks synthetic
keystrokes from a normal-integrity process into an elevated one, so with an admin
terminal, Task Manager or Registry Editor in front, choosing an item copies it but the
paste never arrives — press <kbd>Ctrl</kbd>+<kbd>V</kbd> yourself. Copying *from*
elevated applications is recorded normally, since the clipboard itself is shared.

There is no fix worth its cost: running Wincy elevated would invert the problem and break
pasting into ordinary applications, and the `uiAccess` route needs a code-signed binary
installed under Program Files.

Beyond that: English only, no update checker, and the popup is resized from Preferences
rather than by dragging.

## Building

Requires the **.NET 10 SDK**.

```powershell
git clone https://github.com/iamhayder/wincy.git
cd wincy
dotnet build
dotnet test
dotnet run --project src\Wincy
```

A single self-contained executable:

```powershell
dotnet publish src\Wincy -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

The installer, pointed at that executable:

```powershell
dotnet build installer\Wincy.Installer.wixproj -c Release -p:WincyExe=$PWD\publish\Wincy.exe -o installer\out
```

Nothing in the code needs .NET 10 specifically; change `TargetFramework` in
`src/Wincy/Wincy.csproj` to `net8.0-windows` if that is the SDK you have.

Tagging `v*` runs the release workflow, which builds both artefacts and publishes them.

See [CONTRIBUTING.md](CONTRIBUTING.md) for layout, house style and pull requests.

## How it works

| Concern | Mechanism |
|---|---|
| Noticing a copy | `AddClipboardFormatListener` → `WM_CLIPBOARDUPDATE`, on a message-only window |
| Reading a copy | `EnumClipboardFormats` + `GetClipboardData`, raw bytes per format |
| The global shortcut | `RegisterHotKey`; a `WH_KEYBOARD_LL` hook runs only while the popup is open, to drive the cycle gesture |
| Pasting | `SendInput` Ctrl+V, after handing focus back with `AttachThreadInput` |
| Window look | DWM acrylic backdrop, rounded corners and dark mode via `DwmSetWindowAttribute` |
| Tray icon | `Shell_NotifyIcon` directly — no WinForms dependency |
| Storage | SQLite, with content blobs read lazily and duplicates detected by hash |

Wincy is event-driven rather than polled: Windows announces clipboard changes, so there
is no timer. What replaces it is a short **settle delay** (Preferences → Advanced), since
many applications publish their formats across several passes and reading the instant the
notification arrives would capture only the first.

## Credits

Wincy is an independent Windows implementation of [Maccy](https://maccy.app), the macOS
clipboard manager by Alex Rodionov. It reproduces Maccy's behaviour and interaction
design and contains none of its code.

Some things could not carry over unchanged:

- <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> rather than <kbd>Win</kbd>+<kbd>V</kbd>,
  which Windows reserves.
- Clear is <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Backspace</kbd>;
  <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Delete</kbd> is the Secure Attention Sequence and
  cannot be intercepted by any application.
- Sounds are off by default — the Windows alert sounds are assertive enough that a chime
  on every copy reads as a fault.
- The preview does not open by itself, since widening the window while you read the list
  is disruptive unless asked for.
- Images are stored as PNG, with a DIB regenerated on paste. An uncompressed Windows DIB
  would turn a few hundred screenshots into gigabytes.
- Notifications are tray balloons rather than toasts, which would require an MSIX
  identity and stop Wincy being a file you can copy anywhere.

## License

[MIT](LICENSE) — the same as Maccy.
