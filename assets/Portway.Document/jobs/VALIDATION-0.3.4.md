# 0.3.4 — Monaco 편집기와 공통 팝업

검증일: 2026-09-29 (KST). Windows에서 소스 및 게시 실행 파일의 loopback 호스트를 실행하고 Playwright CLI의 Chromium과 WebKit으로 검증했습니다. UI에 모의 파일 API를 넣지 않고 실제 로컬 디스크와 격리된 Docker SFTP 서버를 사용했습니다.

## 변경 내용

- textarea를 Monaco Editor 0.57.0으로 교체했습니다. 구문 강조, 언어 선택, 찾기/바꾸기, 줄바꿈, 줄·열 및 LF/CRLF 상태, Ctrl/Cmd+S를 제공합니다.
- ESM·CSS·폰트·언어 청크·작업자 5종을 esbuild 0.28.2로 묶어 앱에 포함합니다. 서버 대시보드에는 Monaco를 복사하지 않습니다. CSP에 동일 출처 작업자만 허용하며 외부 JSON 스키마 요청은 끕니다.
- 파일명·경로·저장 상태·편집 도구·하단 작업 영역을 구분하고 Tabler 토큰으로 두 테마를 적용했습니다.
- 연결·전송·설정·확인 창의 여백, 고급 설정 카드, 줄바꿈, 스크롤을 정리했습니다. 인코딩과 프리셋 입력의 기본 브라우저 prompt를 앱 대화상자로 교체했습니다.
- 공통 CSS의 잘못된 `var(--surface)-space` 속성을 `white-space`로 수정하고 Monaco 내부 컨트롤은 앱의 공통 입력 스타일에서 제외했습니다.
- 닫힌 대화상자의 지연된 close 이벤트가 새 창의 편집기·설정·터미널을 정리하지 않도록 dialog 노드를 분리합니다.

## 검증 결과

| 검증 | 결과 |
| --- | --- |
| `npm ci`, 자산 빌드, 프런트엔드 테스트 | 7개 통과. 기존 드롭 4개, 저장 스냅샷·인코딩/BOM 변경·실패 재시도 3개 |
| .NET Release 빌드 | 성공, 초기 솔루션 빌드 경고/오류 0 |
| .NET 기본 테스트 | 58개 통과, 23개 조건부 통합 테스트 건너뜀 |
| 로컬 편집 | 실제 JSON 파일의 찾기/바꾸기 → Ctrl+S → 디스크 내용 확인 |
| 저장 중 추가 입력 | `/api/local/write` 요청을 잠시 보류한 동안 추가 입력. 제출한 스냅샷만 디스크에 저장되고 새 입력은 미저장 상태로 유지됨. 다음 저장도 성공 |
| 인코딩 | UTF-16LE BOM 바이트 `FF FE` 확인. cp949 자동 읽기 실패 → 앱 인코딩 창 → 한국어 내용 편집/저장 → cp949로 다시 읽어 문자열 일치 확인 |
| 저장 충돌 | 편집 중 디스크를 외부 수정한 후 저장 시 HTTP 409. 편집 내용 유지, 외부 파일 덮어쓰기 없음 |
| 닫기·수명 | BOM만 변경해도 버리기 확인. 취소는 편집 내용 유지. 버리기는 디스크 유지. 닫은 뒤 Monaco 모델 수 0, 재열기 성공 |
| 원격 편집 | 격리 fixture의 신뢰된 SSH 호스트 키 비교 후 SFTP 연결, 원격 JSON을 Monaco에서 편집/저장하고 컨테이너 파일 내용 확인 |
| 언어 작업자 | Chromium에서 editor/json/css/html/ts 작업자 생성 확인. JSON·TypeScript 진단 응답 확인 |
| WebKit | editor/json 작업자, JSON 진단, 줄바꿈, 두 테마. Mac 사용자 에이전트의 Meta+A·한글 입력·Meta+S 후 실제 파일 문자열 전체 일치 확인 |
| 팝업·터미널 | 새 연결·호스트 키 확인·전송 옵션·프리셋 저장·설정·닫기 확인. 실제 SSH PTY에서 `echo PORTWAY_TERMINAL_OK` 입출력 확인 |
| 오류 UI | Monaco 엔트리 요청을 의도적으로 차단하면 오류 표시, 저장 비활성화, 닫기 가능. 모듈 로드 실패 복구 안내는 앱 재시작 |
| 화면 크기·테마 | 1440×940 및 980×680. 본문만 스크롤하고 하단 버튼은 계속 표시. 공통 토큰의 라이트/다크 적용 |
| Windows 패키지 | Setup·Portable·full·0.3.3→0.3.4 delta 생성. 실제 배포 ZIP 업로드/다운로드 SHA-256 검증 1개 통과 |
| 게시 파일 | 원본과 게시 wwwroot 152개 파일 SHA-256 일치. 게시 0.3.4 실행 파일에서도 편집기/작업자 로드·실제 저장 성공, 콘솔 오류/경고 0 |

