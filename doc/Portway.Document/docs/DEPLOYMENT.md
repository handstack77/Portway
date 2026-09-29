# 배포 운영

개발 실행·게시 API·패키징의 전체 흐름은 [개발자 가이드](DEVELOPER-GUIDE.md), 앱에서 업데이트를 받는 방법은 [사용자 가이드](USER-GUIDE.md#maintenance)를 참고하세요.

## 공개 서버

Linux Docker 호스트의 DNS A/AAAA 레코드를 배포 도메인으로 연결하고 TCP 80/443을 열어 둡니다. Caddy가 HTTPS 인증서를 발급하고 갱신합니다.

```bash
export PORTWAY_DOMAIN=downloads.example.com
export PORTWAY_PUBLISH_KEY="$(openssl rand -hex 32)"
docker compose -f compose.production.yaml up -d --build
```

키는 예시 값을 그대로 사용하지 말고 비밀 관리 도구에 보관하세요. `releases`, `caddy_data`, `caddy_config` 볼륨을 유지합니다. 릴리스 데이터와 별도로 프로덕션 구성을 백업하세요. 키 회전은 환경 변수 변경 후 distribution 컨테이너를 재생성하면 됩니다.

원하는 OS에서 `scripts/build.ps1`을 실행한 뒤 `scripts/publish.ps1`으로 묶음을 게시합니다. 서버 웹사이트에서도 동일한 ZIP과 관리자 키로 게시할 수 있습니다. 다운로드에는 인증이 필요하지 않습니다. 키는 브라우저 저장소에 저장하지 않습니다.

서버는 다음을 검사합니다:

- 채널 이름 `win|osx|linux` + `x64|arm64` + `stable|beta`
- ZIP 경로 순회, 중복 파일명, 심볼릭 링크, 파일 형식/개수/압축 해제 크기
- `Portway` 패키지 ID, 피드의 패키지 파일 크기와 SHA-1/SHA-256
- 기존 버전 nupkg의 불변성

패키지를 먼저 저장하고 기존 피드와 수신 피드를 버전·형식별로 합친 뒤 피드 파일을 원자적으로 교체합니다. 업로드 ZIP에는 해당 버전만 포함하면 됩니다. 서버는 이전 Full/Delta 목록을 보존하므로 여러 버전을 건너뛰는 변경분 연결도 유지합니다. JSON 피드와 `RELEASES-<채널>` 목록을 함께 갱신합니다. 단일 서버 프로세스에서 채널별 업로드를 직렬화하며 여러 서버 인스턴스가 같은 볼륨에 쓰는 구성은 지원하지 않습니다.

새 변경분에는 `BaseVersion`을 기록합니다. 기준 Full 버전을 먼저 게시하고 현재 서버의 최신 버전을 기준으로 생성해야 합니다. 기준이 없거나 오래된 기준으로 만든 새 변경분은 게시를 거부합니다. 이전 방식의 누적 ZIP도 받을 수 있습니다. 과거 버전을 뒤늦게 게시해도 최신 설치 파일을 이전 것으로 교체하지 않습니다. 같은 버전의 패키지 파일·해시·크기 변경은 거부합니다.

빌드 산출물은 `dist/releases/<RID>-<track>/<version>/`에 생성되며 ZIP에 이전 버전 패키지가 섞이지 않습니다. 서버는 업로드 ZIP 자체를 영구 보관하지 않고 추출한 파일을 서비스합니다. 전체 패키지는 신규 설치와 변경분 실패 시 복구에 필요합니다. 과거 패키지는 자동 삭제하지 않습니다. 운영 보존 정책을 적용할 때는 사용하는 변경분의 기준 Full과 중간 변경분도 함께 보존하세요.

배포 키는 패키지를 게시할 수 있는 관리자 자격 증명입니다. 클라이언트 신뢰는 HTTPS, 서버 접근 통제, OS 코드 서명에 의존합니다. 별도의 오프라인 릴리스 서명 검증 시스템은 구현되어 있지 않습니다.

## GitHub Actions

`Package release`를 수동 실행해 버전, stable/beta, 게시 여부를 선택합니다. 게시를 켜려면 다음을 설정합니다:

| 구분 | 이름 | 용도 |
|---|---|---|
| Repository variable | `PORTWAY_DISTRIBUTION_URL` | HTTPS 배포 서버 루트 |
| Repository secret | `PORTWAY_PUBLISH_KEY` | 서버와 동일한 키 |
| Repository secret | `PORTWAY_SIGN_PARAMS` | Windows signtool 옵션; 인증서 준비는 별도 |

`PORTWAY_DISTRIBUTION_URL`이 설정되어 있으면 CI는 해당 RID/채널의 최신 Full을 내려받아 변경분을 생성합니다. 최초 채널을 만들 때만 `first_release`를 켜서 없는 피드를 허용하세요. `delta_mode`는 `BestSpeed` 또는 `BestSize`를 선택합니다. 서버 URL이 없는 CI는 최초 전체 패키지만 만들 수 있습니다.

워크플로 파일은 준비되어 있지만 이 작업에서 실제 원격 CI 실행이나 공개 게시를 수행하지 않았습니다. OS/아키텍처별 runner 이용 가능 여부는 저장소 요금제에 따라 확인하세요.

## macOS 서명/공증

로컬 Mac에서는 Developer ID Application/Installer 인증서를 키체인에 설치하고 `xcrun notarytool store-credentials`로 공증 프로필을 등록합니다. 다음 환경 변수를 설정하면 `build.ps1`이 vpk 서명 옵션을 전달합니다:

```text
PORTWAY_MAC_APP_IDENTITY         Developer ID Application 인증서 이름
PORTWAY_MAC_INSTALLER_IDENTITY   Developer ID Installer 인증서 이름
PORTWAY_MAC_NOTARY_PROFILE       notarytool 프로필 이름
PORTWAY_MAC_KEYCHAIN             선택: 키체인 파일 절대 경로
```

CI 자동 설치는 `scripts/macos-signing.sh`를 사용합니다. Repository variable `PORTWAY_MAC_SIGNING_ENABLED=true`와 아래 Secrets를 설정합니다:

- 위의 APP_IDENTITY, INSTALLER_IDENTITY, NOTARY_PROFILE
- `PORTWAY_MAC_APP_P12`, `PORTWAY_MAC_INSTALLER_P12`: 각각 P12 파일의 base64 내용
- `PORTWAY_MAC_P12_PASSWORD`: 두 인증서의 내보내기 암호
- `PORTWAY_MAC_KEYCHAIN_PASSWORD`: 임시 키체인용 임의 암호
- `PORTWAY_APPLE_ID`, `PORTWAY_APPLE_TEAM_ID`, `PORTWAY_APPLE_APP_PASSWORD`: 공증 계정 및 앱 전용 암호

각 job은 일회용 키체인을 만들고 작업 마지막에 제거합니다. 공증 설정이 없으면 macOS의 공개 게시 job은 실패합니다. 게시하지 않는 개발 패키지는 서명 없이 만들 수 있습니다. 실제 Apple 인증서는 이 저장소에 포함하지 않습니다.

## 업데이트 확인

0.3.3부터 피드 URL을 저장한 설치본은 시작할 때 백그라운드에서 새 버전을 확인·다운로드합니다. 실행 중에는 버전을 바꾸지 않고 다음 시작의 Velopack 초기화에서 준비된 패키지를 무인 적용합니다. 피드가 없으면 자동 확인을 생략하고, 네트워크 오류는 앱 실행을 막지 않습니다. 수동 확인·다운로드·즉시 재시작도 유지합니다.

1. 검증할 이전 버전의 패키지를 정상 설치합니다.
2. 같은 RID/채널로 더 높은 새 버전을 빌드하고 서버에 게시합니다.
3. 앱 설정의 업데이트 URL을 해당 채널 디렉터리로 지정합니다.
4. 업데이트 확인 → 다운로드 → 재시작을 수행합니다.
5. 버전 표시, 저장된 연결/설정 보존, 설치/제거를 확인합니다.

설치본의 Velopack UpdateManager는 사용할 수 있는 변경분 연결을 선택해 새 Full 패키지를 복원하고 검증합니다. 기준 패키지가 없거나 변경분이 손상되었거나 변경분 다운로드가 더 비효율적이면 Full 다운로드로 전환합니다. 최초 macOS/Linux 업데이트 등 로컬 Full이 없는 경우에는 전체 다운로드가 필요할 수 있습니다. [Velopack 변경분 문서](https://docs.velopack.io/packaging/deltas)를 참고하세요. 시작 시 백그라운드 준비와 다음 시작의 적용 방식은 동일합니다.

자동 경로는 업데이트 버튼이나 동작 API를 호출하지 않고 별도로 검증합니다. 자동 기능이 있는 이전 버전에 URL을 저장하고 실행하여 다운로드 완료를 기다린 뒤, 현재 버전이 유지되는지 확인합니다. 앱을 종료하고 피드 서버를 끈 상태로 다시 실행해서 새 버전 적용과 프로필 보존을 확인합니다. Windows에서는 [test-automatic-update.ps1](../../../scripts/test-automatic-update.ps1)이 이 흐름을 격리 설치로 검사합니다.

이 설치→버전 변경→재시작 전체 흐름은 사용자 대상 배포 전에 실제 OS별로 수행해야 합니다. 0.3.1에서는 Windows 설치→업데이트→새 Photino 창 실행→제거와 패키지 업로드·피드·해시 일치 다운로드를 검증했습니다. macOS 네이티브 설치·업데이트와 Linux의 전체 설치 업데이트 흐름은 아직 검증하지 않았습니다. 세부 환경과 근거는 [0.3.1 검증 결과](../jobs/VALIDATION-0.3.1.md)를 참고하세요.

0.3.3의 자동 준비·오프라인 다음 시작 적용, 수동 업데이트 회귀와 패키지 검증은 [0.3.3 검증 결과](../jobs/VALIDATION-0.3.3.md)에 기록했습니다. 0.3.4는 이 검증에만 사용한 대상 버전이며 공개 릴리스로 게시하지 않았습니다.
