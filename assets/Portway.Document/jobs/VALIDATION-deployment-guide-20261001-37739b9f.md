# 배포 운영 따라 하기 문서 검증

- 날짜: 2026-10-01
- 실행 ID: `deployment-guide-37739b9f7d074aba8a18b9c713fa2bdc`
- 환경: Windows `10.0.26200`, PowerShell `7.6.6`
- 기준 커밋: `a7ebae874e60a673d857f5477ea18b4636e50d72` 위의 문서 변경 작업 트리
- 대상: [DEPLOYMENT.md](../docs/DEPLOYMENT.md), [DEVELOPER-GUIDE.md](../docs/DEVELOPER-GUIDE.md)

## 변경 내용

Windows Setup.exe, macOS 서명·공증 pkg, Ubuntu 24.04 AppImage 생성 예시를 추가했습니다. 로컬 Docker 서버와 Linux HTTPS 운영 서버 준비, 최초/후속 채널 게시, 실제 ZIP 검사, Delta 기준 Full 준비, 게시 후 공개 다운로드 해시 비교, 설치본 업데이트 설정을 따라 하기 순서로 설명합니다. 개발자 가이드의 피드 교체 설명을 현재 서버의 병합 동작으로 수정했습니다.

스크립트 인수·출력 경로·서명 환경 변수는 `scripts/build.ps1`, `scripts/package.ps1`, `scripts/publish.ps1`과 대조했습니다. 서버 환경 변수·포트·볼륨은 두 Compose 파일과 Caddyfile, API와 피드 병합은 `ServerApi.cs`, `ReleaseStore.cs`, `ReleaseStore.Feeds.cs`, ZIP 검사 조건은 `PackageTests.cs`를 확인했습니다. 서명·공증 예시는 문서에 연결한 Microsoft, Apple, Velopack 공식 자료도 참고했습니다.

## 실행한 검사

| 명령 또는 검사 | 결과 |
| --- | --- |
| `./artifacts/qa/deployment-guide-37739b9f7d074aba8a18b9c713fa2bdc/check-guide.ps1` | 통과. PowerShell 예시 14개 구문, Bash 예시 4개 `bash -n`, 빌드/게시 스크립트 호출 5개의 옵션 검사 |
| 같은 검사 스크립트의 Markdown 링크 확인 | 통과. 두 가이드의 로컬 링크 107개, 앵커 19개 |
| `npm.cmd run build --prefix assets/Portway.Document` | 통과. `node --check src/index.js`; Markdown 내용 검사는 별도 수행 |
| `git diff --check` | 통과. 공백 오류 없음 |

원본 검사 결과는 [checks.json](../../../artifacts/qa/deployment-guide-37739b9f7d074aba8a18b9c713fa2bdc/checks.json), 공개 기록은 [verification JSON](verification-deployment-guide-20261001-37739b9f.json)에 있습니다.

## 검증 한계

문서 안의 배포 명령은 실행하지 않고 구문과 소스의 일치 여부를 검사했습니다. 모의 서버 응답은 사용하지 않았습니다. 이번 작업에서 실제 패키지 생성·PackageTests·코드 서명·Apple 공증·Docker 서버 시작·운영 서버 게시·Windows/macOS/Linux 설치 및 업데이트는 실행하지 않았습니다. 기존 버전의 검증 결과를 새 예시 버전의 실행 결과로 재사용하지 않았으며, 앱 소스·프로젝트 버전·기존 배포 산출물은 변경하지 않았습니다.
