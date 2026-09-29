export function createAdvancedUI({
  api,
  esc,
  field,
  toast,
  confirmBox,
  inputBox,
  state,
}) {
  const query = (root, name) => root.querySelector(`[name="${name}"]`);
  const select = (name, title, value, values) =>
    `<label>${title}<select name="${name}">${values.map(([v, t]) => `<option value="${v}" ${v === value ? "selected" : ""}>${t}</option>`).join("")}</select></label>`;
  const check = (name, title, value) =>
    `<label class="check"><input type="checkbox" name="${name}" ${value ? "checked" : ""}>${title}</label>`;
  function attachSite(dialog, site, readSite) {
    const proxy = site.proxy || {},
      jump = site.jump || {};
    const html = `<details><summary>연결 · 인증 · 프록시</summary><div class="grid2">
      ${select(
        "authentication",
        "SSH 인증",
        site.authentication || "automatic",
        [
          ["automatic", "자동 · 개인 키 또는 암호"],
          ["password", "암호"],
          ["key", "개인 키 / PPK"],
          ["agent", "OpenSSH Agent"],
          ["pageant", "PuTTY Pageant · Windows"],
          ["keyboard", "대화형 인증 / MFA"],
        ],
      )}
      ${field("certificatePath", "SSH 사용자 인증서 파일", site.certificatePath)}${field("agentSocket", "Agent 소켓 / 파이프 (빈칸 = 기본)", site.agentSocket)}
      ${field("timeoutSeconds", "연결 제한 시간 (초)", site.timeoutSeconds || 30, "number")}${field("keepAliveSeconds", "연결 유지 간격 (초, 0 = 해제)", site.keepAliveSeconds ?? 20, "number")}
      ${field("filenameEncoding", "파일 이름 인코딩", site.filenameEncoding || "utf-8")}
      ${select(
        "ftpDataMode",
        "FTP 데이터 연결",
        site.ftpDataMode || "passive",
        [
          ["passive", "자동 수동 모드"],
          ["active", "자동 능동 모드"],
          ["epsv", "EPSV"],
          ["pasv", "PASV"],
        ],
      )}
      ${select("proxyType", "프록시", proxy.type || "none", [
        ["none", "사용 안 함"],
        ["http", "HTTP CONNECT"],
        ["socks4", "SOCKS4"],
        ["socks5", "SOCKS5"],
      ])}
      ${field("proxyHost", "프록시 호스트", proxy.host)}${field("proxyPort", "프록시 포트", proxy.port || 8080, "number")}${field("proxyUsername", "프록시 사용자", proxy.username)}${field("proxyPassword", "프록시 암호 (저장값 유지: 빈칸)", "", "password")}</div></details>
      <details ${site.jump ? "open" : ""}><summary>SSH 점프 서버</summary>${check("jumpEnabled", "점프 서버를 통해 연결", !!site.jump)}<div class="grid2">
      ${field("jumpHost", "점프 호스트", jump.host)}${field("jumpPort", "SSH 포트", jump.port || 22, "number")}${field("jumpUsername", "점프 사용자", jump.username)}${field("jumpPassword", "점프 암호", "", "password")}${field("jumpPrivateKey", "점프 개인 키 / PPK", jump.privateKeyPath)}${field("jumpPassphrase", "점프 키 암호", "", "password")}
      ${select(
        "jumpAuthentication",
        "점프 인증",
        jump.authentication || "automatic",
        [
          ["automatic", "키 또는 암호"],
          ["agent", "OpenSSH Agent"],
          ["pageant", "Pageant"],
          ["keyboard", "대화형 / MFA"],
        ],
      )}
      ${field("jumpFingerprint", "점프 호스트 키 · SHA256", jump.fingerprint)}</div><button type="button" id="scan-jump">점프 서버 지문 확인</button></details>
      <details><summary>SFTP 파일 암호화 · WinSCP 호환</summary>${check("encryptFiles", "파일 이름과 내용을 암호화", site.encryptFiles || !!site.encryptionKey)}${field("encryptionKey", "64자리 16진수 키 (저장값 유지: 빈칸)", "", "password")}<button type="button" id="generate-encryption-key">새 키 생성</button><p class="hint">기존 WinSCP 암호화 키를 사용할 수 있습니다. 키를 별도로 백업하세요. 호환 형식은 AES-CTR이며 변조 인증 태그가 없습니다. 암호화 파일은 중단 후 처음부터 재전송합니다.</p></details>
      <details><summary>TLS 인증서 · S3 · 사이트 메모</summary><div class="grid2">
      ${field("tlsFingerprint", "TLS 인증서 SHA256 (빈칸 = 시스템 신뢰)", site.tlsFingerprint)}<button type="button" id="scan-tls">서버 인증서 확인</button>
      ${field("clientCertificatePath", "TLS 클라이언트 인증서 (PFX/P12)", site.clientCertificatePath)}${field("clientCertificatePassword", "클라이언트 인증서 암호", "", "password")}${field("sessionToken", "S3 세션 토큰", "", "password")}
      ${check("s3PathStyle", "S3 경로 방식 주소", site.s3PathStyle !== false)}${check("s3RequesterPays", "S3 요청자 비용 부담", site.s3RequesterPays)}${field("s3StorageClass", "S3 스토리지 클래스", site.s3StorageClass || "STANDARD")}${field("note", "사이트 메모", site.note)}${field("color", "사이트 색상", site.color || getComputedStyle(document.documentElement).getPropertyValue("--tblr-primary").trim(), "color")}</div></details>`;
    dialog.querySelector("#form-error").insertAdjacentHTML("beforebegin", html);
    dialog.querySelector("#generate-encryption-key").onclick = () => {
      const input = query(dialog, "encryptionKey");
      input.value = Array.from(
        crypto.getRandomValues(new Uint8Array(32)),
        (b) => b.toString(16).padStart(2, "0"),
      ).join("");
      input.type = "text";
      query(dialog, "encryptFiles").checked = true;
    };
    dialog.querySelector("#scan-jump").onclick = async () => {
      try {
        const target = readSite().jump;
        if (!target) throw Error("점프 서버 사용을 선택하세요.");
        const result = await api("/fingerprint", "POST", target);
        if (
          await confirmBox(
            "점프 서버 호스트 키",
            `${target.host}\n${result.fingerprint}\n서버 관리자의 값과 비교하세요.`,
            "이 지문 신뢰",
          )
        )
          query(dialog, "jumpFingerprint").value = result.fingerprint;
      } catch (e) {
        toast(e.message, true);
      }
    };
    dialog.querySelector("#scan-tls").onclick = async () => {
      try {
        const result = await api("/certificate", "POST", readSite());
        if (
          await confirmBox(
            "TLS 서버 인증서",
            `${result.subject}\n발급자: ${result.issuer}\n만료: ${result.notAfter}\n${result.fingerprint}`,
            "이 인증서 고정",
          )
        )
          query(dialog, "tlsFingerprint").value = result.fingerprint;
      } catch (e) {
        toast(e.message, true);
      }
    };
    return (fd) => ({
      authentication: fd.get("authentication"),
      certificatePath: fd.get("certificatePath") || null,
      agentSocket: fd.get("agentSocket") || null,
      encryptFiles: fd.has("encryptFiles"),
      encryptionKey: fd.has("encryptFiles")
        ? fd.get("encryptionKey") || null
        : null,
      timeoutSeconds: Number(fd.get("timeoutSeconds")),
      keepAliveSeconds: Number(fd.get("keepAliveSeconds")),
      filenameEncoding: fd.get("filenameEncoding"),
      ftpDataMode: fd.get("ftpDataMode"),
      proxy: {
        type: fd.get("proxyType"),
        host: fd.get("proxyHost"),
        port: Number(fd.get("proxyPort")),
        username: fd.get("proxyUsername"),
        password: fd.get("proxyPassword") || null,
      },
      jump: !fd.has("jumpEnabled")
        ? null
        : {
            ...jump,
            name: "점프 · " + fd.get("jumpHost"),
            protocol: "sftp",
            host: fd.get("jumpHost"),
            port: Number(fd.get("jumpPort")),
            username: fd.get("jumpUsername"),
            password: fd.get("jumpPassword") || null,
            privateKeyPath: fd.get("jumpPrivateKey") || null,
            passphrase: fd.get("jumpPassphrase") || null,
            fingerprint: fd.get("jumpFingerprint"),
            authentication: fd.get("jumpAuthentication"),
            proxy: { type: "none" },
          },
      tlsFingerprint: fd.get("tlsFingerprint") || null,
      clientCertificatePath: fd.get("clientCertificatePath") || null,
      clientCertificatePassword: fd.get("clientCertificatePassword") || null,
      sessionToken: fd.get("sessionToken") || null,
      s3PathStyle: fd.has("s3PathStyle"),
      s3RequesterPays: fd.has("s3RequesterPays"),
      s3StorageClass: fd.get("s3StorageClass"),
      note: fd.get("note"),
      color: fd.get("color"),
    });
  }
  function optionsFields(o = {}) {
    return `<div class="grid2">${field("opt_fileMask", "파일 마스크 (예: *.txt;*.csv | *.bak;node_modules/)", o.fileMask || "", "text", 'class="span2"')}
    ${select("opt_mode", "전송 모드", o.mode || "binary", [
      ["binary", "바이너리 · 그대로"],
      ["automatic", "자동 · 텍스트 마스크"],
      ["text", "텍스트 · 줄바꿈 변환"],
    ])}
    ${select("opt_newline", "원격 줄바꿈", o.remoteNewline || "lf", [
      ["lf", "LF · Unix"],
      ["crlf", "CRLF · Windows"],
    ])}${field("opt_textMask", "텍스트 파일 마스크", o.textMask || "*.txt;*.csv;*.json;*.xml;*.html;*.css;*.js;*.md;*.sh;*.yml")}
    ${select("opt_nameCase", "이름 대소문자", o.nameCase || "none", [
      ["none", "유지"],
      ["lower", "소문자"],
      ["upper", "대문자"],
    ])}
    ${field("opt_permissions", "업로드 권한 (빈칸 = 기존값)", o.permissions || "")}${field("opt_retries", "연결 오류 자동 재시도", o.maxRetries ?? 2, "number")}
    ${check("opt_timestamp", "수정 시각 보존", o.preserveTimestamp !== false)}${check("opt_readonly", "읽기 전용 보존", o.preserveReadOnly)}${check("opt_hidden", "숨김 파일 제외", o.excludeHidden)}${check("opt_empty", "빈 폴더 제외", o.excludeEmptyDirectories)}${check("opt_newer", "새 파일과 더 최신 파일만", o.newerOnly)}${check("opt_checksum", "전송 후 SHA256 검증", o.verifyChecksum)}${check("opt_resume", "이어받기 사용", o.resume !== false)}${check("opt_move", "전송 성공 후 원본 삭제 (이동)", o.removeSource)}</div>`;
  }
  function readOptions(fd) {
    return {
      fileMask: fd.get("opt_fileMask"),
      mode: fd.get("opt_mode"),
      remoteNewline: fd.get("opt_newline"),
      textMask: fd.get("opt_textMask"),
      nameCase: fd.get("opt_nameCase"),
      permissions: fd.get("opt_permissions") || null,
      maxRetries: Number(fd.get("opt_retries")),
      preserveTimestamp: fd.has("opt_timestamp"),
      preserveReadOnly: fd.has("opt_readonly"),
      excludeHidden: fd.has("opt_hidden"),
      excludeEmptyDirectories: fd.has("opt_empty"),
      newerOnly: fd.has("opt_newer"),
      verifyChecksum: fd.has("opt_checksum"),
      resume: fd.has("opt_resume"),
      removeSource: fd.has("opt_move"),
    };
  }
  function attachTransfer(dialog, scheduling = true) {
    const presets = state.prefs.presets || [];
    const html = `<details><summary>전송 필터 · 검증 · 프리셋</summary><div class="inline-field">${select("opt_preset", "저장된 프리셋", "", [["", "현재 설정"], ...presets.map((p, i) => [String(i), p.name])])}<button type="button" id="save-preset">현재 설정을 프리셋으로 저장</button></div><div id="transfer-options">${optionsFields(state.prefs.transferDefaults || {})}</div>${scheduling ? `<div class="grid2">${field("priority", "우선순위 (-10 ~ 10)", 0, "number")}${field("scheduledAt", "예약 실행 (비우면 즉시)", "", "datetime-local")}</div>` : ""}</details>`;
    dialog.querySelector("#form-error").insertAdjacentHTML("beforebegin", html);
    query(dialog, "opt_preset").onchange = (e) => {
      const p = presets[Number(e.target.value)];
      if (e.target.value !== "" && p)
        dialog.querySelector("#transfer-options").innerHTML = optionsFields(
          p.options,
        );
    };
    dialog.querySelector("#save-preset").onclick = async () => {
      try {
        const name = await inputBox(
          "전송 프리셋 저장",
          "현재 전송 옵션을 저장해 다음 전송에서 다시 사용할 수 있습니다.",
          "프리셋 이름",
        );
        if (!name) return;
        const prefs = await api("/preferences");
        prefs.presets = [
          ...(prefs.presets || []).filter((p) => p.name !== name),
          {
            name,
            options: readOptions(new FormData(dialog.querySelector("form"))),
            hostPattern: "*",
          },
        ];
        await api("/preferences", "PUT", prefs);
        state.prefs = prefs;
        toast("프리셋을 저장했습니다.");
      } catch (error) {
        toast(error.message, true);
      }
    };
  }
  let authBusy = false,
    authDialog = null,
    authId = null;
  async function pollAuthentication() {
    if (authBusy) return;
    authBusy = true;
    try {
      const requests = await api("/authentication");
      if (authId && !requests.some((r) => r.id === authId)) {
        authDialog?.close();
        authDialog?.remove();
        authDialog = null;
        authId = null;
      }
      if (authDialog || !requests.length) return;
      const request = requests[0];
      authId = request.id;
      const dialog = document.createElement("dialog");
      authDialog = dialog;
      dialog.innerHTML = `<form><div class="modal-head"><h2>서버 인증 · ${esc(request.site)}</h2></div><div class="modal-body"><p class="hint">${esc(request.host)}<br>${esc(request.instruction)}</p>${request.prompts.map((p, i) => field("answer" + i, p.text, "", p.echo ? "text" : "password")).join("")}<p class="form-error"></p></div><div class="modal-foot"><button type="button" id="auth-cancel">취소</button><button class="primary" type="submit">응답 보내기</button></div></form>`;
      document.body.append(dialog);
      dialog.showModal();
      const reply = async (answers) => {
        try {
          await api("/authentication/" + request.id, "POST", { answers });
          dialog.close();
          dialog.remove();
          authDialog = null;
          authId = null;
        } catch (e) {
          dialog.querySelector(".form-error").textContent = e.message;
        }
      };
      dialog.querySelector("form").onsubmit = (e) => {
        e.preventDefault();
        const fd = new FormData(e.target);
        reply(request.prompts.map((_, i) => fd.get("answer" + i)));
      };
      dialog.querySelector("#auth-cancel").onclick = () => reply(null);
      dialog.oncancel = (e) => {
        e.preventDefault();
        reply(null);
      };
    } finally {
      authBusy = false;
    }
  }
  return {
    attachSite,
    attachTransfer,
    readOptions,
    pollAuthentication,
    optionsFields,
  };
}
