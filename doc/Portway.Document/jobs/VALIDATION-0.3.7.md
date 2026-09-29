# 0.3.7 — 라이트/다크 전환과 Visual Studio 2026 톤

검증일: 2026-09-29 (KST), Windows x64.

## 적용 내용

- 앱 상단과 배포 센터에서 시스템 모드를 제거하고 **라이트 ↔ 다크**로 전환합니다. 설정 창도 두 선택지만 제공합니다. 버튼 아이콘·접근성 이름에 현재 테마와 다음 동작을 표시하고 저장 중 중복 클릭을 막습니다.
- 최초 실행 또는 이전 `system` 값은 시작 시 OS 밝기를 한 번 해석해 `light`/`dark`로 저장합니다. 이후 OS 변경을 구독하지 않습니다. Desktop 프로필 변환은 기존 북마크·전송·업데이트 설정 등을 보존합니다. 기존 API의 `system` 수용은 이전 버전 호환성을 위해 유지합니다.
- 다크 색상은 [Microsoft의 Visual Studio 2026 테마 토큰](https://learn.microsoft.com/en-us/visualstudio/extensibility/ux-guidelines/theme-color-token-reference?view=visualstudio)을 참고했습니다. 캔버스 `#1c1c1c`, 패널 `#202020`, 헤더 `#282828`, 팝업 `#2c2c2c`, 경계 `#454545`로 중성 회색 계층을 구성합니다. 입력·Monaco·터미널 배경은 `#1e1e1e`입니다. Tabler 기본 파란색 버튼과 Noto Sans KR은 유지합니다.
- 공유 토큰을 Desktop·배포 센터에 적용하고 Monaco·터미널도 같은 토큰을 읽습니다. 사용자·개발자 가이드와 AGENTS.md를 갱신했습니다.

## 검증

| 항목 | 결과 |
| --- | --- |
| 프런트엔드 | npm ci/build/test, 21개 통과. 테마 초기 선택·캐시·변환·OS 변경 무시·저장소 실패 7개 포함 |
| .NET 기본 | 58개 통과, 23개 조건부 테스트 건너뜀 |
| 기존 프로필 변환 | `system` → `dark` 저장, 동시 전송 수 3 유지, OS를 라이트로 바꾼 뒤 새로고침해도 다크 유지 |
| 테마 선택 | 상단 토글과 설정 저장 모두 실제 API에 저장. 설정 목록은 라이트·다크만 존재 |
| 화면 | Chromium 1440×940/980×680, 탐색기·선택·작업 메뉴·연결 창·설정·Monaco·배포 센터 확인 |
| 편집 | 다크 Monaco 계산 배경 RGB(30,30,30), 한글 입력 → Ctrl+S → 디스크 문자열 일치 |
| WebKit | Windows용 WebKit에서 두 테마 전환·다크 저장/복원·Monaco 배경 확인 |
| 브라우저 콘솔 | QA 동작 중 Chromium/Desktop·Server 및 WebKit 오류/경고 0 |
| 게시본 | 0.3.7 실행·테마·1440×940/980×680 표시 확인, wwwroot 280개 SHA256 일치 |
| Windows 패키지 | Setup·Portable·full·0.3.6→0.3.7 delta 생성. 격리 서버에 실제 ZIP 게시/다운로드 SHA256 테스트 1개 통과 |

## 증거

- [0.3.7 다크 워크스페이스](../../../output/playwright/vs2026-0.3.7-dark.png), [980×680](../../../output/playwright/vs2026-0.3.7-dark-980.png), [라이트](../../../output/playwright/vs2026-0.3.7-light-980.png)
- [Monaco](../../../output/playwright/vs2026-dark-editor.png), [설정](../../../output/playwright/vs2026-dark-settings.png), [작업 메뉴](../../../output/playwright/vs2026-dark-menu.png), [연결 창](../../../output/playwright/vs2026-dark-connection.png)
- [WebKit](../../../output/playwright/vs2026-dark-webkit.png), [배포 센터](../../../output/playwright/vs2026-dark-server.png)
- [빌드 로그](../../../artifacts/build-0.3.7.log), [패키지 TRX](../../../tests/Portway.Tests/TestResults/package-0.3.7.trx), [기계 판독 요약](verification-0.3.7.json)
- [Windows 설치 파일](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Setup.exe), [포터블](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Portable.zip), [배포 ZIP](../../../dist/Portway-0.3.7-win-x64-stable.zip), [SHA256](../../../dist/SHA256SUMS-0.3.7.txt)

## 범위

화면은 Windows의 Chromium 및 Windows용 WebKit으로 확인했습니다. macOS/Linux Photino 네이티브 실행은 이번 검증 범위에 포함하지 않았습니다. 실제 SSH 터미널 연결·전체 프로토콜 회귀·설치·무인 업데이트는 반복하지 않았습니다. 기존 기능의 검증 기록은 [0.3.6 글꼴](VALIDATION-0.3.6.md), [0.3.5 파일 관리](VALIDATION-0.3.5.md), [0.3.4 편집기](VALIDATION-0.3.4.md), [0.3.3 자동 업데이트](VALIDATION-0.3.3.md)를 참고하세요.

Windows 패키지는 미서명이며 공개 서버에는 업로드하지 않았습니다. 기존 Linux AppImage는 0.3.1입니다. 이전 버전별 게시 폴더와 ZIP은 덮어쓰지 않았습니다. QA 호스트·브라우저·배포 센터 프로세스는 종료하고 테스트 입력과 로그는 artifacts/qa/에 보관했습니다.
