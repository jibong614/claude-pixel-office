# 🏢 Claude Pixel Office

[한국어](README.md) · **English**

**A Windows app that shows your Claude Code sessions as a tiny pixel-art office.**
Each session becomes an employee at a desk. Their pose, monitor and speech bubble show what they are doing right now.
Employees raise a hand when they are waiting for your approval, and you can watch the office from your phone too.

> Unofficial tool, not affiliated with Anthropic. It only reads the transcript files Claude Code writes.
> **The app's interface is in Korean** (an English UI is planned). This page explains everything you need.

![The office in motion](docs/screenshots/demo.gif)

Try it in the browser without installing: **https://jibong614.github.io/claude-pixel-office/?demo**

## Features

- 👥 **Session = employee, working folder = team area (rug), subagent = intern next to the desk**
- 💬 **Shows the actual work**: reading, writing code, running commands, searching, web search, directing interns, planning, writing an answer
- ✋ **Never miss a moment that needs you**: waiting for a question or approval raises a hand; the window title and taskbar show `(✋2)`
- 🔔 **Desktop notifications**: when someone starts waiting, or finishes a long task. Click to open that employee. Sound and quiet hours are configurable
- 🔍 **Click an employee for details**: last instruction, recent actions, interns, your own **nickname and memo**, and **today's stats** (work time, prompts, tokens, top tools)
- 📊 **Today's work log**: work time, prompts and tokens for every employee
- ▤ **Compact widget**: shrink the window to a one-line-per-employee list that stays on top
- 📱 **Phone view (optional)**: only a screen summary, encrypted on your PC, goes to your own Firebase. Works outside your network, no login on the phone. An in-app **setup wizard** walks you through it
- 🔒 **Changes nothing by default**: reads transcript files only. Hooks are optional and installed only after a confirmation dialog

## Quick start

1. Download `ClaudePixelOffice-v*-win-x64.zip` from [Releases](https://github.com/jibong614/claude-pixel-office/releases)
2. Unzip and run `ClaudeOffice.exe`
3. Start a conversation in Claude Code and an employee appears at a desk

- Requires Windows 10/11 and the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (built into Windows 11). No .NET install needed
- The exe is not code-signed, so SmartScreen may warn on first run → **More info → Run anyway**
- Demo without any transcripts: `ClaudeOffice.exe --demo`

Build from source (.NET 8 SDK): `dotnet build ClaudeOffice.sln -c Release`

## Using it

| Control | What it does |
|---|---|
| Click an employee | Detail panel on the right (click again to fold/unfold). The office re-lays out so the panel never covers anyone |
| ▤ | Compact widget (list view, small always-on-top window) |
| 📊 | Today's work log |
| 🔕 / 🔔 | Hook status — click to install/remove hooks (see below) |
| 🚪 / 📌 / ⚙️ | Show/hide employees who went home / always on top / settings |
| Tray icon 🏢 | Click to open, right-click → Exit. Closing the window (✕) only hides it by default |

Settings (⚙️) include: office name, display period (12h–7d), how many employees and how to pick them (most recent / manual checklist), wait-only filter, office size, window behavior (start hidden, close to tray, run at Windows startup; position and size are remembered), notifications, hooks, phone view, update check and the data folder.

Command line: `--demo` (sample office), `--exit` (quit the running instance), `--autostart` (start hidden in the tray), `--offscreen` (for test tools).

## Exact approval status (hooks, optional)

Transcripts do not record "a permission dialog is open". Without hooks the app only *guesses* (a file edit that doesn't finish).
Click 🔕 to add three hooks to `~/.claude/settings.json` after a confirmation dialog:

| Hook | In the office |
|---|---|
| `PermissionRequest` | ✋ raises a hand until that tool call finishes (approved and done, rejected or interrupted) |
| `Notification` | permission and input prompts |
| `SessionEnd` | the employee goes home immediately |

- The hook command is `curl … http://127.0.0.1:47821/hook; exit 0` (run by Git Bash). On PCs without Git Bash an equivalent PowerShell command (`"shell": "powershell"`) is installed instead
- It always exits successfully, even when the app is closed, so no errors show up in Claude Code and it never approves or blocks anything
- Click 🔔 again to remove only the hooks this app added. A backup is kept as `settings.json.claudeoffice.bak`
- Limitation: there is no hook for "approved", so a long command that was approved keeps the hand raised until it finishes

## Phone view (optional)

The PC compresses and encrypts **only the screen summary** (never the raw conversation) with AES-256-GCM and uploads it to your own Firebase Realtime Database (free tier).
Your phone opens a shared web page from a QR code, receives updates in real time and **decrypts on the device**.
The decryption key lives only after the `#` in the QR link, which browsers never send to any server. The page can be installed as an app.

Setup takes about 10 minutes with a Google account: ⚙️ → 휴대폰에서 보기 (Phone view) → **설정 도우미 (Setup wizard)**.
Each of the 6 steps opens the right Firebase console page and the app checks the result automatically.
No Node.js or command line is needed. Details (Korean): [docs/MOBILE.md](docs/MOBILE.md)

## How it works

Claude Code appends each session to `%USERPROFILE%\.claude\projects\<project>\<session-id>.jsonl` (or under `CLAUDE_CONFIG_DIR`).
The app scans that folder every 0.7 s and reads **only appended bytes** (the last 2 MB the first time it sees a large file). The office is drawn on an HTML canvas **entirely in code, with no image assets**.

```
src/ClaudeOffice/
  Monitoring/   folder watcher, line parser, tool → action/bubble catalog, hook state, today's stats, snapshot JSON
  Hooks/        127.0.0.1 hook receiver, settings.json hook install/remove
  Cloud/        phone view — encrypted summary upload to Firebase, anonymous sign-in, setup checks
  MainWindow    WebView2 host, tray icon, notifications, update check, autostart
  wwwroot/      the office screen (canvas pixel art) — same files for the PC app and the phone page
mobile/         Firebase Hosting config and database rules
tools/          test tools (offscreen capture, in-page test steps, fake Firebase)
```

### Data and network

- **Reads only** Claude Code transcripts. Conversation content is never sent anywhere (except the encrypted summary when phone view is on)
- **Stores** in `%LOCALAPPDATA%\ClaudeOffice`: screen settings, a copy of nicknames/memos (`share.json`), `app.json`, the phone-view key (`cloud.json`), the hook log
- **Network**: the hook receiver listens on `127.0.0.1` only. The update check asks the GitHub API for the latest release twice a day (only the app version is sent; can be turned off)

## FAQ

**I don't see an employee.** Sessions whose last message is older than the display period (default 12 hours) are hidden. Increase it in ⚙️.

**Closing the window doesn't quit.** It is hidden in the tray (🏢). Right-click the tray icon → Exit, or change it in ⚙️.

**Does it show Claude Code running on another PC or in WSL?** No. It reads the transcript folder of the Windows user running the app.

**Does it change my Claude Code settings?** Not by default. Only when you install hooks (🔕) and confirm, and removing them takes out only what it added.

## License and credits

- [MIT License](LICENSE)
- Icon 🏢: Microsoft [Fluent Emoji](https://github.com/microsoft/fluentui-emoji) "Office building" — MIT License, © Microsoft Corporation
- QR codes: [qrcode-generator](https://github.com/kazuhikoarase/qrcode-generator) 1.4.4 — MIT License, © Kazuhiko Arase
- "Claude" is a trademark of Anthropic. This project is not affiliated with Anthropic
