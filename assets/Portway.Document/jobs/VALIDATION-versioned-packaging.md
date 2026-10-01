# 버전별 패키징 및 변경분 업데이트 검증

검증일: 2026-09-29. Windows x64, .NET 10, Velopack/vpk 1.2.158에서 현재 개발 소스를 확인했습니다. 기존 공개 릴리스 버전은 0.3.9이며 새 0.3.10은 격리된 개발 검증용 패키지입니다.

## 변경 결과

- `scripts/package.ps1`: 임시 폴더에서 최신 Full을 기준으로 Delta를 생성하고 현재 버전의 패키지·설치 파일·피드만 버전별 폴더와 ZIP으로 내보냅니다. 앱 어셈블리 버전 일치와 기존 산출물 보존을 검사합니다.
- `scripts/build.ps1`: 게시된 앱과 CLI를 새 패키징 단계에 전달합니다. 기존 누적 로컬 폴더와 버전별 하위 폴더를 기준 패키지 검색에 사용할 수 있습니다.
- 원격 기준 패키지: vpk download로 최신 Full만 다운로드하고 피드에서 해당 파일의 버전·메타데이터를 확보합니다. 새 Delta에는 `BaseVersion`이 포함됩니다.
- 배포 서버: 버전별 ZIP의 피드를 기존 Full/Delta 목록과 합칩니다. JSON·RELEASES 피드를 갱신하고 패키지 불변성·기준 버전·SemVer 순서를 검사합니다. 과거 버전 게시 시 최신 설치 파일을 유지합니다. 릴리스 설명 필드도 보존합니다.
- CI: 배포 서버 URL이 설정되어 있으면 같은 RID/채널에서 최신 Full을 가져옵니다. 최초 빈 채널 허용과 변경분 압축 모드를 입력으로 제공합니다.

## 실제 크기 비교

| 항목 | 기존 누적 ZIP / Full | 새 버전별 ZIP / Delta |
| --- | ---: | ---: |
| 배포 ZIP 크기 | 0.3.9: 1,611,021,304 bytes | 검증용 0.3.10: 341,863,947 bytes |
| ZIP의 nupkg 개수 | 25 | 2: 해당 버전 Full·Delta |
| 업데이트 패키지 크기 | 새 Full: 113,739,863 bytes | Delta: 792,833 bytes |

검증용 ZIP은 기존 누적 ZIP보다 약 79% 작고, Delta는 새 Full의 약 0.7%입니다. 설치 프로그램·포터블·Full은 해당 버전에 필요한 배포 형식이므로 새 ZIP에도 포함됩니다. 실제 크기는 변경한 앱·런타임·자산에 따라 달라집니다.

## 실행 검사

| 검사 | 결과 |
| --- | --- |
| ReleaseTests + UpdateTests | 19개 통과, 실패·건너뜀 0 |
| 실제 ZIP PackageTests | 3개 통과, 실패·건너뜀 0 |
| 정상 Delta 다운로드 | UpdateManager가 Delta만 다운로드하고 새 패키지를 복원함; Full 다운로드 없음 |
| 복원 내용 | 원본 Full과 복원 ZIP의 파일 목록 및 모든 파일의 SHA-256 일치 |
| 손상된 Delta | 검사에서 변경분 파일을 손상시키면 Full 다운로드로 전환; 전체 패키지 SHA-256 및 파일 내용 일치 |
| 로컬 기준 패키징 | 0.3.9 → 0.3.10, 이전 패키지 제외, 현재 버전만 포함 |
| 원격 기준 패키징 | 실제 격리 HTTP 서버에서 0.3.9 Full 다운로드, 기준 메타데이터 확보, 현재 버전 Full/Delta 2개만 포함 |
| 이전 결과 보호 | 동일 출력 경로 거부, 기존 ZIP 해시 보존, 앱·패키지 버전 불일치 거부 |
| 스크립트 | PowerShell 9개 구문 검사 통과 |

변경분 복원은 ZIP을 다시 압축하므로 전체 컨테이너 바이트 해시가 원본 ZIP과 같다고 가정하지 않습니다. 파일별 내용과 원본 다운로드 패키지의 해시를 구분해 검사했습니다.

실제 패키지 검사는 `PORTWAY_PACKAGE_TEST`, `PORTWAY_PACKAGE_BASE`, `PORTWAY_PACKAGE_UPDATER`를 설정하고 `PackageTests`를 실행했습니다. 기준 Full 옆에는 해당 채널의 JSON 피드가 있습니다. `Update.exe`는 기존 포터블 ZIP에서 검증 전용 경로로 추출했습니다.

## 증거와 한계

- [기계 판독 요약](verification-versioned-packaging.json)
- [실제 패키지 테스트 TRX](../../../tests/Portway.Tests/TestResults/version-packaging.trx)
- [게시·업데이트 계약 TRX](../../../tests/Portway.Tests/TestResults/version-packaging-contracts.trx)
- [로컬 기준 검증용 ZIP](../../../artifacts/qa/version-packaging/dist/Portway-0.3.10-win-x64-stable.zip)
- [원격 기준 검증 결과](../../../artifacts/qa/version-packaging/remote-results.json)

실제 다운로드·복원 검사는 격리된 서버 저장소와 TestVelopackLocator를 사용했습니다. 설치된 앱의 UI 실행·재시작 적용·OS 등록·제거 검사를 대신하지 않습니다. 설치 이후 다음 시작 적용의 기존 동작은 유지했으며 이번에는 실제 설치본을 새로 실행하지 않았습니다. macOS/Linux 네이티브 패키징과 원격 CI·공개 서버 배포는 실행하지 않았습니다. 검증용 패키지는 서명되지 않았습니다. 이전 dist 릴리스 ZIP을 덮어쓰거나 삭제하지 않았습니다.
