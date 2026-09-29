# Portway API 목록

현재 개발 소스의 HTTP 경로를 기능별 등록 파일에서 정리했습니다. 이 문서는 OpenAPI 생성물이 아닙니다. 경로를 추가·변경하면 등록 파일과 이 목록을 함께 갱신하고 호출 UI 및 계약 테스트를 확인하세요. 구조는 [ARCHITECTURE.md](ARCHITECTURE.md), 분석 순서는 [SOURCE-MAP.md](SOURCE-MAP.md)를 참고하세요.

## Desktop 호출 조건

- 주소는 앱이 만든 `http://127.0.0.1:<port>`이며 아래 경로 앞에 `/api`를 붙입니다.
- 모든 `/api` 요청에는 현재 실행의 `Authorization: Bearer <token>`이 필요합니다. 토큰은 문서·로그·이슈에 기록하지 않습니다.
- Origin 헤더가 있다면 로컬 앱 주소와 일치해야 합니다. Host를 `localhost` 또는 외부 호스트로 바꾸면 거부합니다.
- JSON 요청은 `Content-Type: application/json`을 사용합니다. 드롭 청크는 요청 본문의 바이너리 스트림과 Content-Length를 사용합니다.
- ID는 서버가 반환한 값을 그대로 사용합니다. UI 연결 ID·전송 작업 ID·동기화 계획 ID·터미널 ID를 혼용하지 않습니다.

| 결과 | 의미 |
| --- | --- |
| 2xx | 각 작업의 성공 응답; 작업 ID 응답이 실제 전송 완료를 의미하지는 않음 |
| 400 | 잘못된 입력·지원하지 않는 작업 등 일반 작업 오류 |
| 401 | 없거나 잘못된 Bearer 토큰 |
| 403 | 허용되지 않은 Host 또는 Origin |
| 404 | 없는 파일·종료된 연결·없는 작업 등 |
| 409 | 편집 충돌, 금고 잠금 등 InvalidOperationException 작업 상태 오류 |

일반 작업 오류는 `{ "title": "요청을 완료하지 못했습니다", "detail": "오류 설명", "status": 409 }` 형태입니다. 인증·Host·Origin 거부에는 같은 JSON 본문을 가정하지 않습니다. HTTP 파이프라인·요청 바인딩 오류는 프레임워크 응답이 될 수 있습니다.

## 요청 모델

JSON 필드는 camelCase입니다. 정확한 필드·기본값은 [Models.cs](../../../src/Portway.Core/Models.cs), [TransferOptions.cs](../../../src/Portway.Core/TransferOptions.cs), [DesktopApi.Contracts.cs](../../../src/Portway.Desktop/Api/DesktopApi.Contracts.cs)를 기준으로 합니다.

| 모델 | 주요 필드와 역할 |
| --- | --- |
| Site | `id`, `name`, `protocol`, `host`, `port`, `username`, 연결별 인증·지문·경로·프로토콜 설정 |
| FileRequest | `path`, `destination`, `content`, `etag`, `encoding`, `bom`; 쓰기는 읽기 응답의 ETag 사용 |
| TransferRequest | `sessionId`, `direction`, `paths`, `destination`, `conflict`, `speedLimit`, `options`, `priority`, `scheduledAt` |
| TransferOptions | 모드·필터·검증·메타데이터·이어하기·원본 제거 등 전송 정책 |
| ActionRequest | `action`, 선택적 `sessionId`; 작업별 지원 action과 복구 연결을 확인 |
| SyncRequest | `sessionId`, `localPath`, `remotePath`, `direction`, `deleteExtraneous`, `options`, `comparison` |
| SyncApplyRequest | 선택적 `selected`, `resolutions`; 변경 선택·충돌 방향 |
| ImportRequest | `content`; Portway JSON 또는 지원되는 WinSCP INI 문자열 |
| PasswordRequest | `password`; 금고 잠금 해제 |
| PermissionRequest | `path`, `octal`, `recursive`, `owner`, `group` |
| TerminalRequest / TerminalInput | `sessionId`, `columns`, `rows` / `data`, 선택적 크기 |
| AuthenticationReply | `answers`; 추가 인증 질문 응답 또는 취소 |
| DropManifest / WatchRequest | 관련 모델은 [DropStore.cs](../../../src/Portway.Desktop/DropStore.cs), [LiveSyncService.cs](../../../src/Portway.Desktop/LiveSyncService.cs)에서 확인 |

로컬 복사는 `direction: "local"`, `sessionId: ""`, 절대 원본 경로 배열과 절대 대상 폴더를 사용합니다. `conflict`는 `skip`, `replace`, `newer`, `rename`이며 로컬 전송은 바이너리 모드로 검증합니다. 전송 상태는 `GET /api/transfers`로 별도 확인합니다.

## Desktop 경로

### Connections

등록 위치: [DesktopApi.Connections.cs](../../../src/Portway.Desktop/Api/DesktopApi.Connections.cs).

| 메서드 | /api 이후 경로 |
| --- | --- |
| GET | `/authentication` |
| POST | `/authentication/{id}` |
| POST | `/fingerprint` |
| POST | `/certificate` |
| POST | `/sessions` |
| DELETE | `/sessions/{id}` |

### Files

등록 위치: [DesktopApi.Files.cs](../../../src/Portway.Desktop/Api/DesktopApi.Files.cs).

