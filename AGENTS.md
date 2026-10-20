# Agent instructions

This repository is a Windows desktop widget. User installation instructions are in [INSTALL.md](INSTALL.md); features and controls are in [README.md](README.md).

## Install for a user

- Read `install.ps1` first. Use Windows PowerShell 5.1+ on Windows x64 with .NET Framework 4.7.2+.
- Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1`. The default per-user destination is `%LOCALAPPDATA%\CodexQuotaWidget`.
- Never replace the user's `preferences.json`. Autostart is opt-in (`-StartWithWindows`). Default mode follows Codex and is not always on top.
- Do not ask users for authentication tokens or copy `auth.json`. Codex must already be installed and signed in via its official login flow.
- Do not run real quota probes or previews just to verify an install. The offline tests below do not require an account. A probe writes personal quota data next to the executable; do not publish it.
- A following widget hides while the Codex / GPT host is minimized or absent. Open the host before diagnosing an invisible widget.
- A single-instance signal is shared within each Windows login session. Do not launch another copy during testing: it may reveal the user's existing widget. Use `-NoLaunch -NoShortcut` for staging installs.

## Development

Build: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\out\build`

Offline tests: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1`

Release ZIP: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package.ps1`

The tests use a fake stdio app-server and their own off-screen native windows. They do not move or minimize the user's Codex window. A Windows desktop session is required for the UI/native tests.

`Widget.cs` handles read-only quota RPC/session fallback and settings. `RingWidget.cs` and `Ring.xaml` implement the ring/card. `WindowAttachment.cs` handles host discovery, positioning and visibility.

Keep builds under `out/`; the user may be running a binary in the source root. Never overwrite that binary without arranging an upgrade. Keep personal settings, diagnostics, previews, archives, shortcuts and authentication data out of commits and packages. Package via the explicit file allowlist in `package.ps1`. Do not add dependencies or collect telemetry for installation.
