# Portway 개발 지침

이 저장소는 Windows·macOS·Linux용 Photino 파일 전송 앱, .NET/CLI 자동화 API, Velopack 배포 서버를 함께 관리합니다. 사용자에게는 한국어로 변경 내용과 실제 검증 범위를 설명합니다.

환경 구성과 릴리스 절차는 [개발자 가이드](doc/Portway.Document/docs/DEVELOPER-GUIDE.md), 실제 사용 흐름은 [사용자 가이드](doc/Portway.Document/docs/USER-GUIDE.md)를 참고하고 기능 변경 시 관련 문서도 갱신합니다.

소스 분석은 [소스 탐색 가이드](doc/Portway.Document/docs/SOURCE-MAP.md) → [솔루션 구조](doc/Portway.Document/docs/ARCHITECTURE.md) → [API 목록](doc/Portway.Document/docs/API-REFERENCE.md) 순서로 시작합니다. 리팩토링 검증 근거는 [보고서](doc/Portway.Document/jobs/REFACTORING.md)에 있습니다.

## 적용 범위와 작업 순서

이 문서는 저장소 전체에 적용합니다. 하위 폴더에 `AGENTS.md`가 추가되면 해당 범위의 세부 지침도 읽습니다. 사용자가 세션에서 명시한 요구사항을 우선하며, 현재 코드와 실행 결과로 문서의 가정을 확인합니다.

1. 요청의 완료 조건과 영향을 받는 프로젝트를 확인하고 관련 소스·설정·검증 기록을 읽습니다. 이미 진행 중인 수정과 사용자 데이터를 보존합니다.
2. 검색은 `rg`/`rg --files`를 우선 사용하고 `bin`, `obj`, `node_modules`, 생성된 `vendor` 및 배포 산출물을 소스 검색에서 제외합니다.
3. 기존 구조 안에서 필요한 변경을 구현합니다. 새 의존성이나 공통 추상화는 실제 필요를 확인한 뒤 추가합니다. 프로토콜별 기능 제한은 기존 capability 검사로 처리합니다.
4. 변경에 맞는 검사와 실제 사용 흐름을 검증하고 관련 가이드를 갱신합니다. 통과한 검사도 새 코드 변경이 생기면 영향을 받는 범위를 다시 확인합니다.
5. 최종 보고에는 변경된 동작, 실행한 검증과 결과, 남은 제한을 간결하게 적습니다. 구현·모의 응답·실제 서버·실제 OS 검증을 구분합니다.

개발에는 .NET 10 SDK, Node.js 20 이상, PowerShell 7이 필요합니다. 프로토콜 통합 검증에는 Docker가 필요하고, 네이티브 UI 실행에는 대상 OS의 WebView 의존성과 그래픽 세션이 필요합니다. 버전의 기준은 각 `.csproj`, [프런트엔드 package.json](assets/frontend/package.json)과 lockfile입니다. 상세 설치 조건은 개발자 가이드를 따릅니다.

## 구조와 기준

- `src/Portway.Core`: 프로토콜, 파일·경로 정책, 암호화, 전송·동기화, 자동화. UI 의존성을 추가하지 않습니다.
- `src/Portway.Desktop`: Photino 호스트, loopback Minimal API, 금고·큐·작업 서비스와 `wwwroot`의 ES 모듈 UI.
- `src/Portway.Cli`: Core API를 사용하는 명령줄 인터페이스.
- `src/Portway.Server`: 릴리스 ZIP 검증·배포 API와 다운로드 대시보드.
- `assets/frontend`: 고정 버전 npm 의존성, 오프라인 웹 자산 빌드, 프런트엔드 테스트.
- `doc/Portway.Document`: 문서 작업을 확장하는 Node.js 프로젝트. 솔루션의 `doc` 폴더에 등록하며 `npm start --prefix doc/Portway.Document`, `npm run build --prefix doc/Portway.Document`로 실행·검사합니다. 기존 가이드는 `doc/Portway.Document/docs/`, 검증 기록은 `doc/Portway.Document/jobs/`에 유지합니다.
- `tests/Portway.Tests`, `tests/infrastructure`: 단위·실제 프로토콜 테스트 및 격리된 Docker 서버.
- 기능 지원 여부는 `doc/Portway.Document/docs/PARITY.md`, 검증 근거는 `doc/Portway.Document/jobs/VALIDATION.md`와 버전별 보고서를 먼저 확인합니다. 구현 완료, 모의 테스트, 실제 OS 검증을 구분하며 WinSCP 완전 호환이라고 단정하지 않습니다.

