# Portway 분석서 — 요구사항과 근거

현재 구현은 탐색·연결, 파일 작업, 작업 제어·복구, 자동화, 배포로 분류됩니다. 아래 요구사항은 코드에서 복원한 현재 동작이며 신규 요구사항 승인을 뜻하지 않습니다. 사용자 관점의 성공 조건은 [기획서](PLANNING.md), 분석 기준과 증거 구분은 [읽기 안내](README.md)에 있습니다.

<a id="rules"></a>
## 기능별 업무 규칙과 실패 조건

| 기능군·요구사항 | 현재 규칙 | 실패·제약과 사용자의 다음 행동 |
| --- | --- | --- |
| 탐색·화면, REQ-001·015 | 선택은 경로 기반. 목록 요청의 패널·요청 ID·세션을 확인해 늦은 응답을 버림. 파일 단축키는 실제 목록 포커스를 검사 | 다른 탭·팝업의 상태로 이전 응답을 적용하지 않음. 목록 밖 Delete는 입력 편집에 사용 |
| 연결, REQ-002 | 사이트 검증 → 비밀 복원 → 프로토콜 생성 → 인증·초기 목록 → 연결 ID 등록 | 미지원 프로토콜·잘못된 지문·인증 실패는 연결을 등록하지 않음. SSH 단일 점프만 허용 |
| 사이트·금고, REQ-003 | 금고 잠금 해제 후 비밀 저장·복원. 가져오기는 전체 검증 후 새 ID로 추가. 내보내기는 비밀 제거 | 잘못된 가져오기 배치는 기존 사이트를 변경하지 않음. 암호화 메타데이터 가져오기 후 실제 연결에는 키가 필요 |
| 전송, REQ-004·005 | 충돌 정책 `skip/replace/newer/rename`. 이동은 성공한 파일만 원본 제거. 로컬은 절대 경로·바이너리 모드 | 로컬 동일·포함 경로, 대상 이름 충돌, 링크/재분석 지점 거부. 건너뛴 파일과 실패한 파일은 원본 유지 |
| 큐, REQ-006 | 동시 실행 1–8개, 우선순위 -10–10, 예약·일시 오류 재시도. 체크포인트와 원본 정보를 저장 | 비종료 작업은 재시작 시 `paused`. 원본 크기·수정 시각, 복구 연결의 대상·SSH 지문 등을 확인. 프로토콜·변환 방식에 따라 부분 이어하기 제한 |
| 외부 드롭, REQ-007 | 이벤트 안에서 참조 확보, 폴더 항목을 끝까지 수집, 제한된 청크를 디스크에 저장, 완성된 배치만 큐 접수 | 중복·이탈·잘못된 경로와 크기, 미완성 commit 거부. staging 공간 필요. 전송 중인 임시 원본은 정리하지 않음 |
| 내장 편집, REQ-008 | 16 MiB 이하 일반 파일, 인코딩·BOM·ETag 유지. 원격 저장은 업로드 전·후 원본 해시 비교 | 다른 프로그램이 원본을 수정하면 저장 거부. 저장 중 추가 입력은 다음 저장 대상. 파일을 다시 읽고 변경을 확인해야 함 |
| 동기화, REQ-009 | 미리보기 → 항목 선택·충돌 방향 → 적용. 적용 전 스냅샷 재검사. 양방향에서 추가 파일 삭제 금지 | 원본 변경·링크·이름 충돌 거부. 코드상 항목·깊이 가드와 30분 계획 만료. 일부 적용 이후 전체 롤백 없음 |
| 지속 동기화, REQ-010 | 단방향, 로컬 감시와 주기 비교, 일시정지·재개·삭제, 등록 영속화 | 재시작 후 자동 재개하지 않음. 같은 서버와 SSH 지문을 확인해 재개. 네트워크 오류는 재시도 상태로 표시 |
| 도구, REQ-011 | capability별 원격 도구. 외부 편집은 원본 변경 시 자동 업로드 중단·복구 사본 유지. 휴지통은 같은 부모의 `.portway-trash` | 일반 SSH 연결에서만 터미널. 암호화 세션은 별도 일반 연결 필요. 휴지통 복원은 원위치 충돌과 원격 대상·지문 검사 |
| 자동화, REQ-012 | CLI와 .NET Session은 Core를 직접 사용. 미지원 명령·옵션은 오류 | CLI 종료 코드 0/1/130. continue 모드에서도 오류가 있으면 최종 1. Desktop 큐 영속성은 자동 제공되지 않음 |
| 릴리스, REQ-013 | 게시 인증·ZIP·피드·패키지 해시 검사, 채널별 직렬화, 기존 패키지 불변성 | 잘못된 Delta 기준·같은 버전 내용 변경 거부. 공개 다운로드는 게시 인증과 별개. 다중 서버 인스턴스의 공유 볼륨 쓰기 미지원 |
| 업데이트, REQ-014 | 시작 완료 후 확인·다운로드, 다음 시작 적용. 자동·수동 요청 직렬화 | URL 미설정은 비활성, 비설치 상태는 사용 불가, 네트워크 실패는 앱 시작과 격리. 수동 적용은 활성 작업 검사 |

