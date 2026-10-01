# Portway 0.3.0 검증 결과

검증일: 2026-09-28. Windows x64, Docker의 Ubuntu 24.04 x64, 실제 OpenSSH/FTP/FTPS/WebDAV/HTTPS/S3 호환 테스트 서버를 사용했습니다. 외부 서비스나 사용자 서버의 데이터는 사용하지 않았습니다.

## 결과

| 검증 | 결과 / 근거 |
|---|---|
| Windows .NET 전체 통합 테스트 | **78 통과, 0 실패, 0 건너뜀**. `tests/Portway.Tests/TestResults/windows-0.3.0.trx` |
| Linux .NET 전체 통합 테스트 | **77 통과, 0 실패, 1 건너뜀**. Windows 전용 공식 WinSCP 실행 파일 테스트만 제외. `artifacts/qa/linux-results-030/linux-0.3.0.trx` |
| 프런트엔드 파일 수집 테스트 | **4 통과**. 이벤트 내 참조 확보, 100개 초과 배치, 빈/중첩 폴더, 0바이트, 취소/읽기 실패 |
| 새 외부 드롭 서버 테스트 | 위 .NET 결과에 포함된 **13개**. 경로 이탈·장치 이름·충돌·크기·청크 순서 거부, 중단된 청크 롤백, 불완전 커밋 거부, 실제 SFTP 전송·중복 커밋·사본 수명 |
| 실제 0.3.0 Windows vpk 게시 | ZIP 해시 검증·게시·다운로드 테스트 1 통과. `package-0.3.0.trx`. 전체 Windows 실행 시에는 이전 0.2.1 ZIP을 썼으며 새 ZIP은 별도 재검증 |
| 실제 0.3.0 Linux vpk 게시 | 전체 Linux 테스트에서 새 AppImage 포함 게시 ZIP 사용 |
| 패키지 Desktop API | 토큰·Origin 검사, 실제 큐 업로드, 편집 충돌·권한 보존 통과. `scripts/test-desktop.ps1` |
| Windows 설치·업데이트·제거 | 격리된 QA 패키지 0.1.0 설치 → 피드 확인/다운로드/0.3.0 적용 → 실제 Photino 창 재시작 → API 버전 확인 → 제거 통과 |
| Linux AppImage | 0.3.0 AppImage를 Xvfb에서 실제 시작하고 종료. GUI 프로세스 12초 유지 후 테스트 제한시간으로 정상 종료 |
| macOS ARM64 | 자체 포함 게시 빌드 성공. 실제 Mac 실행·Finder 제스처·설치·업데이트 검증은 미수행 |
| 배포 서버 | `portway-distribution:0.3.0` Docker 이미지 빌드 성공, 기존 서버 테스트 통과, 실제 대시보드 화면 검토 |

설치 검증 증거: `artifacts/qa/PortwayQa4594cebdecae45fca9bf3ceaa8a9564e/result.txt`. Linux의 Xvfb 실행에서는 GPU/DRI3 가속 경고가 표시됐으나 앱은 시작됐습니다. Windows 설치 파일은 코드 서명되지 않았습니다.

## 브라우저 드롭과 UI

Playwright CLI의 Chromium에서 CDP `Input.dispatchDragEvent`에 **실제 로컬 디렉토리·파일 경로**를 넘겼습니다. 입력은 `artifacts/qa/drop-inputs-022`의 전용 테스트 파일입니다.