### 주요 변경 위치

| 변경 대상 | 먼저 확인할 소스 |
| --- | --- |
| 시작·WebView·보안·Desktop API | `Program.cs`, `DesktopHosting.cs`, `DesktopApi.cs`, `Api/DesktopApi.*.cs`, `ProfileStore.cs`, `Connections.cs` |
| 전송·복구·로컬 복사 | Core의 `TransferOperations.cs`, `LocalTransferFileSystem.cs`; Desktop의 `TransferQueue.cs`, `QueueJournal.cs` |
| 동기화·터미널·외부 편집 | Desktop의 `SyncService.cs`, `LiveSyncService.cs`, `TerminalService.cs`, `ExternalEditorService.cs` |
| 외부 파일 드롭·사이트 내보내기 | Desktop의 `DropStore.cs`, `SiteExportService.cs`; UI의 `drop.js` |
| 자동 업데이트 | Desktop의 `AutomaticUpdateWorker.cs`, `UpdateService.cs`, `Program.cs` |
| 파일 탐색·선택·팝업 | `wwwroot/app.js`, `explorer.js`, `selection.js`, `workflows.js`, `advanced.js` |
| 편집기·날짜·테마 | `wwwroot/editor.js`, `editor-state.js`, `display-time.js`, `theme.js`, `design-tokens.css`, `app.css` |
| 화면·파일 행 마크업·공통 UI | `wwwroot/workspace-view.js`, `file-list-view.js`, `ui.js`; 원격 편집은 `RemoteTextFiles.cs` |
| 패키징·배포 | `scripts/build.ps1`, `scripts/package.ps1`, `scripts/publish.ps1`, `src/Portway.Server` |

표의 Desktop 파일은 `src/Portway.Desktop` 기준이며 Core 파일은 `src/Portway.Core` 기준입니다. UI 파일은 Desktop의 `wwwroot` 기준입니다.

## 변경 규칙