의도한 실패 시나리오에서는 400(잘못된 인코딩), 409(동시 수정), 차단한 Monaco 요청의 네트워크 오류가 발생합니다. 이 오류들을 성공 흐름의 오류 없음과 구분했습니다. 저장 중 입력 검증에서는 응답 내용을 위조하지 않고 실제 요청 전달 시점만 제어했습니다.

## 증거와 산출물

- [최종 편집기 — 라이트](../../../output/playwright/editor-0.3.4-light.png), [다크·980×680](../../../output/playwright/editor-0.3.4-dark-980.png)
- [찾기/바꾸기](../../../output/playwright/editor-find-replace-dark.png), [저장 충돌](../../../output/playwright/editor-conflict-dark.png), [닫기 확인](../../../output/playwright/editor-discard-dark.png), [cp949 편집](../../../output/playwright/editor-cp949-light.png)
- [전송 옵션](../../../output/playwright/transfer-dark-980.png), [프리셋 입력](../../../output/playwright/preset-dark-980.png), [설정 라이트](../../../output/playwright/settings-light-980.png), [설정 다크](../../../output/playwright/settings-dark-980.png), [SSH 터미널](../../../output/playwright/terminal-light-980.png)
- [빌드 로그](../../../artifacts/build-0.3.4.log), [기본 테스트 TRX](../../../tests/Portway.Tests/TestResults/editor-ui.trx), [패키지 테스트 TRX](../../../tests/Portway.Tests/TestResults/package-0.3.4.trx), [기계 판독 요약](verification-0.3.4.json)
- [설치 파일](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Setup.exe), [포터블 ZIP](../../../dist/releases/win-x64-stable/Portway-win-x64-stable-Portable.zip), [배포 서버용 ZIP](../../../dist/Portway-0.3.4-win-x64-stable.zip), [SHA-256](../../../dist/SHA256SUMS-0.3.4.txt)

## 검증 범위

이번 실행은 Windows 호스트와 Chromium/WebKit 브라우저 검증입니다. macOS/Linux의 실제 Photino WebView, 네이티브 IME, OS별 창 닫기 동작은 직접 검증하지 않았습니다. Windows Photino 네이티브 창의 UI 자동화도 이번에는 수행하지 않았습니다. WebKit 브라우저 통과를 Mac/Linux 네이티브 통과로 표시하지 않습니다.

Windows 패키지는 서명되지 않았으며 공개 배포 서버에 업로드하지 않았습니다. 이번 0.3.4 패키지의 설치·무인 업데이트·제거 시나리오는 다시 실행하지 않았고, 해당 기능의 실제 설치본 검증은 [0.3.3 기록](VALIDATION-0.3.3.md)에 있습니다. 전체 프로토콜 회귀도 이번에 반복하지 않았습니다. 기존 Linux AppImage는 0.3.1이며 새 Monaco UI는 현재 소스를 자산 빌드 후 대상 OS에서 게시해야 합니다.

QA용 브라우저·호스트 프로세스·격리 SFTP 컨테이너는 종료했습니다. 테스트 입력과 로그는 `artifacts/qa/`에 남겼습니다.
