# Portway 작업별 검증 기록

특정 버전이나 변경 사항의 검증 결과는 이 디렉토리에 보관합니다. 현재 검증 현황은 [VALIDATION.md](VALIDATION.md), 코드 정리 결과는 [REFACTORING.md](REFACTORING.md)를 참고하세요. 개발·사용·배포 가이드는 [docs](../docs/DEVELOPER-GUIDE.md)에 유지합니다.

새 기록은 `VALIDATION-<버전 또는 작업명>.md`와 `verification-<버전 또는 작업명>.json`으로 작성합니다. 같은 작업을 다시 검증할 때는 날짜나 실행 ID를 붙여 이전 결과를 보존합니다. 명령, 소스·빌드, 운영체제, 통과·실패·건너뜀, 모의 응답 여부와 검증 한계를 적습니다. JSON에 기록한 상대 경로는 저장소 루트 기준입니다.

PowerShell에서 JSON 기록을 생성하는 예시입니다.

```powershell
$record = @{
    date = [DateTimeOffset]::Now.ToString('o')
    scope = '변경 사항 설명'
    checks = @('실제로 실행한 검사와 결과')
    limits = @('검증하지 않은 범위')
}
./scripts/write-verification.ps1 -Name ('explorer-' + [Guid]::NewGuid().ToString('N')) -Data $record
```

`-Name`에는 영문·숫자·점·밑줄·하이픈을 사용합니다. 예를 들어 `explorer-20260929`를 지정하면 `doc/Portway.Document/jobs/verification-explorer-20260929.json`이 생성됩니다. 스크립트는 기존 파일을 덮어쓰지 않습니다. 실제 실행 결과를 확인한 뒤 기록하며 비밀번호·인증 토큰·인증 URL은 넣지 않습니다.

기존 버전별 검증 JSON과 이후 생성되는 검증 기록은 모두 `doc/Portway.Document/jobs/`에 보관합니다. `0.3.8` 검증 JSON은 기존에 없었습니다.

빌드·테스트 원본 로그, TRX, 스크린샷과 임시 프로필은 각각 기존 `artifacts/`, `tests/Portway.Tests/TestResults/`, `output/playwright/`, `artifacts/qa/`에 보관하고 보고서에서 연결합니다. 인증 토큰이 들어 있는 `artifacts/browser-qa.json`은 공유 기록이 아닙니다.
