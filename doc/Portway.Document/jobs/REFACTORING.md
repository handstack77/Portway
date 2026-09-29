# Portway 리팩토링 및 검증 보고서

2026-09-29 개발 소스를 기준으로 코드 탐색과 유지보수에 필요한 경계를 정리했습니다. 기존 0.3.9 설치 패키지를 다시 생성하거나 배포한 작업은 아닙니다.

## 변경 결과

| 대상 | 정리 전 | 정리 후 |
| --- | --- | --- |
| Desktop API | DesktopApi.cs에 요청 DTO·67개 경로·원격 편집 처리 혼재 | API 조립, `Api/`의 7개 기능 등록 파일, 계약 파일, RemoteTextFiles로 분리 |
| Desktop 시작 | 창 수명·서비스 등록·보안 처리가 Program.cs에 함께 존재 | Program은 시작·창·종료, DesktopHosting은 DI·보안·오류 처리 |
| 웹 UI | app.js에 상태·작업과 화면 골격·파일 행·UI 도우미 혼재 | workspace-view, file-list-view, ui 모듈로 표시 코드 분리 |
| 배포 서버 | Program.cs에 HTTP 처리까지 포함 | 파이프라인과 ServerApi의 경로 등록 분리 |
| C# 소스·테스트 | 여러 실행문과 블록이 한 줄에 붙어 있음 | 실행 블록을 펼치고 솔루션의 작성 C# 전체 포맷 정리 |
| 프런트엔드·프로젝트 설정 | 기존 포맷을 개별 유지 | JS/CSS/HTML·설정 파일 포맷, .editorconfig·Prettier 설정·검사 명령 추가 |

API 조립 파일은 119줄에서 16줄로 줄었으며 기능 구현은 이름이 있는 파일로 옮겼습니다. 단순히 파일 길이를 줄이는 목적이 아니라 개발자가 변경 위치와 호출 흐름을 찾도록 책임을 나누었습니다. Core 전송·프로토콜 알고리즘은 실행 코드를 바꾸지 않고 포맷과 주요 타입 설명을 정리했습니다.

기존 DesktopApi의 중첩 요청 타입, HTTP 경로·메서드·JSON 필드와 오류 응답을 유지했습니다. WebView 전용 데이터 폴더, Debug F12, loopback·Bearer·Origin·CSP, 작업 서비스 singleton/hosted service 공유도 보존했습니다.

## 문서화 산출물

- [ARCHITECTURE.md](../docs/ARCHITECTURE.md): 프로젝트 의존성, 시작·종료, DI·API·프로토콜, 전송 큐·복구, UI·편집·동기화·업데이트 흐름과 Mermaid 도식.
- [SOURCE-MAP.md](../docs/SOURCE-MAP.md): 처음 읽는 순서, 기능별 UI → API → 서비스 → Core → 테스트 진입점, 저장 데이터와 포맷 절차.
- [API-REFERENCE.md](../docs/API-REFERENCE.md): Desktop 67개·Server 4개 경로, 호출 조건·요청 모델·상태 코드와 계약 검증.
- [DEVELOPER-GUIDE.md](../docs/DEVELOPER-GUIDE.md), [AGENTS.md](../../../AGENTS.md), [README.md](../../../README.md): 새 경계와 문서 진입점 반영.

## 포맷을 유지하는 방법

Prettier 3.9.9를 개발 의존성으로 고정하고 package-lock과 라이선스 안내를 갱신했습니다. 실행 패키지에는 포맷 도구를 포함하지 않습니다. `.prettierignore`는 vendor·node_modules·빌드·검증 산출물·Server의 생성된 공유 자산을 제외합니다. C#은 `.editorconfig`를 기준으로 줄바꿈과 들여쓰기를 유지합니다.

```powershell
dotnet format whitespace Portway.slnx --no-restore
npm run format --prefix assets/frontend

dotnet format whitespace Portway.slnx --no-restore --verify-no-changes
npm run format:check --prefix assets/frontend
```

작성 프런트엔드와 Master 클래스 변경 뒤에는 자산 빌드와 .NET 빌드를 수행합니다. 실행 중인 앱이 기본 출력 파일을 잠그면 전용 `--artifacts-path`에서 검사합니다.

