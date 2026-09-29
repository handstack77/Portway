// 비동기 저장 중에는 저장할 스냅샷과 현재 편집 내용을 분리해 유지합니다.
export function createDocumentState(document, normalizedContent) {
  let saved = { ...document, normalizedContent };
  return {
    isDirty(content, encoding, bom) {
      return (
        content !== saved.normalizedContent ||
        encoding.trim().toLowerCase() !== saved.encoding.toLowerCase() ||
        bom !== saved.bom
      );
    },
    snapshot(content, encoding, bom) {
      return {
        content: content === saved.normalizedContent ? saved.content : content,
        encoding: encoding.trim(),
        bom,
        etag: saved.etag,
      };
    },
    accept(document, submittedContent) {
      saved = { ...document, normalizedContent: submittedContent };
    },
  };
}
