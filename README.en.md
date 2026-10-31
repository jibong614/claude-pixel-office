# 🏢 Claude Pixel Office

[한국어](README.md) · **English**

**A Windows app that shows your Claude Code sessions as a tiny pixel-art office.**
Each session becomes an employee at a desk. Their pose, monitor and speech bubble show what they are doing right now.
Employees raise a hand when they are waiting for your approval, and you can watch the office from your phone too.

> Unofficial tool, not affiliated with Anthropic. It only reads the transcript files Claude Code writes.
> **English UI**: the app starts in Korean — open ⚙️ → 사무실 (Office) → **화면 언어 / Language → English**.
> The screen, speech bubbles, notifications and dialogs switch to English, and the phone follows the PC.

![The office in motion](docs/screenshots/demo.gif)

Try it in the browser without installing: **https://jibong614.github.io/claude-pixel-office/?demo&lang=en**

## Features

- 👥 **Session = employee, working folder = team area (rug), subagent = intern next to the desk**
- 💬 **Shows the actual work**: reading, writing code, running commands, searching, web search, directing interns, planning, writing an answer
- ✋ **Never miss a moment that needs you**: waiting for a question or approval raises a hand; the window title and taskbar show `(✋2)`
- 🔔 **Desktop notifications**: when someone starts waiting, or finishes a long task. Click to open that employee. Sound and quiet hours are configurable
- 🔍 **Click an employee for details**: last instruction, recent actions, interns, your own **nickname and memo**, and **today's stats** (work time, prompts, tokens, top tools)
- 📊 **Today's work log**: work time, prompts and tokens for every employee
- ▤ **Compact widget**: shrink the window to a one-line-per-employee list that stays on top
- 📱 **Phone connection (optional)**: **Turn on → scan one QR** and you're done — no setup, no sign-in, no account. Works anywhere. While you're away the phone can **approve or deny permission requests** and **send an instruction to an employee** (the idle session wakes up and continues the same conversation). 🔒 It goes through a free server, locked with a key only your PC and your phone have, so neither the server nor anyone else can read it
- 🔗 **Claude Code connection (recommended)**: asked once on first start — exact approval/question status plus phone instructions and approvals (a few lines in the Claude Code settings, disconnect any time)
- 🔒 **Changes nothing without asking**: reads transcript files only. The Claude Code settings are edited only after you say yes in a confirmation dialog

## Quick start

