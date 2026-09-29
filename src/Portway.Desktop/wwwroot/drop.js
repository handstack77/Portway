const MAX_ENTRIES = 20000;
const checkCancelled = (signal) => signal?.throwIfAborted();

// 드롭 이벤트 안에서 항목 참조를 동기적으로 확보합니다. DataTransfer는
// 이벤트 처리 함수가 반환되는 즉시 다시 보호 상태가 됩니다. 특히 WebKit에서 주의해야 합니다.
export function captureDrop(dataTransfer) {
  const items = [...(dataTransfer.items || [])].filter(
    (item) => item.kind === "file",
  );
  return {
    items: items.map((item) => ({
      entry: item.webkitGetAsEntry?.(),
      file: item.getAsFile(),
    })),
    files: [...dataTransfer.files],
  };
}
export async function collectDrop(captured, signal) {
  const entries = [];
  const add = (path, isDirectory, file) => {
    checkCancelled(signal);
    if (entries.length >= MAX_ENTRIES)
      throw Error("한 번에 최대 20,000개 항목을 전송할 수 있습니다.");
    if (path.split("/").length > 64)
      throw Error("폴더 깊이는 64단계 이하여야 합니다.");
    entries.push({
      path,
      isDirectory,
      size: file?.size || 0,
      modified: file ? new Date(file.lastModified).toISOString() : null,
      file,
    });
  };
  async function walk(entry, parent = "") {
    checkCancelled(signal);
    const path = parent + entry.name;
    if (entry.isFile)
      add(
        path,
        false,
        await new Promise((resolve, reject) => entry.file(resolve, reject)),
      );
    else if (entry.isDirectory) {
      add(path, true);
      const reader = entry.createReader();
      while (true) {
        checkCancelled(signal);
        const batch = await new Promise((resolve, reject) =>
          reader.readEntries(resolve, reject),
        );
        if (!batch.length) break; // Chromium은 디렉토리 항목을 100개씩 나누어 반환합니다.
        for (const child of batch) await walk(child, path + "/");
      }
    } else throw Error("이 항목을 읽을 수 없습니다: " + path);
  }
  if (captured.items.length) {
    for (const item of captured.items) {
      if (item.entry) await walk(item.entry);
      else if (item.file) add(item.file.name, false, item.file);
      else
        throw Error(
          "폴더를 읽을 수 없습니다. 외부 파일 추가의 폴더 선택을 사용하세요.",
        );
    }
  } else {
    const directories = new Set();
    for (const file of captured.files) {
      const path = file.webkitRelativePath || file.name;
      const parts = path.split("/");
      for (let i = 1; i < parts.length; i++) {
        const dir = parts.slice(0, i).join("/");
        if (!directories.has(dir)) {
          add(dir, true);
          directories.add(dir);
        }
      }
      add(path, false, file);
    }
  }
  if (!entries.length) throw Error("읽을 수 있는 파일·폴더가 없습니다.");
  return entries;
}

