export const $ = (q, root = document) => root.querySelector(q);
export const $$ = (q, root = document) => [...root.querySelectorAll(q)];
export const esc = (v) =>
  String(v ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const icons = {
  folder: "folder",
  file: "file",
  server: "server",
  monitor: "device-desktop",
  transfer: "arrows-transfer-up-down",
  sync: "refresh",
  settings: "settings",
  plus: "plus",
  close: "x",
  search: "search",
  up: "arrow-up",
  down: "arrow-down",
  right: "arrow-right",
  left: "arrow-left",
  refresh: "reload",
  lock: "lock",
  unlock: "lock-open",
  edit: "pencil",
  trash: "trash",
  terminal: "terminal-2",
  more: "dots",
  play: "player-play",
  pause: "player-pause",
  check: "check",
  bookmark: "bookmark",
  external: "external-link",
  queue: "list-details",
  home: "home",
  help: "help-circle",
  theme: "moon-stars",
  upload: "file-upload",
};
export const icon = (name, cls = "") =>
  `<i class="icon ti ti-${icons[name] || icons.file} ${cls}" aria-hidden="true"></i>`;
export const btn = (act, label, i, cls = "") =>
  `<button type="button" data-act="${act}" class="btn btn-sm ${cls === "primary" ? "btn-primary " : ""}${cls}">${i ? icon(i) : ""}${label}</button>`;
export const size = (n) =>
  n == null
    ? "—"
    : n < 1024
      ? n + " B"
      : n < 1048576
        ? (n / 1024).toFixed(1) + " KB"
        : n < 1073741824
          ? (n / 1048576).toFixed(1) + " MB"
          : (n / 1073741824).toFixed(2) + " GB";
