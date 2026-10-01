# Portway 최신 검증 결과

2026-10-01 doc 루트·솔루션 폴더 변경: `doc/` 전체를 `assets/`로 이동하고 솔루션 폴더명·프로젝트·빌드·패키징·문서·기록 저장 경로를 갱신했습니다. Windows 격리 Release 빌드는 경고·오류 0개, .NET 테스트는 96개 통과·25개 조건부 건너뜀, 프런트엔드는 31개 통과입니다. 로컬 링크 431개와 기존 기록의 내용 보존을 확인했고 실제 Windows QA 패키지 생성·ZIP API 검사 1개·웹 자산 287개 내용 검사가 통과했습니다. 서명·실제 설치·업데이트 적용·Visual Studio 화면과 macOS·Linux 패키징은 미검증입니다. 자세한 결과는 [doc → assets 전환 보고서](VALIDATION-doc-to-assets-20261001-ee4d99db.md)에 있습니다. 아래 기록의 doc 경로는 당시 이력으로 유지합니다.

2026-10-01 Document 프로젝트 전환: `.njsproj`를 Artifact와 같은 JavaScript SDK의 `Portway.Document.esproj`로 바꾸고 솔루션 등록·npm 빌드·시작 명령·폴더 표시·Node 디버깅 설정을 갱신했습니다. 격리 Release 빌드는 경고·오류 0개, 솔루션 테스트는 96개 통과·25개 조건부 건너뜀입니다. 새 .esproj의 CLI 콘솔 실행과 문서·검증 기록 항목 등록을 확인했습니다. 실제 Visual Studio F5 실행은 미검증이며 자세한 결과는 [Document .esproj 전환 보고서](VALIDATION-document-esproj-20261001-c73efc37.md)에 있습니다.

2026-10-01 Artifact 자산 표시 수정: 자동 항목 등록을 끈 프로젝트에 빠져 있던 내부 assets 폴더·파일 등록을 명시했습니다. MSBuild에서 폴더 1개와 표시 파일 14개, node_modules 제외·외부 링크 보존을 확인했고 격리 Release 빌드는 경고·오류 없이 통과했습니다. 실제 Visual Studio 화면은 미검증입니다. 자세한 결과는 [assets 표시 설정 보고서](VALIDATION-artifact-assets-visible-20261001-75bb4580.md)에 있습니다.

2026-10-01 자산 경로 변경: 루트 `assets/`를 `doc/Portway.Artifact/assets/`로 이동하고 프로젝트·프런트엔드·빌드·패키징·아이콘 생성·문서 경로를 갱신했습니다. Windows 격리 Release 빌드는 경고·오류 0개, .NET 테스트는 96개 통과·25개 조건부 건너뜀, 프런트엔드는 31개 통과입니다. 새 경로에서 실제 Windows QA 패키지를 생성했고 실제 ZIP API 검사 1개 및 웹 자산 287개의 내용 일치 검사가 통과했습니다. 서명·실제 설치·업데이트 적용과 macOS·Linux 실제 패키징은 미검증입니다. 자세한 명령·범위는 [자산 이동 보고서](VALIDATION-assets-relocation-20261001-5d5ed5c8.md)에 있습니다. 아래 이전 기록의 경로는 당시 이력입니다.

2026-09-30 표기 변경: 코드·화면·문서의 해당 한국어 용어를 `Vault`로 교체했습니다. Windows Release 소스에서 `dotnet test Portway.slnx -c Release --no-restore`는 96개 통과·25개 조건부 건너뜀, `npm test --prefix assets/frontend`는 31개 통과, 프런트엔드 포맷 검사와 문서 빌드가 통과했습니다. 격리된 headless 호스트와 Chromium에서 설정 및 사이트 팝업의 새 표기를 라이트/다크 테마와 980×680 화면에서 확인했습니다. 설치 패키지와 macOS·Linux 네이티브 UI는 이 표기 변경에서 재검증하지 않았습니다.

현재 개발 소스의 Vault 자동 잠금 해제와 Windows 격리 프로필 재시작 검사는 [Vault 자동 잠금 해제 보고서](VALIDATION-vault-auto-unlock-20260930.md)에 기록했습니다. macOS·Linux OS 비밀 저장소와 실제 원격 서버 연결은 해당 변경에서 검증하지 않았습니다.

문서·검증 기록을 `doc/Portway.Document` 아래로 옮긴 결과는 [문서 저장 경로 검증](VALIDATION-document-storage.md)에 있습니다. 기존 58개 파일의 이동·해시 보존, 링크와 프로젝트 등록, 새 경로의 검증 JSON 생성 및 기존 기록 보호를 확인했습니다.

버전별 ZIP과 변경분 업데이트의 최신 검사는 [패키징 검증 보고서](VALIDATION-versioned-packaging.md)에 있습니다. 게시·업데이트 계약 19개와 실제 패키지 검사 3개가 통과했습니다. 아래 수치는 이전 작업의 검증 기록입니다.

현재 개발 소스의 코드 분리·포맷·계약 검증과 실제 Windows UI 결과는 [리팩토링 보고서](REFACTORING.md)에 있습니다. .NET 89개·프런트엔드 31개 통과이며 .NET 조건부 검사 24개는 건너뛰었습니다. 아래 0.3.9 수치와 화면은 해당 릴리스의 기존 기록입니다.

현재 소스와 Windows 릴리스는 **0.3.9**입니다. 로고 정렬, Folded Hover 메뉴, 체크박스 옆 말줄임표 수정은 [0.3.9 보고서](VALIDATION-0.3.9.md)에 기록했습니다.

64px 아이콘 메뉴의 hover·키보드 진입에 따른 260px 펼침과 작업 영역 고정, 저장 사이트 18개의 스크롤·편집, 체크박스의 개별·전체·부분 선택을 확인했습니다. 라이트·다크, 1440×940/980×680, 모션 줄이기, 터치 에뮬레이션의 전체 메뉴도 검증했습니다. 기존 글자·아이콘 크기를 유지합니다.

프런트엔드 21개, .NET 기본 58개 통과·23개 조건부 건너뜀, 실제 배포 ZIP 검증 1개 통과입니다. 게시 웹 자산 280개가 소스와 일치합니다. Windows Chromium/WebKit에서 확인했으며 macOS/Linux Photino 네이티브 실행과 전체 프로토콜·설치·무인 업데이트는 이번에 반복하지 않았습니다.

이전 검증: [다크 테마 0.3.7](VALIDATION-0.3.7.md), [글꼴·크기 0.3.6](VALIDATION-0.3.6.md), [다중 선택·파일 관리 0.3.5](VALIDATION-0.3.5.md), [Monaco·팝업 0.3.4](VALIDATION-0.3.4.md), [자동 업데이트 0.3.3](VALIDATION-0.3.3.md), [프로토콜·드롭 0.3.1](VALIDATION-0.3.1.md).

지원 범위: [기능별 대체 검증표](../docs/PARITY.md), [개발 지침](../../../AGENTS.md).
