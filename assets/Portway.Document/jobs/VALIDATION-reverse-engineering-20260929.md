# Portway 현재 구현 복원 문서 검증

2026-09-29, 실행 ID `reverse-engineering-20260929-a6254ef1a2464c8ca9fcfeb26012a664`. 기획·분석·설계·아키텍처와 읽기 안내를 작성하고 기존 README 두 곳에 진입 링크를 추가한 문서 작업입니다. 앱 기능이나 공개 API·저장 형식은 변경하지 않았습니다.

## 검사 대상과 환경

- 기준 커밋: `cf23cfac76e23f6c78ad0d297ecb3b49cd12d14f`, 개발 소스 `0.3.9`. 착수 시 작업 트리 변경 없음.
- 대상: [현재 구현 복원 문서](../docs/reverse-engineering/README.md) 5개, 저장소·문서 프로젝트 README의 새 안내 링크, 이 보고서.
- 환경: Windows 11 계열, Node.js 20.18.0, npm 10.8.2, PowerShell 7.6.6, Python 3.11.7.
- 분석 방식: 소스·설정·테스트 이름을 직접 확인하고 기존 문서·검증 기록과 대조. 원격 응답 모의 실행이나 실제 프로토콜 연결 없음.

## 실행 결과

| 검사 | 실행 명령·방식 | 결과 |
| --- | --- | --- |
| 문서 프로젝트 | `npm run build --prefix doc/Portway.Document` | 통과. `node --check src/index.js`이며 Markdown 검사는 아님 |
| 상대 링크·명시 앵커 | 격리 QA의 `check-docs.py`, Markdown 파싱 후 대상 존재와 앵커 대조 | 검사 진행 중 |
| 요구사항·근거 | 같은 스크립트로 기획·추적표 ID와 테스트 이름 대조, 소스 흐름 수동 검토 | 검사 진행 중 |
| Mermaid | 도식의 소스·관계를 대조하고 구문 검사 | 검사 진행 중 |
| 변경 범위·공백 | `git status --short`, `git diff --check` | 검사 진행 중 |

도식 검사 도구를 격리 QA 폴더에 준비하는 첫 npm 설치는 JavaScript 메모리 할당 오류로 실패했고, 제한된 메모리로 재시도한 설치도 실패했습니다. 이후에는 Windows의 DLL 로드·프로세스 시작에도 메모리 부족 오류가 발생했습니다. 저장소의 의존성·lockfile은 변경하지 않았습니다. 이후 확인한 검사 결과를 위 표에 기록합니다.

## 결과물과 한계

- 작업 원본·검사 스크립트: `artifacts/qa/reverse-engineering-20260929-a6254ef1a2464c8ca9fcfeb26012a664/`.
- 기능별 근거와 과거 검증 구분: [분석서](../docs/reverse-engineering/ANALYSIS.md#traceability).
- 소스와 과거 문서가 다른 부분: `PARITY.md`의 테마 OS 변경 추적 설명 대신 현재 `theme.js`의 1회 해석·고정 동작을 기록했습니다. 기존 검증표를 현재 실행 결과로 다시 작성하지 않았습니다.

이번 작업은 .NET 빌드·기능 테스트, 실제 SSH/FTP/WebDAV/S3 서버, 브라우저 앱 사용 흐름, 네이티브 Photino 창·F12, 설치·업데이트·패키지 게시·다운로드를 재실행하지 않았습니다. 도식 검사 결과는 앱 UI나 프로토콜 동작 검증을 의미하지 않습니다. macOS·Linux 실기기 검증, 원래 제품 기획·성능 SLA·실사용자 통계도 확인하지 않았습니다. 과거 보고서의 테스트 수치와 설치본 결과를 이번 결과로 사용하지 않았습니다.
