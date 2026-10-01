# Artifact 내부 assets 표시 설정 검증

- 검증일: 2026-10-01
- 실행 ID: `75bb4580002d413a8dbd376b0b479c14`
- 소스: 기존 자산 이동 작업을 보존한 작업 트리의 `doc/Portway.Artifact/Portway.Artifact.esproj`
- 환경: Windows, .NET SDK 10.0.401
- 모의 응답: 사용하지 않음

## 변경과 원인

검사 시점의 프로젝트는 `EnableDefaultItems=false`였지만 내부 `assets` 파일 등록이 없었습니다. 명시적 `Folder Include="assets\"`와 `None Include="assets/**/*" Visible="true"`를 추가했습니다. 내부 파일에는 Link를 지정하지 않아 실제 폴더 구조를 사용하고, node_modules 제외 설정과 기존 deploy·scripts 외부 링크를 유지합니다. 개발 지침·개발자 가이드·구조 문서에 이 등록을 유지하는 규칙을 반영했습니다.

## 실제 실행 결과

| 검사 | 결과 |
| --- | --- |
| `dotnet msbuild doc/Portway.Artifact/Portway.Artifact.esproj -getItem:None,Folder -verbosity:quiet` | 통과. assets 폴더 1개, 내부 파일 14개, 모두 Visible=true이며 Link 없음 |
| 아이콘·frontend/package.json·테스트·라이선스 등록 | 통과 |
| node_modules 제외 및 deploy·scripts 링크 보존 | 통과 |
| `dotnet build Portway.slnx -c Release --artifacts-path artifacts/qa/artifact-assets-visible-75bb4580002d413a8dbd376b0b479c14/build` | 통과. 경고 0개, 오류 0개. Document 구문 검사 포함 |
| `git diff --check` | 통과 |

원본 항목 JSON, 구조 검사 JSON, 빌드 로그는 `artifacts/qa/artifact-assets-visible-75bb4580002d413a8dbd376b0b479c14/`에 보관합니다. 요약은 [verification-artifact-assets-visible-20261001-75bb4580.json](verification-artifact-assets-visible-20261001-75bb4580.json)입니다.

Visual Studio 실제 화면과 프로젝트 재로드는 이번 검사에서 수행하지 않았습니다. 확인한 것은 MSBuild의 폴더·표시 파일 등록과 솔루션 빌드입니다. 파일 표시 설정만 수정했으므로 프런트엔드 테스트·패키징·앱 실행·프로토콜 통합 검증은 반복하지 않았고, 이전 결과를 이번 실행 결과로 재사용하지 않았습니다.
