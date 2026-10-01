# 외부 파일 드롭과 디자인 시스템

Portway 0.3.1은 파일 관리자에서 원격 패널로 파일·폴더를 드롭하는 업로드를 지원합니다. 전송 대상은 드롭 시점에 선택된 서버와 현재 원격 폴더입니다. 왼쪽 패널에 같은 로컬 경로를 미리 열 필요가 없습니다.

## 사용 흐름

1. 원격 서버에 연결합니다.
2. 탐색기·Finder·Linux 파일 관리자에서 파일이나 폴더를 오른쪽 원격 패널로 드래그합니다. 대상 영역에 업로드 표시가 나타납니다.
3. 파일·폴더 수, 크기, 대상 경로와 충돌 정책을 확인하고 **전송 시작**을 누릅니다. 건너뛰기·덮어쓰기·더 최신 파일만·다른 이름으로 보관을 선택할 수 있습니다.
4. 준비 진행률이 표시됩니다. 준비 중 취소하면 수신 중인 요청을 중단하고 임시 사본을 정리합니다.
5. 준비 완료 후 기존 전송 큐가 실제 업로드를 수행합니다. 큐의 일시정지·재시도·재시작 복원 기능을 사용할 수 있습니다.

원본 파일은 유지합니다. **외부 파일**, **외부 폴더** 버튼으로 선택할 수도 있습니다. 폴더 선택의 `webkitdirectory`는 파일 목록만 제공하므로 빈 폴더는 전달되지 않을 수 있습니다. 빈 폴더까지 보존하려면 디렉토리 엔트리를 제공하는 드롭을 사용합니다. 앱에서 외부 파일 관리자로 끌어내는 드래그아웃은 이번 구현에 포함하지 않습니다.

## 구현 경계

- `wwwroot/drop.js`: 이벤트가 끝나기 전에 `DataTransferItem`의 엔트리와 파일을 확보합니다. `webkitGetAsEntry`로 디렉토리를 순회하고 `readEntries`가 빈 배열을 반환할 때까지 반복합니다. Chromium의 100개 단위 배치 때문에 파일이 빠지는 문제를 방지합니다.
- `DropStore.cs`: `/api/drops`에서 메타데이터를 검사하고 개인 프로필의 `drops/<GUID>/files`에 사본을 만듭니다. 각 파일은 최대 8 MiB 요청으로 전송되며 서버는 64 KiB 버퍼로 디스크에 씁니다. 전체 파일을 메모리나 Base64 JSON에 올리지 않습니다.
- 청크는 예상 오프셋·정확한 크기와 일치해야 합니다. 수신이 끊기거나 크기가 넘치면 해당 청크 이전 길이로 되돌립니다. 모든 파일의 크기가 맞아야 큐에 등록됩니다.
- 커밋은 같은 스테이징 경로의 기존 작업을 확인해 중복 등록을 방지합니다. 잘못된 연결로 큐 등록이 실패하면 서버 전송은 시작되지 않습니다.
- 큐에 등록된 임시 원본은 실패·취소·일시정지 상태에서도 재시도할 수 있게 남깁니다. 완료된 사본은 주기적으로 정리하며, 참조되지 않고 24시간 지난 준비 데이터도 정리합니다. 실패 기록을 정리한 경우에도 정리 주기에 따라 사본을 제거합니다.
- 사용자가 제공한 파일 이름으로 프로필 밖에 쓸 수 없습니다. 상위 경로, 절대 경로, Windows 장치 이름, 대소문자 중복, 제어 문자와 사본 내 링크를 거부합니다. 로컬 접근 권한이 같은 별도 프로세스와의 원자적 파일 시스템 격리까지 제공하는 것은 아닙니다.

한 번의 드롭은 총 20,000항목, 최상위 10,000항목, 깊이 64단계, 10 TiB 이하입니다. 실제 사용 가능 디스크 공간도 검사합니다. Windows에서 표현할 수 없는 이름은 다른 OS에서도 조용히 바꾸지 않고 명시적으로 거부합니다. 브라우저가 읽을 수 없는 가상 파일·폴더는 지원되지 않을 수 있습니다. 스테이징 사본에는 원래 OS의 ACL/xattr/소유권 정보가 전달되지 않습니다. 원래 파일이 나중에 변경돼도 이미 준비된 사본을 업로드합니다.

