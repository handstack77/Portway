export function createWorkflows({
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
}) {
  const $ = (q) => document.querySelector(q),
    $$ = (q) => [...document.querySelectorAll(q)];
  const safe = (fn) => async () => {
    try {
      await fn();
    } catch (e) {
      toast(e.message, true);
    }
  };
  const footer =
    '<button type="button" data-close>닫기</button><button type="submit" class="primary">실행</button>';
  const selected = () =>
    state[state.focus].entries.filter((f) =>
      state[state.focus].selected.has(f.path),
    );
  const requireRemote = () => {
    if (!session()) throw Error("서버에 연결하세요.");
    return "/remote/" + state.current;
  };
  async function terminal() {
    requireRemote();
    const { id } = await api("/terminals", "POST", {
      sessionId: state.current,
    });
    const dialog = modal(
      "SSH 터미널",
      '<div id="terminal-screen"></div>',
      '<button type="button" data-close>연결 종료</button>',
      "terminal-dialog",
    );
    const term = new window.Terminal({
        cursorBlink: true,
        fontSize: 16,
        // 터미널의 열 너비를 일정하게 유지하도록 영문은 고정폭 글꼴을, 한글은 포함된 글꼴을 사용합니다.
        fontFamily:
          "Consolas, 'Liberation Mono', 'Noto Sans KR Variable', monospace",
        scrollback: 5000,
        theme: terminalTheme(),
      }),
      fit = new window.FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open($("#terminal-screen"));
    fit.fit();
    term.focus();
    function terminalTheme() {
      const style = getComputedStyle(document.documentElement);
      const color = (name) => style.getPropertyValue(name).trim();
      return {
        background: color("--editor-bg"),
        foreground: color("--ink"),
        cursor: color("--accent"),
        selectionBackground: color("--editor-selection"),
      };
    }
    const themeChanged = () => {
      term.options.theme = terminalTheme();
    };
    window.addEventListener("portway-theme", themeChanged);
    let stopped = false,
      busy = false,
      chain = Promise.resolve();
    const send = (body) => {
      chain = chain
        .then(() => api("/terminals/" + id, "POST", body))
        .catch((e) => toast(e.message, true));
    };
    term.onData((data) => send({ data }));
    term.onResize(({ cols, rows }) => send({ columns: cols, rows }));
    send({ columns: term.cols, rows: term.rows });
    const observer = new ResizeObserver(() => {
      if (!stopped) fit.fit();
    });
    observer.observe($("#terminal-screen"));
    const timer = setInterval(async () => {
      if (busy || stopped) return;
      busy = true;
      try {
        const r = await api("/terminals/" + id);
        if (r.data)
          term.write(Uint8Array.from(atob(r.data), (c) => c.charCodeAt(0)));
        if (!r.connected) {
          clearInterval(timer);
          term.writeln("\r\n[서버 연결 종료]");
        }
      } catch (e) {
        clearInterval(timer);
        toast(e.message, true);
      } finally {
        busy = false;
      }
    }, 100);
    dialog.addEventListener(
      "close",
      () => {
        stopped = true;
        clearInterval(timer);
        observer.disconnect();
        window.removeEventListener("portway-theme", themeChanged);
        term.dispose();
        api("/terminals/" + id, "DELETE").catch(() => {});
      },
      { once: true },
    );
  }
  async function watches() {
    const items = await api("/watches");
    const d = modal(
      "지속 동기화",
      `<p class="hint">로컬 변경 감지와 주기적 서버 비교를 함께 사용합니다. 앱을 다시 시작하면 일시정지 상태로 복원됩니다.</p><div class="grid2">${field("localPath", "로컬 폴더", state.local.path)}${field("remotePath", "원격 폴더", state.remote.path)}<label>방향<select name="direction"><option value="upload">로컬 → 원격</option><option value="download">원격 → 로컬</option></select></label>${field("interval", "확인 간격 (초)", 10, "number")}</div><div id="watch-list">${items.map((w) => `<div class="settings-section"><strong>${esc(w.site)} · ${esc(w.status)}</strong><p class="hint">${esc(w.localPath)} → ${esc(w.remotePath)}<br>${esc(w.error || w.current)} · ${w.changes}개 처리</p><button type="button" data-watch="${w.id}" data-control="${["paused", "failed"].includes(w.status) ? "resume" : "pause"}">${["paused", "failed"].includes(w.status) ? "재개" : "일시정지"}</button> <button type="button" data-watch="${w.id}" data-control="delete">등록 해제</button></div>`).join("")}</div>`,
      '<button type="button" data-close>닫기</button><button type="submit" class="primary">새 감시 시작</button>',
    );
    advanced.attachTransfer(d, false);
    bindForm(async () => {
      requireRemote();
      const fd = new FormData(d.querySelector("form"));
      await api("/watches", "POST", {
        sync: {
          sessionId: state.current,
          localPath: fd.get("localPath"),
          remotePath: fd.get("remotePath"),
          direction: fd.get("direction"),
          options: { ...advanced.readOptions(fd), removeSource: false },
        },
        intervalSeconds: Number(fd.get("interval")),
      });
      await watches();
    });
    $$("[data-watch]").forEach(
      (b) =>
        (b.onclick = safe(async () => {
          await api("/watches/" + b.dataset.watch, "POST", {
            action: b.dataset.control,
            sessionId: state.current,
          });
          await watches();
        })),
    );
  }
  async function trash() {
    const items = await api("/trash");
    modal(
      "복구 가능한 휴지통",
      `<p class="hint">원본과 같은 부모 폴더의 .portway-trash에 보관합니다. 원격 파일은 원래 서버에 연결한 후 복원하세요.</p>${items.map((t) => `<div class="settings-section"><strong>${esc(t.original)}</strong><p class="hint">${t.local ? "내 컴퓨터" : esc(t.site?.name)} · ${esc(t.deletedAt)} · ${esc(t.state)}</p><button type="button" data-restore="${t.id}">원래 위치로 복원</button></div>`).join("") || "<p>휴지통이 비어 있습니다.</p>"}`,
    );
    $$("[data-restore]").forEach(
      (b) =>
        (b.onclick = safe(async () => {
          await api("/trash/" + b.dataset.restore + "/restore", "POST", {
            action: "restore",
            sessionId: state.current,
          });
          await refresh();
          await trash();
        })),
    );
  }
  async function editors() {
    const items = await api("/external-edits");
    modal(
      "외부 편집 세션",
      `<p class="hint">저장하면 서버에 자동 업로드합니다. 충돌이 생기면 로컬 사본을 보존하고 덮어쓰기를 중단합니다. 종료 후에도 복구 사본은 유지됩니다.</p>${items.map((e) => `<div class="settings-section"><strong>${esc(e.remote)} · ${esc(e.status)}</strong><p class="hint">${esc(e.local)}<br>${esc(e.error || "변경 감시 중")}</p>${e.status === "conflict" ? `<button type="button" data-edit-id="${e.id}" data-control="overwrite">로컬 사본으로 서버 덮어쓰기</button>` : ""} <button type="button" data-edit-id="${e.id}" data-control="stop">자동 저장 종료</button></div>`).join("") || "<p>외부 편집 세션이 없습니다.</p>"}`,
    );
    $$("[data-edit-id]").forEach(
      (b) =>
        (b.onclick = safe(async () => {
          await api(
            "/external-edits/" + b.dataset.editId + "/control",
            "POST",
            { action: b.dataset.control },
          );
          await editors();
        })),
    );
  }
  async function search() {
    const endpoint = requireRemote();
    modal(
      "원격 하위 폴더 검색",
      `${field("path", "시작 폴더", state.remote.path)}${field("mask", "파일 마스크", "*")} ${field("text", "포함된 텍스트 (선택)")}${field("encoding", "텍스트 인코딩", "utf-8")}<div id="search-results"></div>`,
      footer,
    );
    bindForm(async () => {
      const fd = new FormData($("#modal-form"));
      const result = await api(
        endpoint + "/search",
        "POST",
        Object.fromEntries(fd),
      );
      $("#search-results").innerHTML =
        `<p class="hint">${result.scanned}개 확인 · ${result.entries.length}개 결과 ${result.truncated ? "(결과 한도 도달)" : ""}</p>` +
        result.entries
          .map(
            (e) =>
              `<button type="button" data-found="${esc(e.isDirectory ? e.path : e.path.slice(0, e.path.lastIndexOf("/")) || "/")}">${esc(e.path)}</button>`,
          )
          .join("");
      $$("[data-found]").forEach(
        (b) =>
          (b.onclick = safe(async () => {
            $("#dialog").close();
            await load("remote", b.dataset.found);
          })),
      );
    });
  }
  async function action(name) {
    if (name === "custom-command") {
      requireRemote();
      const commands = (state.prefs.commands || []).filter(
        (c) => c.remote !== false,
      );
      if (!commands.length) throw Error("설정에서 사용자 명령을 등록하세요.");
      modal(
        "사용자 명령 실행",
        `<label>저장된 SSH 명령<select name="customName">${commands.map((c) => `<option value="${esc(c.name)}">${esc(c.name)}</option>`).join("")}</select></label><p class="hint">현재 원격 폴더와 선택 파일을 명령에 전달합니다.</p><pre class="code-box" id="custom-output"></pre>`,
        footer,
      );
      bindForm(async () => {
        const r = await api("/commands/run", "POST", {
          sessionId: state.current,
          name: $("[name=customName]").value,
          directory: state.remote.path,
          paths: [...state.remote.selected],
        });
        $("#custom-output").textContent = r.output;
      });
      return true;
    }
    if (name === "terminal") {
      await terminal();
      return true;
    }
    if (name === "watches") {
      await watches();
      return true;
    }
    if (name === "trash") {
      await trash();
      return true;
    }
    if (name === "editors") {
      await editors();
      return true;
    }
    if (name === "search-deep") {
      await search();
      return true;
    }
    if (
      ![
        "recycle",
        "external-edit",
        "copy",
        "link",
        "properties",
        "checksum",
        "permissions",
      ].includes(name)
    )
      return false;
    const files = selected();
    if (!files.length) throw Error("파일을 선택하세요.");
    const side = state.focus,
      sessionId = state.current;
    const endpoint = isLocal(side) ? "/local" : requireRemote();
    if (name === "recycle") {
      let completed = 0;
      try {
        for (const f of files) {
          await api("/trash", "POST", {
            path: f.path,
            sessionId: isLocal(side) ? null : sessionId,
          });
          completed++;
        }
        toast(`${completed}개 항목을 휴지통으로 이동했습니다.`);
      } catch (error) {
        throw Error(
          `${completed}/${files.length}개 처리 후 중단: ${error.message}`,
        );
      } finally {
        if (side === "local" || state.current === sessionId)
          await load(side, state[side].path);
      }
      return true;
    }
    if (name === "checksum") {
      if (files.some((f) => f.isDirectory))
        throw Error("체크섬은 파일만 선택하세요.");
      const d = modal(
        "SHA256 체크섬",
        '<p class="hint" id="checksum-progress" role="status">계산 중…</p><div class="batch-results"></div>',
      );
      let completed = 0;
      for (const file of files) {
        if (!d.open) break;
        let value,
          failed = false;
        try {
          value = (
            await api(endpoint + "/checksum", "POST", {
              path: file.path,
              algorithm: "sha256",
            })
          ).hash;
        } catch (error) {
          value = error.message;
          failed = true;
        }
        if (!d.open) break;
        const row = document.createElement("div");
        row.className = "batch-result";
        row.innerHTML = `<strong>${esc(file.name)}</strong><code ${failed ? 'class="danger"' : ""}>${esc(value)}</code>`;
        d.querySelector(".batch-results").append(row);
        d.querySelector("#checksum-progress").textContent =
          `${++completed}/${files.length}개 처리 · 파일별 결과를 확인하세요.`;
      }
      return true;
    }
    if (name === "permissions") {
      if (side !== "remote" || !session()?.capabilities.permissions)
        throw Error("권한 변경을 지원하는 원격 서버에 연결하세요.");
      const d = modal(
        "권한 · 소유자 변경",
        `<p class="hint">선택한 ${files.length}개 항목에 같은 권한을 적용합니다.</p><div class="code-box">${files.map((f) => esc(f.name)).join("\n")}</div>${field("octal", "8진수 권한 (예: 755)", files.length === 1 ? files[0].permissions || "755" : "")}<div class="grid2">${field("owner", "소유자 UID (선택)", "", "number")}${field("group", "그룹 GID (선택)", "", "number")}</div><label class="check"><input type="checkbox" name="recursive">하위 폴더와 파일에 적용</label>`,
        footer,
      );
      bindForm(async () => {
        const fd = new FormData(d.querySelector("form"));
        const octal = fd.get("octal").trim();
        if (!/^[0-7]{3,4}$/.test(octal))
          throw Error("권한은 755 또는 0644처럼 3~4자리 8진수로 입력하세요.");
        let completed = 0;
        try {
          for (const file of files) {
            await api(endpoint + "/chmod", "POST", {
              path: file.path,
              octal,
              recursive: fd.has("recursive"),
              owner: fd.get("owner") ? Number(fd.get("owner")) : null,
              group: fd.get("group") ? Number(fd.get("group")) : null,
            });
            completed++;
          }
          d.close();
          toast(`${completed}개 항목의 권한을 변경했습니다.`);
        } catch (error) {
          throw Error(
            `${completed}/${files.length}개 적용 후 중단: ${error.message}`,
          );
        } finally {
          if (state.current === sessionId) await load(side, state[side].path);
        }
      });
      return true;
    }
    if (files.length !== 1) throw Error("항목 하나를 선택하세요.");
    const file = files[0];
    if (name === "external-edit") {
      if (state.focus !== "remote") throw Error("원격 파일을 선택하세요.");
      await api("/external-edits/" + state.current, "POST", {
        path: file.path,
      });
      await editors();
      return true;
    }
    if (state.focus !== "remote")
      throw Error("이 작업은 원격 파일을 선택하세요.");
    if (name === "properties") {
      const r = await api(endpoint + "/properties", "POST", {
        path: file.path,
      });
      modal(
        "파일 속성",
        `<p>${esc(file.path)}</p><p>${r.files}개 파일 · ${r.folders}개 폴더 · ${r.bytes.toLocaleString()} 바이트</p><p>권한 ${esc(r.entry.permissions)} · 소유자 ${esc(r.entry.owner || "—")} · 그룹 ${esc(r.entry.group || "—")}</p>`,
      );
      return true;
    }

    modal(
      name === "copy" ? "원격 복사" : "심볼릭 링크 생성",
      field(
        "destination",
        "새 절대 경로",
        file.path + (name === "copy" ? ".copy" : ".link"),
      ),
      footer,
    );
    bindForm(async () => {
      await api(endpoint + "/" + name, "POST", {
        path: file.path,
        destination: $("[name=destination]").value,
      });
      $("#dialog").close();
      await refresh();
    });
    return true;
  }
  return { action };
}