export function createExternalDrop({
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
}) {
  let preparing = false;
  function requireTarget() {
    if (!session()) throw Error("먼저 전송할 원격 서버에 연결하세요.");
    if (preparing || document.querySelector("#dialog").open)
      throw Error("현재 작업 창을 마친 뒤 파일을 추가하세요.");
    return {
      sessionId: state.current,
      name: session().name,
      destination: state.remote.path,
    };
  }
  async function receive(captured, target) {
    preparing = true;
    const controller = new AbortController();
    const d = modal(
      "외부 파일 업로드",
      '<p class="hint">파일과 폴더를 읽는 중…</p>',
      '<button type="button" data-close>취소</button>',
    );
    let dropId,
      queued = false,
      uploading = false;
    const closed = () => {
      controller.abort();
      preparing = false;
    };
    d.addEventListener("close", closed, { once: true });
    try {
      const entries = await collectDrop(captured, controller.signal);
      if (!d.open) return;
      const files = entries.filter((e) => !e.isDirectory);
      const total = files.reduce((n, e) => n + e.size, 0);
      d.querySelector(".modal-body").innerHTML =
        `<div class="drop-summary flex ai:center gap:12"><i class="ti ti-folder-up" aria-hidden="true"></i><div><strong>${files.length}개 파일 · ${entries.length - files.length}개 폴더</strong><div class="hint mb:0">${size(total)} · 원본 파일 유지</div></div></div><label class="mt:16">전송 대상</label><div class="code-box">${esc(target.name)} · ${esc(target.destination)}</div><div class="drop-file-list mt:12">${entries
          .filter((e) => !e.path.includes("/"))
          .slice(0, 30)
          .map(
            (e) =>
              `<div class="flex ai:center gap:8"><i aria-hidden="true" class="ti ti-${e.isDirectory ? "folder" : "file"}"></i><span>${esc(e.path)}</span></div>`,
          )
          .join(
            "",
          )}${entries.filter((e) => !e.path.includes("/")).length > 30 ? "<div>…</div>" : ""}</div><label class="mt:16">같은 이름의 파일<select class="form-select" name="conflict"><option value="skip">건너뛰기</option><option value="replace">덮어쓰기</option><option value="newer">더 최신 파일만</option><option value="rename">다른 이름으로 보관</option></select></label><p class="hint">하위 폴더 구조와 빈 폴더를 보존합니다. 전송 준비가 끝나면 큐에서 일시정지·재시도할 수 있습니다.</p><div id="drop-progress" class="drop-progress" hidden><label id="drop-status" aria-live="polite"></label><progress max="100" value="0" aria-label="외부 파일 준비 진행률"></progress></div><div class="form-error" id="form-error" role="alert"></div>`;
      d.querySelector(".modal-foot").innerHTML =
        '<button type="button" id="drop-cancel" class="btn">취소</button><button type="submit" class="btn btn-primary primary">전송 시작</button>';
      d.querySelector("#drop-cancel").onclick = () => {
        controller.abort();
        d.close();
      };
      advanced.attachTransfer(d, false);
      // 고급 프리셋에서 이동을 지정해도 외부 드롭은 항상 복사로 처리합니다.
      const lockCopy = () => {
        const remove = d.querySelector("[name=opt_move]");
        if (remove) {
          remove.checked = false;
          remove.disabled = true;
          remove.closest("label").hidden = true;
        }
      };
      lockCopy();
      d.querySelector("[name=opt_preset]").addEventListener("change", lockCopy);
      bindForm(async () => {
        if (uploading) return;
        uploading = true;
        const options = {
          ...advanced.readOptions(new FormData(d.querySelector("form"))),
          removeSource: false,
        };
        const conflict = d.querySelector("[name=conflict]").value;
        const progress = d.querySelector("#drop-progress");
        progress.hidden = false;
        d.querySelectorAll("input,select,details").forEach((e) => {
          if ("disabled" in e) e.disabled = true;
        });
        try {
          const created = await api("/drops", "POST", {
            entries: entries.map(({ file, ...entry }) => entry),
          });
          dropId = created.id;
          checkCancelled(controller.signal);
          let done = 0;
          for (let i = 0; i < entries.length; i++) {
            const entry = entries[i];
            if (entry.isDirectory) continue;
            for (
              let offset = 0;
              offset < entry.size;
              offset += created.chunkSize
            ) {
              checkCancelled(controller.signal);
              const body = entry.file.slice(offset, offset + created.chunkSize);
              const r = await fetch(
                `/api/drops/${dropId}/files/${i}?offset=${offset}`,
                {
                  method: "PUT",
                  headers: {
                    Authorization: "Bearer " + token,
                    "Content-Type": "application/octet-stream",
                  },
                  body,
                  signal: controller.signal,
                },
              );
              if (!r.ok) {
                const error = await r.json().catch(() => ({}));
                throw Error(error.detail || "파일 준비 실패: " + r.status);
              }
              done += body.size;
              progress.querySelector("progress").value = total
                ? (done / total) * 100
                : 100;
              d.querySelector("#drop-status").textContent =
                `${entry.path} · ${size(done)} / ${size(total)} 준비 중`;
            }
          }
          checkCancelled(controller.signal);
          // 확정 요청은 반복해도 결과가 같습니다. 전송 큐에 원자적으로 전달하는 동안 창을 닫지 못하게 합니다.
          d.querySelector("#drop-cancel").disabled = true;
          d.querySelectorAll("[data-close]").forEach(
            (b) => (b.disabled = true),
          );
          const prevent = (event) => event.preventDefault();
          d.addEventListener("cancel", prevent);
          try {
            const request = {
              sessionId: target.sessionId,
              destination: target.destination,
              direction: "upload",
              paths: [],
              conflict,
              options,
            };
            try {
              await api("/drops/" + dropId + "/commit", "POST", request);
            } catch (error) {
              if (error instanceof TypeError)
                await api("/drops/" + dropId + "/commit", "POST", request);
              else throw error;
            }
            queued = true;
          } finally {
            d.removeEventListener("cancel", prevent);
            d.querySelector("#drop-cancel").disabled = false;
            d.querySelectorAll("[data-close]").forEach(
              (b) => (b.disabled = false),
            );
          }
          d.close();
          await poll();
          toast("외부 파일·폴더를 전송 큐에 추가했습니다.");
        } catch (error) {
          if (!controller.signal.aborted) {
            d.querySelector("#form-error").textContent =
              error.message + " 파일을 다시 추가해 재시도하세요.";
            d.querySelector("[type=submit]").hidden = true;
          }
        } finally {
          if (dropId && !queued)
            await api("/drops/" + dropId, "DELETE").catch(() => {});
          uploading = false;
        }
      });
    } catch (error) {
      if (!controller.signal.aborted) {
        d.close();
        toast(error.message, true);
      }
    }
  }
  function wire() {
    const area = document.querySelector("#pane-remote");
    const external = (e) =>
      [...(e.dataTransfer?.types || [])].includes("Files") &&
      ![...(e.dataTransfer?.types || [])].includes("application/x-portway");
    document.addEventListener("dragover", (e) => {
      if (!external(e)) return;
      e.preventDefault();
      const valid =
        area.contains(e.target) &&
        session() &&
        !preparing &&
        !document.querySelector("#dialog").open;
      e.dataTransfer.dropEffect = valid ? "copy" : "none";
      area.classList.toggle("external-drop", Boolean(valid));
    });
    document.addEventListener("dragleave", (e) => {
      if (!e.relatedTarget) area.classList.remove("external-drop");
    });
    document.addEventListener("dragend", () =>
      area.classList.remove("external-drop"),
    );
    document.addEventListener("drop", (e) => {
      if (!external(e)) return;
      e.preventDefault();
      area.classList.remove("external-drop");
      try {
        if (!area.contains(e.target))
          throw Error("원격 서버 패널에 파일·폴더를 놓으세요.");
        const target = requireTarget();
        const captured = captureDrop(e.dataTransfer);
        void receive(captured, target);
      } catch (error) {
        toast(error.message, true);
      }
    });
  }
  function pick(directory = false) {
    const target = requireTarget();
    const input = document.createElement("input");
    input.type = "file";
    input.multiple = true;
    input.hidden = true;
    if (directory) input.setAttribute("webkitdirectory", "");
    input.onchange = () => {
      const files = [...input.files];
      input.remove();
      if (files.length) void receive({ items: [], files }, target);
    };
    input.addEventListener("cancel", () => input.remove());
    document.body.append(input);
    input.click();
  }
  return { wire, pick };
}
