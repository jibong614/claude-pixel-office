# 🏢 Claude Pixel Office

**한국어** · [English](README.en.md)

**Claude Code 세션을 미니어처 픽셀 사무실로 들여다보는 Windows 프로그램.**
세션 하나가 직원 한 명이 되어, 지금 무슨 일을 하는지 자세·모니터 화면·머리 위 말풍선으로 보여 줍니다.
승인을 기다리는 직원은 손을 들고, 휴대폰으로도 볼 수 있습니다.

> Anthropic과 관계없는 비공식 도구입니다. Claude Code가 남기는 대화 기록 파일을 읽기만 합니다.

![사무실이 움직이는 모습](docs/screenshots/demo.gif)

| 직원 상세 | 설정 | 휴대폰 |
|---|---|---|
| ![상세 패널](docs/screenshots/panel.png) | ![설정](docs/screenshots/settings.png) | <img src="docs/screenshots/mobile.png" width="220" alt="휴대폰 화면"> |

## 주요 기능

- 👥 **세션 = 직원, 작업 폴더 = 팀 구역(카펫), 서브에이전트 = 책상 옆 인턴**
- 💬 **하는 일을 그대로**: 읽기·코드 작성·명령 실행·검색·웹 검색·인턴 지휘·계획 정리·답변 작성 — 자세·모니터·말풍선이 바뀝니다. 말풍선은 되도록 한글로(예: `dotnet build` → "빌드하는 중")
- ✋ **사람이 필요한 순간을 놓치지 않게**: 질문·승인 대기는 손을 들고, 창 제목·작업표시줄에 `(✋2)`처럼 개수가 뜹니다
- 🔍 **직원을 누르면 상세 패널**: 마지막 지시, 최근 작업, 인턴 목록, **오늘 일한 시간·지시·토큰·많이 쓴 도구**, 그리고 내가 붙이는 **별명·메모**(세션 이름을 바꿔도 남음)
- 📊 **오늘의 근무 일지**: 직원마다 일한 시간·지시 횟수·토큰을 한눈에
- ▤ **작게 보기(위젯)**: 창을 작게 줄여 직원·상태만 한 줄씩, 늘 맨 위에
- 🔔 **PC 알림**: ✋ 승인·질문을 기다리기 시작할 때, 오래 걸린 일이 끝났을 때 Windows 알림 — 누르면 그 직원이 열림. 소리·방해 금지 시간대 설정
- ⚙️ **설정**: 사무실 이름, 표시 기간, 보일 인원·고르는 방식(최근 사용순/직접 고르기), 창 동작(위치·크기 기억), 알림, 훅 연결, 휴대폰 보기, 새 버전 알림
- 🪟 **알림 영역 상주**: ✕를 눌러도 숨기만 하고, Windows 시작 시 자동 실행도 됩니다
- 📱 **휴대폰에서 보기(선택)**: 화면용 요약만 PC에서 암호화해 Firebase로 — 밖에서도, 휴대폰 로그인 없이. 앱 안 **설정 도우미**가 단계마다 콘솔 화면을 열어 주고 저절로 확인
- 🏃 **자리 비움 원격(선택)**: 자리를 비운 동안 휴대폰에서 **승인 요청에 승인·거절**하고 **직원에게 지시**를 보내면, 쉬던 세션이 깨어나 같은 대화에서 이어서 일합니다 — 휴대폰에 Claude 로그인 없이
- 🔒 **기본은 아무것도 바꾸지 않음**: Claude Code 설정을 건드리지 않고 대화 기록 파일만 읽습니다(훅 연결은 선택, 확인 창을 거침)

## 빠른 시작

### 1. 내려받아 실행 (권장)

