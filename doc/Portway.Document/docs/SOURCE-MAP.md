# Portway 소스 탐색 가이드

처음 분석하는 개발자를 위한 읽기 순서와 기능별 진입점입니다. 실행 방법은 [개발자 가이드](DEVELOPER-GUIDE.md), 책임과 수명은 [솔루션 구조](ARCHITECTURE.md), HTTP 경로는 [API 목록](API-REFERENCE.md)을 참고하세요.

## 처음 읽는 순서

솔루션의 `doc` 폴더에는 [Portway.Document](../README.md) Node.js 프로젝트와 [Portway.Artifact](../../Portway.Artifact/Portway.Artifact.esproj) JavaScript 프로젝트가 있습니다. Document는 문서 작업용 기본 콘솔 진입점이며 기존 가이드와 검증 기록은 각각 `doc/Portway.Document/docs/`, `doc/Portway.Document/jobs/`에 유지합니다. Artifact는 루트의 `assets/`, `deploy/`, `scripts/` 파일을 현재 위치에서 Visual Studio에 연결합니다.

1. [Portway.slnx](../../../Portway.slnx)와 각 프로젝트 파일로 프로젝트 참조를 확인합니다. 버전은 `Directory.Build.props`, SDK는 `global.json`이 기준입니다.
2. [Desktop Program.cs](../../../src/Portway.Desktop/Program.cs)에서 Velopack 초기화 → 호스트 시작 → Photino 창 → 종료 흐름을 읽습니다.
3. [DesktopHosting.cs](../../../src/Portway.Desktop/DesktopHosting.cs)에서 DI의 singleton/hosted service 공유와 API 보안·오류 처리를 확인합니다.
4. [DesktopApi.cs](../../../src/Portway.Desktop/DesktopApi.cs)에서 분석할 기능의 `Api/DesktopApi.*.cs`로 이동합니다.
5. 해당 API가 호출하는 작업 서비스와 [Core Models.cs](../../../src/Portway.Core/Models.cs)의 모델·Capabilities·IRemoteFileSystem을 읽습니다.
6. 웹 UI는 [app.js](../../../src/Portway.Desktop/wwwroot/app.js)의 `boot`, `load`, `connect`, `transfer`와 마지막 기능 조립 부분부터 읽고 관련 모듈로 이동합니다.
7. 아래 기능 표의 테스트를 실행해 정상·오류·복구 조건을 확인합니다. 실제 원격 서버가 필요한 검사는 실행 조건을 먼저 확인합니다.

## 기능별 호출 경로

표의 API 파일은 `src/Portway.Desktop/Api`, 작업 서비스는 `src/Portway.Desktop`, UI는 Desktop의 `wwwroot`, 테스트는 `tests/Portway.Tests` 기준입니다.

| 분석하려는 기능 | UI → API → 작업 처리 | 먼저 읽을 테스트 |
| --- | --- | --- |
| 첫 실행·빈 화면·F12 | `app.js: boot` → `Program.cs`, `DesktopHosting.cs` | `DesktopApiTests`; 실제 Windows Debug 시작·재시작 |
| 사이트·Vault·가져오기 | `siteDialog`, `importDialog` → `DesktopApi.Profiles.cs` → `ProfileStore`, `SiteExportService`, Core `SiteArchive`, `SiteSecrets` | `SiteArchiveTests`, `CoreTests` |
| 서버 연결·지문·추가 인증 | `connect`, `advanced.js` → `DesktopApi.Connections.cs` → `Connections`, `AuthenticationBroker` → Core `RemoteFactory`, `Protocols` | `AdvancedTests`, `AdvancedProtocolTests`, `ProtocolTests` |
| 목록·선택·단축키·드래그 | `load`, `renderPane`, `explorer.js`, `selection.js` → `DesktopApi.Files.cs` → `LocalFiles`, `Connections.Use` | `assets/frontend/tests/explorer.test.mjs`, `selection.test.mjs`; 실제 브라우저 흐름 |
| 업로드·다운로드·이어하기 | `transfer` → `DesktopApi.Transfers.cs` → `TransferQueue`, `QueueJournal` → Core `TransferOperations` | `TransferRecoveryTests`, `ProtocolTests` |
| 로컬 두 패널 복사·이동 | `isLocal`, `transfer` → `DesktopApi.Transfers.cs` → `TransferQueue` → Core `LocalTransferFileSystem`, `TransferOperations` | `LocalTransferTests` |
| 외부 파일 관리자 드롭 | `drop.js` → `DesktopApi.Transfers.cs` → `DropStore` → `TransferQueue` | `DropTests`, `assets/frontend/tests/drop.test.mjs` |
| Monaco 편집·저장 충돌 | `edit`, `editor.js`, `editor-state.js` → `DesktopApi.Files.cs` → `LocalFiles` / `RemoteTextFiles` | `DesktopApiTests`, `CoreTests`, `assets/frontend/tests/editor.test.mjs` |
| 동기화·지속 동기화 | `syncDialog`, `workflows.js` → `DesktopApi.Synchronization.cs` → `SyncService`, `LiveSyncService` → Core `Synchronizer` | `WorkflowTests` |
| 터미널·휴지통·외부 편집 | `workflows.js` → `DesktopApi.Tools.cs` → `TerminalService`, `TrashService`, `ExternalEditorService` | `WorkflowTests` |
| 업데이트 | `settings` → `DesktopApi.Updates.cs` → `UpdateService`; 시작 자동 작업은 `AutomaticUpdateWorker` | `UpdateTests`, `scripts/test-automatic-update.ps1`, `test-installed-update.ps1` |
| 스크립트·.NET 자동화 | `Portway.Cli/Program.cs` → Core `Automation/ScriptEngine`, `Session` | `AutomationTests` |
| 릴리스 게시·다운로드 | Server `wwwroot/server.js` → `ServerApi.cs` → `ReleaseStore` | `ReleaseTests`, `PackageTests` |