| 메서드 | /api 이후 경로 |
| --- | --- |
| GET | `/local` |
| POST | `/local/mkdir` |
| POST | `/local/rename` |
| POST | `/local/delete` |
| POST | `/local/read` |
| POST | `/local/write` |
| GET | `/remote/{id}` |
| POST | `/remote/{id}/mkdir` |
| POST | `/remote/{id}/rename` |
| POST | `/remote/{id}/delete` |
| POST | `/remote/{id}/chmod` |
| POST | `/remote/{id}/copy` |
| POST | `/remote/{id}/link` |
| POST | `/remote/{id}/search` |
| POST | `/remote/{id}/properties` |
| POST | `/remote/{id}/checksum` |
| POST | `/local/checksum` |
| POST | `/remote/{id}/command` |
| POST | `/remote/{id}/read` |
| POST | `/remote/{id}/write` |

### Profiles

등록 위치: [DesktopApi.Profiles.cs](../../../src/Portway.Desktop/Api/DesktopApi.Profiles.cs).

| 메서드 | /api 이후 경로 |
| --- | --- |
| GET | `/info` |
| GET | `/sites` |
| POST | `/sites/import` |
| POST | `/sites/export` |
| POST | `/sites` |
| DELETE | `/sites/{id}` |
| GET | `/vault` |
| POST | `/vault/unlock` |
| POST | `/vault/lock` |
| GET | `/preferences` |
| POST | `/commands/run` |
| PUT | `/preferences` |

### Synchronization

등록 위치: [DesktopApi.Synchronization.cs](../../../src/Portway.Desktop/Api/DesktopApi.Synchronization.cs).

| 메서드 | /api 이후 경로 |
| --- | --- |
| POST | `/sync/preview` |
| POST | `/sync/{id}/apply` |
| POST | `/sync/{id}/cancel` |
| GET | `/sync/{id}` |
| GET | `/watches` |
| POST | `/watches` |
| POST | `/watches/{id}` |

### Tools

등록 위치: [DesktopApi.Tools.cs](../../../src/Portway.Desktop/Api/DesktopApi.Tools.cs).

| 메서드 | /api 이후 경로 |
| --- | --- |
| GET | `/external-edits` |
| POST | `/external-edits/{sessionId}` |
| POST | `/external-edits/{id}/control` |
| GET | `/trash` |
| POST | `/trash` |
| POST | `/trash/{id}/restore` |
| POST | `/terminals` |
| GET | `/terminals/{id}` |
| POST | `/terminals/{id}` |
| DELETE | `/terminals/{id}` |

### Transfers

등록 위치: [DesktopApi.Transfers.cs](../../../src/Portway.Desktop/Api/DesktopApi.Transfers.cs).

| 메서드 | /api 이후 경로 |
| --- | --- |
| GET | `/transfers` |
| POST | `/drops` |
| PUT | `/drops/{id}/files/{index:int}` |
| POST | `/drops/{id}/commit` |
| DELETE | `/drops/{id}` |
| POST | `/transfers` |
| POST | `/transfers/{id}` |
| DELETE | `/transfers` |

### Updates

등록 위치: [DesktopApi.Updates.cs](../../../src/Portway.Desktop/Api/DesktopApi.Updates.cs).

| 메서드 | /api 이후 경로 |
| --- | --- |
| GET | `/updates` |
| POST | `/updates/check` |
| POST | `/updates/download` |
| POST | `/updates/apply` |

## 배포 서버 경로

배포 서버의 주소와 인증 키는 Desktop 실행 토큰과 별도입니다. 게시 요청은 `Distribution:ApiKey`의 Bearer 키와 `application/zip` 본문을 사용하며 채널 형식은 `win-x64-stable` 같은 OS·아키텍처·track 조합입니다.

등록 위치: [ServerApi.cs](../../../src/Portway.Server/ServerApi.cs).

| 메서드 | 경로 | 조건 |
| --- | --- | --- |
| GET | `/healthz` | 서버 상태 |
| GET | `/api/releases` | 공개 릴리스 목록 |
| GET | `/releases/{channel}/{file}` | 검증된 파일 이름·채널, HTTP Range 다운로드 |
| POST | `/api/releases/{channel}` | 게시 키·ZIP 검증, publish 속도 제한 |

게시 키가 준비되지 않으면 503, 잘못된 키는 401, 다른 Content-Type은 415, 업로드 속도 제한은 429입니다. ZIP·채널·기존 릴리스 검증 오류는 기존 서버 오류 응답의 `detail`을 확인합니다. 운영 배포와 서명은 [DEPLOYMENT.md](DEPLOYMENT.md)를 참고하세요.

게시 ZIP에는 해당 버전의 자산만 담을 수 있습니다. 서버는 기존 Full/Delta 피드와 합쳐 SemVer 내림차순으로 제공하며 응답의 `assets`는 합친 목록의 개수입니다. 새 Delta 자산의 `BaseVersion`은 먼저 게시한 최신 Full 버전이어야 합니다. 기존 패키지 정보 변경과 잘못된 변경분 연결은 400으로 거부합니다. 이전 형식의 누적 ZIP도 지원합니다.

## 관련 계약 검증

- [DesktopApiTests.cs](../../../tests/Portway.Tests/DesktopApiTests.cs): Bearer·Host·Origin·CSP·no-store, 분리된 기능의 DI·프로필 저장, 로컬 편집 충돌·종료된 세션 응답.
- [ReleaseTests.cs](../../../tests/Portway.Tests/ReleaseTests.cs): 게시 인증·ZIP 오류·채널·다운로드 Range.
- [SiteArchiveTests.cs](../../../tests/Portway.Tests/SiteArchiveTests.cs): 내보내기 비밀 제거와 가져오기 원자성.

실제 원격 작업·설치 업데이트·운영 배포는 관련 통합·패키지 검증을 별도로 수행합니다.
