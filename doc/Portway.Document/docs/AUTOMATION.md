# Portway 자동화

`Portway.Core`의 `Portway.Core.Automation.Session`은 UI 없이 같은 전송 엔진을 사용하는 .NET 10 API입니다. 별도 WinSCP 실행 파일이 필요하지 않습니다. 설치 패키지의 `cli/portway-cli`도 같은 API를 사용합니다.

```csharp
await using var session = new Portway.Core.Automation.Session();
await session.Open(new Portway.Core.Site {
    Host = "server.example", Username = "user",
    Password = Environment.GetEnvironmentVariable("SFTP_PASSWORD"),
    Fingerprint = "SHA256:서버에서확인한지문"
});
session.Progress += (path, progress) => Console.WriteLine($"{path}: {progress.Bytes}");
await session.PutFiles(["report.csv"], "/incoming",
    new Portway.Core.TransferOptions { VerifyChecksum = true });
```

스크립트 실행:

```powershell
dotnet run --project src/Portway.Cli -- --script upload.txt
# 패키지에서는: ./cli/portway-cli --script upload.txt
```

`upload.txt`:

```text
option batch abort
option confirm off
option failonnomatch on
open sftp://user@server.example/ -hostkey=SHA256:확인한지문 -passwordenv=SFTP_PASSWORD
lcd "C:\reports"
cd /incoming
put -verify -filemask="*.csv|*.bak" "*.csv" /incoming
checksum sha256 /incoming/report.csv
exit
```

`open`은 사이트 JSON 파일도 읽습니다. JSON 형식은 `Portway.Core.Site`의 camelCase 속성명이며 프록시·점프·키·인증서·암호화 키 설정을 함께 전달할 수 있습니다. 파일과 환경 변수의 비밀 관리 책임은 호출자에게 있습니다. 스크립트 실행기는 원문 명령이나 암호를 로그에 출력하지 않습니다.

| 명령 | 동작 |
|---|---|
| open, close, session | 연결 열기/닫기, 이름으로 세션 선택 |
| pwd, cd, lpwd, lcd, ls, lls | 경로와 목록 |
| put, get | 재귀 전송, 파일 와일드카드, 마스크, ASCII/자동/바이너리, 권한, 시간 보존, 검증, 이동 |
| mkdir, rm, rmdir, mv, cp, ln, chmod | 파일 관리, 원격 복사, 링크, 재귀 권한 |
| checksum, call | 해시와 SSH 명령. 실패한 명령은 오류로 처리 |
| synchronize | remote/local/both 방향, 비교 기준, 선택적 삭제 |
| keepuptodate | 주기적인 로컬→원격 동기화, Ctrl+C로 중단 |
| option, echo, exit | batch abort/continue, confirm off, failonnomatch, 출력, 종료 |

큰따옴표로 공백 경로를 감싸며 큰따옴표 자체는 `""`로 표현합니다. 역슬래시는 Windows 경로 문자로 유지합니다. 와일드카드는 현재 단계의 일반 파일에 적용되며 폴더 경로를 직접 지정하면 재귀 전송합니다. `put/get`의 마지막 인수는 **대상 디렉터리**입니다. 대상 파일명 패턴, WinSCP raw settings, XML 로그, COM 및 WinSCPnet.dll의 바이너리 호환은 제공하지 않습니다. 미지원 명령/스위치는 무시하지 않고 실패합니다. 종료 코드 0은 성공, 1은 오류, 130은 취소입니다. `batch continue`도 한 번이라도 오류가 나면 최종 코드는 1입니다.

SSH 사용자 명령은 설정의 JSON 배열로 등록합니다. 예: `[{"name":"Hash","template":"sha256sum -- {files}","remote":true}]`. `{file}`, `{files}`, `{directory}`를 따옴표로 추가 감싸지 마세요. 엔진이 각 경로를 POSIX 셸 인수로 인용하고 한 번만 치환합니다. 서버 셸 자체는 사용자가 등록한 명령을 그대로 실행합니다.

참조 기준: [WinSCP 스크립트 문서](https://winscp.net/eng/docs/scripting). 문법이 겹치는 부분은 있지만 전체 스크립트 호환 제품이라는 의미는 아닙니다.
