import { createAdvancedUI } from "./advanced.js";
import { createWorkflows } from "./workflows.js";
import { createExternalDrop } from "./drop.js";
import { showTextEditor } from "./editor.js";
import { createExplorer } from "./explorer.js";
import { formatElapsed } from "./display-time.js";
import { $, $$, esc, icon, btn, size } from "./ui.js";
import { workspaceMarkup } from "./workspace-view.js";
import { fileListMarkup, fileFooterMarkup } from "./file-list-view.js";
let token =
  location.hash.slice(1) || sessionStorage.getItem("portway-token") || "";
if (token) {
  sessionStorage.setItem("portway-token", token);
  history.replaceState(null, "", location.pathname);
}
const state = {
  sites: [],
  sessions: [],
  current: null,
  focus: "local",
  info: null,
  prefs: {},
  vault: {},
  jobs: [],
  queueFilter: "all",
  queueCollapsed: innerHeight < 800,
  showHidden: false,
  local: { path: "", entries: [], selected: new Set(), filter: "" },
  remote: { path: "", entries: [], selected: new Set(), filter: "" },
};
state.localRight = state.remote;
async function api(path, method = "GET", body) {
  const r = await fetch("/api" + path, {
    method,
    headers: {
      Authorization: "Bearer " + token,
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await r.text();
  let data;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = null;
  }
  if (!r.ok)
    throw Error(
      data?.detail ||
        data?.title ||
        (r.status === 401
          ? "앱 세션이 만료되었습니다. Portway를 다시 시작하세요."
          : "요청 실패: " + r.status),
    );
  return data;
}
function toast(message, error = false) {
  const d = document.createElement("div");
  d.className = "app-toast" + (error ? " error" : "");
  d.setAttribute("role", error ? "alert" : "status");
  d.textContent = message;
  // 모달의 최상위 레이어 안에 표시해 배경의 토스트가 팝업 뒤에 가려지지 않도록 합니다.
  const dialog =
    document.activeElement?.closest("dialog[open]") ||
    $$("dialog[open]").at(-1);
  let stack = dialog ? $(".toast-stack", dialog) : $("#toasts");
  if (!stack) {
    stack = document.createElement("div");
    stack.className = "toast-stack";
    stack.setAttribute("aria-live", "polite");
    dialog.append(stack);
    dialog.addEventListener(
      "close",
      () => {
        $("#toasts").append(...stack.children);
        stack.remove();
      },
      { once: true },
    );
  }
  stack.append(d);
  setTimeout(() => d.remove(), error ? 9000 : 4500);
}
async function run(fn) {
  try {
    return await fn();
  } catch (e) {
    toast(e.message, true);
  }
}
const session = () => state.sessions.find((s) => s.id === state.current);
const isLocal = (side) => side === "local" || !state.current;
const join = (side, p, n) =>
  isLocal(side)
    ? p.replace(/[\\/]$/, "") + state.info.separator + n
    : (p === "/" ? "" : p) + "/" + n;
function modal(title, body, foot = "", cls = "") {
  explorer.closeMenu();
  const previous = $("#dialog");
  if (previous.open) previous.close();
  // 새 노드를 사용해 대기 중인 닫기 이벤트와 정리 작업이 원래 화면에만 연결되도록 합니다.
  const d = document.createElement("dialog");
  d.id = "dialog";
  d.setAttribute("aria-labelledby", "dialog-title");
  previous.replaceWith(d);
  d.className = cls;
  d.innerHTML = `<form id="modal-form"><div class="modal-head"><div class="modal-heading flex ai:center gap:12"><span class="modal-symbol"><i class="ti ti-layout-dashboard" aria-hidden="true"></i></span><h2 id="dialog-title">${esc(title)}</h2></div><button type="button" data-close class="iconbtn" aria-label="닫기">${icon("close")}</button></div><div class="modal-body">${body}<div class="form-error" id="form-error" role="alert"></div></div><div class="modal-foot">${foot || '<button type="button" data-close>닫기</button>'}</div></form>`;
  $$("[data-close]", d).forEach((b) => (b.onclick = () => d.close()));
  d.showModal();
  return d;
}
function bindForm(handler, { onError, defaultSubmitter } = {}) {
  const form = $("#modal-form"),
    error = $("#form-error");
  let busy = false;
  form.onsubmit = async (e) => {
    e.preventDefault();
    if (busy) return;
    busy = true;
    const b = e.submitter || defaultSubmitter;
    if (b) b.disabled = true;
    error.textContent = "";
    try {
      await handler(e);
    } catch (ex) {
      if (onError) onError(ex);
      else error.textContent = ex.message;
    } finally {
      busy = false;
      if (b) b.disabled = false;
    }
  };
}
function confirmBox(title, message, label = "계속", danger = false) {
  return new Promise((resolve) => {
    const d = $("#confirm");
    d.returnValue = "";
    d.setAttribute("aria-labelledby", "confirm-title");
    d.innerHTML = `<form method="dialog"><div class="modal-head"><div class="modal-heading flex ai:center gap:12"><span class="modal-symbol ${danger ? "is-danger" : ""}"><i class="ti ti-${danger ? "alert-triangle" : "shield-check"}" aria-hidden="true"></i></span><h2 id="confirm-title">${esc(title)}</h2></div></div><div class="modal-body"><p class="dialog-message">${esc(message)}</p></div><div class="modal-foot"><button value="cancel" autofocus>취소</button><button value="ok" class="${danger ? "danger" : "primary"}">${esc(label)}</button></div></form>`;
    d.onclose = () => resolve(d.returnValue === "ok");
    d.showModal();
  });
}
function inputBox(title, message, label, value = "") {
  return new Promise((resolve) => {
    const d = document.createElement("dialog");
    d.className = "prompt-dialog";
    d.setAttribute("aria-labelledby", "prompt-title");
    d.innerHTML = `<form><div class="modal-head"><div class="modal-heading flex ai:center gap:12"><span class="modal-symbol"><i class="ti ti-forms" aria-hidden="true"></i></span><h2 id="prompt-title">${esc(title)}</h2></div><button type="button" class="iconbtn" data-cancel aria-label="닫기">${icon("close")}</button></div><div class="modal-body"><p class="dialog-message">${esc(message)}</p>${field("answer", label, value)}</div><div class="modal-foot"><button type="button" data-cancel>취소</button><button type="submit" class="primary">확인</button></div></form>`;
    const input = $("input", d);
    input.required = true;
    let answer = null;
    $("form", d).onsubmit = (e) => {
      e.preventDefault();
      if (input.value.trim()) {
        answer = input.value.trim();
        d.close();
      }
    };
    $$("[data-cancel]", d).forEach((b) => (b.onclick = () => d.close()));
    d.addEventListener(
      "close",
      () => {
        d.remove();
        resolve(answer);
      },
      { once: true },
    );
    document.body.append(d);
    d.showModal();
    input.focus();
    input.select();
  });
}
function shell() {
  $("#app").innerHTML = workspaceMarkup(isLocal);
  document.addEventListener("click", click);
  document.addEventListener("keydown", keyboard);
  for (const side of ["local", "remote"]) wirePane(side);
  externalDrop.wire();
  renderTheme();
  renderSites();
  renderSessions();
  renderJobs();
  renderQueueSize();
  renderWorkspaceActions();
}
function renderQueueSize() {
  const collapsed = state.queueCollapsed;
  $("#queue").classList.toggle("collapsed", collapsed);
  $("#jobs").hidden = collapsed;
  const button = $("[data-act=toggle-queue]");
  const label = collapsed ? "전송 큐 펼치기" : "전송 큐 접기";
  button.setAttribute("aria-expanded", String(!collapsed));
  button.setAttribute("aria-label", label);
  button.title = label;
  button.innerHTML = `<i class="ti ti-chevron-${collapsed ? "up" : "down"}" aria-hidden="true"></i>`;
}
function renderSites() {
  $("#sites").innerHTML = state.sites.length
    ? state.sites
        .map(
          (s) =>
            `<div class="site"><button type="button" class="sidebar-link site-connect" data-site-connect="${esc(s.id)}" title="${esc(s.name)} 연결" aria-label="${esc(s.name)} 연결">${icon("server")}<span class="sidebar-label">${esc(s.name)}</span>${state.sessions.some((c) => c.siteId === s.id) ? '<i class="site-connected" aria-hidden="true"></i>' : ""}</button><button type="button" class="iconbtn site-edit" data-site-edit="${esc(s.id)}" title="${esc(s.name)} 편집" aria-label="${esc(s.name)} 편집">${icon("more")}</button></div>`,
        )
        .join("")
    : `<p class="sidebar-empty">아직 저장된 사이트가 없습니다.<br>+ 버튼으로 첫 사이트를 추가하세요.</p>`;
}
function renderWorkspaceActions() {
  const local = !session();
  for (const [action, label, glyph] of [
    ["upload", local ? "오른쪽으로 복사" : "업로드", local ? "right" : "up"],
    ["download", local ? "왼쪽으로 복사" : "다운로드", local ? "left" : "down"],
  ]) {
    const button = $(`.workspace-actions [data-act=${action}]`);
    button.innerHTML = icon(glyph) + label;
  }
  for (const action of [
    "external-files",
    "external-folder",
    "sync",
    "watches",
    "terminal",
    "bookmark-add",
  ]) {
    const button = $(`.workspace-actions [data-act=${action}]`);
    button.disabled = local;
    button.title = local ? "원격 서버 탭에서 사용할 수 있습니다." : "";
  }
}
async function activateWorkspace(id) {
  state.current = id;
  const current = session();
  state.remote = current ? current.pane : state.localRight;
  $("#filter-remote").value = state.remote.filter || "";
  renderSessions();
  renderPane("remote");
  await load(
    "remote",
    current ? current.path : state.remote.path || state.info.home,
  );
}
function renderSessions() {
  $("#sessions").innerHTML =
    `<div class="session-tab ${state.current ? "" : "active"}"><span class="label" data-session="">${icon("folder")}로컬 작업 공간</span></div>` +
    state.sessions
      .map(
        (s) =>
          `<div class="session-tab ${state.current === s.id ? "active" : ""}"><span class="label" data-session="${s.id}"><i class="dot live"></i>${esc(s.name)}</span><button class="iconbtn" data-disconnect="${s.id}" aria-label="${esc(s.name)} 연결 종료">${icon("close")}</button></div>`,
      )
      .join("") +
    `<button class="iconbtn" data-act="connect" aria-label="연결 탭 추가">${icon("plus")}</button>`;
  renderSites();
  renderConnectionElapsed();
}
function renderConnectionElapsed() {
  const current = session();
  const elapsed = $("#connection-elapsed");
  elapsed.hidden = !current;
  // 시스템 시계 변경과 관계없이 각 연결의 시작 시점부터 경과 시간을 계산합니다.
  elapsed.textContent = current
    ? `· 접속 ${formatElapsed(performance.now() - current.connectedAt)}`
    : "";
}
function visibleEntries(side) {
  const p = state[side];
  return p.entries
    .filter(
      (f) =>
        (state.showHidden || !f.name.startsWith(".")) &&
        f.name.toLocaleLowerCase().includes(p.filter.toLocaleLowerCase()),
    )
    .sort((a, b) =>
      a.isDirectory !== b.isDirectory
        ? a.isDirectory
          ? -1
          : 1
        : ((p.sort || "name") === "size"
            ? a.size - b.size
            : p.sort === "modified"
              ? new Date(a.modified) - new Date(b.modified)
              : a.name.localeCompare(b.name)) * (p.sortAsc === false ? -1 : 1),
    );
}
function renderPane(side) {
  explorer.beforeRender(side);
  const p = state[side],
    entries = visibleEntries(side),
    list = $("#files-" + side);
  $("#path-" + side).value = p.path;
  const local = isLocal(side);
  const label = side === "local" ? "로컬" : local ? "오른쪽 로컬" : "원격";
  const pane = $("#pane-" + side);
  pane.querySelector(".pane-title strong").textContent = local
    ? "내 컴퓨터"
    : "원격 서버";
  pane.querySelector(".pane-title > .icon").className =
    `icon ti ${local ? "ti-device-desktop" : "ti-server"}`;
  pane
    .querySelector("[data-act=pane-menu]")
    .setAttribute("aria-label", `${label} 파일 작업`);
  $("#path-" + side).setAttribute("aria-label", `${label} 경로`);
  $("#filter-" + side).setAttribute("aria-label", `${label} 파일 검색`);
  list.setAttribute("aria-label", `${label} 파일 목록`);
  $("#tag-" + side).className = "protocol-tag" + (local ? "" : " secure");
  $("#tag-" + side).textContent = local
    ? "LOCAL STORAGE"
    : session().protocol.toUpperCase();
  if (side === "remote") pane.querySelector(".drop-hint").hidden = local;
  renderWorkspaceActions();
  list.setAttribute("aria-rowcount", String(entries.length + 1));
  list.setAttribute("aria-colcount", "5");
  list.innerHTML = fileListMarkup(side, entries, p);
  $("#footer-" + side).innerHTML = fileFooterMarkup(side, session());
  renderSelection(side);
}
function wirePane(side) {
  explorer.wire(side);
  $("#pathform-" + side).onsubmit = (e) => {
    e.preventDefault();
    run(async () => {
      await load(side, $("#path-" + side).value);
      explorer.focus(side);
    });
  };
  $("#filter-" + side).oninput = (e) => {
    state[side].filter = e.target.value;
    renderPane(side);
  };
  for (const prefix of ["path-", "filter-"])
    $("#" + prefix + side).onkeydown = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        if (prefix === "filter-") {
          e.target.value = "";
          state[side].filter = "";
          renderPane(side);
        } else e.target.value = state[side].path;
        explorer.focus(side);
      } else if (prefix === "filter-" && e.key === "Enter") {
        e.preventDefault();
        explorer.focus(side);
      }
    };
}
function renderSelection(side) {
  explorer.renderSelection(side);
}

