import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const source = await readFile(
  new URL(
    "../../../src/Portway.Desktop/wwwroot/display-time.js",
    import.meta.url,
  ),
  "utf8",
);
const { formatFileDate, formatElapsed } = await import(
  "data:text/javascript;base64," + Buffer.from(source).toString("base64")
);

test("수정 시각은 현지 날짜와 오전·오후를 표시하고 자정·정오를 구분합니다", () => {
  assert.equal(
    formatFileDate(new Date(2025, 6, 13, 6, 54)),
    "2025-07-13 오전 6:54",
  );
  assert.equal(
    formatFileDate(new Date(2025, 0, 2, 0, 5)),
    "2025-01-02 오전 12:05",
  );
  assert.equal(
    formatFileDate(new Date(2025, 0, 2, 12, 0)),
    "2025-01-02 오후 12:00",
  );
  assert.equal(
    formatFileDate(new Date(2025, 11, 31, 23, 59)),
    "2025-12-31 오후 11:59",
  );
  const zoned = "2025-07-13T06:54:00+09:00";
  assert.equal(formatFileDate(zoned), formatFileDate(new Date(zoned)));
});

test("없는 수정 시각과 서버의 기본 날짜는 날짜처럼 표시하지 않습니다", () => {
  for (const value of [
    null,
    undefined,
    "",
    "잘못된 날짜",
    "0001-01-01T00:00:00Z",
  ])
    assert.equal(formatFileDate(value), "—");
  assert.notEqual(formatFileDate(new Date(0)), "—");
});

test("접속 경과 시간은 초 단위이며 하루가 지나도 초기화되지 않습니다", () => {
  for (const [milliseconds, expected] of [
    [0, "00:00:00"],
    [999, "00:00:00"],
    [1000, "00:00:01"],
    [59999, "00:00:59"],
    [60000, "00:01:00"],
    [3723000, "01:02:03"],
    [90061000, "25:01:01"],
    [-1000, "00:00:00"],
    [NaN, "00:00:00"],
  ])
    assert.equal(formatElapsed(milliseconds), expected);
});
