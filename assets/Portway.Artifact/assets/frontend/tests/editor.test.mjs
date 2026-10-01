import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
const source = await readFile(
  new URL(
    "../../../../../src/Portway.Desktop/wwwroot/editor-state.js",
    import.meta.url,
  ),
  "utf8",
);
const { createDocumentState } = await import(
  "data:text/javascript;base64," + Buffer.from(source).toString("base64")
);
const original = {
  content: "처음\r\n내용\n",
  etag: "original-hash",
  encoding: "utf-8",
  bom: false,
};

test("메타데이터만 저장할 때 원본의 혼합 줄바꿈과 동시 수정 토큰을 보존합니다", () => {
  const state = createDocumentState(original, "처음\n내용\n");
  assert.equal(state.isDirty("처음\n내용\n", "UTF-8", false), false);
  assert.equal(state.isDirty("처음\n내용\n", "utf-8", true), true);
  assert.equal(state.isDirty("처음\n내용\n", "cp949", false), true);
  assert.deepEqual(state.snapshot("처음\n내용\n", "utf-16le", true), {
    ...original,
    encoding: "utf-16le",
    bom: true,
  });
});

test("저장 중 수정한 내용은 미저장 상태로 남고 다음 저장은 새 ETag를 사용합니다", () => {
  const state = createDocumentState(original, "처음\n내용\n");
  const submitted = state.snapshot("submitted", "utf-8", false);
  assert.equal(submitted.etag, "original-hash");
  state.accept({ ...submitted, etag: "new-hash" }, "submitted");
  assert.equal(state.isDirty("later edit", "utf-8", false), true);
  assert.equal(state.isDirty("submitted", "utf-8", false), false);
  assert.equal(state.snapshot("later edit", "utf-8", false).etag, "new-hash");
});

test("저장 실패 시 원본 ETag와 미저장 상태를 유지해 안전하게 재시도합니다", () => {
  const state = createDocumentState(original, "처음\n내용\n");
  state.snapshot("changed", "cp949", false);
  assert.equal(state.isDirty("changed", "cp949", false), true);
  assert.equal(state.snapshot("changed", "cp949", false).etag, "original-hash");
});
