const state = {
  mode: "excel",
  startDate: "",
  endDate: "",
  csvBatchDate: "",
  rfOptions: [],
  selectedRfId: "",
  stdGroups: [],
  portGroups: [],
  ppbGroups: [],
  selectedStdIds: new Set(),
  selectedPortIds: new Set(),
  selectedPpbIds: new Set()
};

const API_BASE_URL = window.GAS_QC_API_BASE_URL ?? location.pathname.replace(/\/[^/]*$/, "");

const els = {
  startDate: document.querySelector("#startDate"),
  endDate: document.querySelector("#endDate"),
  csvBatchDate: document.querySelector("#csvBatchDate"),
  loadExcelButton: document.querySelector("#loadExcelButton"),
  loadCsvButton: document.querySelector("#loadCsvButton"),
  exportExcelButton: document.querySelector("#exportExcelButton"),
  exportCsvButton: document.querySelector("#exportCsvButton"),
  reloadRfButton: document.querySelector("#reloadRfButton"),
  rfList: document.querySelector("#rfList"),
  stdGroups: document.querySelector("#stdGroups"),
  portGroups: document.querySelector("#portGroups"),
  ppbGroups: document.querySelector("#ppbGroups"),
  stdCount: document.querySelector("#stdCount"),
  portCount: document.querySelector("#portCount"),
  rfCount: document.querySelector("#rfCount"),
  ppbCount: document.querySelector("#ppbCount"),
  ppbGroupCount: document.querySelector("#ppbGroupCount"),
  csvDateText: document.querySelector("#csvDateText"),
  statusText: document.querySelector("#statusText"),
  selectAllStd: document.querySelector("#selectAllStd"),
  clearStd: document.querySelector("#clearStd"),
  selectAllPort: document.querySelector("#selectAllPort"),
  clearPort: document.querySelector("#clearPort"),
  selectAllPpb: document.querySelector("#selectAllPpb"),
  clearPpb: document.querySelector("#clearPpb"),
  tabs: [...document.querySelectorAll(".tab-button")]
};

init();

function init() {
  const today = new Date();
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);

  els.startDate.value = toDateInput(yesterday);
  els.endDate.value = toDateInput(today);
  els.csvBatchDate.value = toDateInput(today);

  els.tabs.forEach(tab => {
    tab.addEventListener("click", () => setMode(tab.dataset.mode));
  });

  els.loadExcelButton.addEventListener("click", loadExcelGroups);
  els.loadCsvButton.addEventListener("click", loadPpbGroups);
  els.reloadRfButton.addEventListener("click", loadRfOptions);
  els.exportExcelButton.addEventListener("click", exportExcel);
  els.exportCsvButton.addEventListener("click", exportCsv);
  els.selectAllStd.addEventListener("click", () => selectAll("std"));
  els.clearStd.addEventListener("click", () => clearAll("std"));
  els.selectAllPort.addEventListener("click", () => selectAll("port"));
  els.clearPort.addEventListener("click", () => clearAll("port"));
  els.selectAllPpb.addEventListener("click", () => selectAll("ppb"));
  els.clearPpb.addEventListener("click", () => clearAll("ppb"));

  loadRfOptions();
  updateSummary();
}

function setMode(mode) {
  state.mode = mode;
  document.querySelectorAll(".excel-view").forEach(el => {
    el.hidden = mode !== "excel";
  });
  document.querySelectorAll(".csv-view").forEach(el => {
    el.hidden = mode !== "csv";
  });
  els.exportExcelButton.hidden = mode !== "excel";
  els.exportCsvButton.hidden = mode !== "csv";
  els.tabs.forEach(tab => {
    tab.classList.toggle("active", tab.dataset.mode === mode);
  });
  setStatus(mode === "excel" ? "載入 STD / PORT / RF 後可匯出 Query2 Excel。" : "載入 PORT_PPB 後可匯出 TO14C CSV。");
  updateSummary();
}

