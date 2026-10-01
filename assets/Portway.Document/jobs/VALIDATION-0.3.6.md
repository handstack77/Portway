# 0.3.6 — Noto Sans KR와 Tabler 기본 파란색

검증일: 2026-09-29 (KST), Windows x64. 글꼴·아이콘 크기와 강조색 변경을 실제 loopback 호스트 및 Chromium/WebKit에서 확인했습니다.

## 적용 내용

- 앱·배포 센터의 UI와 Monaco를 **Noto Sans KR**로 변경했습니다. `@fontsource-variable/noto-sans-kr` 5.3.0을 고정하고 124개 Unicode 분할 WOFF2(합계 약 3.36 MiB), CSS, OFL 라이선스를 패키지에 포함합니다. CDN을 사용하지 않습니다. [Fontsource 공식 안내](https://fontsource.org/fonts/noto-sans-kr/use).
- 기존 UI 글자·Tabler Icons 폰트 크기를 각각 **2px** 키웠습니다. 상속된 크기를 중복 확대하지 않고 Tabler의 rem 기반 폼·테이블 제목을 별도 보정했습니다. Monaco·터미널은 14→16px입니다. 터미널의 라틴 글자는 고정 열 정렬을 위해 고정폭을 유지하며 한글에 Noto Sans KR을 사용합니다.
- 녹색 primary 재정의를 제거하고 설치된 Tabler 1.4.0의 **기본 `#066fd1`**을 사용합니다. 다크 화면의 글자·포커스는 Tabler의 밝은 링크 RGB 토큰을 사용합니다. 버튼·선택·진행 표시·기본 사이트 색상·SVG/OS 아이콘·배포 센터를 함께 변경했습니다.
- Monaco가 폰트 로드를 기다리고, CSS RGB 토큰을 hex로 변환해 테마에 적용합니다. 작은 창의 긴 사이드바 메뉴는 줄바꿈합니다.
- 사용자·개발자 가이드, AGENTS.md, 라이선스 목록과 버전 기본값을 갱신했습니다.

## 검증

| 항목 | 결과 |
| --- | --- |
| 이전 게시본과 실제 계산 크기 비교 | 16개 대표 선택자 모두 정확히 +2px. 본문 13→15, 목록 12→14, 기본 아이콘 18→20, 제목 21→23, 경로 12→14 |
| 실제 한글 글꼴 | Chromium CDP의 렌더링 글꼴 검사에서 Noto Sans KR custom font 확인. WebKit에서도 폰트 로드 및 아이콘 20px 확인 |
| 로컬 자산 | CSS의 폰트 참조 124개 모두 존재, Desktop/Server OFL 포함. 게시 앱 리소스 요청에서 외부 HTTP 출처 없음 |
| 화면 | 1440×940 및 980×680, 라이트·다크. 파일 선택·메뉴·연결 창·Monaco·배포 센터 확인, 메뉴 화면 경계 검사 통과 |
| 편집기 | Noto Sans KR 16px 확인. 한글 문장 입력 → Ctrl+S → 실제 디스크 문자열 전체 일치 |
| 프런트엔드 | npm ci/build, 기존 테스트 14개 통과. 스타일을 그대로 복제하는 새 단위 테스트는 추가하지 않음 |
| .NET | Release 소스 빌드 경고/오류 0. 기본 테스트 58개 통과, 23개 조건부 테스트 건너뜀 |
| 게시 자산 | 소스와 0.3.6 게시 wwwroot 280개 SHA256 일치 |
| Windows 패키지 | Setup·Portable·full·0.3.5→0.3.6 delta 생성. 배포 ZIP의 격리 서버 게시·다운로드 SHA256 테스트 1개 통과 |
| 게시본 UI | 0.3.6 버전·Noto 가족명·Tabler primary·작은 창 확인. Chromium/WebKit 콘솔 오류/경고 0 |

## 증거

- [게시본 라이트](../../../output/playwright/typography-0.3.6-light-980.png), [다크](../../../output/playwright/typography-0.3.6-dark-980.png), [WebKit](../../../output/playwright/typography-webkit-980.png)
- [Monaco](../../../output/playwright/typography-editor-dark.png), [연결 창](../../../output/playwright/typography-connection-dark-980.png), [작업 메뉴](../../../output/playwright/typography-menu-dark-980.png), [배포 센터](../../../output/playwright/typography-server-dark.png)
- [크기 비교 JSON](typography-size-comparison.json), [빌드 로그](../../../artifacts/build-0.3.6.log), [패키지 TRX](../../../tests/Portway.Tests/TestResults/package-0.3.6.trx), [기계 판독 요약](verification-0.3.6.json)
- [Windows 설치 파일](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Setup.exe), [포터블](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Portable.zip), [배포 ZIP](../../../dist/Portway-0.3.6-win-x64-stable.zip), [SHA256](../../../dist/SHA256SUMS-0.3.6.txt)

## 범위

Windows에서 Chromium 및 Windows용 WebKit으로 검증했으며 macOS/Linux Photino 네이티브 실행은 이번에 검증하지 않았습니다. 실제 SSH 터미널 세션·프로토콜 전체 회귀·설치·자동 업데이트는 반복하지 않았습니다. 기능 검증은 [0.3.5](VALIDATION-0.3.5.md), [0.3.4](VALIDATION-0.3.4.md), [0.3.3](VALIDATION-0.3.3.md)을 참고하세요.

Windows 패키지는 미서명이며 공개 서버에 업로드하지 않았습니다. 기존 Linux AppImage는 0.3.1입니다. 버전별 게시 폴더와 ZIP은 덮어쓰지 않았습니다. QA 호스트·브라우저·배포 센터 프로세스는 종료하고 로그와 입력은 artifacts/qa/에 보관했습니다.
