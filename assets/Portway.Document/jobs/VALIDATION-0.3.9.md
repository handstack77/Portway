# 0.3.9 — Folded Hover 메뉴와 체크박스 표시 수정

검증일: 2026-09-29 (KST), Windows x64.

## 적용 내용

- [Tabler Folded Hover 예제](https://preview.tabler.io/layout-folded-hover.html)를 참고해 왼쪽 메뉴를 64px 아이콘 영역으로 접고 hover 시 260px로 펼칩니다. 파일 작업 영역의 위치·너비는 고정해 메뉴가 펼쳐져도 재배치되지 않습니다.
- 로고와 **Portway** 텍스트를 같은 34px 높이 상자에서 수직 중앙 정렬합니다. Noto Sans KR과 기존 글자·아이콘 크기를 유지합니다.
- 키보드 포커스로도 메뉴가 펼쳐집니다. 접힌 버튼은 접근성 이름과 도구 설명을 유지하며 사이트 목록은 스크롤, 하단 도구는 고정합니다. 마우스 hover가 없는 장치는 전체 메뉴를 표시하며 모션 줄이기도 지원합니다.
- 체크박스 옆 작은 점은 34px 선택 열의 여백이 15px+9px로 계산돼 15px 체크박스가 넘치면서 생긴 말줄임표였습니다. 선택 열의 좌우 여백을 9px로 맞추고 컨트롤 셀의 텍스트 생략을 해제했습니다. 체크·부분 선택 상태는 유지합니다.
- 공통 사이드바 버튼과 사이트 항목 마크업을 정리하고 사용자·개발자 가이드와 AGENTS.md를 갱신했습니다.

## 브라우저 검증

| 항목 | 결과 |
| --- | --- |
| Hover | 포인터 진입 시 64→260px, 나가면 64px. main의 x=64 및 너비 유지 |
| 로고 | 이미지·텍스트 상자의 세로 중심 동일 |
| 키보드 | focus-visible 진입, Tab 순서, 메뉴 실행, 포커스 이탈 시 접힘 |
| 많은 사이트 | 전용 프로필에 18개 사이트 저장, 마지막 항목까지 스크롤·편집, 긴 이름 표시 |
| 체크박스 | 헤더와 5개 행 모두 15px 컨트롤이 열 안에 들어감. 오른쪽 점 없음. 개별/전체/부분 선택 및 Home·Space 검증 |
| 작은 창 | 980×680에서 접힘·펼침과 고정 도구 영역 확인 |
| 라이트·다크 | 두 테마 및 1440×940 확인 |
| 모션 줄이기 | reduced-motion에서 메뉴 transition 0s |
| 터치 대체 화면 | Chromium 터치 에뮬레이션의 hover:none에서 메뉴 너비/작업 영역 시작점 260px, 설정 실행 확인 |
| WebKit | Windows용 WebKit에서 hover, 고정 레이아웃, 긴 사이트 목록 편집, 체크박스, 작은 다크 화면 확인 |
| 콘솔 | 정상 QA 동작 중 Chromium/WebKit 오류·경고 0 |

## 빌드·패키지 검증

- npm ci/build/test: **21개 통과**. 이번 변경은 배치·스타일 수정으로, 구현을 복제하는 새 단위 테스트는 추가하지 않았습니다.
- .NET Release 기본 테스트: **58개 통과, 23개 조건부 건너뜀**.
- Windows Setup·Portable·full·delta와 배포 ZIP 생성. 격리 배포 서버에 실제 ZIP 업로드·다운로드 후 SHA256 검증 **1개 통과**.
- 게시 `wwwroot` **280개**가 소스와 SHA256 일치. 실제 0.3.9 호스트에서 두 테마·hover·체크박스·설정 실행 확인.
- 게시본의 메뉴 15px·하단 도구 14px·아이콘 20px 유지, 브랜드 이미지/텍스트의 세로 중심 모두 y=42px 확인.

## 증거

- [0.3.9 라이트 접힘](../../../output/playwright/sidebar-0.3.9-light-folded.png), [라이트 펼침](../../../output/playwright/sidebar-0.3.9-light-expanded.png), [다크 접힘](../../../output/playwright/sidebar-0.3.9-dark-folded.png), [다크 펼침](../../../output/playwright/sidebar-0.3.9-dark-expanded.png), [980×680](../../../output/playwright/sidebar-0.3.9-dark-980.png)
- [체크박스 수정 전](../../../output/playwright/checkbox-before.png), [수정 후](../../../output/playwright/checkbox-after.png)
- [WebKit 접힘](../../../output/playwright/sidebar-webkit-folded.png), [펼침](../../../output/playwright/sidebar-webkit-expanded.png), [터치 대체 화면](../../../output/playwright/sidebar-touch-fallback.png)
- [브라우저 QA 기록](../../../artifacts/qa/sidebar-ui.log), [WebKit 기록](../../../artifacts/qa/sidebar-webkit.log), [터치 기록](../../../artifacts/qa/sidebar-touch.log)

- [게시본 QA 기록](../../../artifacts/qa/sidebar-release.log), [빌드 로그](../../../artifacts/build-0.3.9.log), [패키지 TRX](../../../tests/Portway.Tests/TestResults/package-0.3.9.trx), [검증 JSON](verification-0.3.9.json)
- [Windows 설치 파일](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Setup.exe), [포터블](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Portable.zip), [배포 ZIP](../../../dist/Portway-0.3.9-win-x64-stable.zip), [SHA256](../../../dist/SHA256SUMS-0.3.9.txt)

## 범위

Windows Chromium/WebKit에서 검증했으며 macOS/Linux Photino 네이티브 실행은 이번에 수행하지 않았습니다. 터치 확인은 브라우저 에뮬레이션이며 실제 터치 장치 검증은 아닙니다. 전체 프로토콜·설치·무인 업데이트는 반복하지 않았습니다. [0.3.7 다크 테마](VALIDATION-0.3.7.md), [0.3.6 글꼴](VALIDATION-0.3.6.md), [0.3.5 파일 관리](VALIDATION-0.3.5.md), [0.3.3 자동 업데이트](VALIDATION-0.3.3.md) 검증 기록을 참고하세요.

Windows 패키지는 미서명이며 공개 서버에는 업로드하지 않았습니다. Linux AppImage는 기존 0.3.1입니다. 이전 버전 산출물을 보존하고 최종 배포 버전은 0.3.9로 지정했습니다. 이번에 시작한 QA 호스트와 브라우저는 종료했습니다.