async function loadExcelGroups() {
  const startDate = normalizeDateInput(els.startDate.value);
  const endDate = normalizeDateInput(els.endDate.value);
  if (!startDate || !endDate) {
    setStatus("請選擇開始日期與結束日期。", true);
    return;
  }

  state.startDate = startDate;
  state.endDate = endDate;
  state.selectedStdIds.clear();
  state.selectedPortIds.clear();
  setStatus("載入 STD / PORT 資料中...");
  setBusy(true);

  try {
    const data = await getJson(`/api/export-groups?startDate=${startDate}&endDate=${endDate}`);
    state.stdGroups = data.stdGroups ?? [];
    state.portGroups = data.portGroups ?? [];
    renderGroups("std", state.stdGroups, els.stdGroups);
    renderGroups("port", state.portGroups, els.portGroups);
    setStatus(`已載入 ${startDate} - ${endDate} 的可匯出資料。`, false, true);
  } catch (error) {
    setStatus(error.message, true);
  } finally {
    setBusy(false);
    updateSummary();
  }
}

async function loadPpbGroups() {
  const batchDate = normalizeDateInput(els.csvBatchDate.value);
  if (!batchDate) {
    setStatus("請選擇 CSV 批次日期。", true);
    return;
  }

  state.csvBatchDate = batchDate;
  state.selectedPpbIds.clear();
  setStatus("載入 PORT_PPB 資料中...");
  setBusy(true);

  try {
    const data = await getJson(`/api/port-ppb-options?batchDate=${batchDate}`);
    state.ppbGroups = data.portGroups ?? [];
    renderGroups("ppb", state.ppbGroups, els.ppbGroups);
    setStatus(`已載入 ${batchDate} 的 PORT_PPB 資料。`, false, true);
  } catch (error) {
    setStatus(error.message, true);
  } finally {
    setBusy(false);
    updateSummary();
  }
}

async function loadRfOptions() {
  setStatus("載入 RF 中...");
  try {
    state.rfOptions = await getJson("/api/rf-options");
    if (!state.selectedRfId && state.rfOptions.length > 0) {
      state.selectedRfId = state.rfOptions[0].id ?? "";
    }
    renderRfOptions();
    setStatus(`已載入 ${state.rfOptions.length} 筆 RF。`, false, true);
  } catch (error) {
    setStatus(error.message, true);
  } finally {
    updateSummary();
  }
}

async function exportExcel() {
  if (!state.selectedRfId) {
    setStatus("請選擇一筆 RF。", true);
    return;
  }

  if (state.selectedStdIds.size === 0 || state.selectedPortIds.size === 0) {
    setStatus("請至少選擇一筆 STD 與一筆 PORT。", true);
    return;
  }

  setStatus("產生 Excel 中...");
  setBusy(true);
  try {
    await downloadFromPost("/api/exports/query2-excel", {
      startDate: state.startDate,
      endDate: state.endDate,
      rfId: state.selectedRfId,
      stdRawIds: [...state.selectedStdIds],
      portRawIds: [...state.selectedPortIds]
    }, `Cylinder_Qc[${state.startDate}-${state.endDate}].xlsx`);
    setStatus("Excel 已產生。", false, true);
  } catch (error) {
    setStatus(error.message, true);
  } finally {
    setBusy(false);
  }
}

async function exportCsv() {
  if (!state.csvBatchDate) {
    setStatus("請先載入 CSV 批次日期。", true);
    return;
  }

  if (state.selectedPpbIds.size === 0) {
    setStatus("請至少選擇一筆 PORT_PPB。", true);
    return;
  }

  setStatus("產生 CSV 中...");
  setBusy(true);
  try {
    await downloadFromPost("/api/exports/port-ppb-csv", {
      batchDate: state.csvBatchDate,
      selectedIds: [...state.selectedPpbIds]
    }, `TO14C_PPB[${state.csvBatchDate}].csv`);
    setStatus("CSV 已產生。", false, true);
  } catch (error) {
    setStatus(error.message, true);
  } finally {
    setBusy(false);
  }
}

