# Contributing to Wincy

Thanks for taking an interest.

## Getting set up

You need the **.NET 10 SDK** and **Windows 10 1809 or newer**.

```powershell
git clone https://github.com/iamhayder/wincy.git
cd wincy
dotnet build
dotnet test
dotnet run --project src\Wincy
```

The solution builds on macOS and Linux too, if you set `EnableWindowsTargeting`, which
is useful for catching compile errors. The tests and the app itself need Windows.

## Where things live

| Path | What's in it |
|---|---|
| `src/Wincy/Interop/` | Win32: clipboard, hotkeys, hooks, DWM, tray, monitors, icons |
| `src/Wincy/Models/` | `ClipItem`, `ClipContent`, clipboard format bookkeeping |
| `src/Wincy/Services/` | Capture and write-back, search, sorting, storage, settings |
| `src/Wincy/ViewModels/` | App state, history, navigation, footer, the shortcut matrix |
| `src/Wincy/Views/` | Popup, preferences, about, custom controls |
| `src/Wincy/Themes/` | Colours and control styles, re-derived from the system theme |
| `tests/Wincy.Tests/` | Everything testable without a screen |
| `installer/` | WiX sources for the MSI |

The installer is **not** in `Wincy.sln` on purpose: building an MSI requires Windows, and
including it would stop the solution building anywhere else.

## What is worth testing

Anything that can be checked without a window: the clipboard format parsers, search and
its match ranges, duplicate detection, sorting, screen placement arithmetic, and the
copy/paste modifier matrix. Those have good coverage and new logic there should keep it.

UI behaviour is not covered by automated tests, so if you change the popup, say in the
pull request what you exercised by hand.

## House style

`.editorconfig` carries the settings; your editor should apply them. Beyond that:

- Comments explain **why**, not what. If a line needs a comment to say what it does, the
  line usually wants rewriting instead.
- Win32 interop keeps the platform's own names, so it can be read next to the
  documentation. `NativeMethods.cs` is exempt from the naming rules for that reason.
- Prefer a small, well-named private method over a comment introducing a block.

## Pull requests

- Branch off `main`.
- Keep each pull request to one concern.
- Say what you verified, and on which version of Windows.
- CI must pass: build, tests, and the installer all have to come out clean.

## Reporting bugs

Open an issue with your Windows version, how you installed Wincy, what you expected, and
what happened. If it crashed or misbehaved, `%LOCALAPPDATA%\Wincy\wincy.log` usually says
something useful — check it for clipboard contents before pasting it into a public issue.

Security problems go through [SECURITY.md](SECURITY.md) instead, not a public issue.
