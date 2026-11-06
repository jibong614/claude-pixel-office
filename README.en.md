# 🏢 Claude Pixel Office

<div align="center">

[한국어](README.md) · **English**

[![Release](https://img.shields.io/github/v/release/jibong614/claude-pixel-office?color=7c3aed&label=Release)](https://github.com/jibong614/claude-pixel-office/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078d4)](https://github.com/jibong614/claude-pixel-office)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Tests](https://img.shields.io/badge/Tests-45%20Passed-brightgreen)](tests/ClaudeOffice.Tests)
[![License](https://img.shields.io/github/license/jibong614/claude-pixel-office?color=green)](LICENSE)

**See at a glance what Claude Code is doing and whether it's waiting for your approval — as a retro pixel-art office on Windows.**

[🚀 Quick Start](#-quick-start) · [✨ Key Features](#-key-features) · [📸 Screenshots](#-screenshots) · [📱 Mobile Remote Control](#-mobile-remote-control-optional) · [📖 FAQ](#-frequently-asked-questions-faq)

<br/>

![The office in motion](docs/screenshots/demo.gif)

🎮 **Try it in the browser first (no installation needed)**: **[https://jibong614.github.io/claude-pixel-office/?demo&lang=en](https://jibong614.github.io/claude-pixel-office/?demo&lang=en)**

</div>

> [!NOTE]
> This is an unofficial, community-driven open-source tool not affiliated with Anthropic. It safely and read-only parses local conversation logs recorded by Claude Code on your PC.
> To switch language in the app: Tray right-click ⚙️ **Settings → 🏢 Office → Language → English**.

---

## 📸 Screenshots

### 💻 Main Screens
| 🏢 Pixel Office Mode | 💼 Business Dashboard Mode (`Shortcut O`) |
| :---: | :---: |
| <img src="docs/screenshots/basic_office.png" width="460" alt="Pixel Office Mode"> | <img src="docs/screenshots/office_mode.png" width="460" alt="Business Dashboard Mode"> |
| *Pixel art agents, desks, subagent interns, autonomous idle behaviors* | *Clean, dot-free card grid with a unified dark tone (`#0d0f17`)* |

### 🪟 Desktop MiniWidget
| 🪟 Desktop MiniWidget (3-Tier Responsive) | 🏝️ Ultra-Slim Dynamic Island (MiniBar) |
| :---: | :---: |
| <img src="docs/screenshots/widget.png" width="460" alt="Desktop MiniWidget"> | <img src="docs/screenshots/minibar.png" width="460" alt="Ultra-Slim Dynamic Island"> |
| *Large/Medium/Small scaling + developer meme thought bubbles* | *Floating 44px pill capsule; hover for jump, stop, and widget restore* |

### ⚙️ Details & Standalone Settings
| ⚙️ Standalone Settings Window (Tray Right-Click) | 🔍 Agent Detail Drawer Panel |
| :---: | :---: |
| <img src="docs/screenshots/settings.png" width="460" alt="Standalone Settings Window"> | <img src="docs/screenshots/panel.png" width="460" alt="Agent Detail Drawer"> |
| *Lightweight 720×640 transparent modal without loading main dashboard* | *Active tool, recent actions, today's tokens, and cache cost savings* |

### 📱 Mobile Remote Monitoring & Approvals
| 📱 Real-Time Mobile Office | 🔍 Pre-Approval Git Diff Review |
| :---: | :---: |
| <img src="docs/screenshots/mobile.png" width="240" alt="Mobile Office View"> | <img src="docs/screenshots/mobile-approve.png" width="240" alt="Mobile Approval with Git Diff"> |
| *No app install needed; scan QR code once to monitor over LTE/5G* | *Review code diffs before approving, 🎙️ voice instructions, 🛑 stop* |

---

## ✨ Key Features

### 1. 🏢 Intuitive Multi-Session Visualization & Business Mode
- **Session = Agent, Folder = Team Area (Rug), Subagent = Intern**: Keep track of multiple terminal sessions at a glance without switching windows.
- **Real-Time Action Speech Bubbles**: Shows reading (`Read`), searching (`Grep/Glob`), writing code (`Edit/Write`), running commands (`Bash/PowerShell`), and web queries in clear speech bubbles.
- **Modern Clean Dashboard (`Shortcut O`)**: Switch between the retro pixel office and an enterprise-ready card grid view with a unified dark background (`#0d0f17`).

### 2. 🪟 Desktop MiniWidget & Dynamic Island
- **Always-on-Top Floating Companion**: Monitor your primary agent in the corner of your screen even when editors or browsers are full-screen.
- **3-Tier Responsive Scaling (Large 360px · Medium 300px · Small 260px)**: Fonts (11.5px ➔ 10.5px ➔ 9.5px) and control buttons scale proportionally to preserve whitespace and readability.
- **Ultra-Slim Dynamic Island (MiniBar)**: Floats at the top as a 44px capsule that expands on hover with one-click terminal jump, emergency stop, and widget restore (`🪟` / `💼`) buttons.
- **🚶 Shimeji Walk Mode**: Agent steps away from the desk to stroll, jump, and hop across the bottom of your screen.
- **💡 25 Developer Memes Easter Egg**: Click to trigger humorous developer quotes wrapped into 2-line thought bubbles while keeping the last answer card safe.

### 3. 📱 Mobile Remote Control — Keep Work Moving While Away
- **Zero-Setup Quick Connect**: Scan a QR code on your PC to immediately open the dashboard on your phone's browser (no login required).
- **🔍 Pre-Approval Git Diff Review**: When Claude requests permission to modify files, expand the unified diff (`+N`, `-N`) on your phone to inspect changes before tapping **[Approve]**.
- **🎙️ Voice-to-Prompt Instructions**: Tap the microphone on mobile to dictate instructions; speech is transcribed in real-time and delivered to Claude.
- **Seamless Context Resumption**: Send a follow-up instruction to an idle session, and it wakes up in the exact same conversation context.
- **❓ Multiple-Choice Answers & 🛑 Emergency Stop**: Tap buttons to answer questions directly, or safely halt runaway loops in 0.01 seconds.
- **🔒 End-to-End Encryption (AES-256-GCM)**: Relayed via Google Firebase free tier, but encrypted using a local key in the QR `#hash` that never touches any server.

### 4. 🛋️ Office Life & Productivity Tools
- **🪙 Coins & Office Decor Shop**: Earn coins by finishing tasks and saving prompt cache tokens. Purchase 7 interactive props (Espresso machine, office cat 'Nabi', retro arcade cabinet, etc.) with custom 8-bit sound effects.
- **☕ Autonomous Idle AI & 24h Day/Night Lighting**: Resting agents stretch, sip warm coffee, and share random thoughts. Lighting shifts dynamically from daylight to sunset, evening, and late-night spotlight cones.
- **🤝 Multi-Agent Meeting Table & 📢 Broadcast (`Shortcut B`)**: Collaborative conference table animations and bulk broadcast commands (`/compact`, unit tests, etc.) to all sessions.
- **📋 Whiteboard Kanban & 🧠 Context Gauge**: 3-column To Do / In Progress / Done card view and real-time context token load gauge.
- **⚡ One-Click Console Jump (`Alt+1~9` / `J`)**: Double-click any desk to immediately bring its VS Code, Cursor, Windsurf, or terminal window into focus.

### 5. ⚙️ Lightweight, Native Windows Performance
- **Standalone Transparent Settings**: Right-click the system tray to open a lightweight 720×640 modal without loading the main dashboard.
- **Zero-Idle CPU Footprint**: Efficient 0.7s delta parsing uses 0% CPU when sessions are idle.
- **45 Passing Automated Unit Tests**: Complete xUnit test suite ensuring rendering, hook dispatch, encryption, and widget layout integrity.

---

## 🚀 Quick Start

### 1. Download & Run (Recommended)
1. Download the latest `ClaudePixelOffice-v*-win-x64.zip` from [Releases](https://github.com/jibong614/claude-pixel-office/releases).
2. Extract the archive and launch `ClaudeOffice.exe`.
3. On first launch, click **[Yes]** when prompted **"Connect to Claude Code?"** (recommended).
4. Start a conversation in Claude Code as usual—an employee appears at a desk!
5. *(Optional)* For mobile view: Tray right-click ⚙️ Settings → 📱 Phone → **[Turn On] → [Show QR]** and scan with your phone.

> [!TIP]
> - **Requirements**: Windows 10/11, [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (built into Windows 11; no .NET install required).
> - If Windows SmartScreen appears due to unsigned binary: click **More info ➔ Run anyway**.
> - Future releases can be updated with one click from inside the application.

### 2. Build from Source
Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
```powershell
git clone https://github.com/jibong614/claude-pixel-office.git
cd claude-pixel-office
dotnet build ClaudeOffice.sln -c Release
src\ClaudeOffice\bin\Release\net8.0-windows\ClaudeOffice.exe
```

---

## 🎮 Shortcuts & Controls

| Control | Action |
|:---|:---|
| **Click Employee** | Open detail drawer panel on the right (click again to close) |
| **Double-Click / `Alt+1~9`** | **One-click jump** to the session's IDE or terminal window |
| **`Shortcut O`** | Toggle between Pixel Office and Clean Business Dashboard |
| **`Shortcut B`** | **Broadcast** an instruction to all active sessions simultaneously |
| **`Shortcut N`** | Night Shift monitor & Morning briefing summary report |
| **🪟 Button** | Toggle **Desktop MiniWidget** (switch to Dynamic Island via top-right pill) |
| **🪙 / 🛋️ Button** | View coin balance and open the 7-item Office Decor Shop |
| **🛑 Button** | Immediately terminate active tool process and trigger **Emergency Stop** |
| **📋 Whiteboard** | Real-time session TODO Kanban extracted from conversations |
| **🏆 Button** | View the 30-quest achievement book |
| **Tray Icon Right-Click** | Open **⚙️ Standalone Settings Window** / Toggle MiniWidget / Exit |

---

## 🔗 Claude Code Connection (Recommended)

Selecting [Yes] to **"Connect to Claude Code?"** registers lightweight event hooks in your local `~/.claude/settings.json`.

```
~/.claude/settings.json
  ├── PermissionRequest ➔ ✋ Real-time approval detection & mobile dispatch
  ├── Notification      ➔ Precise input and notification state tracking
  ├── SessionEnd        ➔ Employee departs (🚪) immediately on session exit
  ├── Stop/SessionStart ➔ Wake up idle sessions with mobile instructions (asyncRewake)
  └── PreToolUse        ➔ 🛑 Block tool execution immediately on emergency stop (exit code 2)
```

- All communication stays within `127.0.0.1` loopback; if the app is closed, hooks exit silently without leaving errors in Claude Code.
- Disconnecting cleanly removes only the added lines (an automated backup is preserved).

---

## 📱 Mobile Remote Control (Optional)

Open tray menu ⚙️ Settings → 📱 Phone → **[Turn On] → [Show QR]** to connect instantly.

- **End-to-End Encryption (E2EE)**: Relayed via Firebase free tier, but summaries are locked with **AES-256-GCM** using a local key in the QR `#hash`, meaning the server and third parties can never read your data.
- **Automatic Away Detection**: When your PC is idle for 30 seconds or the screen is locked (`Win+L`), Away mode turns on automatically so approvals and prompts reach your phone.
- **Full Remote Management**: Review Git diffs, approve or reject tool runs, dictate prompts via voice, and trigger emergency stops from anywhere.
- For technical architecture and details, see **[docs/MOBILE.md](docs/MOBILE.md)**.

---

## 🛠️ Architecture & Tech Stack

```
src/ClaudeOffice/
  ├── Monitoring/     Delta transcript watcher, tool catalog, snapshot builder
  ├── Hooks/          Loopback hook receiver (HookServer), settings.json integration
  ├── Cloud/          Mobile summary encryption & Firebase relay integration
  ├── Remote/         Mobile prompt/approval/emergency stop hub (RemoteHub)
  ├── WidgetWindow.cs Always-on-top transparent MiniWidget & Dynamic Island
  ├── SettingsWindow.cs Tray-exclusive 720×640 standalone transparent settings modal
  ├── MainWindow.xaml Main WebView2 host and tray management
  └── wwwroot/        Pure HTML5 Canvas pixel rendering engine (office.js, office.css)
tests/ClaudeOffice.Tests/ 45 automated unit tests (rendering, hooks, crypto, layout)
```

---

## 📖 Frequently Asked Questions (FAQ)

<details>
<summary><b>Q. I don't see my employee in the office.</b></summary>
Sessions whose last conversation exceeds the display period (default 12 hours) are hidden. Increase the display period to 24 hours, 3 days, or 7 days under Tray ⚙️ Settings → 👥 Employees.
</details>

<details>
<summary><b>Q. Clicking ✕ doesn't quit the application.</b></summary>
By default, closing hides the window to the system tray (🧑‍💻). To exit completely, right-click the tray icon and select <b>Exit</b>.
</details>

<details>
<summary><b>Q. Approvals are slow to appear or missing.</b></summary>
Without the Claude Code connection, the app can only guess approvals from file write delays. Connecting via the 🔕 icon or Settings enables 0.01s real-time detection.
</details>

<details>
<summary><b>Q. Can anyone else see my code through the free server?</b></summary>
No. The server only transmits AES-256-GCM encrypted binary blobs. The decryption key exists exclusively in the QR link's <code>#hash</code> and is never transmitted in HTTP headers or logged by the server.
</details>

---

## 📄 License & Credits

- Licensed under the [MIT License](LICENSE).
- All pixel art and office graphics are rendered dynamically in code on HTML5 Canvas without external image files.
- QR Code generation: [qrcode-generator](https://github.com/kazuhikoarase/qrcode-generator) 1.4.4 (MIT License).
- "Claude" is a trademark of Anthropic. This project is not affiliated with Anthropic.
