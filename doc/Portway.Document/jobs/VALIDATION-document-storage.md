# Portway.Document 문서 저장 경로 변경 검증

검증일: 2026-09-29. Windows, .NET SDK 10.0.401과 Visual Studio 2026 Community MSBuild에서 현재 0.3.9 개발 소스를 확인했습니다.

## 변경

- 저장소의 `docs/`를 `doc/Portway.Document/docs/`, `jobs/`를 `doc/Portway.Document/jobs/`로 이동했습니다. 가이드·이미지 14개와 검증 기록 44개, 합계 58개입니다.
- Markdown 링크 22개 파일과 JSON의 현재 문서 참조 8개 파일을 새 위치에 맞게 조정했습니다. 과거 이동 명세의 source/destination과 해시는 당시 이력으로 보존했습니다.
- `Portway.Document.njsproj`에 두 디렉토리의 재귀 파일 항목을 등록했습니다. 이후 해당 폴더에 추가하는 파일도 프로젝트 항목으로 포함됩니다.
- 루트 README·AGENTS.md, 프로젝트 README와 개발·구조·소스 탐색 가이드를 갱신했습니다. CLI 도움말도 새 자동화 가이드 경로를 안내합니다.
- `scripts/write-verification.ps1`의 생성 경로와 Docker·Prettier의 검증 기록 제외 경로를 변경했습니다. 이 생성기를 사용하는 통합·설치·자동 업데이트 검증 스크립트도 새 경로에 JSON을 기록합니다.

## 결과

| 검사 | 결과 |
| --- | --- |
| 기존 파일 이동 | 58개 모두 이동, 이동 직후 SHA-256 원본과 일치 |
| 루트 `docs`·`jobs` | 이동 후 존재하지 않으며 생성기 실행 후에도 다시 생성되지 않음 |
| 가이드·이전 보고서의 로컬 링크 | 안내 갱신 후 339개 대상 확인, 깨진 링크 0개 |
| 과거 이동 명세·가이드 이미지 | 원본 해시 유지 |
| JSON 기록 생성 | 문서 프로젝트를 현재 디렉토리로 사용해도 지정된 새 경로에 생성됨 |
| 기록 보호 | 한국어·JSON 보존, 같은 이름의 재생성 거부 및 기존 파일 해시 유지 |
| PowerShell 생성기 구문 | 통과 |
| 솔루션 Release 빌드 | Node 구문 검사를 포함해 경고·오류 0개 |
| Visual Studio MSBuild | Node 프로젝트 빌드와 기존 문서 파일 58개 항목 등록 확인 |
| CLI 도움말 | 새 `doc/Portway.Document/docs/AUTOMATION.md` 경로 출력 |
| 프로젝트 README 포맷 | 고정된 Prettier 검사 통과 |

빌드는 별도 `artifacts/qa/document-storage-48207493a5e84219ad4b3fc1269c8f78/build`에서 실행했습니다. [기계 판독 기록](verification-document-storage-20260929-48207493a5e84219ad4b3fc1269c8f78.json)과 [원본 이동·링크 검사 자료](../../../artifacts/qa/document-storage-48207493a5e84219ad4b3fc1269c8f78/)를 함께 보관합니다. 문서 관리 방법은 [프로젝트 README](../README.md)를 참고하세요.

Visual Studio GUI의 문서 표시, 실제 설치·업데이트와 macOS·Linux 실행은 이번 문서 경로 변경 검증 범위에 포함하지 않았습니다. 빌드·테스트 원본 로그·TRX·임시 프로필은 기존 산출물 폴더에서 관리합니다.