## 화면 변경의 진입점

- 골격·패널·상태바: `workspace-view.js`. 파일 행·선택 도구: `file-list-view.js`.
- 외부 문자열·아이콘·크기: `ui.js`. 날짜·연결 경과 시간: `display-time.js`.
- 화면 상태·이벤트 연결: `app.js`. 선택과 실제 포커스 판정: `explorer.js`, `selection.js`.
- 테마 색상: `design-tokens.css`. 배치·컴포넌트 스타일: `app.css`. 최초 테마: `theme.js`.
- 오프라인 의존성·Master CSS 생성: `assets/frontend/build.cjs`. `vendor` 파일을 직접 수정하지 않습니다.

## 저장 데이터와 복구를 읽을 때

| 데이터 | 담당 코드 | 확인할 조건 |
| --- | --- | --- |
| 사이트·Vault·설정 | `ProfileStore`, Core `SiteSecrets` | 잠금·비밀 제거·기존 설정 읽기·가져오기 새 ID |
| 전송 체크포인트 | `QueueJournal`, `TransferQueue` | 중단 후 일시정지 복원·원본 변경·서버/키 일치·부분 파일 |
| 외부 드롭 staging | `DropStore` | 완성된 배치만 commit·재시도 중 보존·사용 중 파일 정리 금지 |
| 지속 동기화·외부 편집 | `LiveSyncService`, `ExternalEditorService` | 종료·재시작 상태·충돌·복구용 파일 |
| 업데이트 패키지 | `UpdateService`, `Program` | 다음 시작 적용·수동/자동 직렬화·앱 작업 보호 |
| 서버 릴리스 | `ReleaseStore` | ZIP 이탈·채널·해시·동일 버전 내용 불변성 |

UI의 `state.current`, pane, 선택은 화면 수명에 속합니다. 이를 영속 큐나 프로필 데이터와 혼동하지 않습니다. UI 연결을 닫거나 새 탭을 열 때 늦게 도착한 목록 응답·팝업 close 이벤트가 새 화면에 적용되는지도 확인합니다.

## 포맷과 검증

C#은 루트 `.editorconfig`와 `dotnet format whitespace`, JS/CSS/HTML은 `.prettierrc.json`과 Prettier를 사용합니다. HTML 템플릿의 `/* HTML */` 주석을 유지하면 템플릿 내부도 포맷됩니다. `.prettierignore`는 생성물·의존성·검증 산출물을 제외합니다.

```powershell
dotnet format whitespace Portway.slnx --no-restore
npm run format --prefix assets/frontend
npm run build --prefix assets/frontend
npm test --prefix assets/frontend
dotnet test Portway.slnx -c Release
```

JS 포맷 도구는 devDependency에 고정한 Prettier 3.9.9입니다. `npm ci --prefix assets/frontend`로 동일한 버전을 설치하며 `npm run format:check --prefix assets/frontend`로 쓰기 없이 확인합니다. 생성된 Server 공통 자산은 Desktop 원본과 자산 빌드로 맞춥니다.

실행 중인 앱이 출력 파일을 잠그면 별도 `--artifacts-path`로 빌드합니다. 테스트는 전용 프로필·폴더에서 수행하고 자신의 프로세스만 종료합니다. 예제·실제 OS 검사와 검증 기록 작성 기준은 [AGENTS.md](../../../AGENTS.md)에 있습니다.
