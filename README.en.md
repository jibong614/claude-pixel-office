# 🏢 Claude Pixel Office

[한국어](README.md) · **English**

**See at a glance what Claude Code is doing and whether it's waiting for you — as a tiny pixel-art office on Windows.**

Each Claude Code conversation (session) becomes an employee at a desk. Their pose, monitor and speech bubble show what they're doing right now,
and they raise a hand when they need your approval or an answer.
Watch over them with the floating **MiniPet (🐾)** on your desktop while doing other work, and when you're away, **instruct and approve directly from your phone (📱)** from anywhere.
Decorate with an **Office Decor Shop (🛋️)** and coins, enjoy **Autonomous Idle AI (☕)** with stretches and coffee breaks, **24-hour real-time day/night lighting (🌙)**, collapsible **Git Diff previews (🔍)** on mobile before approving, and **Voice-to-Prompt instructions (🎙️)**.

> Unofficial tool, not affiliated with Anthropic. It only reads the transcript files Claude Code writes.
> **English UI**: the app starts in Korean — open ⚙️ → 사무실 (Office) → **화면 언어 / Language → English**.
> The screen, speech bubbles, notifications and dialogs switch to English, and the phone follows the PC.

![The office in motion](docs/screenshots/demo.gif)

Try it in the browser first, no install: **https://jibong614.github.io/claude-pixel-office/?demo&lang=en**

## Why use it?

1. **Running several Claude Code windows? See who's doing what, at a glance**
   No more clicking through each terminal window.
   e.g. "Cart fixes" is ✏️ writing code, "Design cleanup" is ⌨️ building, "Log review" is ✋ waiting for your approval
2. **🐾 Desktop MiniPet — Shimeji Walk Mode, Ultra-Slim Dynamic Island & Dev Memes**
   Even when your code editor or browser is full screen, a tiny transparent pixel pet stays with you:
   - 🚶 **Shimeji Walk Mode**: Agent steps away from the desk to stroll, turn around, jump, and hop across your screen
   - 🏝️ **Ultra-Slim Dynamic Island**: 38px pill capsule floating neatly at the top of your screen; expands on hover for instant `[⚡ Console Jump]`, `[🛑 Stop]`, and `[Approve]` actions
   - 💡 **Developer Memes Easter Egg**: Click to trigger 25+ hilarious developer quotes in speech bubbles ("Works on my machine!", "It's an undocumented feature!", etc.)
   - Approve tool requests or send the next task (text or 🎙️ voice) directly from the MiniPet.
3. **📱 Keep work moving while you're away — Send instructions & emergency stop from your phone**
   The phone needs no Claude sign-in; scan one QR code and watch live conversations over LTE/5G.
   - 🔍 **Pre-Approval Git Diff Review**: When asked to edit files, expand the unified diff (`+N`, `-N`) on your phone to review changes before tapping **[Approve]**
   - 🎙️ **Voice-to-Prompt**: Tap the microphone to dictate instructions on the go; speech is transcribed in real-time
   - When asked a question → **tap multiple-choice options directly on your phone to answer**
   - Finished a task? Send **"Now write unit tests for this function"** and the resting session wakes up and **continues the same conversation**
   - Stuck in an infinite loop or runaway build? One tap on **[🛑 Emergency Stop]** to safely halt immediately
   - Pinch-to-zoom (1.0x–3.5x) and double-tap to focus on any agent on mobile
4. **🛋️ Office Decor Shop & Coins — Gamified pixel workspace**
   Earn 🪙 coins by completing tasks and saving prompt cache tokens. Purchase 7 interactive props (Espresso machine, office cat 'Nabi', retro arcade cabinet, vending machine, etc.) with unique 8-bit sounds and animations on click.
5. **☕ Autonomous Idle Actions & 24-Hour Day/Night Lighting**
   Idle agents aren't static—they stretch (`\o/`), sip warm coffee, and share random developer thoughts in speech bubbles. The office lighting matches local PC time (Day, Sunset, Night, Night Shift) with spotlight cones over late-night desks.
6. **📋 Office Whiteboard — Real-time project TODOs at a glance**
   Plans and tasks agreed upon in conversations automatically populate the whiteboard next to the window. Get a clear overview of project progress in real time.
7. **🏆 Quest Book — Gamified feature walkthrough**
   Complete **30 master achievements** including office decor, coffee breaks, mobile dictation, and late-night coding. Level up as you master every feature of Claude Pixel Office.
8. **Never miss the moment it needs you**
   A waiting employee raises a hand, the window title and taskbar show `(✋2)`, and a Windows notification pops up.
