import { createDocumentState } from "./editor-state.js";

let monacoPromise;
function loadMonaco() {
  monacoPromise ??= import("./vendor/monaco/monaco.js").catch((error) => {
    monacoPromise = null;
    throw error;
  });
  return monacoPromise;
}

export async function showTextEditor({
  file,
  side,
  data,
  write,
  onSaved,
  modal,
  confirmBox,
  toast,
  esc,
}) {
  const name = file.name || file.path.split(/[\\/]/).pop();
  // 내장 WebKit의 사용자 에이전트를 포함해 Monaco의 플랫폼 감지 방식에 맞춥니다.
  const shortcut = navigator.userAgent.includes("Macintosh") ? "⌘" : "Ctrl";
  const d = modal(
    "텍스트 편집",
    `<div class="editor-file flex ai:center gap:12">
      <span class="editor-file-icon"><i class="ti ti-file-code" aria-hidden="true"></i></span>
      <div class="editor-file-info"><div class="editor-file-title"><strong>${esc(name)}</strong><span class="editor-location badge">${side === "local" ? "내 컴퓨터" : "원격 파일"}</span><span id="editor-dirty" class="editor-dirty" hidden aria-label="저장하지 않은 변경 내용"></span></div><div class="editor-path mono" title="${esc(file.path)}">${esc(file.path)}</div></div>
      <span id="editor-save-state" class="editor-save-state" role="status">편집기 준비 중</span>
    </div>
    <div class="editor-toolbar">
      <div class="editor-options flex ai:center gap:12">
        <label class="editor-option">인코딩<input class="form-control form-control-sm" name="textEncoding" aria-label="저장 인코딩" list="editor-encodings" value="${esc(data.encoding)}" autocomplete="off" spellcheck="false" required></label>
        <datalist id="editor-encodings">${["utf-8", "utf-16le", "utf-16be", "utf-32le", "utf-32be", "cp949", "windows-1252"].map((e) => `<option value="${e}"></option>`).join("")}</datalist>
        <label class="check editor-bom"><input class="form-check-input" type="checkbox" name="textBom" ${data.bom ? "checked" : ""}>BOM</label>
        <label class="editor-option">언어<select class="form-select form-select-sm" id="editor-language" aria-label="문서 언어" disabled><option>자동 감지</option></select></label>
      </div>
      <div class="editor-actions flex ai:center gap:4" role="group" aria-label="편집 도구">
        <button type="button" class="btn btn-sm" id="editor-find" title="찾기 및 바꾸기" disabled><i class="ti ti-search" aria-hidden="true"></i>찾기</button>
        <button type="button" class="btn btn-sm" id="editor-wrap" aria-pressed="false" title="자동 줄바꿈" disabled><i class="ti ti-text-wrap" aria-hidden="true"></i>줄바꿈</button>
      </div>
    </div>
    <div class="editor-stage"><div id="monaco-host" aria-label="텍스트 편집 영역"></div><div id="editor-loading" class="editor-loading" role="status"><span class="spinner-border spinner-border-sm" aria-hidden="true"></span><span>편집기를 불러오는 중…</span></div></div>
    <div class="editor-statusbar"><span id="editor-position">줄 1, 열 1</span><span id="editor-eol"></span><span class="editor-hint">${shortcut}+F 찾기 · F1 명령 팔레트</span></div>`,
    `<span class="left editor-save-hint"><i class="ti ti-shield-check" aria-hidden="true"></i>저장 전 파일 변경 여부 확인</span><button type="button" class="btn" data-close>닫기</button><button type="submit" class="btn btn-primary primary" id="editor-save" disabled><i class="ti ti-device-floppy" aria-hidden="true"></i>저장 <kbd>${shortcut}+S</kbd></button>`,
    "editor-dialog",
  );
  const $ = (q) => d.querySelector(q);
  const form = $("form"),
    host = $("#monaco-host"),
    saveButton = $("#editor-save"),
    encoding = $("[name=textEncoding]"),
    bom = $("[name=textBom]"),
    error = $("#form-error"),
    status = $("#editor-save-state");
  const disposables = [];
  let editor,
    model,
    documentState,
    disposed = false,
    saving = false,
    confirming = false,
    wrap = false;
  const dirty = () =>
    !!editor &&
    documentState.isDirty(model.getValue(), encoding.value, bom.checked);
  function refreshState() {
    if (disposed) return;
    const changed = dirty();
    $("#editor-dirty").hidden = !changed;
    status.textContent = saving
      ? "저장 중…"
      : changed
        ? "저장하지 않은 변경 내용"
        : "저장됨";
    status.classList.toggle("is-dirty", changed);
    saveButton.disabled = !editor || saving || !changed;
    encoding.disabled = bom.disabled = saving;
    d.querySelectorAll("[data-close]").forEach((b) => {
      b.disabled = saving;
    });
    d.setAttribute("aria-busy", String(saving));
  }
  async function save() {
    if (!editor || saving || disposed || !dirty() || !form.reportValidity())
      return;
    const content = model.getValue();
    const snapshot = documentState.snapshot(
      content,
      encoding.value,
      bom.checked,
    );
    saving = true;
    error.textContent = "";
    refreshState();
    try {
      const result = await write(snapshot);
      if (disposed) return;
      // 요청 처리 중에도 사용자가 계속 입력할 수 있습니다.
      documentState.accept(result, content);
      encoding.value = result.encoding;
      bom.checked = result.bom;
      toast("파일을 저장했습니다.");
      Promise.resolve(onSaved()).catch((e) =>
        toast("파일 목록 새로고침 실패: " + e.message, true),
      );
    } catch (e) {
      if (!disposed) error.textContent = e.message;
    } finally {
      saving = false;
      refreshState();
    }
  }
  form.onsubmit = (e) => {
    e.preventDefault();
    save();
  };
  const close = async () => {
    if (saving || confirming || disposed) return;
    if (dirty()) {
      confirming = true;
      const discard = await confirmBox(
        "저장하지 않은 변경 내용",
        `${name}의 변경 내용을 버리고 닫을까요?`,
        "변경 내용 버리기",
        true,
      );
      confirming = false;
      if (!discard || disposed) {
        editor?.focus();
        return;
      }
    }
    d.close();
  };
  d.querySelectorAll("[data-close]").forEach((b) => {
    b.onclick = close;
  });
  d.oncancel = (e) => {
    e.preventDefault();
    close();
  };
  const beforeUnload = (e) => {
    if (dirty() || saving) {
      e.preventDefault();
      e.returnValue = "";
    }
  };
  window.addEventListener("beforeunload", beforeUnload);
  d.addEventListener(
    "close",
    () => {
      disposed = true;
      window.removeEventListener("beforeunload", beforeUnload);
      disposables.forEach((item) => item.dispose());
      editor?.dispose();
      model?.dispose();
      d.oncancel = null;
    },
    { once: true },
  );
  d.addEventListener("keydown", (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
      e.preventDefault();
      e.stopPropagation();
      save();
    }
    // Escape는 먼저 Monaco의 검색·추천·명령 위젯을 닫습니다.
    if (e.key === "Escape" && host.contains(e.target)) e.preventDefault();
  });
  encoding.oninput = bom.onchange = refreshState;
  try {
    const { monaco } = await loadMonaco();
    await document.fonts.load('16px "Noto Sans KR Variable"', "한글 ABC 0123");
    if (disposed || !d.open) return;
    const languages = monaco.languages.getLanguages();
    const lowerName = name.toLowerCase();
    const language =
      languages.find((l) =>
        l.filenames?.some((n) => n.toLowerCase() === lowerName),
      ) ||
      languages
        .filter((l) =>
          l.extensions?.some((ext) => lowerName.endsWith(ext.toLowerCase())),
        )
        .sort(
          (a, b) =>
            Math.max(...b.extensions.map((e) => e.length)) -
            Math.max(...a.extensions.map((e) => e.length)),
        )[0];
    model = monaco.editor.createModel(
      data.content,
      language?.id || "plaintext",
      monaco.Uri.from({
        scheme: "inmemory",
        authority: "portway",
        path: `/${crypto.randomUUID()}/${name}`,
      }),
    );
    documentState = createDocumentState(data, model.getValue());
    function applyTheme() {
      const dark = window.portwayTheme.resolved === "dark";
      const style = getComputedStyle(document.documentElement);
      // 계산된 사용자 정의 속성으로 Tabler의 라이트·다크 테마 토큰을 해석합니다.
      // Monaco는 16진수 색상을 요구하지만 Tabler 다크 테마 토큰은 rgb()나 color-mix()로 계산될 수 있습니다.
      const canvas = document.createElement("canvas");
      canvas.width = canvas.height = 1;
      const ctx = canvas.getContext("2d", { willReadFrequently: true });
      const color = (name) => {
        ctx.clearRect(0, 0, 1, 1);
        ctx.fillStyle = style.getPropertyValue(name).trim();
        ctx.fillRect(0, 0, 1, 1);
        return (
          "#" +
          [...ctx.getImageData(0, 0, 1, 1).data]
            .map((n) => n.toString(16).padStart(2, "0"))
            .join("")
        );
      };
      monaco.editor.defineTheme("portway", {
        base: dark ? "vs-dark" : "vs",
        inherit: true,
        rules: [],
        colors: {
          "editor.background": color("--editor-bg"),
          "editor.foreground": color("--ink"),
          "editorGutter.background": color("--editor-bg"),
          "editorLineNumber.foreground": color("--muted"),
          "editorCursor.foreground": color("--accent"),
          "editor.lineHighlightBackground": color("--canvas"),
          "editor.selectionBackground": color("--editor-selection"),
          "editorWidget.background": color("--floating-bg"),
          "editorWidget.border": color("--line"),
          "input.background": color("--input-bg"),
          "input.foreground": color("--ink"),
          "input.border": color("--line"),
          focusBorder: color("--accent"),
        },
      });
      monaco.editor.setTheme("portway");
    }
    applyTheme();
    window.addEventListener("portway-theme", applyTheme);
    disposables.push({
      dispose: () => window.removeEventListener("portway-theme", applyTheme),
    });
    editor = monaco.editor.create(host, {
      model,
      theme: "portway",
      automaticLayout: true,
      ariaLabel: `${name} 파일 내용`,
      fontFamily: "'Noto Sans KR Variable', 'Noto Sans KR', sans-serif",
      fontSize: 16,
      lineHeight: 25,
      tabSize: 4,
      minimap: { enabled: false },
      padding: { top: 16, bottom: 16 },
      scrollBeyondLastLine: false,
      wordWrap: "off",
      renderWhitespace: "selection",
      fixedOverflowWidgets: false,
      links: false,
      unicodeHighlight: { ambiguousCharacters: false },
    });
    const updatePosition = () => {
      const p = editor.getPosition();
      $("#editor-position").textContent = `줄 ${p.lineNumber}, 열 ${p.column}`;
      $("#editor-eol").textContent = model.getEOL() === "\r\n" ? "CRLF" : "LF";
    };
    disposables.push(
      editor.onDidChangeCursorPosition(updatePosition),
      model.onDidChangeContent(() => {
        refreshState();
        updatePosition();
      }),
    );
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, save);
    const select = $("#editor-language");
    select.innerHTML = languages
      .sort((a, b) =>
        (a.aliases?.[0] || a.id).localeCompare(b.aliases?.[0] || b.id),
      )
      .map(
        (l) =>
          `<option value="${esc(l.id)}">${esc(l.aliases?.[0] || l.id)}</option>`,
      )
      .join("");
    select.value = model.getLanguageId();
    select.disabled = false;
    select.onchange = () => {
      monaco.editor.setModelLanguage(model, select.value);
      editor.focus();
    };
    $("#editor-find").disabled = $("#editor-wrap").disabled = false;
    $("#editor-find").onclick = () => {
      editor.focus();
      editor.getAction("actions.find").run();
    };
    $("#editor-wrap").onclick = () => {
      wrap = !wrap;
      editor.updateOptions({ wordWrap: wrap ? "on" : "off" });
      $("#editor-wrap").setAttribute("aria-pressed", String(wrap));
      editor.focus();
    };
    $("#editor-loading").remove();
    updatePosition();
    refreshState();
    editor.focus();
  } catch (e) {
    if (!disposed) {
      $("#editor-loading").textContent =
        "편집기를 불러오지 못했습니다. 앱을 다시 시작해 주세요.";
      error.textContent = "Monaco Editor 로드 실패: " + e.message;
      status.textContent = "불러오기 실패";
    }
  }
}