규칙의 구현 위치와 검사 항목은 아래 추적표를 기준으로 확인합니다. 한도는 보편적 제품 성능 보장으로 해석하지 않습니다. 동기화의 20,000항목·깊이 64는 `Synchronizer.Preview`의 스캔 가드이며 모든 입력에 대한 정확한 경계 인수 시험을 이번에 실행하지 않았습니다.

<a id="protocols"></a>
## 프로토콜별 기능 제한

**구현 확인:** 다음은 실제 서버 인증 결과가 아닌 [Capabilities 모델](../../../../src/Portway.Core/Models.cs)과 각 구현이 반환하는 값입니다. `예`도 서버 권한·구현에 따라 작업이 실패할 수 있습니다.

| 구현 | 부분 이어하기 | 권한 | 셸 명령 | 원자적 rename 선언 | 시각 설정 | 링크 생성 |
| --- | --- | --- | --- | --- | --- | --- |
| [SFTP](../../../../src/Portway.Core/Protocols/SftpFileSystem.cs) | 예 | 예 | 예 | 예 | 예 | 예 |
| [SCP](../../../../src/Portway.Core/Protocols/ScpFileSystem.cs) | 아니오 | 예 | 예 | 예 | 예 | 예 |
| [FTP·FTPS](../../../../src/Portway.Core/Protocols/FtpFileSystem.cs) | 예 | 예 | 아니오 | 예 | MFMT 기능에 따름 | 아니오 |
| [WebDAV·HTTPS](../../../../src/Portway.Core/Protocols/WebDavFileSystem.cs) | 아니오 | 아니오 | 아니오 | 예 | 아니오 | 아니오 |
| [S3](../../../../src/Portway.Core/Protocols/S3FileSystem.cs) | 아니오 | 아니오 | 아니오 | 아니오 | 아니오 | 아니오 |

[EncryptedFileSystem](../../../../src/Portway.Core/EncryptedFileSystem.cs)은 SFTP 암호화 연결에서 이어하기·셸 명령·링크를 비활성화합니다. 다른 capability는 내부 구현을 계승합니다. [RemoteFactory.Create](../../../../src/Portway.Core/RemoteFactory.cs)는 프로토콜 구현에 경로 보호·암호화·터널을 조합합니다. capability가 false인 것과 기본 전송 자체가 불가능한 것을 혼동하지 않습니다.

<a id="quality"></a>
## 품질 속성과 관리상 의미

