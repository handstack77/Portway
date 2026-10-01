# doc 루트 및 솔루션 폴더의 assets 전환 검증

- 검증일: 2026-10-01
- 실행 ID: `ee4d99dbcf024ef2809bc98554f4b109`
- 소스: 이번 이동과 경로 갱신을 포함한 작업 트리. 기준 커밋은 요약 JSON에 기록합니다.
- 환경: Windows, .NET SDK 10.0.401, Node.js 20.18.0, PowerShell 7.6.6, Python 3.11.7
- 모의 응답: 사용하지 않음. 패키지 API 검사는 실제 ZIP과 인프로세스 ASP.NET Core 테스트 호스트를 사용합니다.

## 변경 내용과 보존

루트 `doc/`를 `assets/`로 이동하고 `Portway.slnx`의 가상 폴더를 `assets`로 바꿨습니다. 두 프로젝트는 `assets/Portway.Artifact/Portway.Artifact.esproj`와 `assets/Portway.Document/Portway.Document.esproj`로 등록합니다. Document의 프로젝트 ID와 내부 폴더 구조는 유지합니다.

빌드·패키징·아이콘 생성·검증 기록 저장 스크립트, Desktop 아이콘 경로, Linux Dockerfile, Docker·Prettier 제외 설정, CLI 도움말과 현재 README·가이드·개발 지침의 경로를 갱신했습니다. 이동 전후 폴더 깊이가 같아 프런트엔드의 상대 소스 경로, 두 .esproj의 내부 항목과 외부 링크, Node 디버깅 설정은 그대로 유지합니다.

추적 파일 100개와 기존 의존성·빌드 캐시를 이동했습니다. 경로 설명을 갱신한 문서 9개를 제외한 91개 파일은 이동·아이콘 재생성 후 SHA-256이 같았습니다. 기존 검증 기록 58개의 내용 보존을 먼저 확인한 뒤 이 최신 안내를 위해 `VALIDATION.md`에 새 단락을 추가했습니다. 개별 과거 검증 보고서·JSON 57개는 최종적으로도 해시가 같습니다. 기록용 README만 새 저장 경로로 갱신하며, 과거 보고서·JSON의 당시 명령과 source/destination 경로는 이력으로 보존합니다. 제품 버전과 의존성·lockfile은 변경하지 않았습니다.

## 실제 실행 결과

| 검사 | 명령·범위 | 결과 |
| --- | --- | --- |
| 아이콘 생성 | `python scripts/make-icons.py` | 통과. 새 OS 아이콘·문서 이미지 위치 사용, 기존 내용 보존, 이전 doc 폴더 재생성 없음 |
| 프런트엔드 빌드 | `npm run build --prefix assets/Portway.Artifact/assets/frontend` | 통과 |
| 프런트엔드 테스트 | `npm test --prefix assets/Portway.Artifact/assets/frontend` | 31개 통과, 실패·건너뜀 0개 |
| 프런트엔드 포맷 | `npm run format:check --prefix assets/Portway.Artifact/assets/frontend` | 통과 |
| Document 콘솔 | `dotnet run --project assets/Portway.Document/Portway.Document.esproj -c Release --no-build` | 통과. 기존 한국어 시작 메시지와 정상 종료 |
| 프로젝트 항목 | 두 .esproj에 `dotnet msbuild -getItem:None,Folder -verbosity:quiet` | 통과. Artifact 파일 26개·폴더 1개, Document 파일 84개·폴더 4개. 평가 시점의 모든 등록 파일 존재, 생성물 제외 |
| 솔루션·문서 | 가상 assets 폴더의 2개 프로젝트와 등록 경로, 현재 가이드·README 등의 로컬 링크 | 통과. 로컬 Markdown 링크 431개 확인 |
| PowerShell | build·package·write-verification 스크립트 파서 검사 | 통과 |
| 솔루션 Release 빌드 | `dotnet build Portway.slnx -c Release --artifacts-path <QA>/build` | 통과. 경고 0개, 오류 0개. Document의 npm 구문 검사 포함 |
| 솔루션 테스트 | `dotnet test Portway.slnx -c Release --no-build --no-restore --artifacts-path <QA>/build --logger "trx;LogFileName=solution-tests.trx" --results-directory <QA>/test-results` | 96개 통과, 25개 조건부 건너뜀, 실패 0개. ReleaseTests 포함 |
| CLI 도움말·출력 아이콘 | 빌드한 CLI 실행, Desktop 출력의 ICO·PNG SHA-256 | 통과. 도움말은 새 AUTOMATION.md 경로 사용, 출력 아이콘 내용 일치 |
| Windows 자체 포함 게시 | Desktop·CLI를 win-x64로 격리 게시 | 통과 |
| Windows 패키지 | `scripts/package.ps1`의 새 아이콘 경로, 별도 최초 beta 채널 | Full nupkg·Setup.exe·배포 ZIP 생성 성공. 서명하지 않음 |
| 실제 ZIP API | 생성 ZIP을 `PORTWAY_PACKAGE_TEST`에 지정하고 `GeneratedVpkArchivePublishesAndDownloadsWithMatchingHash` 실행 | 1개 통과, 실패·건너뜀 0개. 인프로세스 서버 업로드·다운로드·해시 검사 |
| 패키지 내용 | 피드의 Full 크기·SHA-256·단일 버전, CLI·설치 파일·아이콘·전체 웹 자산 | 통과. 웹 자산 287개가 소스와 일치 |
| 기록 저장 | `scripts/write-verification.ps1`로 아래 JSON 생성 후 같은 Name 재요청 | 새 assets/jobs 경로 저장 성공, 기존 JSON 덮어쓰기 거부와 내용 보존 확인 |
| 최종 변경 | `git diff --check`, 실행 소스의 이전 doc 참조 검색, 과거 개별 검증 보고서·JSON 해시 | 통과 |

