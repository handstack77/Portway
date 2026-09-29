const $ = (s) => document.querySelector(s);
function showTheme() {
  $("#theme-label").textContent = {
    light: "라이트",
    dark: "다크",
  }[window.portwayTheme.preference];
  const dark = window.portwayTheme.preference === "dark";
  const button = $("#theme-toggle");
  button.title = `현재 테마: ${dark ? "다크" : "라이트"} · ${dark ? "라이트" : "다크"}로 변경`;
  button.setAttribute("aria-label", button.title);
  button.querySelector(".ti").className =
    `ti ${dark ? "ti-moon-stars" : "ti-sun"}`;
}
$("#theme-toggle").onclick = () => {
  window.portwayTheme.apply(
    window.portwayTheme.preference === "dark" ? "light" : "dark",
  );
};
window.addEventListener("portway-theme", showTheme);
showTheme();
const esc = (s) =>
  String(s ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
async function refresh() {
  const r = await fetch("/api/releases");
  if (!r.ok) throw Error("릴리스 목록을 읽지 못했습니다.");
  const list = await r.json();
  $("#releases").innerHTML = list.length
    ? list
        .map(
          (c) =>
            `<article class="card"><div class="os"><i aria-hidden="true" class="ti ti-${c.channel.startsWith("win") ? "brand-windows" : c.channel.startsWith("osx") ? "brand-apple" : "brand-ubuntu"}"></i></div><h3>${c.channel.startsWith("win") ? "Windows" : c.channel.startsWith("osx") ? "macOS" : "Linux"}</h3><span class="channel">${esc(c.channel)}</span><div class="version">v${esc([...c.assets].filter((a) => a.type === "Full").sort((a, b) => b.version.localeCompare(a.version, undefined, { numeric: true }))[0]?.version || "—")}</div><div class="downloads">${c.downloads.map((d) => `<a class="download" href="${esc(d.url)}" download>${esc(d.name)} <small>${(d.size / 1048576).toFixed(1)} MB ↓</small></a>`).join("") || "<p>업데이트 패키지만 제공됩니다.</p>"}</div><div class="feed">피드: ${esc(location.origin + c.feed)}</div></article>`,
        )
        .join("")
    : '<p class="empty">아직 게시된 릴리스가 없습니다. 아래에서 첫 vpk 릴리스를 게시하세요.</p>';
}
$("#refresh").onclick = () =>
  refresh().catch((e) => ($("#result").textContent = e.message));
$("#publish").onsubmit = async (e) => {
  e.preventDefault();
  const button = e.submitter;
  button.disabled = true;
  $("#result").textContent = "업로드 및 해시 검증 중…";
  try {
    const file = $("#archive").files[0];
    const r = await fetch(
      "/api/releases/" + encodeURIComponent($("#channel").value),
      {
        method: "POST",
        headers: {
          Authorization: "Bearer " + $("#key").value,
          "Content-Type": "application/zip",
        },
        body: file,
      },
    );
    const body = await r.json();
    if (!r.ok) throw Error(body.detail || "게시 실패 (" + r.status + ")");
    $("#key").value = "";
    $("#result").textContent = "게시 완료 · " + body.feed;
    await refresh();
  } catch (e) {
    $("#result").textContent = e.message;
  } finally {
    button.disabled = false;
  }
};
refresh().catch((e) => ($("#releases").textContent = e.message));