- 프로젝트가 직접 작성하는 주석과 로그·진단 설명은 한국어로 씁니다. 명령·옵션·프로토콜 값, JSON 필드, 구조화 로그의 치환 필드 이름과 외부 라이선스 원문은 유지합니다. 외부 라이브러리의 기본 로그나 원격 명령 출력은 임의로 번역하지 않습니다. 생성 자산의 주석은 빌드 원본에서 수정합니다.
- 기존 Core → 서비스 → Minimal API → UI 경계를 유지하고 관련 기능 단위로 수정합니다. .NET 10, 현재 Photino/Velopack 버전을 이유 없이 변경하지 않습니다.
- 경로·파일 이름은 외부 입력입니다. 경로 이탈, 대소문자 충돌, 심볼릭 링크/재분석 지점과 임시 파일 덮어쓰기를 검사합니다. 재귀 삭제는 검증한 앱 소유 경로에만 한정합니다.
- SSH 호스트 키와 TLS 검증을 약화하지 않습니다. 실제 비밀번호·토큰·암호화 키는 소스, 로그, 큐 저널, 스크린샷에 넣지 않습니다.
- Desktop API의 loopback 제한, Bearer 인증, Origin 검사, CSP를 유지합니다. WebView에서 임의 로컬 파일 경로를 추측하거나 브라우저 보안을 해제하지 않습니다.
- Photino 개발자 도구는 `#if DEBUG`의 Debug 빌드에서만 활성화합니다. Windows의 F12는 WebView2 기본 단축키로 처리하며 UI에서 가로채지 않습니다. Release 빌드에는 활성화하지 않습니다.
- Photino의 사용자 데이터·캐시는 `ProfileStore.DataPath/webview`를 생성한 뒤 `SetTemporaryFilesPath`로 지정합니다. 다른 앱과 공용 `%LOCALAPPDATA%/Photino` 폴더를 공유하면 WebView2 초기화가 충돌할 수 있으므로 기본값으로 되돌리지 않습니다. 시작 문제 검증은 headless 브라우저뿐 아니라 실제 Windows Debug 창, 같은 프로필 재시작, F12를 포함합니다.
- 사이트 내보내기/가져오기는 버전이 있는 Portway JSON과 기존 WinSCP INI 가져오기를 지원합니다. 비밀 제거는 `SiteSecrets`를 재사용하고 파일 암호화 사용 여부를 보존합니다. 가져오기는 전체 검증 후 새 ID로 추가하며 기존 사이트·금고 비밀을 덮어쓰지 않습니다. 키 없는 메타데이터 가져오기가 연결·전송의 키 검증을 완화해서는 안 됩니다.
- 자동 업데이트는 호스트 시작 후 백그라운드 확인·다운로드까지만 수행합니다. 준비한 패키지는 다음 시작의 Velopack 초기화에서 UI·파일 작업보다 먼저 적용합니다. 현재 작업 중 강제 재시작을 추가하지 않으며 자동·수동 작업의 직렬화, 종료 취소, 오류 격리를 유지합니다. 관련 변경은 `UpdateTests`와 실제 설치본의 다음 시작 적용을 검증합니다.
- 외부 드롭은 파일 관리자 → 원격 패널 업로드입니다. `drop.js`는 이벤트 안에서 파일 참조를 확보하고 폴더 배치를 끝까지 읽습니다. `DropStore`는 제한된 청크를 디스크로 스트리밍하고 완전한 스냅샷만 큐에 넘깁니다. 원본 파일은 유지하며, 큐가 사용하는 임시 원본은 재시도·재시작 후에도 보존합니다.
- 기존 사용자 데이터, 배포 산출물, 다른 실행 중 프로세스를 작업 편의를 위해 삭제하지 않습니다. 테스트는 GUID 등으로 구분한 전용 프로필·폴더·서버에서 실행하고 자신이 시작한 프로세스만 종료합니다.
- 사용자 파일 삭제·덮어쓰기·이동에 대한 기존 확인과 충돌 정책을 유지합니다. 큐 요청·저널·사이트·설정의 저장 형식을 변경할 때는 기존 데이터 읽기와 재시작 복구를 검증합니다. UI의 늦은 비동기 응답이 새 탭이나 새 팝업의 상태를 덮어쓰지 않도록 합니다.

## 디자인 시스템

