import { esc, icon, size } from "./ui.js";
import { formatFileDate } from "./display-time.js";

// 순수 렌더링 함수에는 이미 필터·정렬한 목록과 표시 상태만 전달합니다.
export function fileListMarkup(side, entries, pane) {
  return entries.length
    ? /* HTML */ `<table class="table table-vcenter" role="presentation">
          <colgroup>
            <col class="selection-col" />
            <col />
            <col class="size-col" />
            <col class="date-col" />
            <col class="permissions-col" />
          </colgroup>
          <thead role="rowgroup">
            <tr role="row" aria-rowindex="1">
              <th role="columnheader" class="selection-cell">
                <input
                  type="checkbox"
                  class="form-check-input"
                  data-select-all
                  tabindex="-1"
                  aria-label="표시된 항목 전체 선택"
                />
              </th>
              ${[
                ["name", "이름"],
                ["size", "크기"],
                ["modified", "수정한 날짜"],
              ]
                .map(
                  ([key, title]) =>
                    `<th role="columnheader" aria-sort="${(pane.sort || "name") === key ? (pane.sortAsc === false ? "descending" : "ascending") : "none"}"><button type="button" class="sort-heading" data-sort="${key}" data-side="${side}" title="${title}순 정렬">${title} ${(pane.sort || "name") === key ? (pane.sortAsc === false ? "↓" : "↑") : ""}</button></th>`,
                )
                .join("")}
              <th role="columnheader">권한</th>
            </tr>
          </thead>
          <tbody role="rowgroup">
            ${entries
              .map(
                (entry, index) =>
                  /* HTML */ `<tr
                    id="row-${side}-${index}"
                    role="row"
                    aria-rowindex="${index + 2}"
                    data-path="${esc(entry.path)}"
                    aria-selected="false"
                  >
                    <td role="gridcell" class="selection-cell">
                      <input
                        type="checkbox"
                        class="form-check-input"
                        data-row-check
                        tabindex="-1"
                        aria-label="${esc(entry.name)} 선택"
                      />
                    </td>
                    <td role="gridcell">
                      <div
                        class="filename"
                        draggable="true"
                        title="${esc(entry.name)} · 이름을 끌어 반대 패널로 전송"
                      >
                        ${icon(entry.isDirectory ? "folder" : "file", entry.isDirectory ? "folder" : "file")}<span
                          >${esc(entry.name)}${entry.isLink ? " ↗" : ""}</span
                        >
                      </div>
                    </td>
                    <td role="gridcell" class="size">
                      ${entry.isDirectory ? "—" : size(entry.size)}
                    </td>
                    <td role="gridcell" class="date">
                      ${formatFileDate(entry.modified)}
                    </td>
                    <td role="gridcell" class="size">
                      ${esc(entry.permissions)}
                    </td>
                  </tr>`,
              )
              .join("")}
          </tbody>
        </table>
        <div class="selection-space" aria-hidden="true"></div>`
    : /* HTML */ `<div class="empty">
        ${icon("folder")}
        <p>
          ${pane.filter ? "검색 결과가 없습니다." : "이 폴더는 비어 있습니다."}
        </p>
      </div>`;
}

export function fileFooterMarkup(side, connection) {
  return /* HTML */ `<div class="selection-summary">
      <span class="selection-count" role="status" aria-live="polite"></span
      ><span class="selection-size"></span>
    </div>
    <div class="selection-actions">
      <button
        type="button"
        class="iconbtn"
        data-explorer-action="clear"
        title="선택 해제 (Esc)"
        aria-label="선택 해제"
        hidden
      >
        ${icon("close")}</button
      ><button
        type="button"
        class="iconbtn"
        data-explorer-action="transfer"
        data-selection-required
        title="선택 항목 ${connection ? (side === "local" ? "업로드" : "다운로드") : "반대 패널로 복사"} (F5)"
        aria-label="선택 항목 ${connection ? (side === "local" ? "업로드" : "다운로드") : "반대 패널로 복사"}"
        disabled
      >
        ${icon(connection ? (side === "local" ? "up" : "down") : side === "local" ? "right" : "left")}</button
      ><button
        type="button"
        class="iconbtn"
        data-explorer-action="recycle"
        data-selection-required
        title="선택 항목 휴지통으로 이동 (Delete)"
        aria-label="선택 항목 휴지통으로 이동"
        disabled
      >
        ${icon("trash")}
      </button>
    </div>`;
}
