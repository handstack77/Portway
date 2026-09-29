// 편집기와 모든 언어 작업자를 로컬에 포함하며 CDN이나 blob 작업자를 사용하지 않습니다.
import * as monaco from "monaco-editor";

globalThis.MonacoEnvironment = {
  getWorker(_moduleId, label) {
    const worker =
      label === "json"
        ? "json"
        : ["css", "scss", "less"].includes(label)
          ? "css"
          : ["html", "handlebars", "razor"].includes(label)
            ? "html"
            : ["typescript", "javascript"].includes(label)
              ? "ts"
              : "editor";
    return new Worker(new URL(`./${worker}.worker.js`, import.meta.url), {
      type: "module",
      name: `portway-${label}`,
    });
  },
};

// 원격 문서는 인터넷에서 스키마나 스크립트를 가져오면 안 됩니다.
monaco.json.jsonDefaults.setDiagnosticsOptions({
  validate: true,
  enableSchemaRequest: false,
  allowComments: true,
  trailingCommas: "ignore",
});

export { monaco };
