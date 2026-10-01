# Vault 자동 잠금 해제 검증 — 2026-09-30

## 범위

현재 개발 소스의 Vault 키 자동 복원, 사이트 비밀 저장 해제, 명시적 Vault 잠금을 Windows에서 확인했습니다. 설치된 배포 패키지의 검증은 아닙니다.

## 실행 결과

| 검사 | 결과 |
| --- | --- |
| `dotnet test tests/Portway.Tests/Portway.Tests.csproj -c Release --filter 'FullyQualifiedName~CoreTests\|FullyQualifiedName~AdvancedTests\|FullyQualifiedName~SiteArchiveTests\|FullyQualifiedName~DesktopApiTests' --no-restore` | 49개 통과, 실패·건너뜀 0개 |
| `dotnet test Portway.slnx -c Release --no-restore` | 96개 통과, 실패 0개, 조건부 통합 검사 25개 건너뜀 |
| `npm test --prefix assets/frontend` | 31개 통과 |
| `npm run format:check --prefix assets/frontend` | 통과 |
| `npm run build --prefix doc/Portway.Document` | 통과 |
| `dotnet format whitespace Portway.slnx --no-restore --verify-no-changes --include ...` | 변경 필요 없음. 작업 영역 로드 경고는 출력됨 |
| `dotnet build src/Portway.Desktop/Portway.Desktop.csproj -c Debug --artifacts-path artifacts/qa/<격리 경로>` | 경고·오류 0개 |
| Windows headless 호스트 + Chromium, 전용 프로필 | Vault 생성, 테스트 사이트 비밀 저장, 호스트 재시작 후 Vault 자동 잠금 해제와 사이트 직접 연결 시도 확인. `POST /api/sessions`가 실행되었고 Vault 설정 안내는 표시되지 않음 |
| 같은 브라우저에서 저장 체크 해제 | 사이트 비밀 표시·자동 잠금 해제 등록 정보가 제거됨 |

테스트 사이트의 원격 호스트는 접속할 수 없는 시험용 주소였습니다. 브라우저 검증의 연결 요청은 실제 서버 인증에 도달하지 않았으며 400 응답으로 끝났습니다. Vault 저장·복원과 UI 경로에는 모의 API 응답을 사용하지 않았습니다.

## 남은 검증 범위

macOS 키체인과 Linux Secret Service의 실제 저장·재실행·잠금 동작, 대상 OS의 네이티브 창, 실제 프로토콜 서버 연결은 이번에 실행하지 않았습니다. Linux에서 Secret Service 제공자 또는 `secret-tool`을 사용할 수 없으면 마스터 비밀번호로 수동 잠금 해제해야 합니다. 인증 토큰과 인증 URL은 이 문서에 기록하지 않았습니다.

기계 판독 기록: [최종 재검증](verification-vault-auto-unlock-20260930-r2.json). 앞선 검사 기록도 같은 디렉터리에 보존했습니다.