1. [Releases](https://github.com/jibong614/claude-pixel-office/releases)에서 `ClaudePixelOffice-v*-win-x64.zip` 을 내려받습니다(1.0.9부터는 새 버전이 나오면 앱 안에서 바로 업데이트)
2. 압축을 풀고 `ClaudeOffice.exe` 를 실행합니다
3. Claude Code로 대화를 시작하면 책상에 직원이 나타납니다

- 필요한 것: Windows 10/11, [Microsoft Edge WebView2 런타임](https://developer.microsoft.com/microsoft-edge/webview2/)(Windows 11에는 기본 포함). .NET은 따로 설치하지 않아도 됩니다
- 서명하지 않은 프로그램이라 처음 실행할 때 Windows SmartScreen 경고가 뜰 수 있습니다 → **추가 정보 → 실행**

### 2. 소스에서 빌드

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 필요합니다.

```powershell
git clone https://github.com/jibong614/claude-pixel-office.git
cd claude-pixel-office
dotnet build ClaudeOffice.sln -c Release
src\ClaudeOffice\bin\Release\net8.0-windows\ClaudeOffice.exe
```

### 견본 화면

Claude Code 기록 없이 모든 상태를 돌아가며 보려면:

```powershell
ClaudeOffice.exe --demo
```

설치 없이 브라우저로 보려면: **https://jibong614.github.io/claude-pixel-office/?demo**

## 사용법

### 화면

| 무엇 | 하는 일 |
|---|---|
| 직원 클릭 | 오른쪽에 상세 패널. 같은 직원을 다시 누르면 접기·펼치기 |
| 패널 왼쪽 손잡이 `›` | 패널 접기(사무실이 창 전체를 씀). 펼쳐져 있을 때는 사무실이 남은 폭에 맞춰 다시 배치돼 직원을 가리지 않음 |
| ▤ | 작게 보기(위젯) — 창을 작게 줄이고 직원·상태 목록만, 늘 맨 위 |
| 📊 | 오늘의 근무 일지 — 일한 시간·지시·토큰 |
| 🏃 | 자리 비움 켜기·끄기 — 휴대폰에서 지시·승인을 받음(자리 비움 원격을 연결했을 때만 보임) |
| 🔕 / 🔔 | 훅 연결 상태 — 누르면 연결·해제(아래 "승인 대기 정확히 보기") |
| 🚪 | 퇴근한 직원 보이기/숨기기 |
| 📌 | 항상 위에 표시 |
| ⚙️ | 설정 |
| 알림 영역 🏢 | 누르면 창 열기, 오른쪽 클릭 → 끝내기 |

### 기록 → 사무실

| Claude Code 기록 | 사무실 |
|---|---|
| 세션 파일 | 직원(이름표 = `/rename` 세션 이름, 없으면 자동 요약 제목) |
| 작업 폴더(`cwd`) | 팀 구역 — 폴더 마지막 두 단계(예: `acme/web-shop`) |
| `<세션>\subagents\agent-*.jsonl` | 책상 옆 인턴 |
| 사람의 지시 | 💭 생각 중 |
| 도구 사용 Read / Grep·Glob / Edit·Write / Bash·PowerShell / WebSearch / Agent | 📄 읽기 / 🔍 찾기 / ✏️ 코드 작성 / ⌨️ 명령 실행 / 🌐 웹 검색 / 👥 인턴 지휘 |
| AskUserQuestion, ExitPlanMode | ✋ 답변 기다림(손 들기) |
| 응답 끝 | ☕ 대기 → 10분 뒤 💤 졸음 → 3시간 뒤 🚪 퇴근 |

### 설정 (⚙️)

| 구역 | 항목 |
|---|---|
| 사무실 | 사무실 이름(벽 간판·창 제목) / 화면 언어(한국어·English) |
| 직원 표시 | 표시 기간(12시간·24시간·3일·7일) / 퇴근한 직원 보이기 / 보일 인원(1~30명) / 고르는 방식 — 최근 사용순 또는 직접 고르기(체크 목록) |
| 팀 | 팀(작업 폴더)마다 표시 이름·카펫 색·순서 — 직원 자리는 직원 상세의 ◀ ▶ |
| 창 | 항상 위에 표시 / 사무실 크기(더 작게~더 크게) / 작게 보기(목록) / 시작 시 알림 영역에 숨기기 / 닫기(✕)를 눌러도 숨기기 / Windows 시작할 때 자동 실행 (창 위치·크기는 저절로 기억) |
| 알림 | ✋ 기다릴 때 / 오래 걸린 일이 끝나면(5분·10분·30분·1시간) / 알림음 / 방해 금지 시간대 / 알림 시험 — 창을 보고 있을 때는 띄우지 않음 |
| Claude Code 연동 | 훅 연결 |
| 휴대폰에서 보기 | 설정 도우미(6단계, 자동 점검) · 켜기 · QR · 휴대폰 페이지(공용 / 내 Firebase Hosting) · 휴대폰에서 별명·메모 고치기 |
| 자리 비움 원격 | 휴대폰에서 지시·승인(연결하기) / 🏃 자리 비움 / 화면을 잠그면 자리 비움 |
| 정보 | 버전·새 버전 확인·**[업데이트]**(앱 안에서 받아 바로 바꿈) / 새 버전 알림 / 별명·메모 내보내기·가져오기 / 설정 초기화 / 문제 보고(GitHub 이슈) / 데이터 폴더 열기 |

### 명령줄

| 옵션 | 뜻 |
|---|---|
| `--demo` | 견본 화면 |
| `--exit` | 켜져 있는 Claude Pixel Office를 정상 종료 |
| `--autostart` | 자동 실행용 — 알림 영역에 숨긴 채 시작 |
| `--offscreen` | 점검 도구용 — 창을 화면 밖에 포커스 없이 띄움 |

이미 켜져 있을 때 다시 실행하면 새로 켜지지 않고 그 창이 열립니다.

## 승인 대기 정확히 보기 (훅, 선택)

대화 기록에는 "권한 승인 창이 떠 있다"가 남지 않습니다. 기본으로는 파일 수정이 오래 끝나지 않으면 승인 대기로 **추정**만 합니다.
🔕 버튼을 누르면 확인 창을 거쳐 Claude Code 설정(`~/.claude/settings.json`)에 훅 3개를 넣고, 그때부터 정확히 표시합니다.

| 훅 | 사무실 |
|---|---|
| `PermissionRequest` | ✋ 승인해 주세요 · 무엇을 하려는지 — 그 도구 호출이 끝나면(승인 후 완료·거절·중단) 내림 |
| `Notification` | 승인·입력 요청 알림 |
| `SessionEnd` | 세션을 닫으면 바로 🚪 퇴근 |

- 훅은 `curl … http://127.0.0.1:47821/hook; exit 0` 입니다(Git Bash로 실행). Git Bash가 없는 PC에서는 같은 일을 하는 PowerShell 명령(`"shell": "powershell"`)을 넣습니다.
  프로그램이 꺼져 있어도 **항상 성공으로 끝나** 대화창에 오류가 남지 않고, 승인을 대신하거나 작업을 막지 않습니다
- 다시 누르면(🔔) 이 프로그램이 넣은 훅만 뺍니다. 고치기 직전 원본은 `settings.json.claudeoffice.bak` 으로 남습니다
- 승인한 순간을 알려 주는 훅은 없어서, 명령 실행(Bash·PowerShell) 승인 대기 중에 **그 명령의 프로세스가 새로 생기면** 승인된 것으로 보고 손을 내립니다(승인 후 오래 도는 빌드도 "실행 중"으로). 명령을 인코딩해 넘기는 경우 등 알아보지 못하면 끝날 때 내립니다

## 휴대폰에서 보기 (선택)

PC가 **화면용 요약만**(대화 원본은 아님) gzip → AES-256-GCM으로 암호화해 Firebase Realtime Database에 올리고,
휴대폰은 QR로 연 웹페이지에서 실시간으로 받아 **기기 안에서** 해독합니다.
웹페이지는 이 저장소의 화면 파일을 올린 **공용 페이지**(GitHub Pages)라 따로 배포할 것이 없습니다(원하면 내 Firebase Hosting 에 직접 올려 쓸 수도 있음).
해독 키는 QR 주소의 `#` 뒤에만 있어 서버로 전송되지 않습니다. 홈 화면에 앱으로 설치할 수 있습니다.
설정에서 켜면 **휴대폰에서 별명·메모도 고칠 수 있습니다**(같은 키로 암호화해 PC에 전달).

PC 프로그램을 끄면 휴대폰에 `💤 PC에서 꺼짐`이 바로 뜨고 사무실이 흐려집니다.
본인 구글 계정으로 Firebase 프로젝트를 한 번 만들어야 합니다 — ⚙️ → 휴대폰에서 보기 → **[설정 도우미]** 가 6단계로 안내합니다(10분쯤, Node.js·명령어 필요 없음). 자세히: **[docs/MOBILE.md](docs/MOBILE.md)**

## 자리 비움 원격 — 휴대폰에서 지시·승인 (선택)

자리를 비운 동안 휴대폰 보기에서 직원(세션)에게 **지시를 보내고**, **승인 요청에 승인·거절로 답합니다.**
휴대폰에는 Claude 로그인이 필요 없습니다 — 일은 PC의 Claude Code가 자기 로그인으로 합니다. 휴대폰 보기를 먼저 켜 두어야 합니다.

1. ⚙️ → 자리 비움 원격 → **[연결하기]** — 확인 창을 거쳐 `settings.json` 에 훅 3개를 넣습니다
2. 자리를 뜰 때 상단 **🏃** 버튼(또는 알림 영역 🏢 메뉴)을 켜거나 **화면을 잠급니다**(⊞+L — 잠금을 풀면 저절로 꺼짐)
3. 휴대폰에서 직원을 누르면 **🏃 자리 비움 원격** 칸이 보입니다
   - ✋ **승인 요청**: 무엇을 하려는지(명령 원문)를 보고 [승인] / [거절]. 거절할 때 "대신 할 일"을 쓰면 Claude가 그대로 따릅니다
   - **지시 보내기**: 쓰고 [보내기] → 쉬고 있던 그 세션이 깨어나 **같은 대화에서 이어서** 일합니다. 일하는 중이면 지금 일을 마치는 순간 전합니다
   - 💬 **마지막 답**: Claude가 마지막으로 한 답

| 훅 | 하는 일 |
|---|---|
| `PermissionRequest` | 자리 비움일 때만 승인 창 대신 휴대폰의 답을 기다립니다(최대 55분, 넘으면 평소 승인 창). 자리에 있으면 바로 평소대로 |
| `Stop` · `SessionStart` | `asyncRewake` 훅 — 답을 마친(또는 막 연) 세션이 뒤에서 기다리다가 휴대폰 지시가 오면 그 세션을 깨워 전합니다(최대 하루) |

- **안전**
  - 자리 비움은 **PC에서만** 켤 수 있습니다. 꺼져 있으면 휴대폰에서 보낸 지시는 버리고, 승인은 PC 승인 창으로 갑니다
  - 휴대폰이 보내는 것은 휴대폰 보기와 같은 키로 암호화돼(AES-256-GCM) QR을 찍은 기기만 보낼 수 있고, 10분 넘은 것·이미 받은 것은 버립니다
  - 지시를 보내도 Claude의 권한 규칙은 그대로입니다 — 승인이 필요한 일은 여전히 승인 요청(→ 휴대폰)
  - 받은 지시·승인·거절은 모두 데이터 폴더의 `remote.log` 에 남습니다
  - **QR·주소가 곧 열쇠입니다.** 남이 봤다면 ⚙️ → 휴대폰에서 보기 → 키 새로 만들기
- 연결한 뒤 **새로 열거나 다시 연 세션은 바로**, 이미 열려 있던 세션은 **한 번 더 답을 마친 뒤부터** 지시를 받습니다. 업데이트처럼 Claude Office를 잠깐 껐다 켜면 15초 뒤 다시 붙습니다(Claude Office가 꺼져 있는 동안 답을 마친 세션은 다음 답부터)
- 휴대폰에 보이는 Claude의 마지막 답도 암호화한 요약에 함께 실립니다(원격을 연결했을 때만)
- 질문(AskUserQuestion)은 답을 골라야 해서 휴대폰으로 넘기지 않습니다(PC에서 답하기)

## 어떻게 동작하나

Claude Code는 세션마다 대화를 `%USERPROFILE%\.claude\projects\<프로젝트>\<세션ID>.jsonl` 에 한 줄씩 덧붙여 씁니다(`CLAUDE_CONFIG_DIR` 을 쓰면 그 아래).
이 프로그램은 그 폴더를 0.7초마다 훑어 **늘어난 부분만** 읽고(처음 보는 큰 파일은 끝 2MB만), 사무실 화면은 HTML 캔버스에 **그림 파일 없이 코드로** 그립니다.

```
src/ClaudeOffice/
  Monitoring/   기록 폴더 감시(TranscriptWatcher), 한 줄 해석(TranscriptParser), 도구 → 행동·말풍선(ToolCatalog),
                훅 반영(HookApplier), 시간 경과 반영 → 화면용 JSON(SnapshotBuilder)
  Hooks/        127.0.0.1 훅 수신기(HookServer), settings.json 훅 넣기/빼기(HookSetup)
  Cloud/        휴대폰 보기 — 요약 암호화·Firebase 업로드, 익명 로그인(CloudSync, CloudConfig)
  Remote/       자리 비움 원격 — 휴대폰 지시·승인을 훅으로 세션에 전함(RemoteHub)
  MainWindow    WebView2 호스트, TrayIcon 알림 영역 아이콘, AutoStart 시작 프로그램 등록
  wwwroot/      사무실 화면(office.js 캔버스 픽셀아트) — PC 앱과 휴대폰 페이지가 같은 파일
mobile/         Firebase Hosting·데이터베이스 규칙
tools/          점검 도구(아래)
```

### 데이터와 네트워크

- **읽기만**: Claude Code 대화 기록. 대화 내용은 어디로도 보내지 않습니다(휴대폰 보기를 켰을 때의 암호화한 요약만 예외)
- **저장**: `%LOCALAPPDATA%\ClaudeOffice` — 화면 설정(WebView2 저장소), 별명·메모 사본 `share.json`, `app.json`, 휴대폰 보기 키(`cloud.json`), 훅 기록, 원격 기록 `remote.log`
- **네트워크**: 훅 수신은 `127.0.0.1` 만(브라우저가 보낸 요청은 받지 않음). 휴대폰 보기를 켰을 때만 암호화한 요약을 Firebase로 보냅니다(자리 비움 원격을 연결하면 Claude의 마지막 답도).
  새 버전 확인은 하루 두 번(그리고 설정을 열 때, 10분에 한 번까지) GitHub API(`api.github.com`)에 최신 릴리스를 묻습니다(보내는 것은 프로그램 버전뿐, ⚙️ → 정보에서 끌 수 있음)

## FAQ

**직원이 안 보여요.**
마지막 대화가 표시 기간(기본 12시간)보다 오래된 세션은 띄우지 않습니다. ⚙️ → 표시 기간을 늘리세요. 판단 기준은 파일 수정 시각이 아니라 **대화 시각**입니다.

**✕를 눌렀는데 꺼지지 않아요.**
알림 영역(🏢)으로 숨은 것입니다. 끝내려면 아이콘 오른쪽 클릭 → 끝내기. ⚙️ → 창에서 바꿀 수 있습니다.

**승인 대기가 늦게 뜨거나 안 떠요.**
🔕 버튼으로 훅을 연결하세요. 연결 전에는 파일 수정만 추정하고 명령 실행 중의 승인 대기는 구분하지 못합니다.

**다른 PC나 WSL에서 쓰는 Claude Code도 보이나요?**
아니요. 프로그램이 실행된 Windows 사용자의 기록 폴더만 읽습니다. 그 PC에서 실행하세요.

**Claude Code 설정을 바꾸나요?**
기본으로는 바꾸지 않습니다. 훅 연결(🔕)·자리 비움 원격 연결을 눌러 확인했을 때만 `settings.json` 에 훅을 넣고, 해제하면 그것만 뺍니다.

**휴대폰에서 보낸 지시가 안 가요.**
PC에서 🏃 자리 비움이 켜져 있어야 합니다(화면 잠금으로도 켜짐). 이미 열려 있던 세션은 원격을 연결한 뒤 한 번 더 답을 마쳐야 연결됩니다 — 직원 상세의 "📡 연결됨"을 확인하세요.

## 개발

- 빌드: `dotnet build ClaudeOffice.sln` (Debug 빌드는 소스의 `wwwroot` 를 직접 읽어 화면은 창에서 **F5**만 누르면 반영)
- 점검 도구(`tools/`) — 시험 창은 화면 밖에 띄워 작업을 방해하지 않습니다
  - `capture.ps1` 창 캡처 / `pagetest.ps1` 화면 안에서 JS 단계 실행·캡처(`-Mobile` 휴대폰 크기)
  - `watchtest` 화면 없이 기록 해석 결과 출력(`--phrases` 말풍선 점검, `--hooks` 훅 수신)
  - `fake-rtdb.py` 가짜 Firebase(휴대폰 보기 시험) / `deploy-mobile.ps1` 휴대폰 페이지 배포
  - `make-icon.ps1`·`make-app-icons.ps1` 아이콘 만들기 / `make-gif.ps1` 캡처 여러 장 → 동작 GIF
- 릴리스: `v1.2.3` 태그를 올리면 `.github/workflows/release.yml` 이 자체 포함 exe 를 빌드해 릴리스 zip 으로 붙입니다. 휴대폰 공용 페이지는 `pages.yml`
- Claude Code로 작업할 때의 규칙: [CLAUDE.md](CLAUDE.md) · 바뀐 내용: [docs/CHANGELOG.md](docs/CHANGELOG.md)

## 라이선스 · 출처

- [MIT License](LICENSE)
- 아이콘 🏢: Microsoft [Fluent Emoji](https://github.com/microsoft/fluentui-emoji) "Office building" — MIT License, © Microsoft Corporation (`src/ClaudeOffice/Assets/LICENSE-fluentui-emoji.txt`)
- QR 코드: [qrcode-generator](https://github.com/kazuhikoarase/qrcode-generator) 1.4.4 — MIT License, © Kazuhiko Arase (`wwwroot/vendor/qrcode.js`)
- 화면 호스트: [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)
- "Claude"는 Anthropic의 상표이며, 이 프로젝트는 Anthropic과 관계가 없습니다