`<QA>`는 저장소 루트 기준 `artifacts/qa/doc-to-assets-ee4d99dbcf024ef2809bc98554f4b109`입니다. 새 검증 보고서·JSON은 프로젝트의 jobs 와일드카드로 표시하며 위 프로젝트 항목 수는 기록 추가 전 평가 결과입니다.

## 패키지 명령과 결과물

제품 버전은 0.3.9로 유지합니다. 다음 QA 버전만 격리 출력에서 사용했습니다.

```powershell
$qaPath = 'artifacts/qa/doc-to-assets-ee4d99dbcf024ef2809bc98554f4b109'
$qaVersion = '0.3.9-assets-root-20261001-ee4d99db'
dotnet tool restore
dotnet publish src/Portway.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:Version=$qaVersion" --artifacts-path "$qaPath/publish-build" -o "$qaPath/publish/win-x64"
Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination "$qaPath/publish/win-x64"
dotnet publish src/Portway.Cli -c Release -r win-x64 --self-contained true "-p:Version=$qaVersion" --artifacts-path "$qaPath/publish-build" -o "$qaPath/publish/win-x64/cli"
./scripts/package.ps1 -PublishDirectory "$qaPath/publish/win-x64" -Version $qaVersion -Runtime win-x64 -Track beta -OutputDirectory "$qaPath/packages" -AllowEmptyChannel
```

- ZIP: `artifacts/qa/doc-to-assets-ee4d99dbcf024ef2809bc98554f4b109/packages/Portway-0.3.9-assets-root-20261001-ee4d99db-win-x64-beta.zip`
- ZIP SHA-256: `BAB9309B584FC566E40C4654BCDFE5B04541F9FEB7EA75D31451BD0A21CBAE72`
- 원본 로그·TRX·이동 전후 해시·프로젝트 평가·구조·패키지 내용 검사 JSON: 위 QA 폴더
- 요약: [verification-doc-to-assets-20261001-ee4d99db.json](verification-doc-to-assets-20261001-ee4d99db.json)

## 검증 한계

Windows 소스 빌드·자체 포함 게시·패키지 생성·ZIP 내용·인프로세스 배포 API를 검사했습니다. 실제 Visual Studio 재로드·탐색기·F5, Photino·브라우저 UI, 실제 설치·업데이트 적용·Delta 복원·서명, macOS·Linux 실제 실행·패키징·공증, Linux Dockerfile 빌드와 프로토콜 서버 통합 검증은 수행하지 않았습니다.

기존 설치본·배포 서버·dist 릴리스는 변경하지 않았습니다. `scripts/build.ps1` 전체로 dist에 새 릴리스를 만들지 않고 구문과 새 경로를 확인한 뒤 각 단계를 QA 출력으로 실행했습니다. lockfile이 바뀌지 않아 이동한 node_modules를 사용하고 npm ci를 반복하지 않았습니다. 건너뛴 25개 조건부 테스트와 과거 패키징 결과를 이번 통과로 보고하지 않습니다.
