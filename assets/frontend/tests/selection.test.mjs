import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
const source = await readFile(
  new URL("../../../src/Portway.Desktop/wwwroot/selection.js", import.meta.url),
  "utf8",
);
const { reconcileSelection, selectPath, selectBox } = await import(
  "data:text/javascript;base64," + Buffer.from(source).toString("base64")
);
const paths = ["folder-a", "folder-b", "file-a", "file-b", "file-c"];
const pane = (selected = [], cursor = null, anchor = null) => ({
  selected: new Set(selected),
  cursor,
  anchor,
});
const selected = (p) => [...p.selected];

test("범위 선택은 표시 순서를 따르며 고정된 기준점에서 범위를 줄입니다", () => {
  let state = selectPath(pane(), paths, "folder-b");
  state = selectPath(state, paths, "file-c", { range: true });
  assert.deepEqual(selected(state), paths.slice(1));
  state = selectPath(state, paths, "file-a", { range: true });
  assert.deepEqual(selected(state), ["folder-b", "file-a"]);
  assert.equal(state.anchor, "folder-b");
});
test("Ctrl 또는 Command 전환과 범위 추가는 떨어진 항목의 선택을 유지합니다", () => {
  let state = selectPath(
    pane(["folder-a"], "folder-a", "folder-a"),
    paths,
    "file-b",
    { toggle: true },
  );
  assert.deepEqual(selected(state), ["folder-a", "file-b"]);
  state = selectPath(state, paths, "file-c", { range: true, additive: true });
  assert.deepEqual(selected(state), ["folder-a", "file-b", "file-c"]);
  state = selectPath(state, paths, "file-b", { toggle: true });
  assert.deepEqual(selected(state), ["folder-a", "file-c"]);
});
test("포커스만 이동할 때 다중 선택을 해제하지 않습니다", () => {
  const initial = pane(["folder-a", "file-b"], "folder-a", "folder-a");
  const state = selectPath(initial, paths, "file-c", { focusOnly: true });
  assert.deepEqual(selected(state), selected(initial));
  assert.equal(state.cursor, "file-c");
  assert.equal(state.anchor, "folder-a");
});
test("필터와 새로고침은 숨겨지거나 삭제된 선택 및 오래된 기준점을 제거합니다", () => {
  const state = reconcileSelection(
    pane(["folder-a", "file-a"], "folder-a", "file-c"),
    ["file-a", "file-b"],
  );
  assert.deepEqual(selected(state), ["file-a"]);
  assert.equal(state.cursor, "file-a");
  assert.equal(state.anchor, null);
  assert.deepEqual(
    selected(
      selectPath(state, ["file-a", "file-b"], "file-b", { range: true }),
    ),
    ["file-a", "file-b"],
  );
});
test("정렬은 경로 식별을 유지하며 새 표시 순서로 범위를 선택합니다", () => {
  const reversed = [...paths].reverse();
  const state = reconcileSelection(
    pane(["folder-b"], "folder-b", "folder-b"),
    reversed,
  );
  assert.equal(state.cursor, "folder-b");
  assert.deepEqual(
    selected(selectPath(state, reversed, "file-b", { range: true })),
    ["file-b", "file-a", "folder-b"],
  );
});
test("박스 선택 전환은 이전 프레임이 아닌 드래그 시작 시 상태를 기준으로 합니다", () => {
  const initial = new Set(["folder-a", "file-a"]);
  assert.deepEqual(
    [...selectBox(initial, ["folder-a", "folder-b"], "toggle")],
    ["file-a", "folder-b"],
  );
  assert.deepEqual([...selectBox(initial, ["folder-a"], "toggle")], ["file-a"]);
  assert.deepEqual([...initial], ["folder-a", "file-a"]);
  assert.deepEqual(
    [...selectBox(initial, ["file-b"], "add")],
    ["folder-a", "file-a", "file-b"],
  );
  assert.deepEqual([...selectBox(initial, ["file-b"])], ["file-b"]);
});
test("빈 목록이나 없는 경로 때문에 다른 항목이 선택되면 안 됩니다", () => {
  assert.deepEqual(
    selected(
      selectPath(pane(["gone"], "gone", "gone"), [], "gone", { range: true }),
    ),
    [],
  );
  assert.deepEqual(
    selected(selectPath(pane(), paths, "missing", { range: true })),
    [],
  );
});
