# Wincy

Clipboard history for Windows. It keeps what you copy and lets you search it, pin it,
and paste it back without leaving the keyboard.

Wincy is a Windows counterpart to [Maccy](https://maccy.app), Alex Rodionov's macOS
clipboard manager. It reproduces Maccy's behaviour and layout, but shares none of its
code — this is a fresh implementation in C# on WPF and Win32.

## Installing

Grab the latest [release](https://github.com/iamhayder/wincy/releases):

- **`Wincy-x.y.z-x64.msi`** — the installer. Adds a Start Menu entry and appears in Add
  or Remove Programs, so it uninstalls like anything else.
- **`Wincy-x.y.z-portable-x64.exe`** — the same application in a single file. Nothing to
  install; run it from wherever you like.

Either way the app is self-contained: there is no .NET runtime to install, and your
history lives in `%LOCALAPPDATA%\Wincy`.

Neither download is code-signed, so SmartScreen will warn the first time you run it.
Choose **More info**, then **Run anyway**. Uninstalling leaves `%LOCALAPPDATA%\Wincy`
in place, so reinstalling keeps your history — delete that folder to remove it.

## Known limitations

**Wincy cannot paste into windows running as administrator.** Windows blocks synthetic
keystrokes from a normal-integrity process to an elevated one, so with an admin terminal,
Task Manager or Registry Editor in front, choosing an item copies it but the paste does
not arrive. Press Ctrl+V yourself in that case. Copying *from* elevated applications is
recorded normally, since the clipboard itself is shared.

There is no way around this that is worth the cost: running Wincy elevated would invert
the problem and break pasting into ordinary applications, and the `uiAccess` route
requires a code-signed binary installed under Program Files.

## Building

Requires the **.NET 10 SDK** and Windows 10 1809 or newer.

```powershell
git clone <this repo>
cd wincy
dotnet build
dotnet run --project src\Wincy
```

For a single self-contained executable with no runtime to install:

```powershell
dotnet publish src\Wincy -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
# -> publish\Wincy.exe
```

The runtime identifier is passed here rather than set in the project file on purpose.
Pinning one to the Release configuration makes `restore` and `build -c Release` resolve
different assets, which fails with NETSDK1047.

To build the installer, point it at that executable:

```powershell
dotnet build installer\Wincy.Installer.wixproj -c Release -p:WincyExe=$PWD\publish\Wincy.exe -o installer\out
# -> installer\out\Wincy.msi
```

The installer is a [WiX](https://wixtoolset.org) project and is deliberately **not** part
of `Wincy.sln`, since building an MSI needs Windows and would otherwise stop the solution
building on other platforms. CI builds it on every push, so a broken installer surfaces
straight away rather than at release time.

Tagging `v*` runs the release workflow, which builds both artefacts and publishes them to
a GitHub Release.

Only the .NET 10 SDK is a hard requirement of the TFM, not of the code. If you have the
.NET 8 SDK instead, change one line in `src/Wincy/Wincy.csproj`:

```xml
<TargetFramework>net8.0-windows</TargetFramework>
```

There is nothing to install and no MSIX packaging: Wincy is one executable that keeps
its data in `%LOCALAPPDATA%\Wincy`.

## Using it

Press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> to open the popup, or click the tray
icon. Then type to search.

| | |
|---|---|
| <kbd>Enter</kbd> | Copy the selected item |
| <kbd>Alt</kbd>+<kbd>Enter</kbd> | Copy **and paste** it |
| <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>Enter</kbd> | Paste without formatting |
| <kbd>Ctrl</kbd>+<kbd>1</kbd>…<kbd>9</kbd> | Copy the *n*-th item directly |
| <kbd>Alt</kbd>+<kbd>1</kbd>…<kbd>9</kbd> | Paste the *n*-th item directly |
| <kbd>↑</kbd> <kbd>↓</kbd> | Move the selection |
| <kbd>PgUp</kbd> / <kbd>PgDn</kbd> | Jump to the first / last item |
| <kbd>Alt</kbd>+<kbd>P</kbd> | Pin or unpin |
| <kbd>Alt</kbd>+<kbd>Backspace</kbd> | Delete the item |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Backspace</kbd> | Clear unpinned items |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>Backspace</kbd> | Clear everything |
| <kbd>Alt</kbd>+<kbd>Space</kbd> | Toggle the preview pane |
| <kbd>Ctrl</kbd>+<kbd>,</kbd> | Preferences |
| <kbd>Esc</kbd> | Close |

The badge on the right of each row always shows what the modifiers you are *currently*
holding will do, so you never have to remember the table above.

**Pinning** moves an item to the top of the list and gives it a permanent letter. From
then on <kbd>Ctrl</kbd>+<kbd>that letter</kbd> copies it and <kbd>Alt</kbd>+<kbd>that
letter</kbd> pastes it, from anywhere in the list.

**Cycling**: hold <kbd>Ctrl</kbd>+<kbd>Shift</kbd> and tap <kbd>V</kbd> repeatedly to
step down the history. Release the modifiers and the item you landed on is taken. This
is the fastest way to reach the last few things you copied.

**Pausing**: <kbd>Alt</kbd>-click the tray icon to stop recording;
<kbd>Alt</kbd>+<kbd>Shift</kbd>-click it to skip only the next copy.

Emacs-style navigation (<kbd>Ctrl</kbd>+<kbd>N</kbd>/<kbd>P</kbd>/<kbd>J</kbd>/<kbd>K</kbd>,
<kbd>Ctrl</kbd>+<kbd>U</kbd>/<kbd>W</kbd>/<kbd>H</kbd> in the search field) works too.

## What it stores

Text, rich text, HTML, images and file paths — each as the exact bytes the source
application published, so pasting reproduces the original rather than an approximation.
Images are transcoded to PNG on capture, which keeps a history full of screenshots to
megabytes rather than gigabytes.

Everything lives in a single SQLite database in `%LOCALAPPDATA%\Wincy\history.db`.
Nothing is sent anywhere. There is no network code in this application.

## Privacy

Wincy honours the standard Windows clipboard privacy formats, so copies from password
managers and private browsing sessions are never recorded:

- `ExcludeClipboardContentFromMonitorProcessing`
- `CanIncludeInClipboardHistory` (set to 0)
- `CanUploadToCloudClipboard` (set to 0)
- `Clipboard Viewer Ignore`

Beyond that, **Preferences → Ignore** lets you exclude applications by executable name,
discard copies matching a regular expression, and add further clipboard formats to the
ignore list.

## Preferences

Six panes, mirroring Maccy's:

- **General** — launch at login, shortcuts, search mode, whether Enter copies or pastes
- **Storage** — which kinds to save, how many items, sort order, database size
- **Appearance** — where the popup opens, on which screen, pin placement, highlight
  style, preview timing, tray icon, and which chrome to show
- **Pins** — reassign or release pin letters
- **Ignore** — applications, patterns, clipboard formats
- **Advanced** — pause recording, clearing behaviour, clipboard settle delay, logs

## How it works

| Concern | Mechanism |
|---|---|
| Noticing a copy | `AddClipboardFormatListener` → `WM_CLIPBOARDUPDATE`, on a message-only window |
| Reading a copy | `EnumClipboardFormats` + `GetClipboardData`, raw bytes per format |
| The global shortcut | `RegisterHotKey`; a `WH_KEYBOARD_LL` hook runs only while the popup is open, to drive the cycle gesture |
| Pasting | `SendInput` Ctrl+V, after handing focus back with `AttachThreadInput` |
| Window look | DWM acrylic backdrop, rounded corners and dark mode via `DwmSetWindowAttribute` |
| Tray icon | `Shell_NotifyIcon` directly — no WinForms dependency |
| Storage | SQLite, with content blobs read lazily and deduplicated by hash |

Wincy is event-driven rather than polled. Maccy checks `NSPasteboard.changeCount` every
500 ms; Windows will simply tell you when the clipboard changes, so there is no timer.
What replaces it is a short *settle delay* (Preferences → Advanced): many applications
publish their formats in several passes, and reading the instant the notification
arrives would capture only the first one.

### Layout

```
src/Wincy/
├── Interop/      Win32: clipboard, hotkeys, hooks, DWM, tray, monitors, icons
├── Models/       ClipItem, ClipContent, clipboard format bookkeeping
├── Services/     capture and write-back, search, sorting, storage, settings
├── ViewModels/   AppState, history, navigation, footer, shortcut matrix
├── Views/        popup, preferences, about, and the custom row renderer
└── Themes/       colours and control styles, re-derived from the system theme

tests/Wincy.Tests/
                  clipboard format parsers, search, dedup, sorting, colour
                  swatches, screen placement, and the modifier matrix

installer/        WiX sources for the MSI
```

## Tests

```powershell
dotnet test
```

The suite covers the parts that can be checked without a screen: the HDROP, HTML and
RTF parsers, all four search modes and their match ranges, hash-based duplicate
detection, sort and pin ordering, colour-swatch parsing, and the copy/paste modifier
matrix — including a check that every action stays reachable under all four
combinations of the two "by default" settings.

The tests reference the app assembly, which targets Windows, so `dotnet test` needs to
run on Windows. CI does this on every push.

## Deliberate differences from Maccy

Some of Maccy's behaviour does not survive the move to Windows unchanged:

- **The shortcut is Ctrl+Shift+V, not Win+V.** Windows reserves Win+V for its own
  clipboard history and will not hand it to another process.
- **Clear is Ctrl+Alt+Backspace, not Ctrl+Alt+Delete.** Ctrl+Alt+Delete is the Secure
  Attention Sequence and cannot be intercepted by any application.
- **Sounds are off by default.** Maccy plays one on every copy; the Windows alert sounds
  are assertive enough that doing the same reads as a fault. Turn it on in General.
- **The preview does not open by itself.** Widening the window while you are reading the
  list is disruptive unless you asked for it. Alt+Space opens it, and Appearance has both
  the automatic setting and a choice of which side it appears on.
- **Images are stored as PNG.** Maccy keeps the original representation; a Windows DIB is
  uncompressed, so a few hundred screenshots would run to gigabytes. A DIB is
  regenerated on paste, so compatibility is unaffected.
- **Notifications use a tray balloon**, not a toast. Toasts require an MSIX identity,
  and Wincy is deliberately a plain executable you can copy anywhere.
- **Multi-selection and the paste stack are implemented but off**, matching Maccy, where
  `multiSelectionEnabled` currently ships as `false`.

## Licence

MIT, the same as Maccy.