1. Download `ClaudePixelOffice-v*-win-x64.zip` from [Releases](https://github.com/jibong614/claude-pixel-office/releases) (from 1.0.9 on, new versions install from inside the app: it downloads the release zip, checks its SHA-256, swaps the files and restarts)
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
| 🔕 | Not connected to Claude Code — click to connect (disappears once fully connected; see below) |
| 🚪 / 📌 / ⚙️ | Show/hide employees who went home / always on top / settings |
| Tray icon 🧑‍💻 | Click to open, right-click → Exit. Closing the window (✕) only hides it by default |

Settings (⚙️) open as a centered pop-up with tabs — 🏢 Office · 👥 Employees · 🔔 Notifications · 📱 Phone · 🖥️ General · ℹ️ About (the last tab is remembered; Esc or a click outside closes it). They include: office name, screen language (한국어 / English), display period (12h–7d), how many employees and how to pick them (most recent / manual checklist), wait-only filter, office size, window behavior (start hidden, close to tray, run at Windows startup; position and size are remembered), notifications, Claude Code connection, phone connection, update check with one-click **Update**, and the data folder.

Command line: `--demo` (sample office), `--exit` (quit the running instance), `--autostart` (start hidden in the tray), `--offscreen` (for test tools).

## Claude Code connection (recommended)

Transcripts do not record "a permission dialog is open", so without the connection the app only *guesses* (a file edit that doesn't finish).
Say **yes** when asked "Connect to Claude Code?" on first start, or later use ⚙️ → 🖥️ General → **Claude Code connection** (or 🔕 in the top bar).
After a confirmation dialog it adds a few lines (hooks) to `~/.claude/settings.json`; phone instructions and approvals come through the same connection.

| Added (hook) | What it does |
|---|---|
| `PermissionRequest` | ✋ raises a hand until that tool call finishes (approved and done, rejected or interrupted). While you're away it waits for Approve/Deny from the phone instead (up to 55 min, then the normal prompt) |
| `Notification` | permission and input prompts |
| `SessionEnd` | the employee goes home immediately |
| `Stop` · `SessionStart` | a session that finished answering (or just opened) waits in the background, and an instruction from the phone wakes it (`asyncRewake`, up to a day) |

- The lines look like `curl … http://127.0.0.1:47821/…; exit 0` (run by Git Bash; on PCs without it, an equivalent PowerShell command). They only talk to this app on this PC,
  and **always exit successfully** when the app is closed, so no errors show up in Claude Code
- Disconnecting removes only the lines this app added. A backup is kept as `settings.json.claudeoffice.bak`
- There is no hook for "approved". While a Bash/PowerShell approval is pending, the app lowers the hand as soon as **a new process running that command appears** (so an approved long build shows as running). If it can't tell (e.g. an encoded command), the hand comes down when the command finishes

## Phone connection (optional)

⚙️ → 📱 Phone → **Phone connection: Turn on** → **Show QR** → scan it with your phone camera. That's it — no setup, sign-in or account, and it works anywhere (installable as an app).

- 🔒 It goes through a **free server** — the phone page is this repo's shared page (GitHub Pages) and delivery is Google Firebase (free tier).
  The PC compresses and locks **only the screen summary** (never the raw conversation) with AES-256-GCM, and the phone **decrypts on the device**.
  The key lives only after the `#` in the QR link, which browsers never send to any server, so **neither the server nor other users can read it.** Each PC can write only to its own slot
- When you quit the PC app, the phone shows `💤 Off on the PC` right away and the office dims
- You can edit nicknames and memos from the phone too (turn it off under ⚙️ → 📱 Phone → Advanced)
- It runs within the free tier — with very heavy use it may pause for a while, but there is never a bill
- If you used **your own Firebase project** up to 1.1.6, it moves to the free server automatically — scan the QR once more (you can delete your old project)

Details (Korean): [docs/MOBILE.md](docs/MOBILE.md)

### While you're away — instruct and approve from the phone

With the Claude Code connection, while you're away the phone can **send instructions** to an employee (session) and **answer permission requests**.
The phone needs no Claude login — the work is done by Claude Code on the PC with its own sign-in.

1. Just walk away — nothing to do on the PC (it can stay hidden in the tray)
   - After **3 minutes** without mouse or keyboard input (1/5/10 min or off in Settings) Away turns on by itself, and it turns off when you're back and move the mouse
   - Even sooner: once the PC has been idle for 30 s, sending an instruction from the phone turns it on
   - To get even the approval requests right after you leave on the phone, **lock the screen** (⊞+L) as you go — Away turns on at once and off when you unlock
2. On the phone, tap an employee to see the **📱 Instruct & approve from the phone** box
   - ✋ **Approval request**: see exactly what it wants to run, then Approve / Deny. When denying you can write what to do instead, and Claude follows it
   - **Send an instruction**: the idle session wakes up and **continues the same conversation**. If it is busy, the instruction is delivered as soon as the current work ends
   - 💬 **Last answer**: Claude's latest reply

- **Safety**
  - **While someone is using the PC** (input in the last 30 s) the phone cannot turn Away on — instructions sent then are dropped and approvals go to the PC prompt
  - What the phone sends is locked with the same key (AES-256-GCM), so only a device that scanned the QR can send; messages older than 10 minutes or already seen are dropped
  - Claude's permission rules stay as they are — anything that needs approval still asks (on the phone)
  - Every instruction, approval and denial is logged in `remote.log` in the data folder
  - **The QR/link is the key.** If someone saw it, use **New key** under ⚙️ → 📱 Phone
- After connecting, **new or reopened sessions** take instructions right away; sessions that were already open start **after their next answer**. A short Claude Office restart (such as an update) reconnects them after 15 seconds; sessions that finished while it was closed connect after their next answer
- Claude's last answer is included in the locked summary (only with the Claude Code connection)
- Questions (AskUserQuestion) need a choice, so they are not sent to the phone — answer them at the PC

## How it works

Claude Code appends each session to `%USERPROFILE%\.claude\projects\<project>\<session-id>.jsonl` (or under `CLAUDE_CONFIG_DIR`).
The app scans that folder every 0.7 s and reads **only appended bytes** (the last 2 MB the first time it sees a large file). The office is drawn on an HTML canvas **entirely in code, with no image assets**.

```
src/ClaudeOffice/
  Monitoring/   folder watcher, line parser, tool → action/bubble catalog, hook state, today's stats, snapshot JSON
  Hooks/        Claude Code connection — 127.0.0.1 hook receiver, settings.json hook install/remove
  Cloud/        phone connection — locked summary upload to the free server (Firebase), anonymous sign-in
  Remote/       delivers phone instructions and approvals to sessions through hooks, Away
  MainWindow    WebView2 host, tray icon, notifications, update check, autostart
  wwwroot/      the office screen (canvas pixel art) — same files for the PC app and the phone page
mobile/         free-server database rules
tools/          test tools (offscreen capture, in-page test steps, fake free server), rules deploy
```

### Data and network

- **Reads only** Claude Code transcripts. Conversation content is never sent anywhere (except the locked summary when the phone connection is on)
- **Stores** in `%LOCALAPPDATA%\ClaudeOffice`: screen settings, a copy of nicknames/memos (`share.json`), `app.json`, the phone-connection key (`cloud.json`), the Claude Code notice log, the phone instruction log (`remote.log`)
- **Network**: the Claude Code connection listens on `127.0.0.1` only and ignores requests from browsers. Only with the phone connection on is the locked summary sent to the free server (Google Firebase); with the Claude Code connection, Claude's last answer is part of it. The update check asks the GitHub API for the latest release twice a day and when you open Settings (at most every 10 minutes; only the app version is sent; can be turned off)

## FAQ

**I don't see an employee.** Sessions whose last message is older than the display period (default 12 hours) are hidden. Increase it in ⚙️.

**Closing the window doesn't quit.** It is hidden in the tray (🧑‍💻). Right-click the tray icon → Exit, or change it in ⚙️.

**Does it show Claude Code running on another PC or in WSL?** No. It reads the transcript folder of the Windows user running the app.

**Does it change my Claude Code settings?** Not without asking. Only when you say yes to the first-start question or the **Claude Code connection** button, and disconnecting takes out only what it added.

**It's a free server — can others see my office?** No. Only a box locked with a key that just your PC and your phone have passes through it, and the key (after `#` in the QR link) never reaches the server. Just keep the QR to yourself (if someone saw it, use New key).

**An instruction from the phone doesn't arrive.** It is refused while someone is using the PC (input in the last 30 s) — send it a little after you leave. Sessions that were already open connect after their next answer — check "📡 Connected" in the employee details.

## License and credits

- [MIT License](LICENSE)
- Icon: the pixel-art employee from the office screen (drawn in code by this project, `src/ClaudeOffice/Assets/office_pixel.png`) — MIT License above
- QR codes: [qrcode-generator](https://github.com/kazuhikoarase/qrcode-generator) 1.4.4 — MIT License, © Kazuhiko Arase
- "Claude" is a trademark of Anthropic. This project is not affiliated with Anthropic
