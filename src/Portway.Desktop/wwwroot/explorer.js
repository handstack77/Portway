import { reconcileSelection, selectPath, selectBox } from "./selection.js";

export function createExplorer({
  state,
  visibleEntries,
  renderPane,
  load,
  session,
  run,
  action,
  transfer,
  esc,
  size,
  toast,
}) {
  const $ = (q) => document.querySelector(q);
  const listFor = (side) => $("#files-" + side);
  const pathsFor = (side) => visibleEntries(side).map((f) => f.path);
  let menu,
    menuSide,
    gesture,
    drag,
    search = "",
    lastTyped = 0;
  const rows = (side) => [...listFor(side).querySelectorAll("tr[data-path]")];
  function focusedList() {
    if (!document.hasFocus()) return null;
    const list = document.activeElement?.closest(".filelist");
    return list?.id === "files-local" || list?.id === "files-remote"
      ? list
      : null;
  }
  function syncFocus() {
    const list = focusedList();
    for (const side of ["local", "remote"])
      $("#pane-" + side).classList.toggle("focused", list === listFor(side));
  }
  function focus(side, list = true) {
    state.focus = side;
    if (list) listFor(side).focus({ preventScroll: true });
    syncFocus();
  }
  function renderSelection(side) {
    syncFocus();
    const pane = state[side],
      entries = visibleEntries(side),
      paths = entries.map((f) => f.path);
    Object.assign(pane, reconcileSelection(pane, paths));
    const list = listFor(side);
    for (const row of rows(side)) {
      const selected = pane.selected.has(row.dataset.path);
      row.classList.toggle("selected", selected);
      row.classList.toggle("cursor", row.dataset.path === pane.cursor);
      row.setAttribute("aria-selected", String(selected));
      row.querySelector("[data-row-check]").checked = selected;
    }
    const current = rows(side).find((row) => row.dataset.path === pane.cursor);
    if (current) list.setAttribute("aria-activedescendant", current.id);
    else list.removeAttribute("aria-activedescendant");
    const all = list.querySelector("[data-select-all]");
    if (all) {
      all.checked = paths.length > 0 && pane.selected.size === paths.length;
      all.indeterminate =
        pane.selected.size > 0 && pane.selected.size < paths.length;
    }
    const footer = $("#footer-" + side);
    if (!footer.querySelector(".selection-count")) return;
    const selected = entries.filter((f) => pane.selected.has(f.path));
    const files = selected.filter((f) => !f.isDirectory),
      folders = selected.length - files.length;
    const count = footer.querySelector(".selection-count");
    count.textContent = selected.length
      ? `${selected.length}개 선택`
      : `${entries.length}개 항목`;
    count.title = selected.length
      ? `폴더 ${folders}개 · 파일 ${files.length}개 · 파일 크기 ${size(files.reduce((n, f) => n + f.size, 0))} (폴더 내부 제외)`
      : "체크박스 또는 빈 공간 드래그로 여러 항목 선택";
    footer.querySelector(".selection-size").textContent = selected.length
      ? `폴더 ${folders} · 파일 ${files.length}`
      : size(
          entries.filter((f) => !f.isDirectory).reduce((n, f) => n + f.size, 0),
        );
    footer
      .querySelectorAll("[data-selection-required]")
      .forEach((b) => (b.disabled = !selected.length));
    footer.querySelector("[data-explorer-action=clear]").hidden =
      !selected.length;
  }
  function select(side, path, options) {
    Object.assign(
      state[side],
      selectPath(state[side], pathsFor(side), path, options),
    );
    renderSelection(side);
  }
  function scrollCursor(side) {
    const list = listFor(side);
    const row = rows(side).find((r) => r.dataset.path === state[side].cursor);
    if (!row) return;
    const bounds = list.getBoundingClientRect();
    const rect = row.getBoundingClientRect();
    const top =
      bounds.top +
      (list.querySelector("thead")?.getBoundingClientRect().height || 0);
    if (rect.top < top) list.scrollTop -= top - rect.top;
    else if (rect.bottom > bounds.bottom)
      list.scrollTop += rect.bottom - bounds.bottom;
  }
  function choose(side, type) {
    const paths = pathsFor(side),
      p = state[side];
    p.selected = new Set(
      type === "all"
        ? paths
        : type === "invert"
          ? paths.filter((path) => !p.selected.has(path))
          : [],
    );
    if (type === "clear") p.anchor = null;
    renderSelection(side);
  }
  function menuItems(side) {
    const items = visibleEntries(side).filter((f) =>
      state[side].selected.has(f.path),
    );
    const count = items.length,
      one = count === 1,
      file = one && !items[0].isDirectory;
    const ready = true;
    return [
      ["open", "열기", "folder-open", "Enter", one],
      [
        "transfer",
        !session()
          ? "반대 패널로 복사…"
          : side === "local"
            ? "선택 항목 업로드…"
            : "선택 항목 다운로드…",
        side === "local" ? "upload" : "download",
        "F5",
        count > 0,
      ],
      ["move-transfer", "반대 패널로 이동…", "arrows-move", "F6", count > 0],
      null,
      ["rename", "이름 변경…", "pencil", "F2", one],
      ["edit", "텍스트 편집…", "file-code", "F4", file],
      ["mkdir", "새 폴더…", "folder-plus", "F7", ready],
      ["info", "선택 항목 정보…", "info-circle", "Alt+Enter", count > 0],
      ["copy-path", "경로 복사", "copy", "Ctrl/⌘+Shift+C", count > 0],
      [
        "checksum",
        "SHA256 체크섬…",
        "fingerprint",
        "",
        count > 0 && items.every((f) => !f.isDirectory),
      ],
      ...(side === "remote" && session()
        ? [
            [
              "permissions",
              "권한 · 소유자…",
              "lock",
              "",
              count > 0 && !!session()?.capabilities.permissions,
            ],
            ["copy", "서버 내 복사…", "files", "", one],
            [
              "link",
              "심볼릭 링크…",
              "link",
              "",
              one && !!session()?.capabilities.links,
            ],
            ["properties", "원격 속성 · 폴더 크기…", "list-details", "", one],
            ["external-edit", "외부 편집기…", "external-link", "", file],
            ["search-deep", "하위 폴더 검색…", "search", "", ready],
            [
              "custom-command",
              "사용자 명령…",
              "terminal-2",
              "",
              ready && !!session()?.capabilities.commands,
            ],
          ]
        : []),
      null,
      ["all", "전체 선택", "checks", "Ctrl/⌘+A", pathsFor(side).length > 0],
      [
        "invert",
        "선택 반전",
        "select-all",
        "Ctrl/⌘+Shift+A",
        pathsFor(side).length > 0,
      ],
      ["clear", "선택 해제", "square-off", "Esc", count > 0],
      ["refresh", "새로고침", "refresh", "Ctrl/⌘+R", ready],
      [
        "hidden",
        state.showHidden ? "숨김 항목 숨기기" : "숨김 항목 표시",
        "eye",
        "",
        ready,
      ],
      null,
      ["recycle", "휴지통으로 이동", "trash", "Delete", count > 0],
      ["delete", "영구 삭제…", "trash-x", "Shift+Delete", count > 0],
    ];
  }
  function closeMenu(restoreFocus = false) {
    if (!menu) return;
    const side = menuSide;
    menu.remove();
    menu = null;
    if (restoreFocus) focus(side);
  }
  function showMenu(side, point) {
    closeMenu();
    focus(side);
    menuSide = side;
    menu = document.createElement("div");
    menu.className = "file-context-menu";
    menu.setAttribute("role", "menu");
    menu.setAttribute(
      "aria-label",
      `${side === "local" ? "로컬" : "원격"} 파일 작업`,
    );
    menu.tabIndex = -1;
    menu.innerHTML =
      `<div class="context-caption">${side === "local" ? "내 컴퓨터" : "원격 서버"} <span>${state[side].selected.size}개 선택</span></div>` +
      menuItems(side)
        .map((item) =>
          item
            ? `<button type="button" role="menuitem" tabindex="-1" data-command="${item[0]}" ${item[4] ? "" : 'disabled aria-disabled="true"'} class="${["recycle", "delete"].includes(item[0]) ? "menu-danger" : ""}"><i class="ti ti-${item[2]}" aria-hidden="true"></i><span>${esc(item[1])}</span><kbd>${item[3]}</kbd></button>`
            : '<div class="menu-separator" role="separator"></div>',
        )
        .join("");
    document.body.append(menu);
    const anchor =
      point ||
      rows(side)
        .find((row) => row.dataset.path === state[side].cursor)
        ?.getBoundingClientRect() ||
      listFor(side).getBoundingClientRect();
    const rect = menu.getBoundingClientRect();
    menu.style.left =
      Math.max(
        8,
        Math.min(point?.x ?? anchor.left + 36, innerWidth - rect.width - 8),
      ) + "px";
    menu.style.top =
      Math.max(
        8,
        Math.min(point?.y ?? anchor.top + 30, innerHeight - rect.height - 8),
      ) + "px";
    menu.querySelector("button:not(:disabled)")?.focus();
    menu.onclick = (e) => {
      const b = e.target.closest("[data-command]");
      if (!b || b.disabled) return;
      const command = b.dataset.command;
      closeMenu(true);
      run(() => execute(command, side));
    };
    menu.oncontextmenu = (e) => e.preventDefault();
    menu.onkeydown = (e) => {
      if (["Escape", "Tab"].includes(e.key)) {
        e.preventDefault();
        closeMenu(true);
        return;
      }
      if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(e.key)) return;
      e.preventDefault();
      e.stopPropagation();
      const buttons = [...menu.querySelectorAll("button:not(:disabled)")];
      const current = buttons.indexOf(document.activeElement);
      const next =
        e.key === "Home"
          ? 0
          : e.key === "End"
            ? buttons.length - 1
            : (current + (e.key === "ArrowDown" ? 1 : -1) + buttons.length) %
              buttons.length;
      buttons[next]?.focus();
    };
  }
  async function execute(command, side = state.focus) {
    focus(side);
    if (["all", "clear", "invert"].includes(command)) {
      choose(side, command);
      return;
    }
    if (command === "menu") {
      showMenu(side);
      return;
    }
    if (command === "refresh") {
      await load(side, state[side].path);
      return;
    }
    if (command === "parent") {
      if (state[side].parent) await load(side, state[side].parent);
      return;
    }
    if (command === "transfer" || command === "move-transfer") {
      await transfer(
        side === "local" ? "upload" : "download",
        [...state[side].selected],
        { move: command === "move-transfer" },
      );
      return;
    }
    if (command === "copy-path") {
      const paths = [...state[side].selected];
      if (!paths.length) return;
      await navigator.clipboard.writeText(paths.join("\n"));
      toast(`${paths.length}개 항목의 경로를 복사했습니다.`);
      return;
    }
    await action(command, side);
  }
  function startBox(e, side) {
    if (
      e.button !== 0 ||
      e.target.closest("button,input,thead,[draggable=true]")
    )
      return;
    gesture?.cancel();
    closeMenu();
    const list = listFor(side),
      p = state[side],
      bounds = list.getBoundingClientRect();
    const baseline = {
      selected: new Set(p.selected),
      cursor: p.cursor,
      anchor: p.anchor,
    };
    const start = {
      x: e.clientX - bounds.left + list.scrollLeft,
      y: e.clientY - bounds.top + list.scrollTop,
    };
    let current = { x: e.clientX, y: e.clientY },
      box,
      active = false,
      frame;
    const mode =
      e.ctrlKey || e.metaKey ? "toggle" : e.shiftKey ? "add" : "replace";
    function paint() {
      if (!active) return;
      const b = list.getBoundingClientRect();
      const dy =
        current.y < b.top + 46
          ? -Math.min(18, (b.top + 46 - current.y) / 3)
          : current.y > b.bottom - 24
            ? Math.min(18, (current.y - b.bottom + 24) / 3)
            : 0;
      list.scrollTop += dy;
      const x =
        Math.max(0, Math.min(b.width, current.x - b.left)) + list.scrollLeft;
      const y =
        Math.max(0, Math.min(b.height, current.y - b.top)) + list.scrollTop;
      const area = {
        left: Math.min(start.x, x),
        top: Math.min(start.y, y),
        right: Math.max(start.x, x),
        bottom: Math.max(start.y, y),
      };
      Object.assign(box.style, {
        left: area.left + "px",
        top: area.top + "px",
        width: area.right - area.left + "px",
        height: area.bottom - area.top + "px",
      });
      const hits = rows(side)
        .filter((row) => {
          const r = row.getBoundingClientRect(),
            top = r.top - b.top + list.scrollTop,
            left = r.left - b.left + list.scrollLeft;
          return (
            top < area.bottom &&
            top + r.height > area.top &&
            left < area.right &&
            left + r.width > area.left
          );
        })
        .map((row) => row.dataset.path);
      p.selected = selectBox(baseline.selected, hits, mode);
      p.cursor = hits.at(-1) || baseline.cursor;
      p.anchor = hits[0] || baseline.anchor;
      renderSelection(side);
      frame = requestAnimationFrame(paint);
    }
    function move(event) {
      if (event.pointerId !== e.pointerId) return;
      current = { x: event.clientX, y: event.clientY };
      if (
        !active &&
        Math.hypot(event.clientX - e.clientX, event.clientY - e.clientY) > 5
      ) {
        active = true;
        focus(side);
        box = document.createElement("div");
        box.className = "selection-marquee";
        box.setAttribute("aria-hidden", "true");
        list.append(box);
        list.classList.add("selecting");
        list.setPointerCapture(e.pointerId);
        paint();
      }
      if (active) event.preventDefault();
    }
    function finish(cancel = false) {
      cancelAnimationFrame(frame);
      box?.remove();
      list.classList.remove("selecting");
      document.removeEventListener("pointermove", move);
      document.removeEventListener("pointerup", up);
      document.removeEventListener("pointercancel", cancelPointer);
      if (list.hasPointerCapture(e.pointerId))
        list.releasePointerCapture(e.pointerId);
      if (active) {
        list.dataset.suppressClick = "true";
        if (cancel) Object.assign(p, baseline);
        renderSelection(side);
      }
      gesture = null;
    }
    const up = (event) => {
      if (event.pointerId === e.pointerId) finish();
    };
    const cancelPointer = () => finish(true);
    gesture = { side, cancel: cancelPointer, finish };
    document.addEventListener("pointermove", move, { passive: false });
    document.addEventListener("pointerup", up);
    document.addEventListener("pointercancel", cancelPointer);
  }
  function clearDrag() {
    document
      .querySelectorAll(".pane.drop,.drop-target,.drag-source")
      .forEach((el) =>
        el.classList.remove("drop", "drop-target", "drag-source"),
      );
    drag?.image.remove();
    drag = null;
  }
  function wire(side) {
    const area = $("#pane-" + side),
      list = listFor(side);
    area.addEventListener("pointerdown", () => focus(side, false));
    area.addEventListener("focusin", () => focus(side, false));
    area.addEventListener("click", (e) => {
      const b = e.target.closest("[data-explorer-action]");
      if (b && !b.disabled) run(() => execute(b.dataset.explorerAction, side));
    });
    list.addEventListener("pointerdown", (e) => {
      delete list.dataset.suppressClick;
      startBox(e, side);
    });
    list.onclick = (e) => {
      if (list.dataset.suppressClick) {
        delete list.dataset.suppressClick;
        e.preventDefault();
        return;
      }
      if (e.target.closest("[data-select-all]")) {
        choose(
          side,
          state[side].selected.size === pathsFor(side).length ? "clear" : "all",
        );
        focus(side);
        return;
      }
      if (e.target.closest("thead,button")) return;
      const row = e.target.closest("tr[data-path]");
      if (row)
        select(side, row.dataset.path, {
          range: e.shiftKey,
          additive: e.ctrlKey || e.metaKey,
          toggle:
            !e.shiftKey &&
            (!!e.target.closest("[data-row-check]") || e.ctrlKey || e.metaKey),
        });
      else if (!e.ctrlKey && !e.metaKey && !e.shiftKey) choose(side, "clear");
      focus(side);
    };
    list.ondblclick = (e) => {
      if (e.target.closest("input,button,thead") || list.dataset.suppressClick)
        return;
      const row = e.target.closest("tr[data-path]");
      if (row) {
        select(side, row.dataset.path);
        run(() => execute("open", side));
      }
    };
    list.oncontextmenu = (e) => {
      e.preventDefault();
      if (gesture) gesture.cancel();
      const path = e.target.closest("tr[data-path]")?.dataset.path;
      if (path) {
        if (!state[side].selected.has(path)) select(side, path);
        else {
          state[side].cursor = path;
          renderSelection(side);
        }
      }
      showMenu(side, { x: e.clientX, y: e.clientY });
    };
    list.ondragstart = (e) => {
      const path = e.target.closest("tr[data-path]")?.dataset.path;
      if (!path || !e.target.closest("[draggable=true]")) {
        e.preventDefault();
        return;
      }
      closeMenu();
      if (!state[side].selected.has(path)) select(side, path);
      const paths = pathsFor(side).filter((p) => state[side].selected.has(p));
      const image = document.createElement("div");
      image.className = "file-drag-preview";
      image.innerHTML = `<i class="ti ti-files" aria-hidden="true"></i> ${paths.length}개 항목 · ${!session() ? "로컬 복사" : side === "local" ? "업로드" : "다운로드"}`;
      document.body.append(image);
      drag = { side, paths, sessionId: state.current, image };
      e.dataTransfer.setData(
        "application/x-portway",
        JSON.stringify({ side, paths, sessionId: state.current }),
      );
      e.dataTransfer.effectAllowed = "copy";
      e.dataTransfer.setDragImage(image, 16, 16);
      rows(side)
        .filter((row) => state[side].selected.has(row.dataset.path))
        .forEach((row) => row.classList.add("drag-source"));
    };
    area.ondragover = (e) => {
      if (
        !drag ||
        drag.side === side ||
        drag.sessionId !== state.current ||
        !e.dataTransfer.types.includes("application/x-portway")
      )
        return;
      e.preventDefault();
      e.dataTransfer.dropEffect = "copy";
      area.classList.add("drop");
      rows(side).forEach((row) => row.classList.remove("drop-target"));
      const row = e.target.closest("tr[data-path]");
      if (
        row &&
        state[side].entries.some(
          (f) => f.path === row.dataset.path && f.isDirectory && !f.isLink,
        )
      )
        row.classList.add("drop-target");
    };
    area.ondragleave = (e) => {
      if (!area.contains(e.relatedTarget)) {
        area.classList.remove("drop");
        rows(side).forEach((row) => row.classList.remove("drop-target"));
      }
    };
    area.ondrop = (e) => {
      // 외부 파일 관리자의 드롭 처리는 drop.js가 계속 담당합니다.
      if (!e.dataTransfer.types.includes("application/x-portway")) return;
      e.preventDefault();
      const source = drag;
      const target =
        e.target.closest("tr.drop-target[data-path]")?.dataset.path ||
        state[side].path;
      clearDrag();
      if (!source || source.side === side || source.sessionId !== state.current)
        return;
      run(() =>
        transfer(
          source.side === "local" ? "upload" : "download",
          source.paths,
          { destination: target, sessionId: source.sessionId },
        ),
      );
    };
  }
  function keyboard(e) {
    const list = focusedList(),
      panelFocused = list && list === e.target.closest(".filelist"),
      blocked = document.querySelector("dialog[open]") || menu;
    if (
      ["F2", "F4", "F5", "F7", "Delete"].includes(e.key) &&
      (!panelFocused || blocked)
    ) {
      // 입력란의 기본 Delete 편집은 유지하고 파일 목록에 키보드 포커스가 없으면
      // F5 새로고침을 포함한 브라우저 기능 키의 기본 동작을 차단합니다.
      if (e.key !== "Delete") e.preventDefault();
      return;
    }
    if (e.defaultPrevented || blocked) return;
    if (e.key === "Escape" && gesture) {
      e.preventDefault();
      gesture.cancel();
      return;
    }
    if (!panelFocused || e.isComposing) return;
    const side = list.id === "files-remote" ? "remote" : "local";
    const p = state[side],
      paths = pathsFor(side),
      modifier = e.ctrlKey || e.metaKey;
    let command;
    if (e.key === "ContextMenu" || (e.shiftKey && e.key === "F10")) {
      e.preventDefault();
      showMenu(side);
      return;
    }
    if (e.key === "Tab") {
      e.preventDefault();
      focus(side === "local" ? "remote" : "local");
      return;
    }
    if (modifier && ["l", "f"].includes(e.key.toLowerCase())) {
      e.preventDefault();
      const input = $(
        "#" + (e.key.toLowerCase() === "l" ? "path-" : "filter-") + side,
      );
      input.focus();
      input.select();
      return;
    }
    if (modifier && e.key.toLowerCase() === "a")
      command = e.shiftKey ? "invert" : "all";
    else if (modifier && e.key.toLowerCase() === "r") command = "refresh";
    else if (modifier && e.shiftKey && e.key.toLowerCase() === "c")
      command = "copy-path";
    else if (e.key === "Escape") command = "clear";
    else if (e.key === "Enter") command = e.altKey ? "info" : "open";
    else if (e.key === "Backspace" || (e.altKey && e.key === "ArrowUp"))
      command = "parent";
    else
      command = {
        F2: "rename",
        F4: "edit",
        F5: "transfer",
        F6: "move-transfer",
        F7: "mkdir",
        Delete: e.shiftKey ? "delete" : "recycle",
      }[e.key];
    if (command) {
      e.preventDefault();
      run(() => execute(command, side));
      return;
    }
    if (
      ["ArrowDown", "ArrowUp", "Home", "End", "PageDown", "PageUp"].includes(
        e.key,
      )
    ) {
      e.preventDefault();
      if (!paths.length) return;
      const index = Math.max(0, paths.indexOf(p.cursor));
      const page = Math.max(
        1,
        Math.floor((listFor(side).clientHeight - 34) / 37) - 1,
      );
      const step =
        e.key === "ArrowDown"
          ? 1
          : e.key === "ArrowUp"
            ? -1
            : e.key === "PageDown"
              ? page
              : -page;
      const next =
        e.key === "Home"
          ? 0
          : e.key === "End"
            ? paths.length - 1
            : Math.max(0, Math.min(paths.length - 1, index + step));
      select(side, paths[next], {
        range: e.shiftKey,
        additive: modifier && e.shiftKey,
        focusOnly: modifier && !e.shiftKey,
      });
      scrollCursor(side);
      return;
    }
    if (e.key === " " || e.key === "Insert") {
      e.preventDefault();
      if (!p.cursor) return;
      select(side, p.cursor, { toggle: true });
      if (e.key === "Insert") {
        const next =
          paths[Math.min(paths.length - 1, paths.indexOf(p.cursor) + 1)];
        select(side, next, { focusOnly: true });
        scrollCursor(side);
      }
      return;
    }
    if (!modifier && !e.altKey && e.key.length === 1) {
      const now = Date.now();
      search = now - lastTyped < 700 ? search + e.key : e.key;
      lastTyped = now;
      const entries = visibleEntries(side),
        index = paths.indexOf(p.cursor);
      const ordered = [
        ...entries.slice(index + 1),
        ...entries.slice(0, index + 1),
      ];
      const found = (search.length > 1 ? entries : ordered).find((f) =>
        f.name.toLocaleLowerCase().startsWith(search.toLocaleLowerCase()),
      );
      if (found) {
        e.preventDefault();
        select(side, found.path);
        scrollCursor(side);
      }
    }
  }
  document.addEventListener(
    "pointerdown",
    (e) => {
      if (menu && !menu.contains(e.target)) closeMenu();
    },
    true,
  );
  document.addEventListener("dragend", clearDrag);
  document.addEventListener("focusin", syncFocus);
  document.addEventListener("focusout", () => queueMicrotask(syncFocus));
  window.addEventListener("focus", syncFocus);
  window.addEventListener("blur", () => {
    syncFocus();
    closeMenu();
    gesture?.cancel();
    clearDrag();
  });
  window.addEventListener("resize", () => closeMenu());
  return {
    wire,
    focus,
    keyboard,
    renderSelection,
    execute,
    showMenu,
    closeMenu,
    beforeRender(side) {
      if (menuSide === side) closeMenu();
      if (gesture?.side === side) gesture.finish();
    },
  };
}