9. **See how much work got done today & cost savings**
   Work time, prompts, token counts, and cost saved via prompt caching for every employee (📊 Today's work log).

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
- 🛋️ **Office Decor Shop & Coin System**: Earn 🪙 coins by finishing tasks (+5) and saving cache tokens (+10). Buy 7 interactive props (Espresso machine, office cat, arcade cabinet, etc.) with custom 8-bit sound effects and click animations
- ☕ **Autonomous Idle AI**: Resting agents don't stand still—they stretch (`\o/`), sip warm coffee, and pop up developer-relatable 💭 random thought bubbles
- 🌙 **24-Hour Day/Night Lighting & Night Shift Spotlights**: Dynamic daylight, sunset glow, evening, and late-night lighting matching local time, casting spotlight cones onto active desks
- 🎵 **8-Bit Retro Sound Pack**: Synthesized in real-time via Web Audio API—coin sounds, purchase fanfares, cat meows, coffee sips, arcade bleeps, and alarms
- 🐾 **Desktop MiniPet**: An always-on-top transparent companion. Shows representative agent speech bubbles, quick approvals, instruction input (text & 🎙️ voice), and emergency stop
- 📱 **Powerful mobile remote control**: **Turn on → scan one QR**. Remote notifications, **🔍 pre-approval Git Diff review**, approvals, **🎙️ Voice-to-Prompt & text instructions**, multiple-choice answers, **real-time emergency stop (🛑)**, and **🔎 touch pinch-to-zoom (1.0x–3.5x) with double-tap focus**. End-to-end encrypted (AES-256-GCM) via free server
- 📋 **Whiteboard**: Real-time project TODO list automatically extracted from session conversations
- 🏆 **Achievements (Quest Book)**: 30 achievements, level/EXP system, completed quest filter, bonus coin rewards
- 🛑 **Dual-action Emergency Stop**: Terminates running tool process trees immediately + blocks subsequent tool calls via `PreToolUse` hook in 0.01 seconds without disturbing Windows idle time
- 🔍 **Click an employee for details**: last instruction, recent actions, interns, today's stats, and your own **nickname and memo** (kept even if the session is renamed)
- 🔔 **Desktop notifications**: when someone starts waiting, or finishes a long task. Sound and quiet hours are configurable
- ▤ **Compact widget**: a one-line-per-employee list that stays on top
- 🪟 **Lives in the tray, can run at startup**: closing (✕) only hides it. New versions install from inside the app
- 🔗 **Claude Code connection (recommended)**: asked once on first start — exact approval/question status, and phone instructions and approvals come through it
- 🌐 Screen language: 한국어 / English
- 🔒 **Safety**: Reads transcript files only; the Claude Code settings are edited only after you say yes in a confirmation dialog

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

| 무엇 / Control | 하는 일 / What it does |
|---|---|
| Click an employee | Detail panel on the right (click again to fold/unfold). The office re-lays out so the panel never covers anyone |
| 🪙 Coins | Current balance. Click to open 🛋️ Decor Shop |
| 🛋️ Shop | Open the 7-item office decor shop (buy, place, and stash props) |
| 🐾 | **Desktop MiniPet** toggle — small, transparent companion on desktop or taskbar |
| 📋 Whiteboard | By the left window — displays real-time session TODOs and goals |
| 🏆 | **Quest Book** — 30 achievements, level progress, completed quests filter |
| 🛑 | **Emergency Stop** — immediately terminates running command processes and safely returns agent to idle |
| ▤ | Compact widget (list view, small always-on-top window) |
| 📊 | Today's work log — work time, prompts, tokens, prompt cache cost savings |
| 🔕 | Not connected to Claude Code — click to connect (disappears once fully connected; see below) |
| 🚪 / 📌 / ⚙️ | Show/hide employees who went home / always on top / settings |
| Tray icon 🧑‍💻 | Click to open, right-click → Exit. Closing the window (✕) only hides it by default |

Settings (⚙️) open as a centered pop-up with tabs — 🏢 Office · 🐾 MiniPet · 👥 Employees · 🔔 Notifications · 📱 Phone · 🖥️ General · ℹ️ About (the last tab is remembered; Esc or a click outside closes it). They include: office name, screen language (한국어 / English), MiniPet toggles and pinned employee, display period (12h–7d), how many employees and how to pick them (most recent / manual checklist), wait-only filter, office size, window behavior (start hidden, close to tray, run at Windows startup; position and size are remembered), notifications, Claude Code connection, phone connection, update check with one-click **Update**, and the data folder.

Command line: `--demo` (sample office), `--exit` (quit the running instance), `--autostart` (start hidden in the tray), `--offscreen` (for test tools).

## Claude Code connection (recommended)

Transcripts do not record "a permission dialog is open", so without the connection the app only *guesses* (a file edit that doesn't finish).
Say **yes** when asked "Connect to Claude Code?" on first start, or later use ⚙️ → 🖥️ General → **Claude Code connection** (or 🔕 in the top bar).
After a confirmation dialog it adds a few lines (hooks) to `~/.claude/settings.json`; phone instructions, approvals, and emergency stops come through the same connection.

| Added (hook) | What it does |
|---|---|
| `PermissionRequest` | ✋ raises a hand until that tool call finishes (approved and done, rejected or interrupted). While you're away it waits for Approve/Deny from the phone instead (up to 55 min, then the normal prompt) |
| `Notification` | permission and input prompts |
| `SessionEnd` | the employee goes home immediately |
| `Stop` · `SessionStart` | a session that finished answering (or just opened) waits in the background, and an instruction from the phone wakes it (`asyncRewake`, up to a day). It reconnects within 15 s when the app restarts (updates etc.) — one per session |
| `PreToolUse` | 🛑 checks emergency stop flag before tool execution — exits with code 2 to block runaway tools and return agent safely to waiting |

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

With the Claude Code connection, while you're away the phone can **send instructions** to an employee (session), **answer permission requests and questions**, and **trigger emergency stops**.
The phone needs no Claude login — the work is done by Claude Code on the PC with its own sign-in.

1. **Just walk away and open the office on your phone.** There's no button to press on the PC or the phone
   - While the phone page is open and the PC has had no input for 30 s, instructions and approvals come to the phone (= Away). Move the mouse or type at the PC and it goes back to the PC
   - Even when you're not looking at the phone, **3 minutes** without input (off/1/5/10 min in Settings) or **locking the screen** (⊞+L, at once) turns Away on — approval requests that arrive meanwhile wait until you open the phone
2. On the phone, tap an employee to see the **📱 Instruct & approve from the phone** box
   - 💬 **Send a new instruction (Key Feature!)**: Enter text and tap [Send] — the idle session wakes up and **continues the same conversation** (e.g. "Tidy up README", "Add unit tests for this function"). If it is busy, the instruction is delivered as soon as the current work ends.
   - ✋ **Approval request**: see exactly what it wants to run, then Approve / Deny. When denying you can write what to do instead, and Claude follows it.
   - ❓ **Multiple-choice questions**: When Claude asks a question (`AskUserQuestion`), option buttons appear on mobile. Tap any option to answer directly.
   - 🛑 **Emergency Stop**: If a command is stuck or runaway, tap to immediately terminate the running process on PC and halt Claude safely.
   - 💬 **Last answer**: Claude's latest reply can be viewed on your phone as well.

- **Safety**
  - **While someone is using the PC** (input in the last 30 s) the phone cannot turn Away on — instructions sent then are dropped and approvals go to the PC prompt
  - What the phone sends is locked with the same key (AES-256-GCM), so only a device that scanned the QR can send; messages older than 10 minutes or already seen are dropped
  - Claude's permission rules stay as they are — anything that needs approval still asks (on the phone)
  - Every instruction, approval, denial, and emergency stop is logged in `remote.log` in the data folder
  - **The QR/link is the key.** If someone saw it, use **New key** under ⚙️ → 📱 Phone
- After connecting, **new or reopened sessions** take instructions right away; sessions that were already open start **after their next answer**. When Claude Office restarts (an update, for example), resting sessions reconnect by themselves within 15 seconds
- Claude's last answer is included in the locked summary (only with the Claude Code connection)

## How it works

Claude Code appends each session to `%USERPROFILE%\.claude\projects\<project>\<session-id>.jsonl` (or under `CLAUDE_CONFIG_DIR`).
The app scans that folder every 0.7 s and reads **only appended bytes** (the last 2 MB the first time it sees a large file). The office is drawn on an HTML canvas **entirely in code, with no image assets**.

```
src/ClaudeOffice/
  Monitoring/   folder watcher, line parser, tool → action/bubble catalog, hook state, today's stats, snapshot JSON, tool process management (ApprovalProbe)
  Hooks/        Claude Code connection — 127.0.0.1 hook receiver, settings.json hook install/remove
  Cloud/        phone connection — locked summary upload to the free server (Firebase), anonymous sign-in
  Remote/       delivers phone instructions, approvals and stops to sessions through hooks, Away
  WidgetWindow  Desktop MiniWidget — transparent window, always on top, interactive companion
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