## 검증 결과

| 검사 | 결과와 범위 |
| --- | --- |
| 리팩토링 전 .NET 기본 검사 | 86개 통과·24개 조건부 건너뜀 |
| 리팩토링 후 .NET 기본 검사 | 89개 통과·24개 조건부 건너뜀·실패 0; Desktop API 계약 테스트 3개 추가 |
| 프런트엔드 테스트 | 31개 통과·실패 0 |
| 솔루션 Debug/Release 빌드 | 두 구성 모두 경고 0·오류 0; 기본 Debug 출력 갱신 |
| C#·프런트엔드 포맷 검사 | 변경 없음 검사 통과 |
| 소스 실행 토큰 비교 | 기존 C# 59개 파일의 실행 토큰 동일; 이동한 Desktop 경로 처리 67개·Server 경로 처리 4개·원격 편집 메서드 본문 3개와 보안 미들웨어 동일 |
| 실제 Windows Photino | Debug 창 정상 표시, 같은 전용 프로필 재시작 정상, F12 개발자 도구 창 확인 |
| 실제 로컬 파일 작업 | 양방향 복사·이동, 내부 드래그, 새 폴더·이름 변경·삭제, 텍스트·바이너리 SHA-256, 빈 폴더 보존 |
| 경로 보호 | 같은 경로·자기 하위 경로·Windows junction 전송 거부 |
| 편집기·탭 | 오른쪽 로컬 파일 Monaco 읽기·입력·저장, 로컬 상태 보존·연결 탭 전환·종료 |
| 날짜·접속 시간 | 현지 날짜, 연결별 타이머 갱신·탭 유지·로컬 숨김·재접속 초기화·진행 메시지 보존 |
| UI·이스케이프 | 라이트/다크, 1440×940·980×680, 동일 패널 너비·페이지 넘침 없음·날짜 잘림 없음, 파일 이름·경로 HTML 주입 방지 |

소스 비교는 SDK에 포함된 Roslyn으로 파싱해 문자열·전처리기 분기의 실행 토큰과 옮긴 메서드 본문을 확인했습니다. 이는 빌드·API 테스트·실제 UI 검사를 보완하며 그 검사를 대신하지 않습니다.

주요 근거:

- [소스 비교 결과](refactor-source-check.json)
- [브라우저 검사 결과](refactor-browser-results.json)
- [전체 검증 요약](verification-refactoring.json)
- [Debug 빌드 로그](../../../artifacts/refactor-debug-build.log), [Release 빌드 로그](../../../artifacts/refactor-release-build.log)
- [xUnit 결과](../../../tests/Portway.Tests/TestResults/refactor.trx)
- [라이트 화면](../../../output/playwright/refactor-local-light-1440.png), [작은 다크 화면](../../../output/playwright/refactor-local-dark-980.png)

위 링크는 이 작업 공간의 검증 산출물입니다. 소스만 가져온 환경에는 해당 파일이 없을 수 있습니다. 인증 토큰이 있는 QA 접속 파일·원본 호스트 로그는 공유 보고서에 포함하지 않습니다.

## 검증 한계

원격 탭과 접속 타이머의 UI는 모의 API 응답으로 검사했습니다. 로컬 파일 작업은 실제 Desktop API와 실제 파일을 사용했습니다. 실제 원격 프로토콜·WinSCP 상호 운용·패키지·설치 검사는 조건이 준비되지 않아 기본 테스트에서 건너뛰었습니다. Windows 심볼릭 링크 검사는 권한 조건으로 건너뛰었고 별도의 실제 junction 보호 검사를 수행했습니다.

이번 작업에서 macOS/Linux Photino 실행, 새로운 vpk 패키지 생성·설치·업데이트·배포는 수행하지 않았습니다. 전체 기능 지원 상태는 기존 [PARITY.md](../docs/PARITY.md)를 기준으로 판단합니다. 리팩토링만으로 WinSCP 전체 동등성이나 모든 OS 검증 완료를 의미하지 않습니다.
