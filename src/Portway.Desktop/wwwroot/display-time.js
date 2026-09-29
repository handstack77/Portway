const pad = (value) => String(value).padStart(2, "0");

export function formatFileDate(value) {
  if (value == null || value === "") return "—";
  const date = new Date(value);
  if (!Number.isFinite(date.getTime()) || date.getFullYear() <= 1) return "—";
  const hour = date.getHours();
  return `${String(date.getFullYear()).padStart(4, "0")}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${hour < 12 ? "오전" : "오후"} ${hour % 12 || 12}:${pad(date.getMinutes())}`;
}

export function formatElapsed(milliseconds) {
  const seconds = Number.isFinite(milliseconds)
    ? Math.max(0, Math.floor(milliseconds / 1000))
    : 0;
  return `${pad(Math.floor(seconds / 3600))}:${pad(Math.floor(seconds / 60) % 60)}:${pad(seconds % 60)}`;
}