function renderRfOptions() {
  els.rfList.classList.toggle("empty", state.rfOptions.length === 0);
  els.rfList.innerHTML = state.rfOptions.length === 0
    ? "沒有 RF 資料"
    : state.rfOptions.map((rf, index) => {
      const id = rf.id ?? "";
      const checked = id === state.selectedRfId || (!state.selectedRfId && index === 0);
      return `
        <div class="rf-item">
          <label>
            <input type="radio" name="rf" value="${escapeHtml(id)}" ${checked ? "checked" : ""}>
            <span>
              <span class="title-line">
                <span>${escapeHtml(id || "未命名 RF")}</span>
                <span class="badge">${escapeHtml(rf.si0Id ?? "")}</span>
              </span>
              <span class="meta">${formatDateTime(rf.anlzTime)} · ${escapeHtml(rf.sampleName ?? "")}</span>
            </span>
          </label>
        </div>`;
    }).join("");

  els.rfList.querySelectorAll("input[name='rf']").forEach(input => {
    input.addEventListener("change", () => {
      state.selectedRfId = input.value;
      updateSummary();
    });
  });
}

function renderGroups(kind, groups, target) {
  const selected = selectedSetFor(kind);
  target.classList.toggle("empty", groups.length === 0);
  target.innerHTML = groups.length === 0
    ? `沒有 ${labelFor(kind)} 資料`
    : groups.map(group => renderGroup(kind, group, selected)).join("");

  target.querySelectorAll("input[data-row-id]").forEach(input => {
    input.addEventListener("change", () => {
      const set = selectedSetFor(input.dataset.kind);
      if (input.checked) {
        set.add(input.value);
      } else {
        set.delete(input.value);
      }
      updateGroupCheckbox(input.closest(".group"));
      updateSummary();
    });
  });

  target.querySelectorAll("input[data-group]").forEach(input => {
    input.addEventListener("change", () => {
      const groupEl = input.closest(".group");
      const rows = groupEl.querySelectorAll("input[data-row-id]");
      rows.forEach(row => {
        row.checked = input.checked;
        const set = selectedSetFor(row.dataset.kind);
        if (input.checked) {
          set.add(row.value);
        } else {
          set.delete(row.value);
        }
      });
      updateSummary();
    });
  });

  target.querySelectorAll("input[data-group][data-partial='true']").forEach(input => {
    input.indeterminate = true;
  });
}

function renderGroup(kind, group, selected) {
  const rows = group.rows ?? [];
  const checkedRows = rows.filter(row => selected.has(row.id)).length;
  const groupChecked = rows.length > 0 && checkedRows === rows.length;
  const groupPartial = checkedRows > 0 && checkedRows < rows.length;
  const sampleText = group.sampleName || group.lotNo || "未命名";
  return `
    <article class="group">
      <label class="group-summary">
        <input type="checkbox" data-group="${escapeHtml(group.groupId)}" ${groupChecked ? "checked" : ""} ${groupPartial ? "data-partial='true'" : ""}>
        <span>
          <span class="title-line">
            <span>${escapeHtml(group.port)} · ${escapeHtml(sampleText)}</span>
            <span class="badge">${rows.length} 筆</span>
          </span>
          <span class="meta">LOT ${escapeHtml(group.lotNo)} · 最近兩筆 ${escapeHtml(lastTwoText(rows))}</span>
        </span>
      </label>
      <div class="row-list">
        ${rows.map(row => `
          <label class="row-item">
            <input type="checkbox" data-kind="${kind}" data-row-id value="${escapeHtml(row.id)}" ${selected.has(row.id) ? "checked" : ""}>
            <span>${formatDateTime(row.anlzTime)} · S${escapeHtml(row.sampleNo ?? "")} · ${escapeHtml(row.sourceFolderName ?? row.id)}</span>
          </label>
        `).join("")}
      </div>
    </article>`;
}

function updateGroupCheckbox(groupEl) {
  if (!groupEl) return;
  const groupInput = groupEl.querySelector("input[data-group]");
  const rows = [...groupEl.querySelectorAll("input[data-row-id]")];
  const checked = rows.filter(row => row.checked).length;
  groupInput.checked = rows.length > 0 && checked === rows.length;
  groupInput.indeterminate = checked > 0 && checked < rows.length;
}