- 파일 날짜는 `display-time.js`에서 현지 시간의 `YYYY-MM-DD 오전/오후 h:mm`으로 표시하며 없는 시각·서버 기본 날짜는 `—`로 표시합니다. 접속 경과 시간은 성공한 연결별 시작 시점을 유지해 상태바 오른쪽에서 초 단위로 갱신합니다. 탭 전환 시 초기화하지 않고 로컬 탭에서는 숨기며, 갱신으로 기존 오류·진행 메시지를 덮어쓰지 않습니다.
- 로컬 작업 공간은 양쪽 모두 내 컴퓨터를 표시합니다. 내부 패널 ID `local`/`remote`는 위치를 나타내므로 로컬 파일 API 선택에는 `isLocal(side)`를 사용합니다. 오른쪽 로컬 상태와 서버별 패널 상태를 분리하고 탭 전환 시 경로·필터·선택 및 지연된 목록 응답을 검증합니다. 로컬 전송은 큐의 `direction: local`과 `LocalTransferFileSystem`을 사용하며 서버 연결 없이 복사·이동·재시작 이어하기를 지원합니다. 같은 경로·포함 관계·링크를 차단하고 내용·줄바꿈을 변환하지 않습니다.
- 앱 창과 웹 문서 제목은 `Portway · 파일 전송 클라이언트`입니다. 별도 `header.topbar` 없이 연결 탭부터 시작하며, 테마 전환은 사이드바와 설정에서 제공하고 새 연결은 새 사이트·연결 탭 추가에서 엽니다.
- 상단의 업로드·다운로드·외부 파일·외부 폴더·동기화·지속 동기화는 접근성 이름을 가진 Tabler `btn-group`으로 묶고 버튼 사이 gap을 두지 않습니다. 파일 패널은 같은 너비의 두 열이며 가운데 전송 아이콘 버튼을 추가하지 않습니다.
- UI와 Monaco 글꼴은 패키지에 포함한 **Noto Sans KR Variable**을 사용합니다. `--tblr-font-sans-serif`와 로컬 Fontsource 자산을 함께 유지합니다. 강조색은 Tabler 기본 primary이며 임의 녹색으로 재정의하지 않습니다. 아이콘 폰트는 Tabler Icons를 유지합니다.
- 기본 컴포넌트와 색상 토큰: **Tabler CSS**. 아이콘: **Tabler Icons 웹 폰트**의 `<i class="ti ti-..." aria-hidden="true">`. 레이아웃 유틸리티: **Master CSS**.
- `app.css`는 워크스페이스 레이아웃과 의미 있는 색상 토큰을 담당합니다. 새 화면의 색상을 개별 하드코딩하지 말고 `--surface`, `--canvas`, `--ink`, `--muted`, `--line`, `--accent` 등과 Tabler 변수를 재사용합니다.
- Master 유틸리티는 `class="flex ai:center gap:12"`처럼 완전한 문자열로 작성합니다. 동적 조각을 연결하면 정적 빌드가 추출하지 못합니다.
- `theme.js`가 최초 렌더 전에 `data-bs-theme`를 지정합니다. 라이트·다크만 선택하게 합니다. 최초 실행 또는 기존 `system` 설정은 시작 시 OS 밝기로 한 번 해석해 고정·저장하고 OS 변경을 구독하지 않습니다. 다크 배경은 Visual Studio 2026의 중성 회색 계층을 유지하고, 입력·헤더·팝업·편집기에 공유 토큰을 사용합니다. Desktop의 영구 설정은 `/api/preferences`에 저장합니다.
- 라이브 CDN/외부 웹 폰트 요청을 추가하지 않습니다. `wwwroot/vendor`는 빌드 산출물이며 수동 수정하지 않습니다. 의존성 변경 시 정확한 버전과 lockfile, 라이선스를 함께 갱신합니다.
- 내장 편집기는 `editor.js`의 Monaco와 `editor-state.js`의 저장 스냅샷을 사용합니다. 저장 요청 시점과 이후 입력을 구분하고 ETag·인코딩·BOM을 보존합니다. 닫을 때 모델·편집기·테마 구독을 해제하며 공통 입력/버튼 CSS가 `.monaco-editor` 내부를 덮지 않게 합니다. 작업자는 동일 출처의 로컬 ESM 파일만 사용하고 외부 스키마 요청을 켜지 않습니다.
- 사이드바는 Folded Hover의 64px 아이콘 열과 260px 펼침을 유지합니다. 마우스 hover와 키보드 `:focus-visible` 진입, hover 없는 장치의 전체 메뉴, 모션 줄이기를 확인합니다. 접힌 버튼의 접근성 이름·도구 설명을 보존하고 펼침이 파일 패널을 밀지 않게 합니다. 선택 체크박스 열에는 말줄임표를 적용하지 않습니다.
- 파일 선택은 `selection.js`의 경로 기반 상태와 `explorer.js`의 입력 처리를 사용합니다. 정렬·필터 이후 범위 기준, 박스 드래그 취소, 고정 헤더 아래 커서 표시, 오래된 목록 응답 무시를 유지합니다. 내부 드래그는 캡처한 세션·선택 경로만 사용하고 외부 파일 드롭을 가로채지 않습니다. 다중 작업 오류는 완료 개수와 남은 목록을 보여 줍니다.
- 파일 작업 단축키는 마지막 선택 패널이 아니라 `document.hasFocus()`와 `document.activeElement`의 실제 파일 목록 포커스를 확인합니다. F2·F4·F5·F7·Delete는 패널 밖에서 파일 작업을 실행하지 않습니다. F5 기본 새로고침을 막되 입력란의 Delete 편집은 유지하고, 패널 강조도 실제 포커스에 맞춥니다.
- SSH 터미널 커서는 표시·숨김을 각각 1초 유지합니다. 전역 모션 줄이기 규칙이 무한 반복 커서의 주기를 단축하지 않도록 보호하고, 포커스 재진입 및 모션 줄이기 켜짐·꺼짐에서 애니메이션을 멈추지 않은 실제 시간 간격을 확인합니다.
- 사이트 연결·편집 입력란의 Enter는 연결 버튼으로 제출합니다. 한글 조합 확정, 입력 중복 제출, 선택 컨트롤 및 저장만 버튼의 명시적 활성화를 구분합니다. 오류는 모달의 최상위 레이어 안에 토스트로 표시하고 입력값·팝업을 유지하며, 닫을 때 남은 토스트를 기본 표시 영역으로 옮깁니다.
- 공통 `modal()`은 새 dialog 노드를 만들어 이전 창의 지연된 close 이벤트가 새 창을 정리하지 않도록 합니다. 편집기·설정·터미널을 연속으로 열고 닫아 이 수명 주기를 검증합니다.
- 버튼·필드에 접근 가능한 이름을 붙이고 키보드 포커스, 대비, 작은 창(980×680), 대화상자, 오류·진행 상태를 두 테마에서 확인합니다.