async function load(side, path) {
  $("#status").textContent = "파일 목록을 읽는 중…";
  const p = state[side];
  const requestId = (p.requestId = (p.requestId || 0) + 1);
  const sessionId = state.current;
  const data = await api(
    isLocal(side)
      ? "/local?path=" + encodeURIComponent(path || "")
      : "/remote/" + state.current + "?path=" + encodeURIComponent(path),
  );
  if (
    state[side] !== p ||
    p.requestId !== requestId ||
    (side === "remote" && state.current !== sessionId)
  )
    return;
  const sameFolder =
    p.path === data.path && (side === "local" || p.sessionId === sessionId);
  Object.assign(p, data, {
    sessionId,
    ...(sameFolder ? {} : { selected: new Set(), cursor: null, anchor: null }),
  });
  if (side === "remote" && session()) session().path = p.path;
  renderPane(side);
  $("#status").textContent = session()
    ? session().host + "에 연결됨"
    : "준비 완료";
}
async function refresh() {
  await Promise.all([
    load("local", state.local.path),
    load("remote", state.remote.path),
  ]);
}
async function connect(site) {
  $("#status").textContent = site.host + "에 연결 중…";
  const c = await api("/sessions", "POST", site);
  c.siteId = site.id;
  c.connectedAt = performance.now();
  state.sessions.push(c);
  state.current = c.id;
  state.remote = c.pane = {
    ...c.listing,
    selected: new Set(),
    filter: "",
    cursor: null,
    anchor: null,
    sessionId: c.id,
  };
  $("#filter-remote").value = "";
  renderSessions();
  renderPane("remote");
  if (site.localPath) await load("local", site.localPath);
  $("#status").textContent = site.host + "에 연결됨";
  toast(site.name + "에 연결되었습니다.");
}
function field(name, label, value = "", type = "text", extra = "") {
  return `<label ${extra}>${label}<input class="form-control" name="${name}" type="${type}" value="${esc(value)}" ${type === "password" ? 'autocomplete="new-password"' : 'autocomplete="off"'}></label>`;
}
function siteDialog(original = {}) {
  const site = {
    id: crypto.randomUUID().replaceAll("-", ""),
    name: "새 사이트",
    protocol: "sftp",
    host: "",
    username: "",
    remotePath: "/",
    region: "us-east-1",
    ...original,
  };
  const d = modal(
    original.id ? "사이트 연결 및 편집" : "새 연결",
    `<div class="grid2">${field("name", "사이트 이름", site.name)}<label>프로토콜<select name="protocol">${[
      ["sftp", "SFTP · SSH 파일 전송"],
      ["scp", "SCP · SSH + POSIX 셸"],
      ["ftps", "FTPS · TLS 암호화"],
      ["ftps-implicit", "FTPS · Implicit TLS"],
      ["ftp", "FTP"],
      ["webdavs", "WebDAV · HTTPS"],
      ["webdav", "WebDAV · HTTP"],
      ["s3", "Amazon S3 / S3 호환"],
    ]
      .map(
        ([v, t]) =>
          `<option value="${v}" ${site.protocol === v ? "selected" : ""}>${t}</option>`,
      )
      .join(
        "",
      )}</select></label>${field("host", "호스트 / S3 엔드포인트", site.host)}${field("port", "포트 (0 = 기본값)", site.port || 0, "number")}${field("username", "사용자 이름 / Access key", site.username)}${field("password", site.hasPassword ? "비밀번호 (저장됨 · 빈칸이면 유지)" : "비밀번호 / Secret key", "", "password")}${field("remotePath", "원격 시작 경로", site.remotePath)}${field("localPath", "로컬 시작 경로", site.localPath || state.local.path)}<div class="span2"><label>SSH 호스트 키 · SHA256</label><div class="inline-field"><input name="fingerprint" value="${esc(site.fingerprint || "")}" placeholder="SFTP / SCP 연결에 필요"><button type="button" id="scan-key">지문 확인</button></div><p class="hint">서버 관리자가 제공한 지문과 비교한 후 신뢰하세요. 변경된 키는 자동으로 수락하지 않습니다.</p></div></div><details ${site.protocol === "s3" ? "open" : ""}><summary>SSH 키 · S3 고급 설정</summary><div class="grid2">${field("privateKeyPath", "SSH 개인 키 파일 경로", site.privateKeyPath)}${field("passphrase", "개인 키 암호", "", "password")}${field("bucket", "S3 버킷", site.bucket)}${field("region", "S3 리전", site.region)}</div></details><label class="check"><input type="checkbox" name="save" checked>이 사이트를 저장</label><label class="check"><input type="checkbox" name="savePassword" ${site.savePassword ? "checked" : ""}>비밀번호를 암호화 금고에 저장</label>`,
    `${original.id ? '<button type="button" id="delete-site" class="danger left">사이트 삭제</button>' : ""}<button type="submit" name="mode" value="save">저장만</button><button type="submit" name="mode" value="connect" class="primary">${icon("play")}연결</button>`,
  );
  const form = $("form", d),
    connectButton = $("button[value=connect]", d);
  form.addEventListener(
    "invalid",
    (event) => {
      event.preventDefault();
      const label = event.target.closest("label")?.textContent.trim();
      toast((label ? label + " " : "") + "입력값을 확인하세요.", true);
      event.target.focus();
    },
    true,
  );
  form.addEventListener("keydown", (event) => {
    if (
      event.key !== "Enter" ||
      event.isComposing ||
      event.keyCode === 229 ||
      event.defaultPrevented ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      event.shiftKey ||
      event.target.closest("button, select, textarea, summary") ||
      event.target.matches('input[type="file"], input[type="color"]')
    )
      return;
    // 저장만 버튼의 기본 제출을 막고, 한글 조합이 끝난 Enter만 연결 버튼으로 제출합니다.
    event.preventDefault();
    if (!event.repeat && !connectButton.disabled)
      form.requestSubmit(connectButton);
  });
  const readAdvanced = advanced.attachSite(d, site, () => read());
  const read = () => {
    const fd = new FormData(form);
    return {
      ...site,
      ...Object.fromEntries(fd),
      port: Number(fd.get("port")),
      password: fd.get("password") || null,
      passphrase: fd.get("passphrase") || null,
      savePassword: fd.has("savePassword"),
      ...readAdvanced(fd),
    };
  };
  $("#scan-key").onclick = async () => {
    const b = $("#scan-key");
    b.disabled = true;
    try {
      const s = read();
      if (!["sftp", "scp"].includes(s.protocol))
        throw Error("호스트 키 확인은 SFTP / SCP에서 사용합니다.");
      const r = await api("/fingerprint", "POST", s);
      if (
        await confirmBox(
          "SSH 호스트 키 확인",
          s.host +
            "\n" +
            r.fingerprint +
            "\n\n서버 관리자의 지문과 일치하는지 확인하세요.",
          "이 지문 신뢰",
        )
      )
        $("[name=fingerprint]", d).value = r.fingerprint;
    } catch (e) {
      toast(e.message, true);
    } finally {
      b.disabled = false;
    }
  };
  if (original.id)
    $("#delete-site").onclick = () =>
      run(async () => {
        if (
          await confirmBox(
            "사이트 삭제",
            site.name + "의 저장된 연결 정보를 삭제합니다.",
            "삭제",
            true,
          )
        ) {
          await api("/sites/" + site.id, "DELETE");
          state.sites = await api("/sites");
          d.close();
          renderSites();
        }
      });
  bindForm(
    async (e) => {
      const mode = e.submitter?.value || "connect";
      const s = read();
      if (s.savePassword && !state.vault.unlocked)
        throw Error(
          "설정 → 암호화 금고에서 먼저 마스터 비밀번호를 설정하거나 잠금 해제하세요.",
        );
      if (s.protocol === "ftp" || s.protocol === "webdav") {
        if (
          !(await confirmBox(
            "암호화되지 않은 연결",
            "이 프로토콜은 비밀번호와 데이터를 암호화하지 않습니다. 신뢰하는 네트워크인지 확인하세요.",
            "연결 계속",
          ))
        )
          return;
      }
      if (mode === "save" || $("[name=save]", d).checked) {
        await api("/sites", "POST", s);
        state.sites = await api("/sites");
        renderSites();
      }
      if (mode === "connect") await connect(s);
      d.close();
    },
    {
      onError: (error) => toast(error.message, true),
      defaultSubmitter: connectButton,
    },
  );
  // 처음 열었을 때 닫기 버튼이 아닌 입력란에 포커스를 두어 Enter로 연결하게 합니다.
  $("[name=host]", d).focus();
}
async function transfer(direction, paths, options = {}) {
  const local = !session();
  const side = direction === "upload" ? "local" : "remote";
  paths = paths || [...state[side].selected];
  if (!paths.length) throw Error("전송할 파일이나 폴더를 선택하세요.");
  paths = [...paths];
  const sessionId = options.sessionId ?? state.current;
  if (sessionId !== state.current)
    throw Error("전송할 탭이 변경되었습니다. 항목을 다시 선택하세요.");
  const destination =
    options.destination ||
    state[direction === "upload" ? "remote" : "local"].path;
  const d = modal(
    options.move
      ? "선택 항목 이동"
      : local
        ? "로컬 파일 복사"
        : direction === "upload"
          ? "파일 업로드"
          : "파일 다운로드",
    `<p class="hint">${paths.length}개 항목을 다음 폴더로 ${options.move ? "이동" : "전송"}합니다.</p>${options.move ? '<p class="warning">전송을 완료한 원본 파일과 비워진 폴더는 삭제됩니다. 대상 경로와 충돌 처리 방식을 확인하세요.</p>' : ""}<div class="code-box">${esc(destination)}</div><p class="hint">${paths.map((p) => esc(p)).join("<br>")}</p><div class="grid2"><label>같은 이름의 파일<select name="conflict"><option value="skip">건너뛰기</option><option value="replace">덮어쓰기</option><option value="newer">더 최신 파일만</option><option value="rename">다른 이름으로 보관</option></select></label>${field("speed", "속도 제한 (KB/s, 0 = 제한 없음)", 0, "number")}</div><p class="hint">폴더는 하위 항목까지 전송합니다. 심볼릭 링크는 따라가지 않습니다.${local ? " 파일 내용과 줄바꿈은 변환하지 않습니다." : ""}</p>`,
    `<button type="button" data-close>취소</button><button class="primary" type="submit">${options.move ? "이동 시작" : "전송 시작"}</button>`,
  );
  bindForm(async () => {
    const form = new FormData($("#modal-form"));
    await api("/transfers", "POST", {
      sessionId: local ? "" : sessionId,
      direction: local ? "local" : direction,
      paths,
      destination,
      conflict: $("[name=conflict]").value,
      speedLimit: Math.max(0, Number($("[name=speed]").value)),
      options: {
        ...advanced.readOptions(form),
        ...(local ? { mode: "binary", permissions: null } : {}),
        ...(options.move ? { removeSource: true } : {}),
      },
      priority: Number(form.get("priority")),
      scheduledAt: form.get("scheduledAt")
        ? new Date(form.get("scheduledAt")).toISOString()
        : null,
    });
    $("#dialog").close();
    await poll();
    toast("전송 큐에 추가했습니다.");
  });
  advanced.attachTransfer(d);
  if (local) {
    const localOptions = () => {
      for (const name of [
        "opt_mode",
        "opt_newline",
        "opt_textMask",
        "opt_permissions",
      ]) {
        const input = d.querySelector(`[name=${name}]`);
        input.closest("label").hidden = true;
      }
      d.querySelector("[name=opt_mode]").value = "binary";
      d.querySelector("[name=opt_permissions]").value = "";
    };
    localOptions();
    d.querySelector("[name=opt_preset]").addEventListener(
      "change",
      localOptions,
    );
  }
  if (options.move) {
    const lockMove = () => {
      const checkbox = d.querySelector("[name=opt_move]");
      checkbox.checked = true;
      checkbox.disabled = true;
    };
    lockMove();
    d.querySelector("[name=opt_preset]").addEventListener("change", lockMove);
  }
}
const statusNames = {
  queued: "대기 중",
  running: "전송 중",
  pausing: "중단 중",
  paused: "일시정지",
  cancelled: "취소됨",
  failed: "실패",
  completed: "완료",
  scheduled: "예약됨",
  retrying: "재연결 대기",
};
function renderJobs() {
  const active = state.jobs.filter((j) =>
    ["queued", "running", "pausing", "scheduled", "retrying"].includes(
      j.status,
    ),
  ).length;
  $("#nav-count").textContent = active;
  $("#queue-count").textContent = state.jobs.length;
  const jobs = state.jobs.filter(
    (j) =>
      state.queueFilter === "all" ||
      (state.queueFilter === "active"
        ? [
            "queued",
            "running",
            "pausing",
            "paused",
            "scheduled",
            "retrying",
          ].includes(j.status)
        : j.status === "completed"),
  );
  $("#jobs").innerHTML = jobs.length
    ? jobs
        .map(
          (j) =>
            `<div class="transfer">${icon(j.direction === "local" ? "transfer" : j.direction === "upload" ? "up" : "down")}<div class="name" title="${esc(j.error || j.current || j.paths.join(", "))}">${esc((j.current || j.paths[0]).split(/[\\/]/).pop())}<small>${esc(j.error || j.site + " → " + j.destination)}</small></div><div><span class="muted">${size(j.bytes)} / ${size(j.total)}</span><div class="bar"><i style="--progress:${j.status === "completed" ? 100 : j.total ? Math.min(100, (j.bytes / j.total) * 100) : 0}%"></i></div></div><div class="status ${j.status}">${statusNames[j.status]}${j.speed > 0 ? "<br><small>" + size(j.speed) + "/s</small>" : ""}</div><div class="controls">${["running", "queued", "scheduled", "retrying"].includes(j.status) ? `<button class="iconbtn" data-job="${j.id}" data-control="pause" title="일시정지">${icon("pause")}</button><button class="iconbtn" data-job="${j.id}" data-control="cancel" title="취소">${icon("close")}</button>` : ["paused", "failed", "cancelled"].includes(j.status) ? `<button class="iconbtn" data-job="${j.id}" data-control="retry" title="이어하기 / 재시도">${icon("play")}</button>` : icon("check")}</div></div>`,
        )
        .join("")
    : `<div class="queue-empty">${icon("transfer")}<span>${state.jobs.length ? "이 상태의 전송이 없습니다." : "모든 파일이 제자리에. 전송할 파일을 선택하거나 반대쪽 패널로 드래그하세요."}</span></div>`;
}
let polling = false;
async function poll() {
  if (polling) return;
  polling = true;
  try {
    const old = state.jobs;
    state.jobs = await api("/transfers");
    renderJobs();
    if (
      state.jobs.some(
        (j) =>
          j.status === "completed" &&
          old.find((x) => x.id === j.id)?.status !== "completed",
      )
    )
      await refresh();
  } finally {
    polling = false;
  }
}
async function edit(side, f) {
  if (f.isDirectory) throw Error("파일을 선택하세요.");
  const endpoint = isLocal(side) ? "/local" : "/remote/" + state.current;
  const originalSession = state.current;
  let data;
  try {
    data = await api(endpoint + "/read", "POST", { path: f.path });
  } catch (e) {
    const encoding = await inputBox(
      "파일 읽기 및 인코딩",
      e.message + "\n파일 인코딩을 알고 있다면 지정해서 다시 열 수 있습니다.",
      "파일 인코딩 (예: cp949, windows-1252)",
      "cp949",
    );
    if (!encoding) return;
    data = await api(endpoint + "/read", "POST", { path: f.path, encoding });
  }
  await showTextEditor({
    file: f,
    side: isLocal(side) ? "local" : "remote",
    data,
    modal,
    confirmBox,
    toast,
    esc,
    write: (snapshot) =>
      api(endpoint + "/write", "POST", { path: f.path, ...snapshot }),
    onSaved: () => {
      if (side === "local" || state.current === originalSession)
        return load(side, state[side].path);
    },
  });
}
async function fileAction(action, side = state.focus) {
  const p = state[side],
    selected = p.entries.filter((f) => p.selected.has(f.path));
  const endpoint = isLocal(side) ? "/local" : "/remote/" + state.current;
  if (action === "edit") {
    if (selected.length !== 1) throw Error("편집할 파일 하나를 선택하세요.");
    return edit(side, selected[0]);
  }
  if (action === "delete") {
    if (!selected.length) throw Error("삭제할 항목을 선택하세요.");
    if (
      !(await confirmBox(
        "선택한 항목 영구 삭제",
        selected.map((f) => f.name).join(", ") +
          "\n하위 폴더와 파일까지 영구 삭제합니다. 이 작업은 되돌릴 수 없습니다.",
        "영구 삭제",
        true,
      ))
    )
      return;
    const sessionId = state.current;
    let completed = 0;
    try {
      for (const f of selected) {
        await api(endpoint + "/delete", "POST", { path: f.path });
        completed++;
      }
      toast(`${completed}개 항목을 영구 삭제했습니다.`);
    } catch (error) {
      throw Error(
        `${completed}/${selected.length}개 삭제 후 중단: ${error.message}`,
      );
    } finally {
      if (side === "local" || state.current === sessionId)
        await load(side, state[side].path);
    }
    return;
  }
  if (action === "rename" && selected.length !== 1)
    throw Error("이름을 바꿀 항목 하나를 선택하세요.");
  if (
    action === "chmod" &&
    (selected.length !== 1 || !session()?.capabilities.permissions)
  )
    throw Error("권한을 변경할 원격 항목 하나를 선택하세요.");
  modal(
    action === "mkdir"
      ? "새 폴더"
      : action === "chmod"
        ? "파일 권한 변경"
        : "이름 변경",
    field(
      "filename",
      action === "chmod" ? "8진수 권한 (예: 755)" : "이름",
      action === "rename"
        ? selected[0].name
        : action === "chmod"
          ? selected[0].permissions
          : "",
    ),
    '<button type="button" data-close>취소</button><button type="submit" class="primary">적용</button>',
  );
  bindForm(async () => {
    const name = $("[name=filename]").value.trim();
    if (!name || name === "." || name === ".." || /[\\/\r\n\0]/.test(name))
      throw Error("올바른 이름을 입력하세요.");
    await api(
      endpoint + "/" + action,
      "POST",
      action === "mkdir"
        ? { path: join(side, p.path, name) }
        : action === "chmod"
          ? { path: selected[0].path, octal: name }
          : { path: selected[0].path, destination: join(side, p.path, name) },
    );
    $("#dialog").close();
    await load(side, p.path);
  });
}
function paneMenu(side) {
  explorer.showMenu(side);
}
async function explorerAction(command, side) {
  state.focus = side;
  const selected = visibleEntries(side).filter((f) =>
    state[side].selected.has(f.path),
  );
  if (command === "open") {
    const f =
      selected.length === 1
        ? selected[0]
        : selected.length === 0
          ? state[side].entries.find((f) => f.path === state[side].cursor)
          : null;
    if (!f) {
      if (selected.length > 1) throw Error("열 항목 하나를 선택하세요.");
      return;
    }
    return f.isDirectory ? load(side, f.path) : edit(side, f);
  }
  if (command === "info") {
    if (!selected.length) return;
    const files = selected.filter((f) => !f.isDirectory);
    modal(
      "선택 항목 정보",
      `<div class="selection-info"><i class="ti ti-files" aria-hidden="true"></i><div><strong>${selected.length}개 항목</strong><p>폴더 ${selected.length - files.length}개 · 파일 ${files.length}개</p></div></div><p class="hint">파일 크기 합계: ${size(files.reduce((n, f) => n + f.size, 0))} · 폴더 내부는 제외합니다.</p><div class="code-box">${selected.map((f) => esc(f.path)).join("\n")}</div>`,
    );
    return;
  }
  if (command === "hidden") {
    state.showHidden = !state.showHidden;
    renderPane("local");
    renderPane("remote");
    return;
  }
  if (await workflows.action(command)) return;
  return fileAction(command, side);
}
async function syncDialog() {
  if (!session()) throw Error("동기화할 서버에 연결하세요.");
  modal(
    "폴더 동기화",
    `<p class="hint">양쪽 폴더를 재귀적으로 비교합니다. 크기가 같은 파일은 내용을 다운로드해 SHA-256으로 비교합니다.</p><div class="grid2">${field("localPath", "로컬 폴더", state.local.path, "text", 'class="span2"')}${field("remotePath", "원격 폴더", state.remote.path, "text", 'class="span2"')}<label class="span2">동기화 방향<select name="direction"><option value="upload">로컬 → 원격</option><option value="download">원격 → 로컬</option><option value="both">양방향 · 최신 파일 기준</option></select></label></div><label class="check"><input type="checkbox" name="deleteExtraneous">대상에만 있는 파일 삭제 (단방향만)</label>`,
    `<button type="button" data-close>취소</button><button type="submit" class="primary">${icon("search")}변경 내용 미리보기</button>`,
  );
  advanced.attachTransfer($("#dialog"), false);
  bindForm(async () => {
    const form = new FormData($("#modal-form"));
    const req = {
      sessionId: state.current,
      ...Object.fromEntries(form),
      deleteExtraneous: form.has("deleteExtraneous"),
      options: { ...advanced.readOptions(form), removeSource: false },
    };
    $("#status").textContent = "폴더 내용을 비교하는 중…";
    const p = await api("/sync/preview", "POST", req);
    showPlan(p);
    $("#status").textContent = "동기화 미리보기 준비 완료";
  });
}
function showPlan(plan) {
  const names = {
    upload: "↑ 업로드",
    download: "↓ 다운로드",
    "mkdir-local": "로컬 폴더 생성",
    "mkdir-remote": "원격 폴더 생성",
    "delete-local": "로컬 삭제",
    "delete-remote": "원격 삭제",
    "delete-local-dir": "로컬 폴더 삭제",
    "delete-remote-dir": "원격 폴더 삭제",
    conflict: "충돌",
  };
  const conflicts = plan.changes.some((c) => c.action === "conflict");
  modal(
    "동기화 변경 내용",
    `<p class="hint">${plan.changes.length}개 변경 사항${conflicts ? " · 충돌별 처리 방향을 선택하세요." : ""}</p>${plan.changes.some((c) => c.action.startsWith("delete")) ? '<div class="warning">삭제 항목은 영구 삭제됩니다. 아래 목록을 확인하세요.</div>' : ""}<div class="sync-list">${plan.changes.map((c) => `<div class="sync-row" title="${esc(c.reason)}"><input type="checkbox" class="sync-choice" data-path="${esc(c.relativePath)}" checked aria-label="${esc(c.relativePath)} 적용"><span class="action ${c.action.startsWith("delete") ? "delete" : c.action}">${c.action === "conflict" ? `<select class="sync-resolution" data-path="${esc(c.relativePath)}"><option value="ignore">건너뛰기</option><option value="upload">로컬 사용</option><option value="download">원격 사용</option></select>` : names[c.action]}</span><span>${esc(c.relativePath)}</span><span class="muted">${size(c.size)}</span></div>`).join("") || '<p class="hint">두 폴더가 일치합니다.</p>'}</div>`,
    `<button type="button" data-close>닫기</button><button type="submit" class="primary" ${!plan.changes.length ? "disabled" : ""}>${plan.changes.length}개 변경 적용</button>`,
  );
  bindForm(async () => {
    await api("/sync/" + plan.id + "/apply", "POST", {
      selected: $$(".sync-choice:checked").map((e) => e.dataset.path),
      resolutions: Object.fromEntries(
        $$(".sync-resolution").map((e) => [e.dataset.path, e.value]),
      ),
    });
    $("#dialog").close();
    toast("동기화를 시작했습니다.");
    const timer = setInterval(
      () =>
        run(async () => {
          const s = await api("/sync/" + plan.id);
          $("#status").textContent = "동기화: " + s.current;
          if (s.status !== "running") {
            clearInterval(timer);
            toast(
              s.status === "completed" ? "동기화를 완료했습니다." : s.error,
              s.status !== "completed",
            );
            await refresh();
          }
        }),
      1200,
    );
  });
}
async function settings() {
  // 금고 작업은 같은 대화상자를 새로 만듭니다. 다음 대화상자의 주기 조회 정리와
  // 버튼 처리 함수를 연결하기 전에 대기 중인 닫기 이벤트를 처리합니다.
  const previous = $("#dialog");
  if (previous.open) {
    await new Promise((resolve) => {
      previous.addEventListener("close", resolve, { once: true });
      previous.close();
    });
  }
  state.vault = await api("/vault");
  state.prefs = await api("/preferences");
  const d = modal(
    "설정",
    `<section class="settings-section"><h3>${icon(state.vault.unlocked ? "unlock" : "lock")} 암호화 금고 · ${state.vault.unlocked ? "잠금 해제됨" : state.vault.configured ? "잠김" : "설정 필요"}</h3><p class="hint">저장된 비밀번호는 마스터 비밀번호로 암호화됩니다. 비밀번호를 잊으면 복구할 수 없습니다. 기존 연결은 금고를 잠근 뒤에도 유지됩니다.</p>${field("master", "마스터 비밀번호 (최초 설정 시 12자 이상)", "", "password")}<div class="buttons"><button type="button" id="vault-unlock">${state.vault.configured ? "잠금 해제" : "금고 생성"}</button><button type="button" id="vault-lock">금고 잠금</button></div></section><section class="settings-section"><h3>앱 업데이트</h3>${field("updateUrl", "배포 서버 피드 URL", state.prefs.updateUrl || "")}<p class="hint">예: https://updates.example.com/releases/win-x64-stable/<br>설치된 패키지와 동일한 운영체제·아키텍처·채널을 사용하세요.</p><p class="hint">앱을 시작하면 백그라운드에서 새 버전을 확인하고 다운로드합니다. 다운로드가 끝나도 작업 중에는 재시작하지 않으며, 앱을 종료한 뒤 다음 실행 시 자동 적용합니다. 피드 URL을 처음 저장한 경우 다음 실행부터 자동 확인합니다.</p><div class="buttons"><button type="button" id="update-check">업데이트 확인</button><button type="button" id="update-download" disabled>다운로드</button><button type="button" id="update-apply" disabled>적용 후 재시작</button></div><p class="hint" id="update-result" role="status" aria-live="polite"></p></section><section class="settings-section"><h3>작업 설정</h3>${field("maxConcurrent", "동시 전송 수 (1–8)", state.prefs.maxConcurrent || 2, "number")}${field("editorExecutable", "외부 편집기 실행 파일", state.prefs.editorExecutable || "")}${field("editorArguments", "편집기 인수 (JSON 배열)", JSON.stringify(state.prefs.editorArguments || ["{file}"]))}${field("customCommands", "SSH 사용자 명령 (JSON 배열)", JSON.stringify(state.prefs.commands || []))}<p class="hint">예: [{&quot;name&quot;:&quot;SHA256&quot;,&quot;template&quot;:&quot;sha256sum -- {files}&quot;,&quot;remote&quot;:true}]<br>{file}, {files}, {directory}는 안전하게 인용된 경로로 치환합니다.</p><h3>표시</h3><label>테마<select class="form-select" name="theme"><option value="light">라이트</option><option value="dark">다크</option></select></label><label class="check"><input type="checkbox" name="hidden" ${state.showHidden ? "checked" : ""}>숨김 파일 표시</label></section><p class="hint">Portway ${esc(state.info.version)} · ${esc(state.info.os)}<br>Photino · SSH.NET · FluentFTP · AWS SDK · Velopack</p>`,
    `<button type="button" data-close>닫기</button><button class="primary" type="submit">설정 저장</button>`,
  );
  d.querySelector("[name=theme]").value = window.portwayTheme.preference;
  const safe = (fn) => async () => {
    try {
      await fn();
      $("#form-error").textContent = "";
    } catch (e) {
      $("#form-error").textContent = e.message;
    }
  };
  $("#vault-unlock").onclick = safe(async () => {
    state.vault = await api("/vault/unlock", "POST", {
      password: $("[name=master]").value,
    });
    $("[name=master]").value = "";
    toast("암호화 금고를 잠금 해제했습니다.");
    await settings();
  });
  $("#vault-lock").onclick = safe(async () => {
    state.vault = await api("/vault/lock", "POST");
    await settings();
  });
  const save = async () => {
    state.prefs = {
      ...state.prefs,
      updateUrl: $("[name=updateUrl]").value.trim(),
      maxConcurrent: Number($("[name=maxConcurrent]").value),
      editorExecutable: $("[name=editorExecutable]").value,
      editorArguments: JSON.parse($("[name=editorArguments]").value),
      commands: JSON.parse($("[name=customCommands]").value),
      theme: $("[name=theme]").value,
    };
    await api("/preferences", "PUT", state.prefs);
    window.portwayTheme.apply(state.prefs.theme);
    renderTheme();
  };
  let updateBusy = false,
    updatePollBusy = false,
    updateStopped = false;
  const renderUpdateState = (r) => {
    if (updateStopped) return;
    const busy = updateBusy || ["checking", "downloading"].includes(r.state);
    $("#update-result").textContent =
      r.state === "downloading"
        ? `업데이트 다운로드 중… ${r.progress || 0}%`
        : r.message ||
          (r.available ? `새 버전 ${r.version}을 사용할 수 있습니다.` : "");
    $("#update-check").disabled = busy;
    $("#update-download").disabled = busy || !r.available || r.ready;
    $("#update-apply").disabled = busy || !r.ready;
  };
  const refreshUpdateState = async () => {
    if (updatePollBusy || updateStopped) return;
    updatePollBusy = true;
    try {
      renderUpdateState(await api("/updates"));
    } catch {
      // 수동 작업은 자체 오류를 표시하며 주기 조회가 사용자의 작업을 방해하면 안 됩니다.
    } finally {
      updatePollBusy = false;
    }
  };
  const updateTimer = setInterval(refreshUpdateState, 1000);
  d.addEventListener(
    "close",
    () => {
      updateStopped = true;
      clearInterval(updateTimer);
    },
    { once: true },
  );
  await refreshUpdateState();
  if (updateStopped) return;
  $("#update-check").onclick = safe(async () => {
    await save();
    updateBusy = true;
    renderUpdateState({ state: "checking", message: "업데이트 확인 중…" });
    try {
      await api("/updates/check", "POST");
    } finally {
      updateBusy = false;
      await refreshUpdateState();
    }
  });
  $("#update-download").onclick = safe(async () => {
    updateBusy = true;
    renderUpdateState({ state: "downloading" });
    try {
      await api("/updates/download", "POST");
    } finally {
      updateBusy = false;
      await refreshUpdateState();
    }
  });
  $("#update-apply").onclick = safe(async () => {
    if (
      await confirmBox(
        "업데이트 적용",
        "앱을 종료하고 새 버전으로 다시 시작합니다.",
        "재시작",
      )
    )
      await api("/updates/apply", "POST");
  });
  bindForm(async () => {
    await save();
    state.showHidden = $("[name=hidden]").checked;
    renderPane("local");
    renderPane("remote");
    d.close();
    toast("설정을 저장했습니다.");
  });
}
async function terminal() {
  if (!session()?.capabilities.commands)
    throw Error("SSH 명령은 SFTP / SCP 연결에서 사용할 수 있습니다.");
  const id = state.current;
  modal(
    "SSH 명령 실행",
    `<p class="hint">각 명령은 새 셸에서 실행됩니다. 대화형 프로그램은 지원하지 않으며 30초 후 시간 초과됩니다.</p>${field("command", "명령", "pwd")}<pre id="command-output" class="code-box">$</pre>`,
    `<button type="button" data-close>닫기</button><button type="submit" class="primary">실행</button>`,
  );
  bindForm(async () => {
    const r = await api("/remote/" + id + "/command", "POST", {
      command: $("[name=command]").value,
    });
    $("#command-output").textContent = r.output || "(출력 없음)";
  });
}
async function bookmarks(add = false) {
  state.prefs = await api("/preferences");
  if (add) {
    if (!session()) throw Error("북마크할 서버에 연결하세요.");
    modal(
      "현재 경로 북마크",
      field("bookmarkName", "북마크 이름", session().name) +
        '<p class="hint">' +
        esc(state.local.path) +
        "<br>" +
        esc(state.remote.path) +
        "</p>",
      '<button type="button" data-close>취소</button><button type="submit" class="primary">저장</button>',
    );
    bindForm(async () => {
      state.prefs.bookmarks = [
        ...(state.prefs.bookmarks || []),
        {
          name: $("[name=bookmarkName]").value,
          localPath: state.local.path,
          remotePath: state.remote.path,
        },
      ];
      await api("/preferences", "PUT", state.prefs);
      $("#dialog").close();
      toast("북마크를 저장했습니다.");
    });
    return;
  }
  modal(
    "북마크",
    `<p class="hint">원격 경로는 현재 연결된 서버에서 열립니다.</p>` +
      (state.prefs.bookmarks || [])
        .map(
          (b, i) =>
            `<div class="inline-field" style="margin-bottom:10px"><button type="button" data-bookmark="${i}" style="flex:1;justify-content:start">${icon("bookmark")}${esc(b.name)}</button><button type="button" data-bookmark-delete="${i}" aria-label="북마크 삭제">${icon("trash")}</button></div>`,
        )
        .join("") +
      (!(state.prefs.bookmarks || []).length
        ? '<p class="hint">저장된 북마크가 없습니다.</p>'
        : ""),
  );
  $$("[data-bookmark]").forEach(
    (b) =>
      (b.onclick = () =>
        run(async () => {
          const item = state.prefs.bookmarks[Number(b.dataset.bookmark)];
          await load("local", item.localPath);
          if (session()) await load("remote", item.remotePath);
          $("#dialog").close();
        })),
  );
  $$("[data-bookmark-delete]").forEach(
    (b) =>
      (b.onclick = () =>
        run(async () => {
          state.prefs.bookmarks.splice(Number(b.dataset.bookmarkDelete), 1);
          await api("/preferences", "PUT", state.prefs);
          await bookmarks();
        })),
  );
}
async function click(e) {
  const b = e.target.closest("button,[data-session],[data-sort]");
  if (!b) return;
  const a = b.dataset.act;
  await run(async () => {
    if (b.dataset.siteEdit) {
      siteDialog(state.sites.find((s) => s.id === b.dataset.siteEdit));
      return;
    }
    if (b.dataset.siteConnect) {
      const s = state.sites.find((s) => s.id === b.dataset.siteConnect);
      if (
        (s.hasPassword && state.vault.unlocked) ||
        (s.privateKeyPath && !s.hasPassword)
      )
        await connect(s);
      else siteDialog(s);
      return;
    }
    if ("session" in b.dataset) {
      await activateWorkspace(b.dataset.session || null);
      return;
    }
    if (b.dataset.disconnect) {
      await api("/sessions/" + b.dataset.disconnect, "DELETE");
      state.sessions = state.sessions.filter(
        (s) => s.id !== b.dataset.disconnect,
      );
      if (state.current === b.dataset.disconnect)
        await activateWorkspace(state.sessions.at(-1)?.id || null);
      else renderSessions();
      return;
    }
    if (b.dataset.sort) {
      const side = b.dataset.side,
        p = state[side];
      p.sortAsc =
        (p.sort || "name") === b.dataset.sort ? p.sortAsc === false : true;
      p.sort = b.dataset.sort;
      renderPane(side);
      explorer.focus(side);
      return;
    }
    if (b.dataset.filter) {
      state.queueFilter = b.dataset.filter;
      $$("[data-filter]").forEach((x) => x.classList.toggle("active", x === b));
      renderJobs();
      return;
    }
    if (b.dataset.job) {
      await api("/transfers/" + b.dataset.job, "POST", {
        action: b.dataset.control,
        sessionId: state.jobs.find((j) => j.id === b.dataset.job)?.restored
          ? state.current
          : null,
      });
      await poll();
      return;
    }
    if (!a) return;
    if (await workflows.action(a)) return;
    switch (a) {
      case "theme": {
        const theme =
          window.portwayTheme.preference === "dark" ? "light" : "dark";
        b.disabled = true;
        try {
          const prefs = await api("/preferences");
          await api("/preferences", "PUT", { ...prefs, theme });
          state.prefs = { ...prefs, theme };
          window.portwayTheme.apply(theme);
        } finally {
          b.disabled = false;
        }
        break;
      }
      case "external-files":
        externalDrop.pick();
        break;
      case "external-folder":
        externalDrop.pick(true);
        break;
      case "connect":
      case "new-site":
        siteDialog();
        break;
      case "upload":
      case "download":
        await transfer(a);
        break;
      case "refresh":
        await refresh();
        break;
      case "pane-refresh":
        await load(b.dataset.side, state[b.dataset.side].path);
        break;
      case "parent": {
        const side = b.dataset.side;
        if (state[side].parent) await load(side, state[side].parent);
        break;
      }
      case "pane-menu":
        paneMenu(b.dataset.side);
        break;
      case "mkdir":
      case "rename":
      case "edit":
      case "delete":
      case "chmod":
        await fileAction(a);
        break;
      case "hidden":
        state.showHidden = !state.showHidden;
        renderPane("local");
        renderPane("remote");
        break;
      case "sync":
        await syncDialog();
        break;
      case "settings":
        await settings();
        break;
      case "import":
        importDialog();
        break;
      case "terminal":
        await terminal();
        break;
      case "bookmark-add":
        await bookmarks(true);
        break;
      case "bookmarks":
        await bookmarks();
        break;
      case "clear-queue":
        await api("/transfers", "DELETE");
        await poll();
        break;
      case "toggle-queue":
        state.queueCollapsed = !state.queueCollapsed;
        renderQueueSize();
        break;
      case "queue":
        state.queueCollapsed = false;
        renderQueueSize();
        $("#jobs").scrollIntoView({ behavior: "smooth" });
        toast(
          `최대 ${state.prefs.maxConcurrent || 2}개 전송 · 작업은 앱 종료 후에도 보존됩니다.`,
        );
        break;
      case "explorer":
        $("#files-local").focus();
        break;
      case "help":
        modal(
          "키보드 단축키",
          `<p class="hint">목록의 빈 공간·크기·날짜 영역을 드래그하면 박스로 선택합니다. 파일 이름을 반대 패널 또는 대상 폴더로 끌면 선택한 항목들을 전송합니다.</p><table class="shortcut-table">${[
            ["Ctrl/⌘ + 클릭 · 체크박스", "선택 추가 / 해제"],
            ["Shift + 클릭 · ↑/↓", "연속 범위 선택"],
            ["Ctrl/⌘ + ↑/↓", "선택을 유지한 채 포커스 이동"],
            ["Space · Insert", "현재 항목 선택 전환 (Insert는 다음 항목으로)"],
            ["Ctrl/⌘+A · Ctrl/⌘+Shift+A", "전체 선택 · 선택 반전"],
            [
              "Home/End · PageUp/PageDown",
              "처음/끝 · 한 화면 이동 (Shift로 범위 선택)",
            ],
            ["Tab · Shift+F10", "반대 패널 · 컨텍스트 메뉴"],
            ["Enter · Alt+Enter", "항목 열기 · 선택 정보"],
            ["F2 · F4", "이름 변경 · Monaco 편집"],
            ["F5 · F6", "선택 항목 전송 · 이동 전송"],
            ["F7 · Delete · Shift+Delete", "새 폴더 · 휴지통 · 영구 삭제 확인"],
            ["Ctrl/⌘+L · Ctrl/⌘+F", "경로 입력 · 현재 폴더 검색"],
            ["Ctrl/⌘+R · Backspace/Alt+↑", "새로고침 · 상위 폴더"],
            ["Ctrl/⌘+Shift+C · Esc", "선택한 경로 복사 · 선택 해제"],
            ["Ctrl/⌘+S", "Monaco에서 저장"],
          ]
            .map(
              ([key, description]) =>
                `<tr><td>${key}</td><td>${description}</td></tr>`,
            )
            .join(
              "",
            )}</table><p class="hint">선택은 현재 표시된 항목에만 적용됩니다. 필터로 가려진 항목은 선택에서 제외하며, 목록 새로고침·정렬은 남아 있는 항목의 선택을 유지합니다.</p>`,
        );
        break;
    }
  });
}
function keyboard(e) {
  explorer.keyboard(e);
}
function renderTheme() {
  const button = document.querySelector("[data-act=theme]");
  if (!button) return;
  const dark = window.portwayTheme.preference === "dark";
  const current = dark ? "다크" : "라이트";
  button.querySelector(".sidebar-label").textContent = `${current} 테마`;
  button.title = `현재 테마: ${current} · ${dark ? "라이트" : "다크"}로 변경`;
  button.setAttribute("aria-label", button.title);
  button.querySelector(".ti").className =
    `icon ti ${dark ? "ti-moon-stars" : "ti-sun"}`;
}
window.addEventListener("portway-theme", renderTheme);
async function boot() {
  shell();
  state.info = await api("/info");
  state.sites = await api("/sites");
  state.vault = await api("/vault");
  state.prefs = await api("/preferences");
  const theme = window.portwayTheme.apply(state.prefs.theme);
  if (state.prefs.theme !== theme) {
    state.prefs = { ...state.prefs, theme };
    // 이전 시스템 테마 모드를 변환할 때 기존 설정을 모두 보존합니다.
    await api("/preferences", "PUT", state.prefs).catch((e) =>
      toast("테마 설정을 저장하지 못했습니다: " + e.message, true),
    );
  }
  renderTheme();
  $("#version").textContent = "Portway " + state.info.version;
  renderSites();
  await Promise.all([
    load("local", state.info.home),
    load("remote", state.info.home),
  ]);
  setInterval(() => {
    renderConnectionElapsed();
    poll().catch((e) => {
      $("#status").textContent = e.message;
    });
  }, 1000);
  setInterval(() => advanced.pollAuthentication().catch(() => {}), 500);
}
const advanced = createAdvancedUI({
  api,
  esc,
  field,
  toast,
  confirmBox,
  inputBox,
  state,
});
const workflows = createWorkflows({
  api,
  esc,
  field,
  modal,
  bindForm,
  state,
  toast,
  load,
  refresh,
  session,
  isLocal,
  advanced,
});
const explorer = createExplorer({
  state,
  visibleEntries,
  renderPane,
  load,
  session,
  run,
  action: explorerAction,
  transfer,
  esc,
  size,
  toast,
});
const externalDrop = createExternalDrop({
  api,
  token,
  state,
  session,
  modal,
  bindForm,
  esc,
  size,
  toast,
  poll,
  advanced,
});
boot().catch((e) => {
  toast(e.message, true);
  $("#status").textContent = e.message;
});

