# 자산 디렉터리 이동 검증

- 검증일: 2026-10-01
- 실행 ID: `5d5ed5c871704726b0835cfdcd85926f`
- 검사 소스: 이 변경을 포함한 작업 트리. 기준 커밋은 요약 JSON에 기록합니다.
- 환경: Windows 11, .NET SDK 10.0.401, Node.js 20.18.0, PowerShell 7.6.6, Python 3.11.7
- 모의 응답: 사용하지 않음. 패키지 서버 검사는 실제 ZIP과 인프로세스 ASP.NET Core 테스트 호스트를 사용합니다.

## 변경 내용

루트 `assets/`를 `doc/Portway.Artifact/assets/`로 이동했습니다. 추적 파일 14개와 기존 `node_modules`를 이동했으며, 솔루션의 `doc` 폴더와 Artifact 프로젝트의 위치는 유지했습니다.

Desktop의 아이콘 포함·복사 경로, Artifact의 내부 자산 연결과 의존성 제외, 프런트엔드 빌드·테스트·포맷 명령의 상대 경로, 빌드·패키징·아이콘 생성 스크립트와 Linux Dockerfile을 수정했습니다. README·개발 지침과 현재 가이드·분석 문서의 명령·링크도 새 경로로 갱신했습니다. 과거 검증 기록의 실행 명령과 경로는 당시 이력으로 보존합니다.

의존성 버전과 lockfile은 변경하지 않았습니다. 기존 라이선스, Monaco 진입점, lockfile, OS 아이콘 3개의 SHA-256이 이동 전과 같습니다. 아이콘 생성 스크립트를 실제 실행해 같은 파일 내용을 재생성하는 것도 확인했습니다. 생성 자산은 기존 Desktop·Server의 `wwwroot/vendor/`에 유지합니다.

## 실행 결과

| 검사 | 명령·범위 | 결과 |
| --- | --- | --- |
| 아이콘 생성 | `python scripts/make-icons.py` | 통과. ICO·PNG·ICNS 내용 보존 |
| 프런트엔드 빌드 | `npm run build --prefix doc/Portway.Artifact/assets/frontend` | 통과 |
| 프런트엔드 테스트 | `npm test --prefix doc/Portway.Artifact/assets/frontend` | 31개 통과, 실패·건너뜀 0개 |
| 포맷 | 변경한 JS·JSON·테스트에 고정 Prettier 적용 후 `npm run format:check --prefix doc/Portway.Artifact/assets/frontend` | 통과 |
| 솔루션 Release 빌드 | `dotnet build Portway.slnx -c Release --artifacts-path <QA>/build` | 통과. 경고 0개, 오류 0개. Document 구문 검사 포함 |
| 솔루션 테스트 | `dotnet test Portway.slnx -c Release --no-build --no-restore --artifacts-path <QA>/build --logger "trx;LogFileName=solution-tests.trx" --results-directory <QA>/test-results` | 96개 통과, 25개 조건부 건너뜀, 실패 0개. ReleaseTests 포함 |
| Artifact MSBuild 평가 | `dotnet msbuild doc/Portway.Artifact/Portway.Artifact.esproj -getItem:None -verbosity:quiet` | 통과. 전체 연결 항목 26개, 자산 14개, node_modules 제외 |
| 경로·문서 검사 | 등록 프로젝트 존재, 수정한 자산 링크 13개, 루트 assets 부재, PowerShell 구문 검사, 빌드 출력 아이콘 해시 | 통과 |
| Windows 자체 포함 게시 | Desktop·CLI를 `win-x64`로 별도 QA 출력에 게시 | 통과 |
| Windows 실제 패키징 | `scripts/package.ps1`, 최초 격리 beta 채널, QA 버전 | Full nupkg·설치 파일·배포 ZIP 생성 성공. 서명하지 않음 |
| 실제 ZIP API 테스트 | `PORTWAY_PACKAGE_TEST=<생성 ZIP>`, `PORTWAY_PACKAGE_CHANNEL=win-x64-beta`로 `GeneratedVpkArchivePublishesAndDownloadsWithMatchingHash` 실행 | 1개 통과, 실패·건너뜀 0개. 인프로세스 서버 업로드·다운로드·해시 검사 |
| 패키지 내용 | 피드의 Full 크기·SHA-256, 단일 버전, 설치 파일·CLI 포함, 아이콘 내용, 웹 자산 전체 | 통과. 웹 자산 287개가 소스와 일치 |
| 변경 검사 | `git diff --check`, 과거 검증 기록을 제외한 현재 소스·가이드의 이전 자산 경로 검색 | 통과 |

