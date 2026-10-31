# 🏢 Claude Pixel Office

[한국어](README.md) · **English**

**See at a glance what Claude Code is doing and whether it's waiting for you — as a tiny pixel-art office on Windows.**

Each Claude Code conversation (session) becomes an employee at a desk. Their pose, monitor and speech bubble show what they're doing right now,
and they raise a hand when they need your approval or an answer. While you're away, your phone can watch, approve, and hand out the next task.

> Unofficial tool, not affiliated with Anthropic. It only reads the transcript files Claude Code writes.
> **English UI**: the app starts in Korean — open ⚙️ → 사무실 (Office) → **화면 언어 / Language → English**.
> The screen, speech bubbles, notifications and dialogs switch to English, and the phone follows the PC.

![The office in motion](docs/screenshots/demo.gif)

Try it in the browser first, no install: **https://jibong614.github.io/claude-pixel-office/?demo&lang=en**

## Why use it?

1. **Running several Claude Code windows? See who's doing what, at a glance**
   No more clicking through each window.
   e.g. "Cart fixes" is ✏️ writing code, "Design cleanup" is ⌨️ building, "Log review" is ✋ waiting for your approval
2. **Never miss the moment it needs you**
   A waiting employee raises a hand, the window title and taskbar show `(✋2)`, and a Windows notification pops up.
   e.g. while you were writing an email, a session sat at "approve this command?" for 20 minutes — now you notice right away
3. **Know when a long task is done**
   e.g. kick off a 10-minute build or test run, do something else, and get a notification when it ends (click it to open that employee)
4. **Keep work moving while you're away — approve and give the next task from your phone**
   The phone needs no Claude sign-in; scan one QR and it works from anywhere.
   e.g. at lunch: "May I run the tests?" → tap [Approve] on the phone.
   When it's done, send "now tidy up the README" from the phone and the resting session wakes up and **continues the same conversation**
5. **See how much work got done today**
   Work time, prompts, tokens and top tools for every employee (📊 Today's work log)
6. **It's fun to watch**
   Busy coders, dozing employees, empty desks after they go home… The compact view (▤) fits in a corner of the screen

## Screens (shown in Korean UI)

| PC — employee details | PC — settings |
|---|---|
| ![Current work, last instruction, recent actions and today's stats](docs/screenshots/panel.png) | ![Settings as a centered pop-up with tabs](docs/screenshots/settings.png) |

| Phone — office | Phone — approve while away |
|---|---|
| <img src="docs/screenshots/mobile.png" width="260" alt="The office on a phone"> | <img src="docs/screenshots/mobile-approve.png" width="260" alt="Approve or deny on the phone"> |

## Features

- 👥 **Session = employee, working folder = team area (rug), subagent = intern next to the desk**
- 💬 **Shows the actual work**: reading, writing code, running commands, searching, web search, directing interns, planning, writing an answer
- 🔍 **Click an employee for details**: last instruction, recent actions, interns, today's stats, and your own **nickname and memo** (kept even if the session is renamed)
- 🔔 **Desktop notifications**: when someone starts waiting, or finishes a long task. Sound and quiet hours are configurable
- ▤ **Compact widget**: a one-line-per-employee list that stays on top
- 🪟 **Lives in the tray, can run at startup**: closing (✕) only hides it. New versions install from inside the app
- 🔗 **Claude Code connection (recommended)**: asked once on first start — exact approval/question status, and phone instructions and approvals come through it
- 📱 **Phone connection (optional)**: **Turn on → scan one QR**. 🔒 It goes through a free server, locked with a key only your PC and your phone have, so neither the server nor anyone else can read it
- 🌐 Screen language: 한국어 / English
- 🔒 **Changes nothing without asking**: reads transcript files only; the Claude Code settings are edited only after you say yes in a confirmation dialog

## Quick start

1. Download `ClaudePixelOffice-v*-win-x64.zip` from [Releases](https://github.com/jibong614/claude-pixel-office/releases) (after that, new versions install from inside the app: it downloads the release zip, checks its SHA-256, swaps the files and restarts)
2. Unzip and run `ClaudeOffice.exe`
3. On first start, answer **"Connect to Claude Code?"** with **Yes** (recommended)
4. Start a conversation in Claude Code and an employee appears at a desk
5. (Optional) To watch from your phone: ⚙️ → 📱 Phone → **Turn on → Show QR** → scan it with your phone camera

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
| `Stop` · `SessionStart` | a session that finished answering (or just opened) waits in the background, and an instruction from the phone wakes it (`asyncRewake`, up to a day). It reconnects within 15 s when the app restarts (updates etc.) — one per session |

- The lines look like `curl … http://127.0.0.1:47821/…; exit 0` (run by Git Bash; on PCs without it, an equivalent PowerShell command). They only talk to this app on this PC,
  and **always exit successfully** when the app is closed, so no errors show up in Claude Code (only the hook waiting for phone instructions keeps quietly waiting in the background until the app is back)
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
- If you used **your own Firebase project** with 1.1.6 or earlier, it moves to the free server automatically — scan the QR once more (you can delete your old project)

Details (Korean): [docs/MOBILE.md](docs/MOBILE.md)

### While you're away — instruct and approve from the phone

With the Claude Code connection, while you're away the phone can **send instructions** to an employee (session) and **answer permission requests**.
The phone needs no Claude login — the work is done by Claude Code on the PC with its own sign-in.

1. **Just walk away and open the office on your phone.** There's no button to press on the PC or the phone
   - While the phone page is open and the PC has had no input for 30 s, instructions and approvals come to the phone (= Away). Move the mouse or type at the PC and it goes back to the PC
   - Even when you're not looking at the phone, **3 minutes** without input (off/1/5/10 min in Settings) or **locking the screen** (⊞+L, at once) turns Away on — approval requests that arrive meanwhile wait until you open the phone
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
- After connecting, **new or reopened sessions** take instructions right away; sessions that were already open start **after their next answer**. When Claude Office restarts (an update, for example), resting sessions reconnect by themselves within 15 seconds
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

**Does it change my Claude Code settings?** Not without asking. Only when you say yes to the first-start question or the **Claude Code connection** button, and disconnecting takes out only what it added. Once connected, a new version may update just those lines to its new commands without asking (the file from right before is kept as `settings.json.claudeoffice.bak`).

**It's a free server — can others see my office?** No. Only a box locked with a key that just your PC and your phone have passes through it, and the key (after `#` in the QR link) never reaches the server. Just keep the QR to yourself (if someone saw it, use New key).

**An instruction from the phone doesn't arrive.** It is refused while someone is using the PC (input in the last 30 s) — send it a little after you leave. It is delivered right away when the employee details show "📡 Connected" — sessions that were open before you connected Claude Code connect after their next answer. What was received and delivered is logged in `remote.log` in the data folder.

## License and credits

- [MIT License](LICENSE)
- Icon: the pixel-art employee from the office screen (drawn in code by this project, `src/ClaudeOffice/Assets/office_pixel.png`) — MIT License above
- QR codes: [qrcode-generator](https://github.com/kazuhikoarase/qrcode-generator) 1.4.4 — MIT License, © Kazuhiko Arase
- "Claude" is a trademark of Anthropic. This project is not affiliated with Anthropic
