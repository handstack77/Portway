// 재현 가능한 오프라인 실행 자산입니다. 이 디렉토리에서 npm ci와 npm run build를 실행하세요.
const fs = require("node:fs");
const path = require("node:path");
const { StyleSheet } = require("@master/css");
const { render } = require("@master/css/render");
const esbuild = require("esbuild");
const web = path.resolve(__dirname, "../../src/Portway.Desktop/wwwroot");
const vendor = path.join(web, "vendor");
const serverWeb = path.resolve(__dirname, "../../src/Portway.Server/wwwroot");
function copy(source, target) {
  const output = path.join(vendor, target);
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.copyFileSync(path.join(__dirname, "node_modules", source), output);
}
copy("@tabler/core/dist/css/tabler.min.css", "tabler/tabler.min.css");
// @tabler/core 1.4.0의 npm 패키지에는 LICENSE가 없어 해당 릴리스의 MIT 라이선스 원문을 보관합니다.
fs.copyFileSync(
  path.join(__dirname, "licenses/tabler-LICENSE"),
  path.join(vendor, "tabler/LICENSE"),
);
copy(
  "@tabler/icons-webfont/dist/tabler-icons.min.css",
  "tabler-icons/tabler-icons.min.css",
);
copy("@tabler/icons-webfont/LICENSE", "tabler-icons/LICENSE");
for (const file of fs.readdirSync(
  path.join(__dirname, "node_modules/@tabler/icons-webfont/dist/fonts"),
)) {
  if (/^tabler-icons\.(woff2?|ttf)$/.test(file))
    copy(
      "@tabler/icons-webfont/dist/fonts/" + file,
      "tabler-icons/fonts/" + file,
    );
}
// 유니코드 범위별로 나눈 가변 글꼴을 로컬에 포함해 한글과 영문을 오프라인으로 표시합니다.
copy("@fontsource-variable/noto-sans-kr/index.css", "noto-sans-kr/index.css");
copy("@fontsource-variable/noto-sans-kr/LICENSE", "noto-sans-kr/LICENSE");
for (const file of fs.readdirSync(
  path.join(__dirname, "node_modules/@fontsource-variable/noto-sans-kr/files"),
)) {
  if (file.endsWith("-wght-normal.woff2"))
    copy(
      "@fontsource-variable/noto-sans-kr/files/" + file,
      "noto-sans-kr/files/" + file,
    );
}
copy("@master/css/LICENSE", "master/LICENSE");
copy("@xterm/xterm/lib/xterm.js", "xterm.js");
copy("@xterm/xterm/css/xterm.css", "xterm.css");
copy("@xterm/xterm/LICENSE", "xterm-LICENSE");
copy("@xterm/addon-fit/lib/addon-fit.js", "addon-fit.js");
copy("@xterm/addon-fit/LICENSE", "addon-fit-LICENSE");
copy("monaco-editor/LICENSE", "monaco/LICENSE");
copy("monaco-editor/ThirdPartyNotices.txt", "monaco/ThirdPartyNotices.txt");
esbuild.buildSync({
  absWorkingDir: __dirname,
  entryPoints: {
    monaco: "monaco-entry.mjs",
    "editor.worker": "monaco-editor/editor/editor.worker.js",
    "json.worker": "monaco-editor/language/json/json.worker.js",
    "css.worker": "monaco-editor/language/css/css.worker.js",
    "html.worker": "monaco-editor/language/html/html.worker.js",
    "ts.worker": "monaco-editor/language/typescript/ts.worker.js",
  },
  outdir: path.join(vendor, "monaco"),
  bundle: true,
  splitting: true,
  format: "esm",
  target: "es2022",
  minify: true,
  loader: { ".ttf": "file" },
  assetNames: "assets/[name]-[hash]",
  chunkNames: "chunks/[name]-[hash]",
  legalComments: "linked",
});
// v1 렌더러는 JavaScript 템플릿을 포함해 완전한 class="..." 문자열을 검색합니다.
// Master 클래스는 완전한 문자열로 작성하고 유틸리티를 동적으로 이어 붙이지 않습니다.
const sources = fs
  .readdirSync(web)
  .filter((f) => /\.(js|html)$/.test(f))
  .sort();
const serverSources = fs
  .readdirSync(serverWeb)
  .filter((f) => /\.(js|html)$/.test(f))
  .sort();
const { css } = render(
  [
    ...sources.map((f) => fs.readFileSync(path.join(web, f), "utf8")),
    ...serverSources.map((f) =>
      fs.readFileSync(path.join(serverWeb, f), "utf8"),
    ),
  ].join("\n"),
  { StyleSheet },
);
if (!css.includes("gap"))
  throw Error("Master CSS 유틸리티를 생성하지 못했습니다.");
fs.mkdirSync(path.join(vendor, "master"), { recursive: true });
fs.writeFileSync(
  path.join(vendor, "master/master.css"),
  "/* @master/css 1.37.8로 생성했습니다. 직접 수정하지 말고 npm run build를 실행하세요. */\n" +
    css +
    "\n",
);
for (const asset of ["tabler", "tabler-icons", "master", "noto-sans-kr"])
  fs.cpSync(path.join(vendor, asset), path.join(serverWeb, "vendor", asset), {
    recursive: true,
  });
for (const asset of ["theme.js", "design-tokens.css", "logo.svg"])
  fs.copyFileSync(path.join(web, asset), path.join(serverWeb, asset));
console.log(
  "Tabler CSS, 아이콘 웹 폰트, Master CSS 유틸리티, xterm 및 Monaco 자산 빌드를 완료했습니다.",
);