- 108개 파일과 4개 폴더: 105개 파일이 들어 있는 폴더, 한글 폴더/파일, 빈 폴더, 0바이트 파일, 9 MiB+17바이트 바이너리.
- 8 MiB보다 큰 파일의 청크 수신 → 큐 등록 → 실제 SFTP 전송 완료.
- 전송 전후 바이너리 SHA-256: `6f43279c24d699e11de05fa68ba6743033fff4ba2eaedc48e3fdccf13b0b27a4`.
- 서버의 하위 항목 105개, 빈 폴더, 0바이트 파일을 직접 확인했습니다. 최종 0.3.0 소스에서도 별도 대상 폴더로 동일 검증을 수행했습니다.
- PUT 요청을 보류한 상태에서 **취소** → 대화상자 닫힘 → 스테이징 DELETE 200 및 임시 파일 정리 확인.
- 라이트/다크 전환, 시스템 모드의 OS 색상 변경 추적, 새로고침 후 저장된 다크 테마 복원을 확인했습니다.
- 웹 폰트 실제 로드, 로컬 Master CSS 생성, 앱 콘솔 오류·경고 0개를 확인했습니다. Chromium의 복합 인증 폼 안내 메시지 1개는 verbose 수준입니다.
- 980×680 화면에서 가로 넘침이 없고 양쪽 패널과 주요 버튼이 보이는 것을 확인했습니다.
- 배포 센터의 실제 이전 릴리스 데이터로 다운로드 카드·라이트/다크 테마를 확인했습니다. 스크린샷의 릴리스 번호 0.2.1은 대시보드 검증용 기존 피드 데이터입니다.
- 게시된 Windows 패키지의 모든 `wwwroot` 파일을 현재 소스/생성 자산과 해시 비교해 일치를 확인했습니다.

![라이트 테마](../docs/images/workspace-light.png)

![다크 테마](../docs/images/workspace-dark.png)

추가 화면: `output/playwright/drop-hover-0.3.0.png`, `drop-confirm-dark.png`, `design-small.png`, `server-light-0.3.0.png`, `server-dark-0.3.0.png`.

## 산출물

- Windows: `dist/releases/win-x64-stable/Portway-win-x64-stable-Setup.exe`, 포터블 ZIP, 0.3.0 full 및 0.2.1 → 0.3.0 delta nupkg.
- Linux: `dist/releases/linux-x64-0.3.0-stable/Portway-linux-x64-stable.AppImage`와 full nupkg/피드.
- 배포 ZIP: `dist/Portway-0.3.0-{win,linux}-x64-stable.zip`.
- 체크섬: `dist/SHA256SUMS-0.3.0.txt` 및 `doc/Portway.Document/jobs/verification-0.3.0.json`.

## 남은 환경 검증

실제 Explorer/Finder/리눅스 파일 관리자와 **Photino 네이티브 창 사이의 OS 드래그 제스처**는 이번 자동화에서 직접 조작하지 않았습니다. Chromium 파일 경로 드롭 검증과 macOS 교차 빌드가 해당 실기기 검증을 대체하지 않습니다. 드롭 수신 코드는 표준 WebView 파일 API를 사용하지만 배포 대상 WebKit/WKWebView 버전에서 별도 인수 테스트가 필요합니다. 앱 → OS 파일 관리자 드래그아웃은 미구현입니다.

공개 도메인 배포, macOS 코드 서명·공증·실제 업데이트, ARM64 실기기 검증은 수행하지 않았습니다. 기존 WinSCP 고급 기능의 남은 차이는 [PARITY.md](../docs/PARITY.md)에 유지했습니다.

## 재현

```powershell
npm ci --prefix assets/frontend
npm run build --prefix assets/frontend
npm test --prefix assets/frontend
docker compose -f tests/infrastructure/compose.yaml up -d
$env:PORTWAY_INTEGRATION = '1'
$env:PORTWAY_WINSCP_REFERENCE = 'C:/path/to/official-winscp-reference'
$env:PORTWAY_PACKAGE_TEST = (Resolve-Path dist/Portway-0.3.0-win-x64-stable.zip).Path
dotnet test tests/Portway.Tests -c Release --logger 'trx;LogFileName=integration.trx'
./scripts/test-desktop.ps1 -Executable dist/publish/win-x64/0.3.0/Portway.exe
./scripts/test-installed-update.ps1
docker compose -f tests/infrastructure/compose.yaml down
```

참조 WinSCP가 없으면 해당 환경 변수를 지정하지 않고 Windows 전용 교차 테스트를 건너뜁니다. 테스트 기록에 건너뜀을 포함해 보고합니다. 릴리스 빌드는 기존 버전별 산출물을 덮어쓰지 않으므로 다시 빌드할 때 새 버전을 지정합니다.