function importDialog() {
  const dialog = modal(
    "사이트 내보내기/가져오기",
    `<section class="settings-section"><h3>저장된 사이트 내보내기</h3><p class="hint">저장된 모든 사이트를 Portway JSON 파일로 내보냅니다. S3·프록시·점프 서버 등 고급 설정을 포함합니다. 비밀번호·토큰·암호화 키 등 비밀 정보와 개인 키·인증서 파일은 제외합니다.</p><p class="hint">암호화 설정은 유지합니다. 연결 전에 기존 키를 다시 입력하세요.</p></section><section class="settings-section"><h3>사이트 가져오기</h3><p class="hint">Portway JSON 또는 WinSCP INI 파일을 선택하세요. 기존 사이트를 유지하고 새 사이트로 추가합니다. WinSCP INI는 기본 연결 정보만 가져오며 비밀번호·호스트 키는 이전하지 않고 S3는 건너뜁니다. 가져온 뒤 비밀 정보와 파일 경로를 확인하세요.</p><label for="import-file">사이트 파일 (.json / .ini)</label><input id="import-file" class="form-control" type="file" accept=".json,.ini" required></section>`,
    '<button type="button" data-close>닫기</button><button type="button" class="btn" id="export-sites"><i class="ti ti-file-export" aria-hidden="true"></i>내보내기</button><button type="submit" class="btn btn-primary primary"><i class="ti ti-file-import" aria-hidden="true"></i>가져오기</button>',
  );
  const fileInput = $("#import-file", dialog),
    error = $("#form-error", dialog);
  let busy = false;
  async function operate(handler) {
    if (busy) return;
    busy = true;
    const controls = $$("button, input", dialog);
    controls.forEach((control) => (control.disabled = true));
    error.textContent = "";
    try {
      await handler();
    } catch (ex) {
      error.textContent = ex.message;
    } finally {
      controls.forEach((control) => (control.disabled = false));
      busy = false;
    }
  }
  dialog.addEventListener("cancel", (event) => {
    if (busy) event.preventDefault();
  });
  fileInput.onchange = () => (error.textContent = "");
  $("#export-sites", dialog).onclick = () =>
    operate(async () => {
      const result = await api("/sites/export", "POST");
      if (result.cancelled) return;
      if (result.content != null) {
        // 브라우저 호스트의 다운로드는 임시 URL만 사용하고 사용 후 해제합니다.
        const url = URL.createObjectURL(
          new Blob([result.content], {
            type: "application/json;charset=utf-8",
          }),
        );
        const link = document.createElement("a");
        link.href = url;
        link.download = result.fileName;
        document.body.append(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
      }
      toast(result.count + "개 사이트를 내보냈습니다. · " + result.fileName);
    });
  $("form", dialog).onsubmit = (event) => {
    event.preventDefault();
    return operate(async () => {
      const file = fileInput.files[0];
      if (!file) throw Error("JSON 또는 INI 파일을 선택하세요.");
      if (file.size > 2 * 1024 * 1024)
        throw Error("사이트 파일은 2 MiB 이하여야 합니다.");
      const result = await api("/sites/import", "POST", {
        content: await file.text(),
      });
      state.sites = await api("/sites");
      renderSites();
      dialog.close();
      toast(
        result.imported +
          "개 사이트를 가져왔습니다." +
          (result.skipped ? " S3 " + result.skipped + "개 건너뜀." : ""),
      );
    });
  };
}