| 속성 | 구현상 근거 | 판단 한계 |
| --- | --- | --- |
| 보안 | loopback·Bearer·Origin·CSP, SSH/TLS 검증, AES-GCM 금고, 공개 모델의 비밀 제거, 경로 검사 | 보안 감사·침투 시험 완료나 위험 부재를 뜻하지 않음 |
| 복구 가능성 | 큐 저널·원본 스냅샷, 임시 파일 커밋, 지속 동기화의 일시정지 복원 | 정전·강제 종료 전체 조합의 복구 보장은 없음 |
| 일관성 | ETag·해시 충돌 검사, 작업별 직렬화, API/worker 서비스 인스턴스 공유 | 외부 프로세스나 서버 작업과의 전역 트랜잭션은 없음 |
| 사용성·접근성 | 두 패널·키보드 포커스·오류 토스트, 라이트/다크, 최소 창 980×680, 로컬 폰트·아이콘 | 전면 접근성 인증이나 모든 보조 기술 검증은 미확인 |
| 성능·자원 제어 | 큐 동시 실행·속도 제한, 드롭 8 MiB 청크, 텍스트 크기·검색/동기화 한도 | 처리량·응답 시간·대규모 파일 수 SLA는 미확인 |
| 유지보수·이식성 | Core/서비스/API/UI 경계, .NET 10, 대상 OS별 패키징과 오프라인 자산 | 교차 빌드로 네이티브 OS 사용성과 설치 호환성을 대체할 수 없음 |

