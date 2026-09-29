// 첫 화면이 그려지기 전에 head의 동기 스크립트가 캐시된 테마 설정을 적용합니다.
(() => {
  const key = "portway-theme";
  // 신규 또는 이전 형식의 설정은 한 번만 해석한 뒤 명시적인 선택을 유지합니다.
  // 사용 중 운영체제 테마가 바뀌어도 작업 공간의 테마는 전환하지 않습니다.
  const initialTheme = matchMedia("(prefers-color-scheme: dark)").matches
    ? "dark"
    : "light";
  let preference = initialTheme;
  try {
    preference = localStorage.getItem(key) || initialTheme;
  } catch {}
  function apply(value) {
    preference = value === "light" || value === "dark" ? value : initialTheme;
    const resolved = preference;
    document.documentElement.dataset.bsTheme = resolved;
    document.documentElement.style.colorScheme = resolved;
    try {
      localStorage.setItem(key, preference);
    } catch {}
    window.dispatchEvent(
      new CustomEvent("portway-theme", { detail: { preference, resolved } }),
    );
    return preference;
  }
  window.portwayTheme = {
    apply,
    get preference() {
      return preference;
    },
    get resolved() {
      return document.documentElement.dataset.bsTheme;
    },
  };
  apply(preference);
})();
