# Claude Pixel Office

Claude Code 세션을 미니어처 픽셀 사무실로 보여 주는 Windows 프로그램(WPF + WebView2, .NET 8).
세션 = 직원, 작업 폴더 = 팀 구역, 서브에이전트 = 인턴. 지금 하는 일은 자세·모니터·말풍선으로 표시.
사용법·구조는 [README.md](README.md), 휴대폰 보기 설정은 [docs/MOBILE.md](docs/MOBILE.md).

## 버전
- 버전은 `src/ClaudeOffice/ClaudeOffice.csproj` 의 `<Version>` 하나가 원본이다(창 제목 `… v1.0.6`, exe 파일 버전).
- 올릴 때 [docs/CHANGELOG.md](docs/CHANGELOG.md) 맨 위에 그 버전에서 바뀐 것을 적는다.
- 휴대폰 화면은 앱과 따로 배포되므로 `wwwroot/office.js` 의 `PAGE_VERSION` 에도 같은 버전을 적는다(다르면 빌드가 멈춘다). 설정 맨 위에 보인다.

## 작업 규칙
- 화면을 바꿨으면 빌드 후 `tools/capture.ps1` 로 캡처해 **직접 눈으로 확인**한다(견본 `--demo` + 실제 세션 둘 다).
  캡처 두 장을 시간차로 찍어 비교하면 "상태가 안 바뀌는" 류의 버그가 잡힌다.
- **시험 창은 작업 중인 화면에 띄우지 않는다**: capture.ps1·pagetest.ps1 은 앱을 `--offscreen`(화면 밖·포커스 없음·작업표시줄 없음)으로 띄운다.
  ✕ 는 숨기기라서 시험 앱은 `ClaudeOffice.exe --exit`(같은 `CLAUDEOFFICE_DATA_DIR`)로 끝낸다.
- 시험은 임시 데이터 폴더(`CLAUDEOFFICE_DATA_DIR`)로 한다 — 평소 쓰는 화면 설정·휴대폰 보기 키를 건드리지 않게.
  자동 실행 시험은 `CLAUDEOFFICE_RUN_KEY` 로 시험용 레지스트리 키에 — 실제 시작 프로그램 목록은 건드리지 않는다.
- 클릭·입력이 필요한 화면(설정 등)은 `tools/pagetest.ps1` 로 JS 단계를 돌려 결과와 캡처를 확인한다(실제 마우스 안 씀).
  주소를 옮긴 다음에는 `@sleep` 단계를 넣는다(페이지 안에서 기다리면 응답이 사라져 멈춤).
- 휴대폰 보기는 `tools/fake-rtdb.py`(가짜 Firebase) + `pagetest.ps1 -Mobile -DataDir` 로 시험한다.
- 대화 기록 해석 로직을 바꿨으면 `dotnet run --project tools\watchtest` 로 실제 기록에 대한 결과를 확인한다
  (`-- --phrases 200`: 말풍선 문구 점검, `-- --hooks 30`: 훅 수신 점검).
- `~/.claude/settings.json` 같은 **Claude Code 전역 설정은 사용자 확인 없이 바꾸지 않는다**(훅 연동 포함).
  훅 시험은 `claude -p --settings <임시파일> --no-session-persistence` 나 `watchtest --setup`(복사본 폴더)으로 한다.
- 견본 데이터·스크린샷·주석 예시에는 실제 회사·프로젝트·세션 이름을 쓰지 않는다(가상의 이름 acme 등).
- 주석·문서는 한국어. 코드 주석은 "왜"를 적는다.
- 화면 글꼴은 맑은 고딕이라 `\`가 `₩`로 그려진다 — 경로는 `/`로 바꾸거나 고정폭 글꼴(`.mono`)로 표시.