## 개발·검증 명령

다음 명령은 저장소 루트에서 실행합니다. `npm ci`는 최초 구성 또는 lockfile 변경 시 사용합니다. Master 클래스·의존성·공유 테마 등 자산 빌드 입력을 바꿨다면 프런트엔드 빌드 후 .NET을 빌드해 실행 폴더에 최신 `wwwroot`가 복사되도록 합니다. 소스 폴더와 실행 중인 오래된 패키지를 혼동하지 않습니다.

```powershell
npm ci --prefix assets/frontend
npm run build --prefix assets/frontend
npm test --prefix assets/frontend
dotnet build Portway.slnx -c Release
dotnet test Portway.slnx -c Release
```

Visual Studio 또는 실행 중인 앱이 기본 출력 파일을 잠그면 해당 프로세스를 종료하는 대신 별도 출력으로 검증합니다. 브라우저 검증 스크립트의 `-Executable`에는 이 빌드에서 생성한 실행 파일을 지정합니다.

```powershell
$qaBuildPath = Join-Path 'artifacts/qa' ('build-' + [Guid]::NewGuid().ToString('N'))
dotnet build src/Portway.Desktop/Portway.Desktop.csproj -c Debug --artifacts-path $qaBuildPath
```

### 변경별 검증 범위

| 변경 | 검증 기준 |
| --- | --- |
| 파일 탐색·선택·키보드·팝업·날짜 | 관련 프런트엔드 테스트와 Playwright 사용 흐름, 라이트/다크·980×680 화면 |
| 전송·충돌·이동·이어하기 | `LocalTransferTests`, `TransferRecoveryTests` 등 관련 테스트, 실제 대상 파일·폴더와 SHA-256, 취소·실패·재시작 |
| 프로토콜·인증·암호화 | 관련 단위 테스트 및 격리 서버 통합 검증; WinSCP 상호 운용은 실행 조건이 충족된 경우 별도 확인 |
| 사이트 내보내기/가져오기 | `SiteArchiveTests`, 비밀 제거·잘못된 입력·새 ID·기존 사이트 보존, 팝업 사용 흐름 |
| 자동 업데이트 | `UpdateTests`, 해당 OS 설치본의 백그라운드 다운로드·다음 시작 적용 |
| 배포 서버·릴리스 ZIP | `ReleaseTests`, 실제 ZIP을 사용하는 `PackageTests`, 업로드 인증·채널·다운로드·기존 버전 불변성 |
| Photino 초기화·네이티브 창 | 해당 OS의 실제 앱 창; Windows Debug 정상 시작·같은 프로필 재시작·F12 |
| 문서만 변경 | 로컬 링크, 파일명, 명령 옵션, 버전·검증 범위를 소스와 대조 |

위 표는 변경에 따라 검사를 선택하는 기준입니다. 관련 검사가 통과하고 남은 의문이 없으면 불필요하게 전체 검사를 반복하지 않습니다. 회귀 위험이 여러 프로젝트에 걸치면 솔루션 빌드·테스트까지 확대합니다.

