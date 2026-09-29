// 정렬과 새로고침 전후의 항목은 행 번호가 아니라 경로로 식별합니다.
export function reconcileSelection(pane, paths) {
  const visible = new Set(paths);
  return {
    selected: new Set([...pane.selected].filter((path) => visible.has(path))),
    cursor: visible.has(pane.cursor) ? pane.cursor : paths[0] || null,
    anchor: visible.has(pane.anchor) ? pane.anchor : null,
  };
}

export function selectPath(
  pane,
  paths,
  path,
  { range = false, additive = false, toggle = false, focusOnly = false } = {},
) {
  const next = reconcileSelection(pane, paths);
  if (!paths.includes(path)) return next;
  if (focusOnly) return { ...next, cursor: path };
  if (range) {
    const anchor = next.anchor || next.cursor || path;
    const a = paths.indexOf(anchor),
      b = paths.indexOf(path);
    const selected = new Set(additive ? next.selected : []);
    paths
      .slice(Math.min(a, b), Math.max(a, b) + 1)
      .forEach((p) => selected.add(p));
    return { selected, cursor: path, anchor };
  }
  const selected = new Set(toggle || additive ? next.selected : []);
  if (toggle && selected.has(path)) selected.delete(path);
  else selected.add(path);
  return { selected, cursor: path, anchor: path };
}

export function selectBox(baseline, hits, mode = "replace") {
  const selected = new Set(mode === "replace" ? [] : baseline);
  for (const path of hits) {
    if (mode === "toggle" && selected.has(path)) selected.delete(path);
    else selected.add(path);
  }
  return selected;
}