function selectAll(kind) {
  const groups = groupsFor(kind);
  const selected = selectedSetFor(kind);
  groups.flatMap(group => group.rows ?? []).forEach(row => selected.add(row.id));
  renderGroups(kind, groups, targetFor(kind));
  updateSummary();
}

function clearAll(kind) {
  const groups = groupsFor(kind);
  selectedSetFor(kind).clear();
  renderGroups(kind, groups, targetFor(kind));
  updateSummary();
}

function updateSummary() {
  els.stdCount.textContent = state.selectedStdIds.size.toString();
  els.portCount.textContent = state.selectedPortIds.size.toString();
  els.rfCount.textContent = state.selectedRfId ? "1" : "0";
  els.ppbCount.textContent = state.selectedPpbIds.size.toString();
  els.ppbGroupCount.textContent = state.ppbGroups.length.toString();
  els.csvDateText.textContent = state.csvBatchDate || "-";
  els.exportExcelButton.disabled = !canExportExcel();
  els.exportCsvButton.disabled = !canExportCsv();
}

function canExportExcel() {
  return Boolean(state.selectedRfId && state.selectedStdIds.size > 0 && state.selectedPortIds.size > 0 && state.startDate && state.endDate);
}

function canExportCsv() {
  return Boolean(state.csvBatchDate && state.selectedPpbIds.size > 0);
}

function setBusy(isBusy) {
  els.loadExcelButton.disabled = isBusy;
  els.loadCsvButton.disabled = isBusy;
  els.reloadRfButton.disabled = isBusy;
  els.exportExcelButton.disabled = isBusy || !canExportExcel();
  els.exportCsvButton.disabled = isBusy || !canExportCsv();
}

function setStatus(message, isError = false, isOk = false) {
  els.statusText.textContent = message;
  els.statusText.className = isError ? "status-error" : isOk ? "status-ok" : "";
}

async function downloadFromPost(url, body, fallbackName) {
  const response = await fetch(apiUrl(url), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });

  if (!response.ok) {
    throw new Error(await readError(response));
  }

  const blob = await response.blob();
  const fileName = parseDownloadName(response.headers.get("content-disposition")) || fallbackName;
  const objectUrl = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = objectUrl;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(objectUrl);
}

async function getJson(url) {
  const response = await fetch(apiUrl(url));
  if (!response.ok) {
    throw new Error(await readError(response));
  }
  return response.json();
}

function apiUrl(path) {
  return API_BASE_URL ? `${API_BASE_URL}${path}` : path;
}

async function readError(response) {
  try {
    const data = await response.json();
    return data.message || response.statusText;
  } catch {
    return response.statusText;
  }
}

function groupsFor(kind) {
  if (kind === "std") return state.stdGroups;
  if (kind === "port") return state.portGroups;
  return state.ppbGroups;
}

function selectedSetFor(kind) {
  if (kind === "std") return state.selectedStdIds;
  if (kind === "port") return state.selectedPortIds;
  return state.selectedPpbIds;
}

function targetFor(kind) {
  if (kind === "std") return els.stdGroups;
  if (kind === "port") return els.portGroups;
  return els.ppbGroups;
}

function labelFor(kind) {
  if (kind === "std") return "STD";
  if (kind === "port") return "PORT";
  return "PORT_PPB";
}

function lastTwoText(rows) {
  return [...rows]
    .sort((a, b) => new Date(a.anlzTime) - new Date(b.anlzTime))
    .slice(-2)
    .map(row => formatTime(row.anlzTime))
    .join("、") || "不足兩筆";
}

function formatDateTime(value) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function formatTime(value) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function normalizeDateInput(value) {
  return value ? value.replaceAll("-", "") : "";
}

function toDateInput(date) {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function pad(value) {
  return value.toString().padStart(2, "0");
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function parseDownloadName(header) {
  if (!header) return "";
  const match = /filename\*?=(?:UTF-8'')?("?)([^";]+)\1/i.exec(header);
  return match ? decodeURIComponent(match[2]) : "";
}