위 속성의 구현 책임은 [아키텍처](ARCHITECTURE.md#security)와 [설계서의 데이터 수명](DESIGN.md#data)에 연결됩니다. 개선 우선순위나 목표 수치를 새로 설정하지 않습니다.

<a id="traceability"></a>
## 요구사항 추적표

상태 `구현 확인 / 기존 기록`은 소스를 확인했고 연결한 과거 보고서에 관련 결과가 있다는 뜻입니다. **이번 작업에서 기능 테스트가 통과했다는 표시가 아닙니다.** 테스트 열의 이름은 현재 테스트 파일에서 확인한 대표 항목이며 전체 검사를 열거하지 않습니다.

| ID·사용자 기능 | 주요 구현 위치와 진입점 | 설계·아키텍처 | 대표 검사 항목 | 확인 상태·기존 기록 |
| --- | --- | --- | --- | --- |
| REQ-001 탐색·선택 | [app.js](../../../../src/Portway.Desktop/wwwroot/app.js) `load`, [explorer.js](../../../../src/Portway.Desktop/wwwroot/explorer.js) `createExplorer`, [selection.js](../../../../src/Portway.Desktop/wwwroot/selection.js) | [화면](DESIGN.md#screens), [UI 경계](ARCHITECTURE.md#components) | [selection.test.mjs](../../../../assets/frontend/tests/selection.test.mjs) 정렬 후 경로·범위, [explorer.test.mjs](../../../../assets/frontend/tests/explorer.test.mjs) 실제 포커스 | 구현 확인 / [리팩토링 기록](../../jobs/REFACTORING.md) |
| REQ-002 연결·인증 | [Connections](../../../../src/Portway.Desktop/Connections.cs) `Connect/Use`, [RemoteFactory](../../../../src/Portway.Core/RemoteFactory.cs) `Create`, [SshConnection](../../../../src/Portway.Core/Protocols/SshConnection.cs), [TlsOptions](../../../../src/Portway.Core/Protocols/TlsOptions.cs) | [접속 흐름](DESIGN.md#screens), [보안](ARCHITECTURE.md#security) | [AdvancedProtocolTests](../../../../tests/Portway.Tests/AdvancedProtocolTests.cs) `TlsRejectsUnknownCertificateAndAcceptsOnlyExactPin`, [ProtocolTests](../../../../tests/Portway.Tests/ProtocolTests.cs) | 구현 확인 / [0.3.1 기록](../../jobs/VALIDATION-0.3.1.md), 조합별 미검증 있음 |
| REQ-003 사이트·금고·이전 | [ProfileStore](../../../../src/Portway.Desktop/ProfileStore.cs) `Save/Hydrate/ImportSites`, [SiteArchive](../../../../src/Portway.Core/SiteArchive.cs), [SiteSecrets](../../../../src/Portway.Core/SiteSecrets.cs) | [데이터](DESIGN.md#data), [보안](ARCHITECTURE.md#security) | [SiteArchiveTests](../../../../tests/Portway.Tests/SiteArchiveTests.cs) `InvalidBatchLeavesAllExistingSitesUntouched`, `SecretsAreRemovedRecursivelyAndEncryptionStaysRequired` | 구현 확인 / [리팩토링 기록](../../jobs/REFACTORING.md) |
| REQ-004 원격 전송 | [TransferOperations](../../../../src/Portway.Core/TransferOperations.cs) `Transfer/Commit`, [TransferOptions](../../../../src/Portway.Core/TransferOptions.cs) | [전송](DESIGN.md#transfer), [구성](ARCHITECTURE.md#components) | [TransferRecoveryTests](../../../../tests/Portway.Tests/TransferRecoveryTests.cs) `MasksTextModeChecksumsAndMetadataApplyToActualRemoteFiles` | 구현 확인 / [0.3.1 기록](../../jobs/VALIDATION-0.3.1.md) |
| REQ-005 로컬 복사·이동 | [LocalTransferFileSystem](../../../../src/Portway.Core/LocalTransferFileSystem.cs) `Validate/Download`, [TransferQueue](../../../../src/Portway.Desktop/TransferQueue.cs) `Add/Worker` | [전송](DESIGN.md#transfer), [설계 선택](ARCHITECTURE.md#decisions) | [LocalTransferTests](../../../../tests/Portway.Tests/LocalTransferTests.cs) `CopiesNestedEmptyFoldersAndBinaryContentWithoutNewlineConversion` | 구현 확인 / [리팩토링 기록](../../jobs/REFACTORING.md) |
| REQ-006 큐·복구 | [TransferQueue](../../../../src/Portway.Desktop/TransferQueue.cs) `Control/Worker`, [QueueJournal](../../../../src/Portway.Desktop/QueueJournal.cs) `Load/Save` | [상태·복구](DESIGN.md#queue), [수명](ARCHITECTURE.md#lifecycle) | [TransferRecoveryTests](../../../../tests/Portway.Tests/TransferRecoveryTests.cs) `DurableQueueResumesTheSamePartialFileAfterHostRestartAndVaultUnlock`, [LocalTransferTests](../../../../tests/Portway.Tests/LocalTransferTests.cs) `QueueRestoresAndResumesLocalCopyWithoutAnyServerConnection` | 구현 확인 / [0.3.1](../../jobs/VALIDATION-0.3.1.md)·[리팩토링 기록](../../jobs/REFACTORING.md) |
| REQ-007 외부 드롭 | [drop.js](../../../../src/Portway.Desktop/wwwroot/drop.js) `captureDrop/collectDrop`, [DropStore](../../../../src/Portway.Desktop/DropStore.cs) `Append/Commit` | [전송](DESIGN.md#transfer), [데이터](DESIGN.md#data) | [DropTests](../../../../tests/Portway.Tests/DropTests.cs) `PreservesEmptyFoldersAndRejectsIncompleteCommit`, [drop.test.mjs](../../../../assets/frontend/tests/drop.test.mjs) 항목 묶음·빈 폴더 | 구현 확인 / [0.3.1 기록](../../jobs/VALIDATION-0.3.1.md), 실제 OS 드래그는 별도 |
| REQ-008 내장 편집 | [editor-state.js](../../../../src/Portway.Desktop/wwwroot/editor-state.js) `snapshot/accept`, [RemoteTextFiles](../../../../src/Portway.Desktop/RemoteTextFiles.cs) `Read/Write`, [LocalFiles](../../../../src/Portway.Core/LocalFiles.cs) | [편집](DESIGN.md#editing), [데이터](DESIGN.md#data) | [editor.test.mjs](../../../../assets/frontend/tests/editor.test.mjs) 저장 중 입력·실패, [DesktopApiTests](../../../../tests/Portway.Tests/DesktopApiTests.cs) `LocalTextEditingRetainsConflictProtectionAndMissingSessionErrors` | 구현 확인 / [0.3.4](../../jobs/VALIDATION-0.3.4.md)·[리팩토링 기록](../../jobs/REFACTORING.md) |
| REQ-009 미리보기 동기화 | [SyncService](../../../../src/Portway.Desktop/SyncService.cs) `Preview/Apply`, [Synchronizer](../../../../src/Portway.Core/Synchronizer.cs) | [동기화](DESIGN.md#sync), [수명](ARCHITECTURE.md#lifecycle) | [WorkflowTests](../../../../tests/Portway.Tests/WorkflowTests.cs) `SynchronizationRejectsMetadataPreservingEditsAndAppliesSelectedConflictResolution` | 구현 확인 / [0.3.1 기록](../../jobs/VALIDATION-0.3.1.md) |
| REQ-010 지속 동기화 | [LiveSyncService](../../../../src/Portway.Desktop/LiveSyncService.cs) `Add/Control/Run` | [동기화](DESIGN.md#sync), [데이터](DESIGN.md#data) | [WorkflowTests](../../../../tests/Portway.Tests/WorkflowTests.cs) `LiveSyncObservesAtomicSavesPausesAndRestoresPaused` | 구현 확인 / [0.3.1 기록](../../jobs/VALIDATION-0.3.1.md) |
| REQ-011 고급 도구 | [workflows.js](../../../../src/Portway.Desktop/wwwroot/workflows.js), [Tools API](../../../../src/Portway.Desktop/Api/DesktopApi.Tools.cs), [RemoteFileOperations](../../../../src/Portway.Core/RemoteFileOperations.cs), [Files API](../../../../src/Portway.Desktop/Api/DesktopApi.Files.cs) | [도구](DESIGN.md#editing), [수명](ARCHITECTURE.md#lifecycle) | [WorkflowTests](../../../../tests/Portway.Tests/WorkflowTests.cs) `ExternalEditorUploadsSavesDetectsConflictAndKeepsRecoveryCopy`, `RemoteToolsCopySearchPermissionsSymlinkAndTrashRestore`, `RealPtyShellSupportsInputResizeAndCtrlC` | 구현 확인 / [0.3.1 기록](../../jobs/VALIDATION-0.3.1.md) |
| REQ-012 자동화 | [CLI Program](../../../../src/Portway.Cli/Program.cs), [ScriptEngine](../../../../src/Portway.Core/Automation/ScriptEngine.cs) `Run`, [Session](../../../../src/Portway.Core/Automation/Session.cs) | [인터페이스](DESIGN.md#interfaces), [구성](ARCHITECTURE.md#components) | [AutomationTests](../../../../tests/Portway.Tests/AutomationTests.cs) `ScriptRejectsUnsupportedSettingsAndReturnsFailureEvenInContinueMode` | 구현 확인 / [0.3.1 기록](../../jobs/VALIDATION-0.3.1.md) |
| REQ-013 릴리스 | [ServerApi](../../../../src/Portway.Server/ServerApi.cs) `MapDistributionApi`, [ReleaseStore](../../../../src/Portway.Server/ReleaseStore.cs) `Publish`, [피드 병합](../../../../src/Portway.Server/ReleaseStore.Feeds.cs) | [인터페이스](DESIGN.md#interfaces), [배포](ARCHITECTURE.md#deployment) | [ReleaseTests](../../../../tests/Portway.Tests/ReleaseTests.cs) `InvalidDeltaBasisAndChangedVersionLeaveThePublishedFeedUnchanged`, [PackageTests](../../../../tests/Portway.Tests/PackageTests.cs) | 구현 확인 / [패키징 기록](../../jobs/VALIDATION-versioned-packaging.md) |
| REQ-014 업데이트 | [AutomaticUpdateWorker](../../../../src/Portway.Desktop/AutomaticUpdateWorker.cs) `ExecuteAsync`, [UpdateService](../../../../src/Portway.Desktop/UpdateService.cs), [Program](../../../../src/Portway.Desktop/Program.cs) | [업데이트](DESIGN.md#updates), [수명](ARCHITECTURE.md#lifecycle) | [UpdateTests](../../../../tests/Portway.Tests/UpdateTests.cs) `StartupPreparesUpdateButNeverAppliesDuringTheRunningSession`, `AutomaticAndManualRequestsShareASingleDownload` | 구현 확인 / [0.3.3](../../jobs/VALIDATION-0.3.3.md)·[패키징 기록](../../jobs/VALIDATION-versioned-packaging.md) |
| REQ-015 테마·접근성 | [theme.js](../../../../src/Portway.Desktop/wwwroot/theme.js), [workspace-view.js](../../../../src/Portway.Desktop/wwwroot/workspace-view.js), [app.css](../../../../src/Portway.Desktop/wwwroot/app.css), [display-time.js](../../../../src/Portway.Desktop/wwwroot/display-time.js) | [화면](DESIGN.md#screens), [자산](ARCHITECTURE.md#components) | [theme.test.mjs](../../../../assets/frontend/tests/theme.test.mjs) OS 밝기 1회 해석·고정, [display-time.test.mjs](../../../../assets/frontend/tests/display-time.test.mjs) 날짜·경과 시간 | 구현 확인 / [리팩토링 기록](../../jobs/REFACTORING.md), 접근성 인증 미확인 |

<a id="verification"></a>
## 검증 수준과 남은 확인

| 근거 | 기록된 결과·범위 | 이번 분석에서의 해석 |
| --- | --- | --- |
| [REFACTORING.md](../../jobs/REFACTORING.md) | 2026-09-29 개발 소스: .NET 89개 통과·24개 조건부 건너뜀, 프런트엔드 31개 통과, 실제 Windows Debug·F12·같은 프로필 재시작·로컬 파일·테마 흐름 | 기존 검증 기록. 이번 실행 결과가 아니며 네이티브 OS 전체 인증이 아님 |
| [0.3.1 보고서](../../jobs/VALIDATION-0.3.1.md) | 격리 프로토콜 서버, 인증·드롭·워크플로·자동화 및 당시 Linux 컨테이너 등 | 기존 버전의 실행 근거. 현재 커밋 전체의 프로토콜 재검증으로 취급하지 않음 |
| [0.3.3 보고서](../../jobs/VALIDATION-0.3.3.md) | Windows 격리 설치 환경의 자동 준비·다음 시작 적용 | 기존 설치 검증. macOS/Linux 실제 무인 업데이트로 확대하지 않음 |
| [버전별 패키징 보고서](../../jobs/VALIDATION-versioned-packaging.md) | 게시·업데이트 계약 19개와 실제 패키지 검사 3개, Delta 복원·손상 시 Full 전환 | 격리 서버와 TestVelopackLocator 검사. 설치본 UI·OS 등록·제거 검사를 대신하지 않음 |
| [PARITY.md](../PARITY.md) | 버전 0.3.6 표기와 여러 과거 버전의 기능별 검증 | 지원 한계 탐색에 사용. 테마 행의 OS 변경 추적 설명은 현재 `theme.js`의 고정 테마와 다름 |
| [이번 문서 검사](../../jobs/VALIDATION-reverse-engineering-20260929.md) | 새 문서·근거 링크·요구사항 ID·도식, 문서 Node 프로젝트 구문 검사 | 이번 실행 확인. 기능 동작 검증과 구분 |

`IntegrationFact/IntegrationTheory`는 [ProtocolTests](../../../../tests/Portway.Tests/ProtocolTests.cs)에서 `PORTWAY_INTEGRATION=1` 조건을 검사합니다. [PackageTests](../../../../tests/Portway.Tests/PackageTests.cs)의 실제 패키지 검사는 별도 환경 변수와 Windows 조건이 필요합니다. 테스트를 읽었다는 이유로 이 조건부 검사를 통과 처리하지 않습니다.

현재 판단할 수 없는 범위는 Pageant·CA 서명 SSH 인증서·상호 TLS의 실제 조합, 외부 OTP 공급자별 MFA, AWS IAM/STS·Requester Pays·대용량 복사, macOS·ARM64 실기기와 전체 OS 호환성입니다. WinSCP 전체 기능·스크립트·바이너리 호환도 달성한 것으로 표시하지 않습니다. 원래 사업 요구·성능 SLA·실사용자 통계는 확인할 자료가 없습니다.
