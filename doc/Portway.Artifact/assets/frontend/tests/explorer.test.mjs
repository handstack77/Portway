import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
const web = new URL(
  "../../../../../src/Portway.Desktop/wwwroot/",
  import.meta.url,
);
const selection = await readFile(new URL("selection.js", web), "utf8");
const selectionUrl =
  "data:text/javascript;base64," + Buffer.from(selection).toString("base64");
const source = (await readFile(new URL("explorer.js", web), "utf8")).replace(
  '"./selection.js"',
  JSON.stringify(selectionUrl),
);
const { createExplorer } = await import(
  "data:text/javascript;base64," + Buffer.from(source).toString("base64")
);
const keys = ["F2", "F4", "F5", "F7", "Delete"];

function harness(t) {
  const previous = { document: globalThis.document, window: globalThis.window };
  t.after(() => Object.assign(globalThis, previous));
  const events = { document: new Map(), window: new Map() };
  const classes = { local: new Set(), remote: new Set() };
  const lists = Object.fromEntries(
    ["local", "remote"].map((side) => [
      side,
      {
        id: "files-" + side,
        closest: () => lists[side],
        focus: () => {
          document.activeElement = lists[side];
        },
      },
    ]),
  );
  const outside = { closest: () => null };
  let active = true,
    dialog = false;
  globalThis.document = {
    activeElement: outside,
    hasFocus: () => active,
    querySelectorAll: () => [],
    querySelector: (selector) => {
      if (selector === "dialog[open]") return dialog ? {} : null;
      const side = selector.endsWith("remote") ? "remote" : "local";
      if (selector.startsWith("#files-")) return lists[side];
      if (selector.startsWith("#pane-"))
        return {
          classList: {
            toggle: (name, enabled) =>
              enabled ? classes[side].add(name) : classes[side].delete(name),
          },
        };
      throw new Error("예상하지 못한 선택자입니다: " + selector);
    },
    addEventListener: (name, handler) => events.document.set(name, handler),
  };
  globalThis.window = {
    addEventListener: (name, handler) => events.window.set(name, handler),
  };
  const calls = [];
  const explorer = createExplorer({
    state: { focus: "local", local: {}, remote: {} },
    visibleEntries: () => [],
    run: (callback) => calls.push(callback),
  });
  const send = (key, target = document.activeElement, extra = {}) => {
    const event = {
      key,
      target,
      defaultPrevented: false,
      preventDefault() {
        this.defaultPrevented = true;
      },
      ...extra,
    };
    explorer.keyboard(event);
    return event;
  };
  return {
    lists,
    outside,
    calls,
    events,
    classes,
    explorer,
    send,
    setActive: (value) => {
      active = value;
    },
    setDialog: (value) => {
      dialog = value;
    },
  };
}

test("두 파일 목록 밖에 포커스가 있으면 파일 작업 단축키를 실행하지 않습니다", (t) => {
  const h = harness(t);
  for (const key of keys) {
    const event = h.send(key);
    assert.equal(h.calls.length, 0, key);
    assert.equal(event.defaultPrevented, key !== "Delete", key);
  }
});

test("포커스 이동 후에는 목록의 오래된 이벤트로 파일 작업을 실행하지 않습니다", (t) => {
  const h = harness(t);
  for (const key of keys) h.send(key, h.lists.local);
  assert.equal(h.calls.length, 0);
});

test("포커스가 있는 로컬 또는 원격 목록은 다섯 파일 작업 단축키를 처리합니다", (t) => {
  const h = harness(t);
  for (const side of ["local", "remote"]) {
    document.activeElement = h.lists[side];
    for (const key of keys) {
      const before = h.calls.length;
      assert.equal(h.send(key).defaultPrevented, true, side + ": " + key);
      assert.equal(h.calls.length, before + 1, side + ": " + key);
    }
  }
});

test("다른 패널의 이벤트로 그 패널의 이전 선택을 처리하지 않습니다", (t) => {
  const h = harness(t);
  document.activeElement = h.lists.remote;
  for (const key of keys) h.send(key, h.lists.local);
  assert.equal(h.calls.length, 0);
});

test("DOM 포커스가 남아 있어도 백그라운드 창에서는 파일 단축키를 실행하지 않습니다", (t) => {
  const h = harness(t);
  document.activeElement = h.lists.local;
  h.setActive(false);
  for (const key of keys) h.send(key);
  assert.equal(h.calls.length, 0);
});

test("대화상자는 파일 작업과 F5 새로고침을 막고 기본 Delete 편집은 유지합니다", (t) => {
  const h = harness(t);
  document.activeElement = h.lists.local;
  h.setDialog(true);
  for (const key of keys)
    assert.equal(h.send(key).defaultPrevented, key !== "Delete", key);
  assert.equal(h.calls.length, 0);
});

test("패널 강조는 실제 포커스를 따르며 창을 벗어나면 해제됩니다", (t) => {
  const h = harness(t);
  h.explorer.focus("local");
  assert.equal(h.classes.local.has("focused"), true);
  document.activeElement = h.outside;
  h.events.document.get("focusin")();
  assert.equal(h.classes.local.has("focused"), false);
  h.explorer.focus("remote");
  assert.equal(h.classes.remote.has("focused"), true);
  h.setActive(false);
  h.events.window.get("blur")();
  assert.equal(h.classes.remote.has("focused"), false);
});
