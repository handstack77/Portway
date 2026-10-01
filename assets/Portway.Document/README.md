# Portway.Document

Portway의 가이드·이미지·검증 기록을 관리하는 Node.js 콘솔 프로젝트입니다. Visual Studio 프로젝트 파일은 `Portway.Document.esproj`이며 Artifact와 같은 `Microsoft.VisualStudio.JavaScript.Sdk/1.0.5277448`을 사용합니다. Node.js 20 이상을 사용하며 기본 콘솔 진입점에는 외부 의존성이 없습니다.

```text
assets/Portway.Document/
  docs/                  개발·사용·배포·API·구조 가이드
    images/              가이드에서 사용하는 이미지
  jobs/                  버전별·작업별 검증 보고서와 JSON
  src/index.js           Node 콘솔 진입점
  Portway.Document.esproj
  .vscode/launch.json     Node 디버깅 설정
  package.json
```

새 가이드와 이미지는 `docs/`, 검증 보고서와 JSON은 `jobs/`에 추가합니다. Visual Studio에서는 두 디렉토리의 파일을 프로젝트 항목으로 표시합니다. JSON 생성은 저장소 루트에서 `scripts/write-verification.ps1`을 사용하며 저장 경로는 `assets/Portway.Document/jobs/`입니다.

저장소 루트에서 실행합니다.

```powershell
npm ci --prefix assets/Portway.Document
npm start --prefix assets/Portway.Document
npm run dev --prefix assets/Portway.Document
npm run build --prefix assets/Portway.Document
```

`start`는 기본 진입점을 실행하고 `dev`는 파일 변경 시 다시 실행합니다. `build`는 현재 JavaScript 진입점의 구문을 검사합니다. 문서 생성 기능을 추가하면 빌드 명령도 해당 도구에 맞게 확장합니다.

Visual Studio에서는 `Portway.slnx`의 `assets` 솔루션 폴더 아래에서 프로젝트를 찾을 수 있습니다. `.esproj`를 지원하는 JavaScript·TypeScript 프로젝트 도구를 사용하며, 시작 프로젝트와 `Portway.Document` Node 실행 프로필을 선택해 `src/index.js`를 디버깅하도록 설정했습니다. 실제 Visual Studio 실행은 검증 기록의 확인 범위를 참고하세요. CLI의 프로젝트·솔루션 빌드에서는 `npm run build`로 구문 검사를 실행하고 `dotnet run --project assets/Portway.Document/Portway.Document.esproj`로 콘솔 진입점을 실행할 수 있습니다.

자동 npm 설치·감사는 비활성화하고 기존 lockfile과 npm 명령을 유지합니다. `src/`, `docs/`, `jobs/`, 패키지 파일과 디버깅 설정은 프로젝트에 명시적으로 등록합니다. 문서 프로젝트는 .NET 테스트 대상이 아닙니다. 이전 `.njsproj`와 Node.js Tools 전용 `Build.targets`는 사용하지 않습니다.

가이드는 [개발자 가이드](docs/DEVELOPER-GUIDE.md), [사용자 가이드](docs/USER-GUIDE.md), [배포 운영](docs/DEPLOYMENT.md), [소스 탐색 가이드](docs/SOURCE-MAP.md)에서 시작합니다. 검증 기록 작성 방법은 [jobs 보관 규칙](jobs/README.md)을 참고하세요. 빌드·테스트 원본 로그, TRX와 임시 프로필은 저장소의 기존 산출물 경로에 보관하고 보고서에서 연결합니다.

기획·관리 검토용 [현재 구현 복원 문서](docs/reverse-engineering/README.md)는 기획서·분석서·설계서·아키텍처와 요구사항 추적표를 제공합니다. 현재 소스에서 확인한 동작과 제품 의도에 대한 추론, 기존 실행 기록과 이번 문서 검증을 구분합니다.
