# 검증 결과

2026-09-28, Portway **0.2.1**. Windows x64 / .NET SDK 10.0.401 / Docker Desktop Ubuntu 24.04 x64. 0.2.1은 0.2.0의 전송 옵션 중복 표시를 수정하고 충돌 정책 선택을 추가한 패치입니다.

## 실행한 검사

| 검사 | 결과 / 증거 |
|---|---|
| Windows 전체 자동 검사 | 65개 통과, 실패/건너뜀 0개. `tests/Portway.Tests/TestResults/advanced-0.2.1.trx` |
| Linux 실제 프로토콜 및 패키지 검사 | 64개 통과, Windows 전용 WinSCP 실행 검사만 1개 제외. `artifacts/qa/linux-results/linux-0.2.1.trx` |
| 실제 서버 | OpenSSH, FTP/FTPS pyftpdlib, HTTP/HTTPS WsgiDAV, S3 호환 moto, HTTP/SOCKS 프록시, ssh-agent |
| 접속 검증 | 잘못된 SSH 키·TLS 인증서 거부, 정확한 핀 성공, HTTP/SOCKS4/5, SSH 점프, MFA, OpenSSH/PPK 및 실제 Agent 서명 |
| 파일 작업 | 왕복 바이트 비교, 한글/공백/따옴표/$ 이름, 권한·시각, 복사·검색·소유권 API, 정상/깨진 링크, 휴지통 복원 |
| 영속 전송 큐 | 전송 중지 → 서비스/프로필 재생성 → 잠긴 Vault에서 거부 → 잠금 해제 → 동일 부분 파일에서 완료 |
| 고급 전송 | 마스크, 텍스트 줄바꿈, 대소문자, 빈 폴더 제외, SHA256, 권한 640, 수정 시각 보존 |
| 동기화 | 같은 크기/시각으로 내용만 바뀐 미리보기 거부, 부분 선택, 충돌 방향, 지속 감시·정지·복원 |
| 편집 | UTF8/16/32 BOM·cp949 왕복, 표현 불가능한 문자 거부, 외부 편집 업로드, 서버 동시 변경 충돌과 복구 사본 |
| 대화형 SSH | PTY 입력, 창 크기 변경, Ctrl+C, 실제 브라우저 xterm 화면 |
| 파일 암호화 | NIST AES-256 CTR 벡터, 기존 평문 정책, 암호화 파일/폴더. 공식 **WinSCP 6.5.7**이 Portway 파일을 읽고 반대 방향도 같은 바이트를 반환 |
| 자동화 | 실제 SFTP 스크립트 전송/권한/해시/복사/동기화, 인용 경로, 미지원 스위치 거부, 오류 종료 코드, 셸 매크로 인젝션 거부 |
| 배포 서버 | 관리자 인증, ZIP 경로 순회·잘못된 해시 거부, 실제 Windows/Linux vpk ZIP 게시, 최신 Full 패키지 SHA256, Range 206 |
| Windows 설치·업데이트 | 별도 `PortwayQa<GUID>` ID와 작업 폴더 사용. Setup 0.1.0 설치 → 앱의 피드 확인/다운로드/적용 → 새 Photino 창 → 새 API 버전 확인 → 제거. `scripts/test-installed-update.ps1` |
| Windows 최종 실행 파일 | 토큰 없는 API 401, 외부 Origin 403, 큐 업로드, 편집, 권한 보존, 오래된 etag 거부. `scripts/test-desktop.ps1` |
| Linux 네이티브 | Docker/Xvfb에서 Photino 바이너리 및 생성한 AppImage 실제 시작/종료 |
| macOS | osx-arm64 자체 포함 교차 빌드. 실기기 검증 아님 |
| 브라우저 | 고급 옵션 업로드(소문자·640·SHA256), 휴지통 이동/복원, 동기화 6개 중 1개 적용, 지속 동기화 시작/일시정지, 터미널 입출력. 콘솔 오류 0 |
| 서버 컨테이너 | 비루트 ASP.NET Core 배포 이미지 빌드 |
| 의존성 점검 | NuGet에서 직접/전이 패키지에 알려진 취약점 보고 없음(검사 시점 기준) |

검증 이미지: [파일 화면](../docs/images/workspace.png), [터미널](../docs/images/terminal.png). 원본 UI 스냅샷과 화면은 `output/playwright/`에 보관합니다. 설치 검사 로그와 성공 기록은 `artifacts/qa/PortwayQa*/`에 있습니다.

## 재현

```powershell
./scripts/test-integration.ps1 -KeepFixtures
# 공식 WinSCP NuGet 패키지를 별도로 내려받아 압축 해제했을 때:
$env:PORTWAY_WINSCP_REFERENCE = 'C:\path\to\extracted-winscp-package'
$env:PORTWAY_PACKAGE_TEST = (Resolve-Path dist/Portway-0.2.1-win-x64-stable.zip).Path
$env:PORTWAY_INTEGRATION = '1'
dotnet test Portway.slnx -c Release
./scripts/test-desktop.ps1 -Executable dist/publish/win-x64/0.2.1/Portway.exe
./scripts/test-installed-update.ps1
```

Linux 통합 검사는 Docker의 host networking에서 같은 loopback fixture에 접속했습니다. Linux 빌드 자체의 기본 검사는 선택 테스트를 건너뛰므로, **별도로 `PORTWAY_INTEGRATION=1`을 설정한 실행 결과**가 위 64개 검사입니다. 테스트 서버는 반복적인 호스트 키 거부 검사로 인한 차단을 막기 위해 `PerSourcePenalties=no`를 사용합니다. 이 설정은 loopback 테스트 컨테이너 전용입니다.

## 검증 경계

- WinSCP **모든 고급 기능 동등성은 미완료**입니다. [기능별 상태](../docs/PARITY.md)에 GSS/Kerberos, COM/전체 스크립트 호환, OS 셸 통합 등 미구현 항목을 명시했습니다.
- macOS 설치/실행/업데이트, macOS 및 Windows ARM64 실기기, 실제 코드 서명·Apple 공증은 확인하지 못했습니다. Windows 설치 파일은 서명되지 않았습니다.
- 공개 도메인 운영 배포, 원격 GitHub Actions 실행, 실제 AWS IAM/STS/Requester Pays 과금, 기업 Kerberos/OTP/CA와의 호환은 검증 범위 밖입니다.
- Linux GUI 검사는 가상 디스플레이 검사이며 실제 Linux 배포판·데스크톱 전체 조합을 의미하지 않습니다.
- 동기화 20,000항목·깊이 64, 내부 편집 16MiB 제한이 있습니다. 동시 변경에 대한 해시 검사는 폴더 트랜잭션이나 원격 원자적 비교 교체를 대신하지 않습니다.
- WinSCP 호환 암호화 형식은 인증 태그 없는 AES-CTR입니다. 콘텐츠 변조 검출을 보장하지 않으며 키를 잃으면 복원할 수 없습니다. 형식 설명: [WinSCP 공식 문서](https://winscp.net/eng/docs/file_encryption).

0.1.0의 이전 기록은 [별도 문서](VALIDATION-0.1.0.md)에 보존했습니다. 배포에는 버전이 일치하는 `dist/Portway-0.2.1-*.zip`을 사용하세요.
