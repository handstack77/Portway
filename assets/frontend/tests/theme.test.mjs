import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";

const source = await readFile(
  new URL("../../../src/Portway.Desktop/wwwroot/theme.js", import.meta.url),
  "utf8",
);
function start({ saved, dark = false, blocked = false } = {}) {
  const values = new Map(saved ? [["portway-theme", saved]] : []);
  const listeners = [];
  const media = {
    matches: dark,
    addEventListener: (_, fn) => listeners.push(fn),
  };
  const root = { dataset: {}, style: {} };
  const events = [];
  const window = { dispatchEvent: (event) => events.push(event.detail) };
  runInNewContext(source, {
    matchMedia: () => media,
    document: { documentElement: root },
    window,
    localStorage: {
      getItem(key) {
        if (blocked) throw Error("저장소 접근이 차단되었습니다.");
        return values.get(key);
      },
      setItem(key, value) {
        if (blocked) throw Error("저장소 접근이 차단되었습니다.");
        values.set(key, value);
      },
    },
    CustomEvent: class {
      constructor(type, { detail }) {
        this.type = type;
        this.detail = detail;
      }
    },
  });
  return {
    theme: window.portwayTheme,
    root,
    values,
    events,
    changeOS(dark) {
      media.matches = dark;
      for (const listener of listeners) listener({ matches: dark });
    },
  };
}

test("첫 실행에서 운영체제 테마를 한 번 해석하고 고정된 테마를 저장합니다", () => {
  for (const dark of [false, true]) {
    const app = start({ dark });
    const expected = dark ? "dark" : "light";
    assert.equal(app.theme.preference, expected);
    assert.equal(app.root.dataset.bsTheme, expected);
    assert.equal(app.root.style.colorScheme, expected);
    assert.equal(app.values.get("portway-theme"), expected);
  }
});

test("이전 시스템 설정과 잘못된 설정을 명시적인 테마로 변환합니다", () => {
  for (const saved of ["system", "unknown"]) {
    for (const dark of [false, true]) {
      const app = start({ saved, dark });
      assert.equal(app.theme.preference, dark ? "dark" : "light");
      assert.equal(app.values.get("portway-theme"), app.theme.preference);
    }
  }
});

test("명시적으로 캐시한 테마를 운영체제 테마보다 우선합니다", () => {
  for (const saved of ["light", "dark"]) {
    const app = start({ saved, dark: saved === "light" });
    assert.equal(app.theme.preference, saved);
    app.changeOS(saved === "dark");
    assert.equal(app.theme.resolved, saved);
    assert.equal(app.events.length, 1);
  }
});

test("변환한 테마는 운영체제 변경과 이후 실행에도 고정됩니다", () => {
  const app = start({ saved: "system", dark: true });
  app.changeOS(false);
  assert.equal(app.theme.preference, "dark");
  const next = start({ saved: app.values.get("portway-theme"), dark: false });
  assert.equal(next.theme.preference, "dark");
});

test("프로필 설정이 캐시보다 우선하며 해석한 테마를 알립니다", () => {
  const app = start({ saved: "dark", dark: true });
  assert.equal(app.theme.apply("light"), "light");
  assert.equal(app.theme.resolved, "light");
  assert.equal(app.values.get("portway-theme"), "light");
  assert.equal(app.events.at(-1).preference, "light");
  assert.equal(app.events.at(-1).resolved, "light");
  assert.equal(app.theme.apply("dark"), "dark");
});

test("이전 프로필 값은 시작 시 운영체제 테마를 기준으로 변환합니다", () => {
  const app = start({ saved: "light", dark: true });
  app.changeOS(false);
  assert.equal(app.theme.apply("system"), "dark");
  assert.equal(app.values.get("portway-theme"), "dark");
});

test("저장소를 사용할 수 없어도 첫 화면과 수동 테마 변경은 동작합니다", () => {
  const app = start({ blocked: true, dark: true });
  assert.equal(app.theme.resolved, "dark");
  app.theme.apply("light");
  assert.equal(app.root.dataset.bsTheme, "light");
});
