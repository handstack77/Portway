# Document JavaScript SDK 프로젝트 전환 검증

- 검증일: 2026-10-01
- 실행 ID: `c73efc37f0854017bfea76784d612cac`
- 소스: 기존 자산 이동·표시 수정과 이번 Document 프로젝트 전환을 포함한 작업 트리
- 환경: Windows, .NET SDK 10.0.401, Node.js 20.18.0
- 모의 응답: 사용하지 않음

## 변경 내용

`Portway.Document.njsproj`를 `Portway.Document.esproj`로 전환하고 솔루션 등록 경로를 갱신했습니다. 솔루션의 `doc` 폴더, 기존 프로젝트 ID와 실제 문서 경로는 유지합니다. Artifact와 같은 `Microsoft.VisualStudio.JavaScript.Sdk/1.0.5277448`을 사용하며 이전 Node.js Tools 전용 `Build.targets`는 제거했습니다.

`BuildCommand=npm run build`, `StartupCommand=npm run start`를 지정하고 자동 npm 설치·감사는 비활성화했습니다. `IsTestProject=false`로 문서 프로젝트를 .NET 테스트 대상에서 제외합니다. `src/`, `docs/`, `jobs/`, README·패키지 파일과 `.vscode/launch.json`은 명시적인 Folder·None 항목으로 표시합니다. Node 디버깅 프로필은 기존 `src/index.js`를 대상으로 설정했습니다. 기존 소스와 package.json·lockfile, 문서·이미지·기존 검증 기록은 보존했습니다.

## 실제 실행 결과

| 검사 | 결과 |
| --- | --- |
| `dotnet sln Portway.slnx list` | 통과. Document의 새 .esproj 경로를 포함한 7개 프로젝트 등록 |
| `dotnet build Portway.slnx -c Release --artifacts-path artifacts/qa/document-esproj-c73efc37f0854017bfea76784d612cac/build` | 통과. 경고 0개, 오류 0개. SDK 빌드에서 `npm run build` → `node --check src/index.js` 실행 확인 |
| `dotnet run --project doc/Portway.Document/Portway.Document.esproj -c Release --no-build` | 통과. 기존 한국어 시작 메시지 출력 후 정상 종료 |
| `npm run build --prefix doc/Portway.Document` | 통과. 기존 명령과 구문 검사 유지 |
| `dotnet test Portway.slnx -c Release --no-build --no-restore --artifacts-path artifacts/qa/document-esproj-c73efc37f0854017bfea76784d612cac/build --logger "trx;LogFileName=solution-tests.trx" --results-directory artifacts/qa/document-esproj-c73efc37f0854017bfea76784d612cac/test-results` | 96개 통과, 25개 조건부 건너뜀, 실패 0개. 문서 프로젝트 관련 테스트 대상 오류 없음 |
| `dotnet msbuild doc/Portway.Document/Portway.Document.esproj -getItem:None,Folder -getProperty:BuildCommand,StartupCommand,ShouldRunNpmInstall,ShouldRunNpmAudit,ShouldRunBuildScript,IsTestProject -verbosity:quiet` | 통과. 폴더 4개와 문서·이미지·기존 검증 기록·소스·패키지·디버깅 설정 표시 확인, bin·obj·node_modules 제외 |
| 솔루션·구성 검사 | 프로젝트 ID 유지, 이전 .njsproj·Build.targets 부재, Node 실행 설정·수정한 프로젝트 문서 링크 확인 |
| 고정 Prettier로 `.vscode/launch.json` 검사 | 통과 |
| `git diff --check` | 통과 |

## 결과물과 한계

원본 빌드·실행·npm·테스트 로그, TRX와 항목 평가 JSON은 `artifacts/qa/document-esproj-c73efc37f0854017bfea76784d612cac/`에 보관합니다. 요약은 [verification-document-esproj-20261001-c73efc37.json](verification-document-esproj-20261001-c73efc37.json)입니다.

Visual Studio의 실제 프로젝트 로드·탐색기·F5 디버깅은 검사하지 않았습니다. Node 프로필은 JSON 구조와 실제 진입점만 확인했고 CLI 실행과 구분합니다. macOS·Linux 실제 실행, 브라우저·Photino 네이티브 UI, 실제 프로토콜 서버, 설치·패키징·업데이트 검증은 반복하지 않았습니다. 이번에 건너뛴 조건부 테스트를 통과로 간주하지 않고, 이전 패키징 결과를 이번 전환의 검증으로 재사용하지 않습니다.
