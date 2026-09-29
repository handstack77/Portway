# Portway 0.3.3 자동 업데이트 검증

검증일: 2026-09-29. Windows x64 환경에서 시작 시 자동 확인·다운로드와 다음 시작 시 무인 적용을 추가하고 검증했습니다. 앱은 기존 수동 확인·다운로드·즉시 재시작도 지원합니다.

## 변경 동작

- `AutomaticUpdateWorker`가 호스트 시작 이후 한 번 백그라운드 작업을 수행합니다. 저장된 피드 URL이 없으면 생략하고 소스 실행에서는 패키지 업데이트를 시도하지 않습니다.
- 자동·수동 확인/다운로드/적용을 직렬화하여 중복 다운로드와 준비 상태 유실을 방지합니다. `GET /api/updates`로 상태와 진행률을 읽습니다.
- 실행 중 자동 작업은 다운로드까지만 수행합니다. 다음 실행의 `VelopackApp.SetAutoApplyOnStartup(true)`가 UI와 파일 작업 시작 전에 준비된 패키지를 적용합니다.
- 네트워크 실패는 앱 실행을 중단하지 않습니다. 다음 실행 또는 수동 확인에서 재시도합니다. 종료 시 진행 중 다운로드에 취소 토큰을 전달합니다.
- 설정 화면에 자동 동작 안내, 다운로드 진행률, 다음 실행 시 적용 안내를 추가했습니다. 닫힌 설정 창은 상태 폴링을 중단합니다. 금고 작업으로 설정을 다시 그릴 때 이전 close 이벤트가 새 버튼·폴링을 해제하지 않도록 수명 주기도 수정했습니다.

## 실행 결과

| 항목 | 결과와 근거 |
| --- | --- |
| 업데이트 회귀 | **11 통과**, 최종 기본 .NET 58개에 포함. [최종 빌드·테스트 로그](../../../artifacts/build-0.3.3.log) |
| 기본 .NET 검사 | `build.ps1` 안의 `dotnet test Portway.slnx -c Release`: **58 통과, 23 조건부 건너뜀**, 실패 0. 위 11개 회귀를 포함. [로그](../../../artifacts/build-0.3.3.log) |
| 프런트엔드 검사 | `npm test --prefix assets/frontend`: **4 통과**. 기존 드롭 수집 회귀 |
| Windows 패키징 | 실제 `vpk pack` 0.3.3 성공. full, 0.3.2→0.3.3 delta, Setup, Portable, 게시 ZIP 생성 |
| 게시 ZIP 검증 | **1 통과**. 서버 게시·피드·다운로드·해시 검사. [TRX](../../../tests/Portway.Tests/TestResults/package-0.3.3.trx) |
| 무인 업데이트 | 격리 설치 0.3.3 → 자동 확인·다운로드 → 기존 프로세스/버전 유지 → 앱 종료·피드 서버 종료 → 다음 시작에서 0.3.4 적용 → 프로필 보존 → 추가 오프라인 시작 → 제거 **통과** |
| 수동 업데이트 회귀 | 격리 설치 0.3.3 → check/download/apply API → 새 Photino 네이티브 창 → API 0.3.4 확인 → 제거 **통과** |
| 설정 UI | 실제 API로 비설치 실행 상태 확인. 다운로드 50%와 ready 응답을 주입해 버튼 상태·폴링 종료 검증. 라이트/다크 스크린샷 확인. 금고 생성·잠금으로 설정을 다시 그린 뒤 수동 버튼·상태 폴링이 유지되는 회귀도 통과 |
| 브라우저 콘솔 | 오류·경고 0. 비밀번호 입력 폼 관련 verbose 안내 3개 |

자동 검증은 **업데이트 동작 POST API를 호출하지 않았습니다.** GET 상태 조회만 사용했고, 로컬 준비 완료 후 피드 서버를 종료하여 다음 시작의 적용이 온라인 확인에 의존하지 않는지도 검사했습니다. 종료는 준비가 끝난 headless 프로세스 종료 방식으로 검사했습니다. 수동 회귀에서는 실제 Photino 창이 재시작되고 창 닫기로 정상 종료되는지도 확인했습니다.

0.3.4는 같은 변경 코드로 별도 게시한 **격리 QA 대상 버전**입니다. 공개 릴리스로 게시하지 않았습니다. 제공하는 제품 패키지는 0.3.3입니다.

증거:

- [자동 업데이트 결과](../../../artifacts/qa/PortwayAutoQa120e36e2034f44859f03f0837d86b0ea/result.txt), [자동 준비 상태](../../../artifacts/qa/PortwayAutoQa120e36e2034f44859f03f0837d86b0ea/prepared.json)
- [수동 업데이트 결과](../../../artifacts/qa/PortwayQae86be62d03e747c79ffaf8c8c0b45cc7/result.txt)
- 재현 스크립트: [자동 경로](../../../scripts/test-automatic-update.ps1), [수동 경로](../../../scripts/test-installed-update.ps1)

## 화면

아래 이미지는 준비 완료 **UI 응답을 주입한 화면 검증**입니다. 실제 패키지 준비와 적용 결과는 위 설치 테스트에서 별도로 확인했습니다.

![라이트 테마 업데이트 준비 완료](../../../output/playwright/update-ready-light-0.3.3.png)

![다크 테마 업데이트 준비 완료](../../../output/playwright/update-ready-dark-0.3.3.png)

## 산출물과 범위

- [Windows x64 설치 파일](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Setup.exe)
- [Windows x64 포터블 ZIP](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Portable.zip)
- [배포 서버 게시용 ZIP](../../../dist/Portway-0.3.3-win-x64-stable.zip)
- [SHA-256](../../../dist/SHA256SUMS-0.3.3.txt), [기계 판독 검증 기록](verification-0.3.3.json)

Windows 패키지는 미서명입니다. 이번에 실제로 설치·무인 업데이트를 검증한 OS는 Windows x64입니다. macOS/Linux용 코드는 같은 서비스와 Velopack 시작 경로를 사용하지만 실제 해당 OS의 무인 업데이트는 검증하지 않았습니다. Linux 제공 AppImage는 이전 0.3.1이며 새 기능을 포함하지 않습니다. 공개 도메인이나 원격 CI에는 게시하지 않았습니다.

조건부로 건너뛴 23개에는 실제 프로토콜·공식 WinSCP·패키지 테스트가 포함됩니다. 패키지는 위 별도 실행으로 검사했으며 프로토콜 전체 검증은 [0.3.1 보고서](VALIDATION-0.3.1.md)에 있습니다. 새 구현을 이유로 기존 미구현·OS 미검증 항목을 완료로 변경하지 않았습니다. QA가 만든 설치 앱·서버·브라우저 프로세스는 종료했고 증거 파일은 보존했습니다.