`<QA>`는 저장소 루트 기준 `artifacts/qa/assets-relocation-5d5ed5c871704726b0835cfdcd85926f`입니다. 기존 출력 파일 잠금과 기존 릴리스 덮어쓰기를 피하기 위해 빌드·게시·패키지를 모두 이 경로에서 생성했습니다.

## 패키지 생성 명령과 결과물

QA 버전은 `0.3.9-assets-path-20261001-5d5ed5c8`이며 저장소의 제품 버전은 변경하지 않았습니다. 다음 명령은 저장소 루트에서 실행했습니다.

```powershell
$qaPath = 'artifacts/qa/assets-relocation-5d5ed5c871704726b0835cfdcd85926f'
$qaVersion = '0.3.9-assets-path-20261001-5d5ed5c8'
dotnet tool restore
dotnet publish src/Portway.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:Version=$qaVersion" --artifacts-path "$qaPath/publish-build" -o "$qaPath/publish/win-x64"
Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination "$qaPath/publish/win-x64"
dotnet publish src/Portway.Cli -c Release -r win-x64 --self-contained true "-p:Version=$qaVersion" --artifacts-path "$qaPath/publish-build" -o "$qaPath/publish/win-x64/cli"
./scripts/package.ps1 -PublishDirectory "$qaPath/publish/win-x64" -Version $qaVersion -Runtime win-x64 -Track beta -OutputDirectory "$qaPath/packages" -AllowEmptyChannel
```

- ZIP: `artifacts/qa/assets-relocation-5d5ed5c871704726b0835cfdcd85926f/packages/Portway-0.3.9-assets-path-20261001-5d5ed5c8-win-x64-beta.zip`
- ZIP SHA-256: `59A6D3AEED9759092815261FF073F7496924A6166CF214560A8D896DEDB04244`
- 원본 로그·TRX·이동 전후 해시·MSBuild 항목·구조 및 패키지 내용 검사: 위 QA 폴더
- 요약: [verification-assets-relocation-20261001-5d5ed5c8.json](verification-assets-relocation-20261001-5d5ed5c8.json)

## 검증 한계

Windows 패키지 생성·ZIP 내용·인프로세스 배포 API를 검사했으며 실제 설치·시작·업데이트 적용, Delta 생성·복원, 서명, 공용 배포 서버 업로드는 수행하지 않았습니다. 기존 설치본과 기존 `dist/` 릴리스는 변경하지 않았습니다.

macOS·Linux 실제 앱 실행·패키징·서명·공증과 Linux Dockerfile 빌드는 실행하지 않았습니다. Linux Dockerfile의 새 아이콘 경로와 OS별 아이콘 원본 존재·내용 보존은 확인했습니다. Visual Studio 실제 탐색기 화면, 브라우저·네이티브 UI, 실제 프로토콜 서버 통합 검증은 실행하지 않았습니다. 기본 테스트의 25개 조건부 건너뜀을 통과로 간주하지 않습니다.

`scripts/build.ps1` 전체를 실행해 `dist/`에 릴리스를 만들지 않았으며, 수정한 스크립트의 구문과 자산 경로를 검사하고 프런트엔드·.NET·게시·패키징 명령을 위의 격리 출력에서 실행했습니다. lockfile과 의존성이 바뀌지 않아 이동한 기존 node_modules를 사용했고 npm ci는 반복하지 않았습니다.