## 테마와 자산

| 역할 | 고정 버전 / 위치 |
|---|---|
| 기본 컴포넌트·색상 | Tabler CSS 1.4.0 |
| 아이콘 | Tabler Icons webfont 3.48.0, 로컬 WOFF2/WOFF/TTF |
| 레이아웃 유틸리티 | Master CSS 1.37.8의 정적 renderer |
| 공통 색상 토큰 | Desktop `wwwroot/design-tokens.css` |
| 테마 적용 | Desktop `wwwroot/theme.js` |
| 자산 재생성 | `assets/Portway.Artifact/assets/frontend/build.cjs` |

Tabler 1.4 계열을 사용해 최신 WebKit 전용 CSS 기능에 대한 의존성을 늘리지 않았습니다. 업데이트는 실제 지원 WebView에서 검증한 뒤 진행합니다. Master CSS는 빌드할 때 완전한 클래스 문자열을 스캔해 `vendor/master/master.css`를 생성합니다. 앱 실행에 Node.js나 Master 런타임, CDN 접속이 필요하지 않습니다.

`data-bs-theme="light|dark"`를 공통 기준으로 사용합니다. 0.3.7부터 시스템 선택지를 제거했습니다. 최초 실행 또는 기존 `system` 값만 시작 시 `prefers-color-scheme`으로 한 번 해석하고 라이트/다크로 저장합니다. OS 테마 변경은 추적하지 않습니다. 다크 배경은 Visual Studio 2026 톤의 중성 회색 계층을 사용합니다. Desktop 설정은 `/api/preferences`에 영구 저장하고, 페이지의 첫 렌더를 위해 현재 웹 origin의 localStorage에도 캐시합니다. 배포 센터의 테마는 해당 웹 origin의 localStorage에 저장합니다. 앱과 배포 센터는 같은 토큰·CSS·웹 폰트를 사용하며, 네이티브 창 프레임 색상은 OS/Photino 정책을 따릅니다.

```powershell
npm ci --prefix assets/Portway.Artifact/assets/frontend
npm run build --prefix assets/Portway.Artifact/assets/frontend
npm test --prefix assets/Portway.Artifact/assets/frontend
```

Desktop의 `theme.js`, `design-tokens.css`, `logo.svg`, 파비콘(`favicon.svg`, `favicon.ico`, `apple-touch-icon.png`)과 공통 vendor 자산은 빌드 시 Server의 `wwwroot`로 복사됩니다. 서버 쪽 복사본을 직접 수정하지 않습니다. 새 Master CSS 클래스를 추가했으면 자산을 다시 빌드합니다. 라이선스 원문도 패키지의 vendor 디렉토리에 포함됩니다.

## 검증 범위

Windows Chromium에서 실제 파일 경로를 CDP 드롭 입력으로 전달하여 중첩 폴더·빈 폴더·한글 파일·105개 디렉토리 항목·9 MiB 다중 청크·0바이트 파일을 실제 SFTP 서버로 업로드했습니다. 파일 해시, 빈 폴더와 전체 항목 수를 확인했습니다. 수신 중 취소와 임시 사본 정리, 두 테마, 시스템 모드, 새로고침 후 복원, 980×680 레이아웃도 확인했습니다.

CDP 드롭은 파일을 메모리로 만든 단순 합성 이벤트보다 실제 파일 드롭 경로에 가깝지만, **실제 Explorer/Finder/WebKitGTK 창 사이의 OS 드래그 제스처 검증을 대신하지 않습니다.** 특히 macOS Finder 실기기 검증은 아직 수행하지 않았습니다. 세부 결과는 [0.3.1 검증 보고서](../jobs/VALIDATION-0.3.1.md)에 기록합니다.

참고한 공식 문서: [Tabler 테마](https://tabler.io/guides/bootstrap-5-dark-mode-dashboard), [Tabler Icons 웹 폰트](https://docs.tabler.io/icons/libraries/webfont), [Master CSS v1 정적 렌더링](https://css.master.co/docs/setup/nextjs), [Photino](https://github.com/tryphotino/photino.NET).