실제 프로토콜 검증이 필요한 변경은 다음 격리 서버를 사용합니다. 기본 단위 테스트 실행에서 건너뛴 통합 테스트를 통과로 보고하지 않습니다.

```powershell
./scripts/test-integration.ps1
```

후속 브라우저/API 검증을 위해 서버를 유지할 때는 대신 다음 명령을 사용합니다.

```powershell
./scripts/test-integration.ps1 -KeepFixtures
# 후속 검증이 끝나면 같은 Compose 프로젝트를 종료합니다.
docker compose -p portway-tests -f tests/infrastructure/compose.yaml down
```

- 통합 스크립트는 `PORTWAY_INTEGRATION`을 설정하고 기본 실행에서는 테스트 종료 후 fixture를 정리합니다. 수동으로 서버를 시작한다면 시작·종료에 같은 Compose 프로젝트명과 파일을 사용합니다. 기존 운영 서버를 테스트 fixture로 사용하지 않습니다.
- `PORTWAY_WINSCP_REFERENCE`와 `PORTWAY_PACKAGE_TEST`은 공식 WinSCP 상호 운용/실제 패키지 테스트를 활성화하는 선택 환경 변수입니다. 세부 조건은 해당 테스트와 검증 문서를 읽습니다.
- 실제 Windows 변경분 검사는 `PORTWAY_PACKAGE_BASE`(기준 Full nupkg)와 `PORTWAY_PACKAGE_UPDATER`(검증 전용 Update.exe)도 지정합니다. 격리된 경로에서 다운로드·복원만 검사하며 TestVelopackLocator 검사를 실제 설치·시작 적용으로 보고하지 않습니다.
- 관련된 회귀 테스트를 추가·실행합니다. 단순한 문구·가역적인 스타일 수정에 구현을 그대로 따라 쓰는 테스트를 추가하지 않습니다.
- 브라우저 검증은 Playwright CLI를 우선 사용합니다. 결과는 `output/playwright/`, 임시 입력·프로필은 `artifacts/qa/`에 보관합니다. `scripts/start-browser-qa.ps1 -Executable <실행 파일>`로 격리된 headless 호스트를 시작할 수 있습니다.
- 외부 드롭 변경은 실제 파일 경로를 넣은 Chromium 드롭/CDP 또는 OS 파일 관리자로 검증합니다. 중첩·빈 폴더, 100개 초과 디렉토리 항목, 청크보다 큰 파일, 유니코드 이름, 취소, 충돌, 전송 후 해시를 확인합니다. 합성 이벤트만으로 Explorer/Finder 네이티브 검증을 완료했다고 쓰지 않습니다.
- 포맷은 C#의 루트 `.editorconfig`와 `dotnet format whitespace`, JS/CSS/HTML의 `.prettierrc.json`과 고정된 Prettier를 사용합니다. `npm run format --prefix assets/frontend`, `npm run format:check --prefix assets/frontend`로 작성 소스만 처리하고 `.prettierignore`의 생성물 제외를 유지합니다. 여러 실행문을 한 줄로 이어 쓰지 않고 HTML 템플릿은 `/* HTML */` 포맷 주석을 유지합니다. 기능 변경 뒤 필요한 검증이 통과하면 불필요하게 전체 검사를 반복하지 않습니다.
- `DesktopApi`는 `/api` 그룹 조립을 유지하고 기능 경로는 `Api/DesktopApi.*.cs`에서 등록합니다. 기존 DTO 중첩 타입·URL·HTTP 메서드·JSON 필드·상태 코드를 보존합니다. UI 마크업은 뷰 모듈에서 생성하고 API·이벤트·상태 변경은 app.js와 기능 모듈에서 처리합니다. 경로 변경 시 `doc/Portway.Document/docs/API-REFERENCE.md`를 갱신합니다.

## 문서와 검증 기록

