# Portway.Document 프로젝트 추가 검증

- 날짜: 2026-09-29
- 소스 기준: Portway 0.3.9 솔루션에 문서용 Node 프로젝트 추가
- 환경: Windows, .NET SDK 10.0.401, Node.js 20.18.0, npm 10.8.2, Visual Studio 2026 Community의 MSBuild

## 변경

`Portway.slnx`에 `doc` 솔루션 폴더와 `doc/Portway.Document/Portway.Document.njsproj`를 등록했습니다. 기본 콘솔 진입점, npm 실행·개발·구문 검사 명령과 lockfile을 만들었습니다. Visual Studio Node.js Tools 및 해당 도구가 없는 CLI 환경에서 빌드할 수 있습니다. CLI의 NuGet 복원과 VSTest 호출도 처리합니다.

프로젝트 README, 개발자 가이드, 소스 탐색 가이드와 AGENTS.md에 위치·실행 방법·문서 보관 규칙을 기록했습니다.

## 결과

| 검사 | 결과 |
| --- | --- |
| `dotnet sln Portway.slnx list` | Node 프로젝트와 기존 5개 프로젝트 등록 확인 |
| `npm ci --prefix doc/Portway.Document` | 성공, 외부 의존성 없음 |
| `npm start --prefix doc/Portway.Document` | 한국어 시작 메시지 출력 후 정상 종료 |
| `npm run build --prefix doc/Portway.Document` | JavaScript 구문 검사 성공 |
| `dotnet build Portway.slnx -c Release --artifacts-path artifacts/qa/document-project-build --nologo` | Node 구문 검사를 포함한 솔루션 빌드 성공, 경고·오류 0개 |
| Visual Studio MSBuild의 Node 프로젝트 Build | Debug·Release에서 npm 구문 검사 실행 확인 |
| `dotnet test Portway.slnx -c Release --artifacts-path artifacts/qa/document-project-build --no-build --no-restore --filter FullyQualifiedName~ReleaseTests --nologo` | 기존 테스트 8개 통과, Node 프로젝트의 VSTest 호출 오류 없음 |
| 고정된 Prettier로 프로젝트 JS·JSON·README 검사 | 포맷 일치 |

현재 프로젝트는 기본 Node 콘솔 진입점입니다. 문서 사이트나 문서 생성기는 포함하지 않습니다. Visual Studio GUI의 프로젝트 로딩·F5 디버깅과 macOS·Linux 실행은 이번 검증 범위에 포함하지 않았습니다.
