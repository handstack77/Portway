import { icon, btn } from "./ui.js";

// 접힌 메뉴에서 화면의 이름표가 숨겨져도 명시적인 접근성 이름은 유지합니다.
function sidebarButton(action, label, glyph, active = false, extra = "") {
  return /* HTML */ `<button
    type="button"
    class="sidebar-link${active ? " active" : ""}"
    data-act="${action}"
    aria-label="${label}"
    title="${label}"
  >
    ${icon(glyph)}<span class="sidebar-label">${label}</span>${extra}
  </button>`;
}

// 마크업 생성은 상태 변경·이벤트 등록과 분리합니다.
export function workspaceMarkup(isLocal) {
  return /* HTML */ `<aside class="sidebar" aria-label="주 메뉴">
      <div class="brand" aria-label="Portway">
        <img src="/logo.svg" alt="" />
        <span class="brand-label">Portway</span>
      </div>
      <div class="sidebar-scroll">
        <nav class="nav" aria-label="파일 작업">
          ${sidebarButton("explorer", "파일 탐색기", "folder", true)}${sidebarButton("queue", "전송 관리", "transfer", false, '<span class="count" id="nav-count">0</span>')}${sidebarButton("sync", "폴더 동기화", "sync")}${sidebarButton("bookmarks", "북마크", "bookmark")}
        </nav>
        <section class="sites-section" aria-label="저장된 사이트">
          ${sidebarButton("new-site", "새 사이트", "plus")}
          <div class="sites" id="sites"></div>
        </section>
      </div>
      <nav class="sidebar-bottom" aria-label="도구 및 설정">
        ${sidebarButton("import", "사이트 내보내기/가져오기", "external")}${sidebarButton("theme", "테마 변경", "theme")}${sidebarButton("settings", "설정 및 업데이트", "settings")}${sidebarButton("trash", "휴지통 복원", "trash")}${sidebarButton("editors", "외부 편집 세션", "edit")}${sidebarButton("help", "키보드 단축키", "help")}
      </nav>
    </aside>
    <main class="main">
      <section class="workspace">
        <div id="sessions" class="sessionbar"></div>
        <div class="workspace-actions">
          <div class="btn-group" role="group" aria-label="파일 전송 및 동기화">
            ${btn("upload", "업로드", "up")}${btn("download", "다운로드", "down")}${btn("external-files", "외부 파일", "upload")}${btn("external-folder", "외부 폴더", "folder")}${btn("sync", "동기화", "sync")}${btn("watches", "지속 동기화", "sync")}
          </div>
          <span class="spacer"> </span>
          <span class="info-label" id="selection-help"
            >빈 공간 드래그 · Ctrl/⌘ 추가 · Shift 범위 · 우클릭 작업</span
          >
          <button
            class="iconbtn"
            data-act="terminal"
            title="SSH 명령"
            aria-label="SSH 명령"
          >
            ${icon("terminal")}
          </button>
          <button
            class="iconbtn"
            data-act="bookmark-add"
            title="현재 경로 북마크"
            aria-label="현재 경로 북마크"
          >
            ${icon("bookmark")}
          </button>
          <button
            class="iconbtn"
            data-act="refresh"
            title="새로고침"
            aria-label="새로고침"
          >
            ${icon("refresh")}
          </button>
        </div>
        <div class="panels">
          ${paneMarkup("local", isLocal("local"))}${paneMarkup("remote", isLocal("remote"))}
        </div>
        <section class="queue card" id="queue">
          <div class="queue-header">
            ${icon("queue")}<strong>전송 큐</strong>
            <span class="badge" id="queue-count">0</span>
            <div class="tabs">
              <button data-filter="all" class="active">전체</button>
              <button data-filter="active">진행 중</button>
              <button data-filter="completed">완료</button>
            </div>
            <button class="iconbtn" data-act="clear-queue">기록 정리</button>
            <button
              class="iconbtn queue-toggle"
              data-act="toggle-queue"
              aria-controls="jobs"
              aria-expanded="true"
              aria-label="전송 큐 접기"
              title="전송 큐 접기"
            ></button>
          </div>
          <div class="queue-body" id="jobs"></div>
        </section>
      </section>
      <footer class="statusbar">
        <span class="left">
          <i class="dot"> </i>
          <span id="status">준비 완료</span>
          <span id="connection-elapsed" title="연결 이후 접속 경과 시간" hidden>
          </span>
        </span>
        <span class="shortcuts">
          <span> <kbd>F2</kbd>이름 변경</span>
          <span> <kbd>F4</kbd>편집</span>
          <span> <kbd>F5</kbd>전송</span>
          <span> <kbd>F7</kbd>새 폴더</span>
          <span> <kbd>Del</kbd>삭제</span>
        </span>
        <span id="version">Portway</span>
      </footer>
    </main>`;
}

function paneMarkup(side, local) {
  const label = side === "local" ? "로컬" : local ? "오른쪽 로컬" : "원격";
  return /* HTML */ `<section
    class="pane card ${side === "local" ? "focused" : ""}"
    id="pane-${side}"
    data-side="${side}"
  >
    <div class="pane-title">
      ${icon(local ? "monitor" : "server")}<strong
        >${local ? "내 컴퓨터" : "원격 서버"}</strong
      >
      <span class="protocol-tag" id="tag-${side}"
        >${local ? "LOCAL STORAGE" : "NOT CONNECTED"}</span
      >
      <button
        class="iconbtn"
        data-act="pane-menu"
        data-side="${side}"
        aria-label="${label} 파일 작업"
      >
        ${icon("more")}
      </button>
    </div>
    <form class="pathbar" id="pathform-${side}">
      <button
        type="button"
        data-act="parent"
        data-side="${side}"
        title="상위 폴더"
        aria-label="상위 폴더"
      >
        ${icon("up")}
      </button>
      <input
        id="path-${side}"
        aria-label="${label} 경로"
        autocomplete="off"
        spellcheck="false"
      />
      <button
        type="button"
        data-act="pane-refresh"
        data-side="${side}"
        title="새로고침"
        aria-label="패널 새로고침"
      >
        ${icon("refresh")}
      </button>
    </form>
    <div class="filterbar">
      ${icon("search")}<input
        id="filter-${side}"
        aria-label="${label} 파일 검색"
        placeholder="이 폴더에서 검색…"
      />
      <button
        type="button"
        class="iconbtn"
        data-act="help"
        title="선택 방법 · 단축키"
        aria-label="선택 방법 · 단축키"
      >
        ${icon("help")}
      </button>
    </div>
    <div
      class="filelist"
      id="files-${side}"
      tabindex="0"
      role="grid"
      aria-multiselectable="true"
      aria-describedby="selection-help"
      aria-label="${label} 파일 목록"
    ></div>
    <div class="pane-footer" id="footer-${side}"></div>
    ${
      side === "remote"
        ? /* HTML */ `<div
              class="drop-hint flex ai:center gap:8"
              ${local ? "hidden" : ""}
            >
              <i class="ti ti-drag-drop" aria-hidden="true"> </i>탐색기·Finder의
              파일과 폴더를 여기에 놓으세요
            </div>
            <div class="drop-overlay">
              <i class="ti ti-cloud-upload" aria-hidden="true"> </i>
              <strong>이 폴더로 업로드</strong>
              <span>파일·폴더를 놓고 전송 옵션을 확인하세요</span>
            </div>`
        : ""
    }
  </section>`;
}