- 새 가이드·API·구조 문서와 이미지는 `doc/Portway.Document/docs/` 아래에서 관리합니다. 이미지 경로는 `doc/Portway.Document/docs/images/`입니다. 문서의 상대 링크는 해당 Markdown 파일의 위치를 기준으로 작성합니다.
- 특정 버전·변경 사항의 검증 문서와 JSON은 `doc/Portway.Document/jobs/`에 생성합니다. 파일명은 `VALIDATION-<버전 또는 작업명>.md`, `verification-<버전 또는 작업명>.json`을 사용하며 재검증 시 날짜·실행 ID로 이전 기록을 보존합니다. PowerShell JSON 생성에는 `scripts/write-verification.ps1`을 사용합니다. [기록 보관 규칙](doc/Portway.Document/jobs/README.md)을 따릅니다. 기존 버전별 검증 JSON도 모두 `doc/Portway.Document/jobs/`에 보관합니다. JSON 내부의 경로는 저장소 루트 기준으로 기록하고 과거 이동 명세의 source/destination은 당시 이력으로 보존합니다.
- 사용자 동작이 바뀌면 [사용자 가이드](doc/Portway.Document/docs/USER-GUIDE.md), 구조·환경·API·검증 절차가 바뀌면 [개발자 가이드](doc/Portway.Document/docs/DEVELOPER-GUIDE.md)를 갱신합니다. 이후 개발자가 유지해야 하는 규칙은 이 문서에도 반영합니다.
- 기능 지원 범위가 바뀌면 [PARITY.md](doc/Portway.Document/docs/PARITY.md), 검증 결과는 [VALIDATION.md](doc/Portway.Document/jobs/VALIDATION.md) 또는 해당 변경의 검증 보고서에 기록합니다. 이전 테스트 수치와 패키지 버전을 새 코드의 결과로 재사용하지 않습니다.
- 검증 기록에는 검사한 소스/빌드, 운영체제, 실행 명령, 통과·실패·건너뜀, 모의 응답 사용 여부, 주요 결과물과 미검증 범위를 남깁니다. 스크린샷·JSON 보고서는 내용을 확인한 뒤 공유합니다.
- `artifacts/browser-qa.json`에는 인증 토큰이 들어 있습니다. 이 파일이나 인증 URL이 포함된 로그·브라우저 기록을 문서·보고서·커밋에 포함하지 않습니다. 공개할 기록은 비밀을 제거한 별도 파일로 만듭니다.

## 릴리스

- 일반 개발·버그 수정은 소스와 로컬 검증까지 수행합니다. 릴리스가 요청되면 새 버전·패키지·배포 절차를 진행하며, 개발 코드의 검증을 기존 설치 패키지의 검증으로 표시하지 않습니다.
- 버전은 `Directory.Build.props`와 빌드 스크립트 기본값을 맞춥니다. 이미 존재하는 `dist/publish/<RID>/<version>`과 게시 ZIP을 덮어쓰지 말고 새 버전을 사용합니다.
- 프런트엔드 빌드 후 `scripts/build.ps1 -Version <새 버전> -Runtime <RID>`로 대상 OS에서 vpk를 생성합니다. CLI도 패키지에 포함됩니다.
- 패키징은 `scripts/package.ps1`에서 버전별 `dist/releases/<채널>/<버전>/`을 생성하고 ZIP에는 해당 버전만 넣습니다. 최신 Full은 변경분 기준으로만 사용합니다. 새 작업 공간에서는 `-PreviousReleaseUrl`로 같은 채널의 최신 Full을 받고 최초 채널일 때만 `-AllowEmptyChannel`을 사용합니다. 서버는 Full/Delta 피드를 합치며 새 Delta의 `BaseVersion`이 서버 최신 Full과 일치하는지 검사합니다. 변경 시 `ReleaseTests`, 실제 ZIP `PackageTests`와 정상 변경분·손상 시 전체 다운로드 전환을 검증합니다.
- Windows 교차 게시가 macOS 네이티브 실행·서명·공증 검증을 대신하지 않습니다. 사용하지 못한 실제 OS나 원격 환경은 검증 한계에 명시합니다.
- 배포 서버 업로드가 필요하면 실제 생성된 ZIP의 무결성과 채널을 확인합니다. 서명 여부, 설치/업데이트 테스트 여부를 정확히 기록하고 기존 릴리스의 불변성을 유지합니다.

사용자가 명시한 범위의 구현·수정·로컬 검증은 계속 진행합니다. 환경이나 필수 정보가 없을 때만 해당 의존성을 설명하고, 독립적으로 할 수 있는 작업은 완료합니다.
