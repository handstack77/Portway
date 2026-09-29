import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
// 앱의 패키지 유형을 바꾸지 않고 실제 브라우저 모듈을 불러옵니다.
const source = await readFile(
  new URL("../../../src/Portway.Desktop/wwwroot/drop.js", import.meta.url),
  "utf8",
);
const { collectDrop, captureDrop } = await import(
  "data:text/javascript;base64," + Buffer.from(source).toString("base64")
);
function file(name, content = "abc") {
  return { name, size: content.length, lastModified: 1700000000000 };
}
function fileEntry(name) {
  return { name, isFile: true, file: (callback) => callback(file(name)) };
}
function directory(name, batches) {
  return {
    name,
    isDirectory: true,
    createReader: () => {
      let i = 0;
      return { readEntries: (callback) => callback(batches[i++] || []) };
    },
  };
}
test("이벤트가 끝나기 전에 네이티브 DataTransfer 참조를 확보합니다", async () => {
  const data = {
    items: [
      {
        kind: "file",
        webkitGetAsEntry: () => fileEntry("한글.txt"),
        getAsFile: () => file("한글.txt"),
      },
    ],
    files: [],
  };
  const captured = captureDrop(data);
  data.items = [];
  const entries = await collectDrop(captured);
  assert.equal(entries[0].path, "한글.txt");
  assert.equal(entries[0].size, 3);
});
test("모든 디렉토리 항목 묶음을 읽고 빈 디렉토리를 유지합니다", async () => {
  const batches = [
    Array.from({ length: 100 }, (_, i) => fileEntry(i + ".txt")),
    [fileEntry("last.txt"), directory("empty", [])],
  ];
  const entries = await collectDrop({
    items: [{ entry: directory("project", batches) }],
    files: [],
  });
  assert.equal(entries.length, 103);
  assert.equal(entries.at(-2).path, "project/last.txt");
  assert.deepEqual(entries.at(-1), {
    path: "project/empty",
    isDirectory: true,
    size: 0,
    modified: null,
    file: undefined,
  });
});
test("폴더 선택기는 상대 경로 구조와 0바이트 파일을 유지합니다", async () => {
  const entries = await collectDrop({
    items: [],
    files: [{ ...file("zero", ""), webkitRelativePath: "project/sub/zero" }],
  });
  assert.deepEqual(
    entries.map((e) => [e.path, e.isDirectory]),
    [
      ["project", true],
      ["project/sub", true],
      ["project/sub/zero", false],
    ],
  );
  assert.equal(entries.at(-1).size, 0);
});
test("취소와 읽을 수 없는 항목 및 빈 데이터는 명시적으로 오류를 반환합니다", async () => {
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(
    collectDrop(
      { items: [{ entry: fileEntry("x") }], files: [] },
      controller.signal,
    ),
    { name: "AbortError" },
  );
  await assert.rejects(
    collectDrop({ items: [{ entry: null, file: null }], files: [] }),
  );
  await assert.rejects(collectDrop({ items: [], files: [] }));
});
