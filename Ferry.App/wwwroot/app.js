"use strict";

// ================= 状态 =================
let plugins = [];
let projects = [];
let currentProjectId = "";
let nav = { workspaces: [], unassigned: [] };
let currentConfig = null;
let currentWorkspaceId = "";
let snapshot = null;
let sourceText = "";
let errors = [];
let collapsedPaths = new Set();
let sourceOpen = false;
let settingsCategory = "general";
let ctxConfig = null;
let dragCfg = null;
let pendingDrop = null;
let dataDir = "";
let requestSeq = 0;
let tooltipTimer = null;
let promptCallback = null;
let confirmCallback = null;
const inflight = new Map();
const wizard = { pluginKey: "", templateId: "__blank", step: 1, autoName: true };

function loadLocal(key, fallback) {
  try {
    const v = localStorage.getItem(key);
    return v === null ? fallback : JSON.parse(v);
  } catch (e) { return fallback; }
}
function saveLocal(key, value) {
  try { localStorage.setItem(key, JSON.stringify(value)); } catch (e) {}
}

// ================= IPC（requestId 配对，容忍乱序/重复交付） =================
function send(action, payload, onOk) {
  const requestId = "r" + (++requestSeq);
  const item = { action, onOk, t0: performance.now() };
  inflight.set(requestId, item);
  item.timer = setTimeout(() => {
    if (inflight.has(requestId)) {
      inflight.delete(requestId);
      if (onOk) onOk({ ok: false, errors: ["IPC 超时"] });
    }
  }, 10000);
  window.external.sendMessage(JSON.stringify(Object.assign({ action, requestId }, payload || {})));
}

function log(text) {
  try { window.external.sendMessage(JSON.stringify({ action: "log", text })); } catch (e) {}
}
window.onerror = function (msg, src, line) {
  log("js-error:" + msg + " @" + (src || "") + ":" + line);
};
window.addEventListener("unhandledrejection", function (e) {
  log("unhandled-rejection:" + (e.reason && e.reason.message ? e.reason.message : String(e.reason)));
});

window.external.receiveMessage(function (json) {
  try {
    const data = JSON.parse(json);
    if (data.action === "spike:run") { runSpike(); return; }
    const item = data.requestId ? inflight.get(data.requestId) : null;
    if (!item) return;
    clearTimeout(item.timer);
    inflight.delete(data.requestId);
    if (data.latencyMs !== undefined) {
      document.getElementById("latency").textContent =
        "IPC " + data.latencyMs.toFixed(1) + "ms";
    }
    if (item.onOk) item.onOk(data);
  } catch (e) {
    log("receive-error:" + e.message);
  }
});

// ================= 通用 =================
function okOr(data) {
  if (!data.ok) {
    setStatus((data.errors || ["操作失败"]).join("；"), true);
    return false;
  }
  return true;
}
function setStatus(text, isError) {
  const el = document.getElementById("statusText");
  el.textContent = text;
  el.className = isError ? "bad" : "ok";
}
function showToast(message) {
  const toast = document.getElementById("toast");
  toast.textContent = message;
  toast.classList.add("show");
  clearTimeout(showToast._timer);
  showToast._timer = setTimeout(() => toast.classList.remove("show"), 2200);
}
function openModal(id) { document.getElementById(id).classList.add("open"); }
function closeModal(id) { document.getElementById(id).classList.remove("open"); }
function textSpan(text) {
  const el = document.createElement("span");
  el.className = "hint";
  el.textContent = text;
  return el;
}
function el(tag, cls, text) {
  const node = document.createElement(tag);
  if (cls) node.className = cls;
  if (text !== undefined) node.textContent = text;
  return node;
}
function escapeHtml(text) {
  return String(text).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

function applyFormUpdate(data) {
  if (!data) return;
  if (data.snapshot) { snapshot = data.snapshot; renderForm(); }
  if (data.text !== undefined && data.text !== null) { sourceText = data.text; renderPreview(); }
  if (data.errors) {
    errors = data.errors;
    setStatus(errors.length === 0 ? "校验通过" : "校验：" + errors.length + " 个错误", errors.length > 0);
  }
}
function applyLightUpdate(data) {
  if (!data) return;
  if (data.snapshot) snapshot = data.snapshot;
  if (data.text !== undefined && data.text !== null) { sourceText = data.text; renderPreview(); }
  if (data.errors) {
    errors = data.errors;
    setStatus(errors.length === 0 ? "校验通过" : "校验：" + errors.length + " 个错误", errors.length > 0);
  }
  updateModuleStatus();
}

// ================= 项目 / 导航 =================
function refreshProjects(selectProjectId) {
  send("projects:list", null, (data) => {
    projects = data.projects || [];
    if (projects.length === 0) {
      send("project:create", { name: "默认项目" }, () => refreshProjects(selectProjectId));
      return;
    }
    currentProjectId = selectProjectId || loadLocal("ferry.projectId", "") || projects[0].id;
    if (!projects.some(p => p.id === currentProjectId)) currentProjectId = projects[0].id;
    saveLocal("ferry.projectId", currentProjectId);
    renderProjectSelector();
    loadNav();
  });
}

function renderProjectSelector() {
  const project = projects.find(p => p.id === currentProjectId);
  document.getElementById("projectName").textContent = project ? project.name : "选择项目";
  const list = document.getElementById("projectMenuList");
  list.textContent = "";
  for (const p of projects) {
    const item = document.createElement("div");
    item.className = "menu-item" + (p.id === currentProjectId ? " active" : "");
    item.appendChild(el("span", "", p.name));
    if (p.id === currentProjectId) item.appendChild(el("span", "check", "✓"));
    item.onclick = () => {
      if (p.id !== currentProjectId) {
        currentProjectId = p.id;
        saveLocal("ferry.projectId", currentProjectId);
        clearOpenConfig();
        loadNav();
        renderProjectSelector();
      }
      toggleProjectMenu(false);
    };
    list.appendChild(item);
  }
  const sep = document.createElement("div");
  sep.className = "menu-sep";
  list.appendChild(sep);
  const rename = document.createElement("div");
  rename.className = "menu-item";
  rename.textContent = "重命名";
  rename.onclick = () => {
    toggleProjectMenu(false);
    const cur = projects.find(p => p.id === currentProjectId);
    if (!cur) return;
    showPrompt("项目名称", cur.name, (name) => {
      if (!name) return;
      send("project:rename", { id: cur.id, name }, (d) => {
        if (okOr(d)) { refreshProjects(cur.id); showToast("已重命名项目"); }
      });
    });
  };
  list.appendChild(rename);
  const del = document.createElement("div");
  del.className = "menu-item";
  del.style.color = "#f85149";
  del.textContent = "删除项目";
  del.onclick = () => {
    toggleProjectMenu(false);
    const cur = projects.find(p => p.id === currentProjectId);
    if (!cur) return;
    showConfirm("删除项目", "确定删除「" + cur.name + "」及其全部工作空间、配置与版本？", () => {
      send("project:delete", { id: cur.id }, (d) => {
        if (okOr(d)) { currentProjectId = ""; refreshProjects(); showToast("已删除项目"); }
      });
    });
  };
  list.appendChild(del);
}

function toggleProjectMenu(open) {
  const menu = document.getElementById("projectMenu");
  menu.classList.toggle("open", open !== undefined ? open : !menu.classList.contains("open"));
}

function loadNav(callback) {
  send("nav:tree", { projectId: currentProjectId }, (data) => {
    if (!okOr(data)) return;
    nav = { workspaces: data.workspaces || [], unassigned: data.unassigned || [] };
    renderWorkspaceTree();
    renderUnassignedTree();
    renderTools();
    renderWelcome();
    updateTbCrumb();
    if (callback) callback();
  });
}

function renderTools() {
  const modules = window.FerryModules ? FerryModules.list() : [];
  document.getElementById("toolsSection").style.display = modules.length ? "" : "none";
  const tree = document.getElementById("toolsTree");
  tree.textContent = "";
  for (const m of modules) {
    const item = el("div", "item child");
    item.appendChild(el("span", "name", m.name || m.id));
    if (m.onOpen) item.onclick = m.onOpen;
    tree.appendChild(item);
  }
  document.getElementById("toolsCount").textContent = modules.length || "";
}

function renderWorkspaceTree() {
  const elW = document.getElementById("workspaceTree");
  elW.textContent = "";
  if (nav.workspaces.length === 0) elW.appendChild(textSpan("暂无工作空间"));
  for (const ws of nav.workspaces) {
    const header = document.createElement("div");
    header.className = "tree";
    header.appendChild(el("span", "", "▾ " + ws.name));
    const menuBtn = el("span", "menu-btn", "⋯");
    menuBtn.title = "工作空间操作";
    menuBtn.onclick = (e) => { e.stopPropagation(); showWsCtxMenu(e, ws); };
    header.appendChild(menuBtn);
    const plus = el("span", "plus", "＋");
    plus.title = "在该工作空间快速新建配置";
    plus.onclick = (e) => { e.stopPropagation(); openWizard("", "", ws.id); };
    header.appendChild(plus);
    header.addEventListener("contextmenu", (e) => { e.preventDefault(); showWsCtxMenu(e, ws); });
    header.addEventListener("dragover", (e) => {
      if (!dragCfg) return;
      e.preventDefault();
      header.classList.add("drag-over");
    });
    header.addEventListener("dragleave", (e) => {
      if (e.relatedTarget && header.contains(e.relatedTarget)) return;
      header.classList.remove("drag-over");
    });
    header.addEventListener("drop", (e) => {
      e.preventDefault();
      e.stopPropagation();
      header.classList.remove("drag-over");
      const from = dragCfg;
      dragCfg = null;
      hideDragPreview();
      setWsDropZoneVisible(false);
      clearDropMarks();
      if (!from) return;
      if (from.ws === ws.id) {
        const ids = wsConfigIds(ws.id);
        const fromIdx = ids.indexOf(from.id);
        if (fromIdx < 0) return;
        ids.splice(fromIdx, 1);
        ids.push(from.id);
        saveOrderFor(ws.id, ids);
        return;
      }
      pendingDrop = { configId: from.id, targetWs: ws.id, targetCfgId: null, before: true };
      send("config:move", { configId: from.id, workspaceId: ws.id }, (d) => {
        if (!okOr(d)) { pendingDrop = null; loadNav(); return; }
        loadNav(applyPendingDropOrder);
      });
    });
    const group = document.createElement("div");
    group.className = "tree-group open";
    header.onclick = () => group.classList.toggle("open");
    elW.appendChild(header);
    const order = loadWsOrder(ws.id);
    const ordered = (ws.configs || []).slice().sort((a, b) => {
      const ia = order.indexOf(a.id), ib = order.indexOf(b.id);
      if (ia < 0 && ib < 0) return 0;
      if (ia < 0) return 1;
      if (ib < 0) return -1;
      return ia - ib;
    });
    for (const cfg of ordered) group.appendChild(renderConfigItem(cfg, ws.id));
    elW.appendChild(group);
  }
}

function wsOrderKey(workspaceId) {
  return "ferry.order." + currentProjectId + "." + (workspaceId || "unassigned");
}
function loadWsOrder(workspaceId) {
  return loadLocal(wsOrderKey(workspaceId), []);
}
function saveWsOrder(workspaceId, ids) {
  saveLocal(wsOrderKey(workspaceId), ids);
}
function setWsDropZoneVisible(show) {
  const zone = document.getElementById("wsDropZone");
  if (zone) zone.classList.toggle("visible", !!show && !!dragCfg);
}

function wsConfigIds(workspaceId) {
  const list = workspaceId
    ? ((nav.workspaces.find(w => w.id === workspaceId) || {}).configs || []).map(c => c.id)
    : (nav.unassigned || []).map(c => c.id);
  const order = loadWsOrder(workspaceId);
  return list.slice().sort((a, b) => {
    const ia = order.indexOf(a), ib = order.indexOf(b);
    if (ia < 0 && ib < 0) return 0;
    if (ia < 0) return 1;
    if (ib < 0) return -1;
    return ia - ib;
  });
}

function saveOrderFor(workspaceId, ids) {
  saveWsOrder(workspaceId, ids);
  renderWorkspaceTree();
  renderUnassignedTree();
}

function showDragPreview(name) {
  const el = document.getElementById("dragPreview");
  el.textContent = name;
  el.style.display = "block";
}
function moveDragPreview(x, y) {
  const el = document.getElementById("dragPreview");
  if (el.style.display === "none") return;
  el.style.left = (x + 14) + "px";
  el.style.top = (y + 18) + "px";
}
function hideDragPreview() {
  document.getElementById("dragPreview").style.display = "none";
}

function clearDropMarks() {
  document.querySelectorAll(
    ".item.drop-before, .item.drop-after, .tree.drag-over, " +
    "#unassignedTree.drag-over, #workspaceTree.drag-over, .ws-drop-zone.drag-over"
  ).forEach(n => n.classList.remove("drop-before", "drop-after", "drag-over"));
}

function flashDropTarget(workspaceId, cfgId) {
  const sel = '#workspaceTree .item[data-cfg="' + cfgId + '"], #unassignedTree .item[data-cfg="' + cfgId + '"]';
  const node = document.querySelector(sel);
  if (!node) return;
  node.classList.add("drop-flash");
  setTimeout(() => node.classList.remove("drop-flash"), 450);
}

function applyPendingDropOrder() {
  const p = pendingDrop;
  pendingDrop = null;
  if (!p) return;
  const ids = wsConfigIds(p.targetWs);
  const fromIdx = ids.indexOf(p.configId);
  if (fromIdx >= 0) ids.splice(fromIdx, 1);
  const toIdx = ids.indexOf(p.targetCfgId || "");
  if (toIdx < 0) {
    ids.push(p.configId);
  } else {
    ids.splice(p.before ? toIdx : toIdx + 1, 0, p.configId);
  }
  saveWsOrder(p.targetWs, ids);
  saveOrderFor(p.targetWs, ids);
  flashDropTarget(p.targetWs, p.configId);
  if (currentConfig && currentConfig.id === p.configId) {
    currentWorkspaceId = p.targetWs;
    updateTbCrumb();
  }
}

function renderUnassignedTree() {
  const elU = document.getElementById("unassignedTree");
  elU.textContent = "";
  document.getElementById("cfgCount").textContent = nav.unassigned.length || "";
  if (nav.unassigned.length === 0) {
    elU.appendChild(textSpan("暂无未归类配置"));
    return;
  }
  const order = loadWsOrder("");
  const ordered = nav.unassigned.slice().sort((a, b) => {
    const ia = order.indexOf(a.id), ib = order.indexOf(b.id);
    if (ia < 0 && ib < 0) return 0;
    if (ia < 0) return 1;
    if (ib < 0) return -1;
    return ia - ib;
  });
  for (const cfg of ordered) elU.appendChild(renderConfigItem(cfg, ""));
}

function renderConfigItem(cfg, workspaceId) {
  const item = document.createElement("div");
  item.className = "item child" + (currentConfig && cfg.id === currentConfig.id ? " active" : "");
  item.dataset.cfg = cfg.id;
  item.draggable = true;
  item.appendChild(el("span", "", "🌐"));
  item.appendChild(el("span", "name", cfg.name));
  if (cfg.pluginMissing) item.appendChild(el("span", "badge missing", "缺插件"));
  const menuBtn = el("span", "menu-btn", "⋯");
  menuBtn.onclick = (e) => { e.stopPropagation(); showCtxMenu(e, cfg, workspaceId); };
  item.appendChild(menuBtn);
  item.onclick = () => openConfig(cfg, workspaceId);
  item.addEventListener("contextmenu", (e) => { e.preventDefault(); showCtxMenu(e, cfg, workspaceId); });
  item.addEventListener("dragstart", (e) => {
    dragCfg = { id: cfg.id, ws: workspaceId, name: cfg.name };
    e.dataTransfer.effectAllowed = "move";
    try {
      const ghost = new Image();
      ghost.src = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";
      e.dataTransfer.setDragImage(ghost, 0, 0);
    } catch (err) { /* 自定义预览已隐藏默认幽灵图 */ }
    item.classList.add("dragging-source");
    showDragPreview(cfg.name);
    setWsDropZoneVisible(false);
  });
  item.addEventListener("dragover", (e) => {
    if (!dragCfg || dragCfg.id === cfg.id) return;
    e.preventDefault();
    e.stopPropagation();
    setWsDropZoneVisible(false);
    document.querySelectorAll(".item.drop-before, .item.drop-after")
      .forEach(n => n.classList.remove("drop-before", "drop-after"));
    const rect = item.getBoundingClientRect();
    const before = e.clientY < rect.top + rect.height / 2;
    item.classList.toggle("drop-before", before);
    item.classList.toggle("drop-after", !before);
  });
  item.addEventListener("dragleave", (e) => {
    if (e.relatedTarget && item.contains(e.relatedTarget)) return;
    item.classList.remove("drop-before", "drop-after");
  });
  item.addEventListener("drop", (e) => {
    e.preventDefault();
    e.stopPropagation();
    const from = dragCfg;
    const before = item.classList.contains("drop-before");
    dragCfg = null;
    item.classList.remove("dragging-source", "drop-before", "drop-after");
    hideDragPreview();
    setWsDropZoneVisible(false);
    clearDropMarks();
    if (!from || from.id === cfg.id) return;
    if (from.ws === workspaceId) {
      const ids = wsConfigIds(workspaceId);
      const fromIdx = ids.indexOf(from.id);
      if (fromIdx < 0) return;
      ids.splice(fromIdx, 1);
      const toIdx = ids.indexOf(cfg.id);
      if (toIdx < 0) return;
      ids.splice(before ? toIdx : toIdx + 1, 0, from.id);
      saveOrderFor(workspaceId, ids);
      flashDropTarget(workspaceId, cfg.id);
      return;
    }
    pendingDrop = { configId: from.id, targetWs: workspaceId, targetCfgId: cfg.id, before };
    send("config:move", { configId: from.id, workspaceId }, (d) => {
      if (!okOr(d)) { pendingDrop = null; loadNav(); return; }
      loadNav(applyPendingDropOrder);
    });
  });
  item.addEventListener("dragend", () => {
    dragCfg = null;
    item.classList.remove("dragging-source", "drop-before", "drop-after");
    hideDragPreview();
    setWsDropZoneVisible(false);
    clearDropMarks();
  });
  return item;
}

function updateTbCrumb() {
  const project = projects.find(p => p.id === currentProjectId);
  const ws = nav.workspaces.find(w => w.id === currentWorkspaceId);
  const text = currentConfig
    ? (project ? project.name : "项目") + " / " + (ws ? ws.name : "未归类")
    : project ? project.name : "";
  document.getElementById("tbCrumb").textContent = text;
}

// ================= 配置菜单 =================
function hideCtxMenu() { document.getElementById("ctxMenu").classList.remove("open"); }

function openCtxMenu(items, anchorEl) {
  const menu = document.getElementById("ctxMenu");
  menu.textContent = "";
  for (const it of items) {
    const row = document.createElement("div");
    row.className = "menu-item";
    row.textContent = it.text;
    if (it.danger) row.style.color = "#f85149";
    if (it.disabled) {
      row.style.color = "#666";
      row.style.cursor = "not-allowed";
    } else {
      row.onclick = () => { hideCtxMenu(); it.fn(); };
    }
    menu.appendChild(row);
  }
  menu.style.visibility = "hidden";
  menu.classList.add("open");
  const rect = anchorEl.getBoundingClientRect();
  const menuW = menu.offsetWidth;
  const menuH = menu.offsetHeight;
  let left = rect.left;
  let top = rect.bottom;
  if (left + menuW > window.innerWidth - 8) left = window.innerWidth - menuW - 8;
  if (top + menuH > window.innerHeight - 8) top = window.innerHeight - menuH - 8;
  menu.style.left = left + "px";
  menu.style.top = top + "px";
  menu.style.visibility = "";
}

function showCtxMenu(e, cfg, workspaceId) {
  ctxConfig = { cfg, workspaceId };
  const dynamic = window.FerryModules ? FerryModules.list()
    .filter(m => m.configMenu)
    .map(m => ({ text: m.configMenu.label, fn: m.configMenu.onOpen })) : [];
  const items = [
    { text: "查看", fn: () => openConfig(cfg, workspaceId) },
    { text: "导出", fn: () => exportConfigArchive(cfg, workspaceId) },
    { text: "复制", fn: () => openWizard(cfg.pluginKey, cfg.name + " - 副本", workspaceId) },
    { text: "移动", fn: () => openMoveModal() },
    { text: "历史", fn: () => openVersions() },
    { text: "回滚", fn: () => openVersions() },
    {
      text: "恢复全部默认配置",
      fn: () => showConfirm("恢复默认配置", "确定将「" + cfg.name + "」恢复为插件默认？", () => {
        if (currentConfig && currentConfig.id === cfg.id) {
          send("config:reset", {}, (d) => { if (okOr(d)) applyFormUpdate(d); });
        } else {
          openConfig(cfg, workspaceId);
        }
      })
    },
    { text: "推送", disabled: true },
    ...dynamic,
    { text: "删除", danger: true, fn: () => deleteConfig(cfg, workspaceId) }
  ];
  openCtxMenu(items, e.currentTarget);
}

function showWsCtxMenu(e, ws) {
  const items = [
    { text: "快速新建配置", fn: () => openWizard("", "", ws.id) },
    {
      text: "重命名",
      fn: () => showPrompt("工作空间名称", ws.name, (name) => {
        if (!name) return;
        send("workspace:rename", { id: ws.id, name }, (d) => {
          if (okOr(d)) { loadNav(); showToast("已重命名工作空间"); }
        });
      })
    },
    {
      text: "导出存档",
      fn: () => showPrompt("存档包导出路径", ws.name + ".zip", (path) => {
        if (!path) return;
        send("archive:exportWorkspace", { workspaceId: ws.id, path }, (d) => {
          if (okOr(d)) showToast("已导出：" + d.path);
        });
      })
    },
    {
      text: "删除",
      danger: true,
      fn: () => showConfirm("删除工作空间", "确定删除「" + ws.name + "」？将先存档到回收站，可还原。", () => {
        const zipName = ws.name + "-" + Date.now() + ".zip";
        send("archive:exportWorkspace", { workspaceId: ws.id, path: trashPath(zipName) }, (d) => {
          if (!okOr(d)) return;
          send("workspace:delete", { id: ws.id }, (r) => {
            if (okOr(r)) { loadNav(); FerryNotifications.add("ok", "工作空间「" + ws.name + "」已移入回收站"); showToast("已删除（可回收站还原）"); }
          });
        });
      })
    }
  ];
  openCtxMenu(items, e.currentTarget);
}

function trashPath(name) {
  return dataDir ? (dataDir + "\\trash\\" + name) : name;
}

function deleteConfig(cfg, workspaceId) {
  showConfirm("删除配置", "确定删除「" + cfg.name + "」？将先存档到回收站，可还原。", () => {
    const zipName = (cfg.name || "config") + "-" + Date.now() + ".zip";
    send("archive:exportConfig", { workspaceId, configId: cfg.id, path: trashPath(zipName) }, (d) => {
      if (!okOr(d)) return;
      send("config:delete", { workspaceId, configId: cfg.id }, (data) => {
        okOr(data);
        if (currentConfig && cfg.id === currentConfig.id) clearOpenConfig();
        loadNav();
        FerryNotifications.add("ok", "配置「" + cfg.name + "」已移入回收站");
        showToast("已删除（可到 设置→存储 回收站还原）");
      });
    });
  });
}

function exportConfigArchive(cfg, workspaceId) {
  showPrompt("存档包导出路径", cfg.name + ".zip", (path) => {
    if (!path) return;
    send("archive:exportConfig", { workspaceId, configId: cfg.id, path }, (d) => {
      if (okOr(d)) showToast("已导出：" + d.path);
    });
  });
}

// ================= 打开 / 关闭配置 =================
function clearOpenConfig() {
  currentConfig = null;
  snapshot = null;
  sourceText = "";
  document.getElementById("welcome").style.display = "";
  document.getElementById("editor").style.display = "none";
  document.getElementById("topBar").style.display = "none";
  setSourceOpen(false);
  updateTbCrumb();
  document.getElementById("moduleStatus").textContent = "";
  document.getElementById("formRoot").textContent = "";
  renderPreview();
}

function applyOpenData(data) {
  currentConfig = data.config;
  snapshot = data.snapshot || [];
  sourceText = data.sourceText || "";
  errors = data.errors || [];
  collapsedPaths = new Set();
  document.getElementById("topBar").style.display = "flex";
  updateTbCrumb();
  document.getElementById("tbConfigName").textContent = data.config.name;
  document.getElementById("tbConfigMeta").textContent =
    data.config.pluginName + " · v" + data.config.pluginVersion
    + (data.versionChanged ? "（字段可能有增减）" : "");
  const tplName = loadLocal("ferry.tplCfg." + data.config.id, "");
  document.getElementById("formTemplate").textContent = tplName ? "模板：" + tplName : "";
  document.getElementById("welcome").style.display = "none";
  document.getElementById("editor").style.display = "block";
  setSourceOpen(false);
  seedCollapsed();
  renderForm();
  renderPreview();
  renderNavActive();
  setStatus(data.pluginMissing ? "插件缺失：仅可查看/导出源码" : "已打开：" + data.config.name, !!data.pluginMissing);
  updateRecents(data.config.pluginKey, "");
}

function openConfig(cfg, workspaceId) {
  send("config:open", { workspaceId, configId: cfg.id }, (data) => {
    if (!okOr(data)) return;
    currentWorkspaceId = workspaceId;
    applyOpenData(data);
    renderConfigListRefresh();
  });
}

function renderConfigListRefresh() {
  renderWorkspaceTree();
  renderUnassignedTree();
}

function renderNavActive() {
  document.querySelectorAll("#workspaceTree .item, #unassignedTree .item").forEach(n => n.classList.remove("active"));
  if (currentConfig) {
    const sel = '#workspaceTree .item[data-cfg="' + currentConfig.id + '"], #unassignedTree .item[data-cfg="' + currentConfig.id + '"]';
    const node = document.querySelector(sel);
    if (node) node.classList.add("active");
  }
}

// ================= 移动 / 版本 =================
function openMoveModal() {
  if (!ctxConfig) return;
  const sel = document.getElementById("moveWorkspace");
  sel.innerHTML = "";
  const none = document.createElement("option");
  none.value = "";
  none.textContent = "（未归类配置）";
  sel.appendChild(none);
  for (const ws of nav.workspaces) {
    const opt = document.createElement("option");
    opt.value = ws.id;
    opt.textContent = ws.name;
    sel.appendChild(opt);
  }
  sel.value = ctxConfig.workspaceId;
  document.getElementById("moveTargetLabel").textContent = "移动「" + ctxConfig.cfg.name + "」到：";
  openModal("moveModal");
}

function openVersions() {
  if (!ctxConfig) { setStatus("请选择配置", true); return; }
  document.getElementById("versionNote").value = "";
  refreshVersionsList();
  openModal("versionsModal");
}

function refreshVersionsList() {
  const list = document.getElementById("versionList");
  list.textContent = "";
  if (!ctxConfig) { list.appendChild(textSpan("请选择配置")); return; }
  send("versions:list", { workspaceId: ctxConfig.workspaceId, configId: ctxConfig.cfg.id }, (data) => {
    const versions = data.versions || [];
    if (versions.length === 0) { list.appendChild(textSpan("暂无留档")); return; }
    for (const v of versions) {
      const item = document.createElement("div");
      item.className = "list-item";
      item.appendChild(el("span", "t", v.timestamp + (v.note ? " · " + v.note : "") + "（" + v.length + " 字符）"));
      const acts = el("span", "acts");
      const restore = el("span", "", "回滚");
      restore.onclick = () => showConfirm("回滚版本", "回滚到该版本？当前表单将被该版本源码重建。", () => {
        send("version:restore", { workspaceId: ctxConfig.workspaceId, configId: ctxConfig.cfg.id, versionId: v.id }, (data) => {
          if (!okOr(data)) return;
          currentWorkspaceId = ctxConfig.workspaceId;
          applyOpenData(data);
          refreshVersionsList();
          showToast("已回滚到该版本");
        });
      });
      const del = el("span", "del", "删除");
      del.onclick = () => send("version:delete", { workspaceId: ctxConfig.workspaceId, configId: ctxConfig.cfg.id, versionId: v.id }, () => refreshVersionsList());
      acts.appendChild(restore);
      acts.appendChild(del);
      item.appendChild(acts);
      list.appendChild(item);
    }
  });
}

// ================= 配置编辑器（字段树） =================
function seedCollapsed() {
  collapsedPaths = new Set();
  const collect = (nodes) => {
    for (const n of nodes) {
      if ((n.isModule || n.type === "Object") && (n.children || []).length > 0) {
        collapsedPaths.add(n.path);
      }
      collect(n.children || []);
    }
  };
  collect(snapshot || []);
}

function renderForm() {
  const root = document.getElementById("formRoot");
  root.textContent = "";
  if (!snapshot) return;
  const filter = document.getElementById("fieldFilter").value;
  const search = document.getElementById("fieldSearch").value.trim().toLowerCase();
  for (const node of snapshot) root.appendChild(renderTreeNode(node, 0, filter, search));
  updateModuleStatus();
}

function passNodeFilter(node, filter) {
  if (filter === "selected") return node.isEnabled;
  if (filter === "unselected") return !node.isEnabled;
  return true;
}

function matches(node, search) {
  return (node.label || "").toLowerCase().includes(search)
    || (node.id || "").toLowerCase().includes(search);
}

function renderTreeNode(node, depth, filter, search) {
  if (!node.isVisible) return document.createDocumentFragment();
  if (!passNodeFilter(node, filter)) return document.createDocumentFragment();
  const wrap = document.createElement("div");
  wrap.className = "field-row" + (node.isEnabled ? "" : " disabled");
  wrap.style.paddingLeft = (depth * 22 + 8) + "px";
  if (node.type === "Array") return renderArrayNode(node, depth, filter, search);

  const head = document.createElement("div");
  head.className = "field-head";
  if (node.isModule || node.type === "Object") {
    const hasChildren = (node.children || []).length > 0;
    const collapsed = collapsedPaths.has(node.path) && !search;
    const arrow = el("span", "f-arrow" + (collapsed ? "" : " open"), hasChildren ? "⌄" : "");
    head.appendChild(arrow);
    head.appendChild(node.required ? renderLock() : renderTriCheck(node));
    head.appendChild(makeLabel(node, search));
    if (node.isModule && node.enabledChildModulesText) {
      head.appendChild(el("span", "f-count", node.enabledChildModulesText));
    }
    wrap.appendChild(head);
    const body = document.createElement("div");
    body.className = "f-children" + (collapsed ? " collapsed" : "");
    for (const child of node.children || []) body.appendChild(renderTreeNode(child, depth + 1, filter, search));
    wrap.appendChild(body);
    if (hasChildren) {
      head.onclick = (e) => {
        if (e.target.closest(".f-check")) return;
        const was = body.classList.toggle("collapsed");
        if (was) collapsedPaths.add(node.path); else collapsedPaths.delete(node.path);
        arrow.classList.toggle("open", !was);
      };
    }
    return wrap;
  }

  head.appendChild(el("span", "f-arrow", ""));
  head.appendChild(node.required ? renderLock() : renderTriCheck(node));
  head.appendChild(makeLabel(node, search));
  head.appendChild(renderFieldControl(node));
  wrap.appendChild(head);
  return wrap;
}

function makeLabel(node, search) {
  const lbl = el("span", "f-label" + (search && matches(node, search) ? " match" : ""), node.label || node.id);
  attachTooltip(lbl, node);
  return lbl;
}

function renderArrayNode(node, depth, filter, search) {
  const wrap = document.createElement("div");
  wrap.className = "field-row";
  wrap.style.paddingLeft = (depth * 22 + 8) + "px";
  const head = document.createElement("div");
  head.className = "field-head";
  head.appendChild(el("span", "f-arrow", "⌄"));
  head.appendChild(node.required ? renderLock() : renderTriCheck(node));
  head.appendChild(el("span", "f-label", node.label || node.id));
  head.appendChild(el("span", "f-count", ((node.children || []).length) + " 项"));
  const add = el("span", "f-add", "＋ 添加项");
  add.onclick = (e) => { e.stopPropagation(); send("form:addItem", { path: node.path }, applyFormUpdate); };
  head.appendChild(add);
  wrap.appendChild(head);
  const body = document.createElement("div");
  body.className = "f-children";
  for (const item of node.children || []) body.appendChild(renderTreeNode(item, depth + 1, filter, search));
  wrap.appendChild(body);
  return wrap;
}

function renderTriCheck(node) {
  const box = document.createElement("span");
  const state = nodeTriState(node);
  box.className = "f-check"
    + (state === 2 ? " checked" : "")
    + (state === 1 ? " indeterminate" : "");
  box.textContent = state === 2 ? "☑" : state === 1 ? "◩" : "☐";
  if (!node.isSelectable) {
    box.style.cursor = "not-allowed";
    box.title = "父级未启用时锁定";
  } else {
    box.title = "取消勾选后该项不写入输出（值保留）";
  }
  box.onclick = (e) => {
    e.stopPropagation();
    if (!node.canToggleEnabled) return;
    if (node.isEnabled && (node.children || []).some(c => c.isModule)) {
      cascadeUncheck(node);
    } else {
      send("form:toggle", { path: node.path, enabled: !node.isEnabled }, applyFormUpdate);
    }
  };
  return box;
}

function nodeTriState(node) {
  if (!node.isEnabled) return 0;
  const childModules = (node.children || []).filter(c => c.isModule);
  if (childModules.length === 0) return 2;
  const enabled = childModules.filter(c => c.isEnabled).length;
  if (enabled === childModules.length) return 2;
  return enabled > 0 ? 1 : 0;
}

function cascadeUncheck(node) {
  const paths = [node.path];
  const walk = (n) => {
    for (const c of n.children || []) {
      if (c.isModule) { paths.push(c.path); walk(c); }
    }
  };
  walk(node);
  paths.forEach(p => send("form:toggle", { path: p, enabled: false }));
  send("form:snapshot", null, (d) => { if (d.ok) applyFormUpdate(d); });
}

function renderLock() {
  return el("span", "f-check lock", "🔒");
}

function validateFieldValue(node, raw) {
  const text = String(raw === undefined || raw === null ? "" : raw).trim();
  if (node.type === "Number") {
    if (text === "") return node.required ? "必填字段不能为空" : null;
    if (!/^-?\d+(\.\d+)?$/.test(text) || !Number.isFinite(Number(text))) return "请输入数字";
    const num = Number(text);
    if (node.integerOnly && !Number.isInteger(num)) return "仅允许整数";
    if (node.min !== undefined && node.min !== null && num < node.min) return "不能小于 " + node.min;
    if (node.max !== undefined && node.max !== null && num > node.max) return "不能大于 " + node.max;
    return null;
  }
  if (node.type === "Enum" && node.allowCustomValue) {
    if (text === "") return node.required ? "必填字段不能为空" : null;
    const known = (node.enumOptions || []).some(o => String(o.value) === text);
    if (known) return null;
    if (!/^-?\d+(\.\d+)?$/.test(text) || !Number.isFinite(Number(text))) return "请输入数字";
    const num = Number(text);
    if (node.integerOnly && !Number.isInteger(num)) return "仅允许整数";
    if (node.min !== undefined && node.min !== null && num < node.min) return "不能小于 " + node.min;
    if (node.max !== undefined && node.max !== null && num > node.max) return "不能大于 " + node.max;
    return null;
  }
  return null;
}

function renderFieldControl(node) {
  const ctl = document.createElement("span");
  ctl.className = "f-ctl";
  const setValue = (value) => send("form:setValue", { path: node.path, value }, applyLightUpdate);
  const makeErr = () => {
    const err = el("span", "f-error", "");
    err.style.display = "none";
    ctl.appendChild(err);
    return err;
  };
  switch (node.type) {
    case "String": {
      const input = document.createElement("input");
      input.type = "text";
      input.value = node.value ?? "";
      input.oninput = () => setValue(input.value);
      ctl.appendChild(input);
      break;
    }
    case "Number": {
      const input = document.createElement("input");
      input.type = "text";
      input.inputMode = "decimal";
      input.value = node.value ?? "";
      input.placeholder = "请输入数值";
      const err = makeErr();
      err.textContent = node.validationError || "";
      err.style.display = err.textContent ? "" : "none";
      input.oninput = () => {
        const msg = validateFieldValue(node, input.value);
        if (msg) {
          err.textContent = msg;
          err.style.display = "";
        } else {
          err.textContent = "";
          err.style.display = "none";
          setValue(input.value.trim());
        }
      };
      ctl.appendChild(input);
      break;
    }
    case "Boolean": {
      const toggle = el("span", "f-toggle" + (node.value === true ? " active" : ""));
      toggle.onclick = (e) => { e.stopPropagation(); setValue(node.value !== true); };
      ctl.appendChild(toggle);
      break;
    }
    case "Enum": {
      const select = document.createElement("select");
      for (const opt of node.enumOptions || []) {
        const o = document.createElement("option");
        o.value = opt.value;
        o.textContent = opt.value;
        select.appendChild(o);
      }
      if (node.allowCustomValue) {
        const custom = document.createElement("input");
        custom.type = "text";
        custom.placeholder = "自定义";
        custom.value = (node.value ?? "").toString();
        const err = makeErr();
        err.textContent = node.validationError || "";
        err.style.display = err.textContent ? "" : "none";
        select.onchange = () => {
          custom.value = select.value;
          err.textContent = "";
          err.style.display = "none";
          setValue(select.value);
        };
        custom.oninput = () => {
          const msg = validateFieldValue(node, custom.value);
          if (msg) {
            err.textContent = msg;
            err.style.display = "";
          } else {
            err.textContent = "";
            err.style.display = "none";
            setValue(custom.value.trim());
          }
        };
        ctl.appendChild(select);
        ctl.appendChild(custom);
      } else {
        select.value = (node.value ?? "").toString();
        select.onchange = () => setValue(select.value);
        ctl.appendChild(select);
      }
      break;
    }
  }
  return ctl;
}

function attachTooltip(lbl, node) {
  lbl.onmouseenter = (e) => {
    if (!node.description && !node.label) return;
    const delay = parseInt(loadLocal("ferry.tooltipDelay", 500), 10) || 500;
    tooltipTimer = setTimeout(() => {
      const tip = document.getElementById("tooltip");
      tip.innerHTML = "";
      tip.appendChild(el("div", "tt-name", node.label || node.id));
      if (node.description) tip.appendChild(el("div", "tt-desc", node.description));
      tip.style.display = "";
      tip.style.left = Math.min(e.clientX + 12, window.innerWidth - 320) + "px";
      tip.style.top = Math.min(e.clientY + 14, window.innerHeight - 120) + "px";
    }, delay);
  };
  lbl.onmouseleave = () => {
    clearTimeout(tooltipTimer);
    document.getElementById("tooltip").style.display = "none";
  };
}

function updateModuleStatus() {
  document.getElementById("moduleStatus").textContent =
    snapshot ? (countEnabled(snapshot) + "/" + countTotal(snapshot) + " 模块已启用") : "";
}
function countEnabled(nodes) {
  let n = 0;
  for (const node of nodes) {
    if (node.isModule && node.isEnabled) n++;
    n += countEnabled(node.children || []);
  }
  return n;
}
function countTotal(nodes) {
  let n = 0;
  for (const node of nodes) {
    if (node.isModule) n++;
    n += countTotal(node.children || []);
  }
  return n;
}
function collectPaths(nodes) {
  const out = [];
  for (const n of nodes) {
    if (n.isModule || n.type === "Object" || n.type === "Array") out.push(n.path);
    out.push(...collectPaths(n.children || []));
  }
  return out;
}

// ================= 源码停靠面板 =================
function renderPreview() {
  document.getElementById("previewCode").textContent = sourceText || "（点击顶部「源码」打开停靠面板）";
}

function setSourceOpen(open) {
  sourceOpen = open;
  document.getElementById("dockPanel").style.display = open ? "flex" : "none";
  document.getElementById("splitResizer").style.display = open ? "" : "none";
  document.getElementById("btnSource").classList.toggle("active", open);
  document.getElementById("btnSourceFull").style.display = open ? "" : "none";
  if (!open) {
    document.getElementById("mainPanel").classList.remove("full");
    document.getElementById("btnSourceFull").textContent = "⛶ 全占";
  } else {
    renderPreview();
  }
}

function toggleFull() {
  const main = document.getElementById("mainPanel");
  const full = main.classList.toggle("full");
  const panel = document.getElementById("dockPanel");
  if (full) {
    panel.style.flex = "";
    panel.style.minWidth = "";
    panel.style.maxWidth = "";
  }
  document.getElementById("btnSourceFull").textContent = full ? "⤢ 还原" : "⛶ 全占";
}

// ================= 向导 =================
function openWizard(pluginKey, presetName, workspaceId) {
  wizard.pluginKey = pluginKey || "";
  wizard.templateId = pluginKey ? (loadLocal("ferry.tpl." + pluginKey, "") || "__blank") : "__blank";
  wizard.step = 1;
  wizard.autoName = !presetName;
  const nameInput = document.getElementById("wzName");
  nameInput.value = presetName || "";
  nameInput.placeholder = presetName ? "" : "输入配置名";
  document.getElementById("wzPluginSearch").value = "";
  fillWizardWorkspace(workspaceId);
  renderWizardPlugins();
  renderWizardTemplates();
  goWizardStep(pluginKey ? 2 : 1);
  openModal("wizardModal");
}

function goWizardStep(n) {
  wizard.step = n;
  document.querySelectorAll("#wizardModal .step").forEach(s => s.classList.remove("active"));
  document.getElementById("wz" + n).classList.add("active");
  const dots = [
    { id: "wsDot1", idx: 1 },
    { id: "wsDot2", idx: 2 },
    { id: "wsDot3", idx: 3 }
  ];
  for (const d of dots) {
    const dot = document.getElementById(d.id);
    dot.textContent = "";
    dot.classList.remove("active", "done", "future", "clickable");
    if (d.idx < n) {
      dot.classList.add("done", "clickable");
      dot.title = "返回第 " + d.idx + " 步";
      dot.onclick = () => goWizardStep(d.idx);
    } else if (d.idx === n) {
      dot.classList.add("active");
      dot.title = "当前步骤";
      dot.onclick = null;
    } else {
      dot.classList.add("future");
      dot.title = "尚未到达";
      dot.onclick = null;
    }
  }
  if (n === 3 && wizard.autoName) {
    const plugin = plugins.find(p => p.key === wizard.pluginKey);
    const defName = plugin && plugin.defaultFileName ? plugin.defaultFileName : "";
    const nameInput = document.getElementById("wzName");
    nameInput.value = defName;
    nameInput.placeholder = defName ? "" : "输入配置名";
    wizard.autoName = false;
  }
  document.getElementById("wzBackIcon").style.display = n >= 2 ? "" : "none";
  document.getElementById("wzBackIcon2").style.display = n >= 2 ? "" : "none";
  document.getElementById("wzCreate").style.display = n === 3 ? "" : "none";
}

function fillWizardWorkspace(selected) {
  const sel = document.getElementById("wzWorkspace");
  sel.innerHTML = "";
  if (nav.workspaces.length === 0) {
    const none = document.createElement("option");
    none.value = "";
    none.textContent = "---选择工作空间---";
    sel.appendChild(none);
    sel.disabled = true;
    return;
  }
  sel.disabled = false;
  const none = document.createElement("option");
  none.value = "";
  none.textContent = "---选择工作空间---";
  sel.appendChild(none);
  for (const ws of nav.workspaces) {
    const opt = document.createElement("option");
    opt.value = ws.id;
    opt.textContent = ws.name;
    sel.appendChild(opt);
  }
  sel.value = selected !== undefined && selected !== null ? selected : "";
}

function isPluginEnabled(p) {
  return !loadLocal("ferry.pluginDisabled." + p.key, false);
}

function renderWizardPlugins() {
  const recentsEl = document.getElementById("wzRecentList");
  const listEl = document.getElementById("wzPluginList");
  recentsEl.textContent = "";
  listEl.textContent = "";
  const filter = document.getElementById("wzPluginSearch").value.trim().toLowerCase();
  const recents = loadLocal("ferry.recentPlugins", [])
    .map(k => plugins.find(p => p.key === k))
    .filter(p => p && isPluginEnabled(p));
  document.getElementById("wzRecentLabel").style.display = (!filter && recents.length > 0) ? "" : "none";
  if (!filter && recents.length > 0) {
    for (const p of recents.slice(0, 4)) recentsEl.appendChild(renderPluginOption(p));
  }
  const visible = plugins.filter(p => isPluginEnabled(p) && (!filter
    || p.name.toLowerCase().includes(filter)
    || p.key.toLowerCase().includes(filter)
    || (p.description || "").toLowerCase().includes(filter)));
  for (const p of visible) listEl.appendChild(renderPluginOption(p));
  if (visible.length === 0) listEl.appendChild(textSpan("没有找到匹配的插件"));
}

function renderPluginOption(p) {
  const opt = el("div", "option");
  opt.innerHTML = "🌐 " + escapeHtml(p.name) + " <small>v" + escapeHtml(p.version) + "</small><div class='desc'>" + escapeHtml(p.description || p.rendererType) + "</div>";
  opt.title = p.description || "";
  opt.onclick = () => {
    wizard.pluginKey = p.key;
    wizard.templateId = loadLocal("ferry.tpl." + p.key, "") || "__blank";
    document.querySelectorAll("#wzPluginList .option, #wzRecentList .option").forEach(o => o.classList.remove("active"));
    opt.classList.add("active");
    renderWizardTemplates();
    goWizardStep(2);
  };
  return opt;
}

function renderWizardTemplates() {
  const elW = document.getElementById("wzTemplateList");
  elW.textContent = "";
  const plugin = plugins.find(p => p.key === wizard.pluginKey);
  const known = (plugin && plugin.templates || []).some(t => t.id === wizard.templateId);
  if (wizard.templateId !== "__blank" && !known) wizard.templateId = "__blank";
  const blank = el("div", "option" + (wizard.templateId === "__blank" ? " active" : ""));
  blank.innerHTML = "默认模板<div class='desc'>空白默认配置</div>";
  blank.title = "空白默认配置";
  blank.onclick = () => { wizard.templateId = "__blank"; selectWizardTemplate(blank); goWizardStep(3); };
  elW.appendChild(blank);
  for (const t of (plugin && plugin.templates) || []) {
    const opt = el("div", "option" + (wizard.templateId === t.id ? " active" : ""));
    opt.innerHTML = escapeHtml(t.name) + "<div class='desc'>" + escapeHtml(t.description || "场景模板") + "</div>";
    opt.title = t.description || "";
    opt.onclick = () => { wizard.templateId = t.id; selectWizardTemplate(opt); goWizardStep(3); };
    elW.appendChild(opt);
  }
}

function selectWizardTemplate(target) {
  document.querySelectorAll("#wzTemplateList .option").forEach(o => o.classList.remove("active"));
  target.classList.add("active");
  if (wizard.pluginKey) saveLocal("ferry.tpl." + wizard.pluginKey, wizard.templateId);
}

function submitCreate() {
  const pluginKey = wizard.pluginKey;
  if (!pluginKey) { setStatus("请选择插件", true); return; }
  const workspaceId = document.getElementById("wzWorkspace").value || "";
  const name = document.getElementById("wzName").value.trim() || undefined;
  const templateId = wizard.templateId;
  send("config:create", { projectId: currentProjectId, workspaceId, pluginKey, name }, (d) => {
    if (!okOr(d)) return;
    const cfgId = d.configId;
    send("config:open", { workspaceId, configId: cfgId }, (data) => {
      if (!okOr(data)) { closeModal("wizardModal"); loadNav(); return; }
      const finish = () => {
        closeModal("wizardModal");
        currentWorkspaceId = workspaceId;
        const plugin = plugins.find(p => p.key === pluginKey);
        const tpl = ((plugin && plugin.templates) || []).find(t => t.id === templateId);
        if (tpl) saveLocal("ferry.tplCfg." + data.config.id, tpl.name);
        applyOpenData(data);
        loadNav();
        updateRecents(pluginKey, templateId);
        showToast(templateId === "__blank" ? "已创建配置" : "已创建配置并应用模板");
      };
      if (templateId !== "__blank") {
        send("form:applyPreset", { preset: templateId }, (r) => {
          if (okOr(r)) applyFormUpdate(r);
          finish();
        });
      } else {
        finish();
      }
    });
  });
}

function updateRecents(pluginKey, templateId) {
  const recents = loadLocal("ferry.recentPlugins", []);
  saveLocal("ferry.recentPlugins", [pluginKey, ...recents.filter(k => k !== pluginKey)].slice(0, 5));
  if (templateId && templateId !== "__blank") {
    const tpls = loadLocal("ferry.recentTemplates", []);
    saveLocal("ferry.recentTemplates", [templateId, ...tpls.filter(t => t !== templateId)].slice(0, 5));
  }
  renderWelcome();
}

function renderWelcome() {
  const row = document.getElementById("recentChips");
  row.textContent = "";
  const recents = loadLocal("ferry.recentPlugins", []);
  let count = 0;
  for (const key of recents.slice(0, 4)) {
    const p = plugins.find(x => x.key === key);
    if (!p || !isPluginEnabled(p)) continue;
    const chip = document.createElement("div");
    chip.className = "recent-chip";
    chip.title = p.name + " · v" + p.version;
    chip.textContent = p.name;
    chip.onclick = () => openWizard(p.key, "", "");
    row.appendChild(chip);
    count++;
  }
  if (count === 0) {
    const hint = textSpan("创建配置后这里会显示最近使用的插件");
    hint.className = "recent-hint";
    row.appendChild(hint);
  }
}

// ================= 设置页面 =================
function openSettings() {
  settingsCategory = "general";
  document.querySelector(".project-selector").style.display = "none";
  document.getElementById("btnNewConfig").style.display = "none";
  document.getElementById("wsSection").style.display = "none";
  document.getElementById("workspaceTree").style.display = "none";
  document.getElementById("cfgSection").style.display = "none";
  document.getElementById("unassignedTree").style.display = "none";
  document.getElementById("toolsSection").style.display = "none";
  document.getElementById("btnSettings").style.display = "none";
  document.getElementById("settingsNav").style.display = "";
  document.getElementById("welcome").style.display = "none";
  document.getElementById("editor").style.display = "none";
  document.getElementById("settingsPage").style.display = "";
  document.getElementById("topBar").style.display = "none";
  setSourceOpen(false);
  renderSettingsNav();
  renderSettingsCategory(settingsCategory);
}

function exitSettings() {
  document.querySelector(".project-selector").style.display = "";
  document.getElementById("btnNewConfig").style.display = "";
  document.getElementById("wsSection").style.display = "";
  document.getElementById("workspaceTree").style.display = "";
  document.getElementById("cfgSection").style.display = "";
  document.getElementById("unassignedTree").style.display = "";
  document.getElementById("toolsSection").style.display = "";
  document.getElementById("btnSettings").style.display = "";
  document.getElementById("settingsNav").style.display = "none";
  document.getElementById("settingsPage").style.display = "none";
  document.getElementById("topBar").style.display = currentConfig ? "flex" : "none";
  if (currentConfig) {
    document.getElementById("editor").style.display = "block";
  } else {
    document.getElementById("welcome").style.display = "";
  }
  renderTools();
  renderWelcome();
  updateTbCrumb();
}

function renderSettingsNav() {
  const nav = document.getElementById("settingsNav");
  nav.textContent = "";
  const cats = [["general", "常规"], ["appearance", "外观"], ["plugins", "插件管理"], ["modules", "模块管理"], ["storage", "存储"], ["notifications", "通知"]];
  for (const [id, name] of cats) {
    const item = document.createElement("div");
    item.className = "settings-nav-item" + (settingsCategory === id ? " active" : "");
    item.textContent = name;
    item.onclick = () => { settingsCategory = id; renderSettingsNav(); renderSettingsCategory(id); };
    nav.appendChild(item);
  }
  const back = document.createElement("div");
  back.className = "settings-nav-item";
  back.textContent = "← 返回";
  back.onclick = exitSettings;
  nav.appendChild(back);
}

function renderSettingsCategory(cat) {
  const box = document.getElementById("settingsContent");
  box.textContent = "";
  const titles = { general: "常规", appearance: "外观", plugins: "插件管理", modules: "模块管理", storage: "存储", notifications: "通知" };
  box.appendChild(el("div", "settings-cat", titles[cat]));
  if (cat === "general") renderSettingsGeneral(box);
  else if (cat === "appearance") renderSettingsAppearance(box);
  else if (cat === "plugins") renderSettingsPluginsPage(box);
  else if (cat === "modules") renderSettingsModules(box);
  else if (cat === "storage") renderSettingsStorage(box);
  else if (cat === "notifications") renderSettingsNotifications(box);
}

function settingRow(label, control) {
  const row = document.createElement("div");
  row.className = "settings-row";
  row.appendChild(el("label", "", label));
  row.appendChild(control);
  return row;
}
function makeCheck(key, def) {
  const cb = document.createElement("input");
  cb.type = "checkbox";
  cb.checked = loadLocal(key, def);
  cb.onchange = () => saveLocal(key, cb.checked);
  return cb;
}
function makeText(key, def, placeholder) {
  const input = document.createElement("input");
  input.type = "text";
  input.value = loadLocal(key, def) || "";
  input.placeholder = placeholder || "";
  input.onchange = () => saveLocal(key, input.value);
  return input;
}
function makeNumber(key, def) {
  const input = document.createElement("input");
  input.type = "number";
  input.value = loadLocal(key, def);
  input.onchange = () => saveLocal(key, parseInt(input.value, 10) || def);
  return input;
}
function makeSelect(key, def, options) {
  const select = document.createElement("select");
  for (const [v, l] of options) {
    const o = document.createElement("option");
    o.value = v;
    o.textContent = l;
    select.appendChild(o);
  }
  select.value = loadLocal(key, def);
  select.onchange = () => saveLocal(key, select.value);
  return select;
}
function makeReadonly(text, btnFn) {
  const span = document.createElement("span");
  span.style.cssText = "color:#999;font-size:12px;word-break:break-all;flex:1";
  span.textContent = text;
  const wrap = document.createElement("span");
  wrap.style.cssText = "display:flex;gap:8px;align-items:center;flex:1";
  wrap.appendChild(span);
  if (btnFn) {
    const b = el("button", "btn small", "打开");
    b.onclick = btnFn;
    wrap.appendChild(b);
  }
  return wrap;
}

function renderSettingsGeneral(box) {
  box.appendChild(settingRow("启动时恢复上次项目", makeCheck("ferry.restoreProject", true)));
  box.appendChild(settingRow("默认路径", makeText("ferry.defaultPath", "", "例如 D:\\configs")));
  send("logs:path", null, (d) => {
    if (d.ok) box.appendChild(settingRow("日志文件", makeReadonly(d.path, () => send("logs:open"))));
  });
  const importRow = document.createElement("div");
  importRow.className = "settings-row";
  importRow.appendChild(el("label", "", "导入存档包"));
  const input = document.createElement("input");
  input.type = "text";
  input.placeholder = "zip 路径";
  const btn = el("button", "btn small", "导入");
  btn.onclick = () => {
    if (!input.value) return;
    send("archive:import", { path: input.value }, (d) => {
      if (okOr(d)) { showToast("存档导入完成"); refreshProjects(); }
    });
  };
  importRow.appendChild(input);
  importRow.appendChild(btn);
  box.appendChild(importRow);
}

function renderSettingsAppearance(box) {
  box.appendChild(settingRow("主题", makeSelect("ferry.theme", "dark", [["dark", "深色"], ["light", "浅色（预留）"]])));
  box.appendChild(settingRow("动画", makeCheck("ferry.animations", true)));
  box.appendChild(settingRow("Tooltip 延迟（ms）", makeNumber("ferry.tooltipDelay", 500)));
}

function renderSettingsPluginsPage(box) {
  if (!plugins.length) { box.appendChild(textSpan("暂无插件")); return; }
  for (const p of plugins) {
    const row = document.createElement("div");
    row.className = "settings-row";
    row.appendChild(el("label", "", "🌐 " + p.name + "（v" + p.version + "）"));
    if (p.loadErrors && p.loadErrors.length) row.appendChild(el("span", "f-error", p.loadErrors[0]));
    const cb = document.createElement("input");
    cb.type = "checkbox";
    cb.checked = isPluginEnabled(p);
    cb.title = "启用/禁用（UI 层过滤显示）";
    cb.onchange = () => saveLocal("ferry.pluginDisabled." + p.key, !cb.checked);
    row.appendChild(cb);
    box.appendChild(row);
  }
}

function renderSettingsModules(box) {
  const modules = window.FerryModules ? FerryModules.list() : [];
  if (!modules.length) { box.appendChild(textSpan("暂无已安装模块（未来动态模块在此显示）")); return; }
  for (const m of modules) box.appendChild(settingRow(m.name || m.id, makeCheck("ferry.moduleEnabled." + m.id, true)));
}

function renderSettingsStorage(box) {
  box.appendChild(settingRow("回收站保留时间（天）", makeNumber("ferry.trashDays", 30)));
  box.appendChild(settingRow("回收站最大空间（MB）", makeNumber("ferry.trashSizeMB", 2048)));
  box.appendChild(el("div", "settings-cat", "回收站"));
  renderTrash(box);
}
function renderTrash(box) {
  send("trash:list", null, (d) => {
    if (!okOr(d)) return;
    let items = d.items || [];
    const days = parseInt(loadLocal("ferry.trashDays", 30), 10) || 30;
    const maxMB = parseInt(loadLocal("ferry.trashSizeMB", 2048), 10) || 2048;
    const now = Date.now();
    const expired = items.filter(i => now - new Date(i.modified).getTime() > days * 86400000);
    expired.forEach(i => send("trash:delete", { path: i.path }));
    if (expired.length) items = items.filter(i => !expired.includes(i));
    let total = items.reduce((s, i) => s + i.size, 0);
    if (total > maxMB * 1048576) {
      const sorted = items.slice().sort((a, b) => new Date(a.modified) - new Date(b.modified));
      while (total > maxMB * 1048576 && sorted.length) {
        const old = sorted.shift();
        send("trash:delete", { path: old.path });
        total -= old.size;
        items = items.filter(i => i !== old);
      }
    }
    if (!items.length) { box.appendChild(textSpan("回收站为空")); return; }
    for (const it of items) {
      const row = document.createElement("div");
      row.className = "settings-row";
      row.appendChild(el("label", "", it.name + "（" + (it.size / 1024).toFixed(0) + "KB · " + it.modified + "）"));
      const restore = el("button", "btn small", "还原");
      restore.onclick = () => send("archive:import", { path: it.path }, (r) => {
        if (okOr(r)) { showToast("已还原"); renderSettingsCategory("storage"); }
      });
      const del = el("button", "btn small", "永久删除");
      del.onclick = () => showConfirm("永久删除", "确定永久删除「" + it.name + "」？", () =>
        send("trash:delete", { path: it.path }, () => renderSettingsCategory("storage")));
      row.appendChild(restore);
      row.appendChild(del);
      box.appendChild(row);
    }
  });
}

function renderSettingsNotifications(box) {
  box.appendChild(settingRow("启用通知", makeCheck("ferry.notifyEnabled", true)));
  box.appendChild(settingRow("提示方式", makeSelect("ferry.notifyStyle", "panel", [["panel", "通知面板"], ["toast", "轻提示"]])));
  box.appendChild(textSpan("通知持久化与后端来源（推送/模块结果）为未来扩展，暂仅本地记录。"));
}

// ================= 通知中心 =================
function renderNotifications() {
  const items = FerryNotifications.load();
  const list = document.getElementById("notifyList");
  list.textContent = "";
  const count = document.getElementById("bellCount");
  count.textContent = items.length;
  count.style.display = items.length ? "" : "none";
  if (items.length === 0) {
    const empty = textSpan("暂无通知");
    empty.style.padding = "14px";
    list.appendChild(empty);
    return;
  }
  for (const n of items) {
    const row = document.createElement("div");
    row.className = "notify-item";
    row.appendChild(el("span", n.type === "error" ? "err" : "ok", n.type === "error" ? "×" : "✓"));
    row.appendChild(el("span", "t", n.text));
    row.appendChild(el("span", "time", new Date(n.time).toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })));
    row.onclick = () => FerryNotifications.consume(n.id);
    list.appendChild(row);
  }
}

// ================= 通用输入 / 确认 =================
function showPrompt(title, defaultValue, callback, placeholder) {
  promptCallback = callback;
  document.getElementById("promptTitle").textContent = title;
  const input = document.getElementById("promptInput");
  input.value = defaultValue || "";
  const placeholders = {
    "项目名称": "输入项目名称",
    "工作空间名称": "输入工作空间名称",
    "存档包导出路径": "输入导出路径"
  };
  input.placeholder = placeholder || placeholders[title] || "输入内容";
  document.getElementById("promptHint").style.display = "none";
  document.getElementById("promptOk").disabled = false;
  input.oninput = () => {
    document.getElementById("promptHint").style.display = "none";
  };
  openModal("promptModal");
  setTimeout(() => input.focus(), 60);
}

function showConfirm(title, message, callback) {
  confirmCallback = callback;
  document.getElementById("confirmTitle").textContent = title;
  document.getElementById("confirmMessage").textContent = message;
  openModal("confirmModal");
}

// ================= 事件绑定 =================
document.getElementById("projectBtn").onclick = () => toggleProjectMenu();
document.getElementById("projectBtn").addEventListener("contextmenu", (e) => { e.preventDefault(); toggleProjectMenu(true); });
document.getElementById("btnNewProject").onclick = () => {
  showPrompt("项目名称", "", (name) => {
    if (!name) return;
    send("project:create", { name }, (d) => {
      if (okOr(d)) { currentProjectId = d.project.id; saveLocal("ferry.projectId", currentProjectId); refreshProjects(currentProjectId); showToast("已创建项目"); }
    });
  });
};
document.getElementById("btnNewConfig").onclick = () => openWizard();
document.getElementById("btnWelcomeNew").onclick = () => openWizard();

document.getElementById("wsSection").onclick = () => {
  const open = document.getElementById("workspaceTree").classList.toggle("open");
  document.getElementById("wsSection").classList.toggle("collapsed", !open);
  document.getElementById("wsCollapse").textContent = open ? "▾" : "▸";
};
document.getElementById("wsPlus").onclick = (e) => {
  e.stopPropagation();
  showPrompt("工作空间名称", "", (name) => {
    if (!name) return;
    send("workspace:create", { projectId: currentProjectId, name }, (d) => {
      if (okOr(d)) { loadNav(); showToast("已创建工作空间"); }
    });
  });
};
document.getElementById("cfgSection").onclick = () => {
  const open = document.getElementById("unassignedTree").classList.toggle("open");
  document.getElementById("cfgSection").classList.toggle("collapsed", !open);
  document.getElementById("cfgCollapse").textContent = open ? "▾" : "▸";
};
document.getElementById("cfgPlus").onclick = (e) => {
  e.stopPropagation();
  openWizard("", "", "");
};

document.getElementById("btnSettings").onclick = openSettings;
document.getElementById("bellBtn").onclick = (e) => {
  e.stopPropagation();
  renderNotifications();
  const panel = document.getElementById("notifyPanel");
  panel.style.display = panel.style.display === "none" ? "" : "none";
};
document.getElementById("notifyClear").onclick = () => {
  FerryNotifications.clearAll();
  renderNotifications();
};

document.getElementById("btnSource").onclick = () => setSourceOpen(!sourceOpen);
document.getElementById("btnSourceFull").onclick = toggleFull;
document.getElementById("splitResizer").addEventListener("mousedown", (e) => {
  e.preventDefault();
  const startX = e.clientX;
  const startWidth = document.getElementById("dockPanel").offsetWidth;
  const mainWidth = document.getElementById("mainPanel").offsetWidth;
  const minWidth = mainWidth * 0.35;
  const maxWidth = mainWidth * 0.6;
  const closeDistance = 28;
  const onMove = (ev) => {
    const delta = ev.clientX - startX;
    const target = startWidth - delta;
    if (target < minWidth - closeDistance) {
      setSourceOpen(false);
      document.removeEventListener("mousemove", onMove);
      document.removeEventListener("mouseup", onUp);
      return;
    }
    const width = Math.max(minWidth, Math.min(maxWidth, target));
    const panel = document.getElementById("dockPanel");
    panel.style.flex = "0 0 " + width + "px";
    panel.style.minWidth = width + "px";
    panel.style.maxWidth = width + "px";
  };
  const onUp = () => {
    document.removeEventListener("mousemove", onMove);
    document.removeEventListener("mouseup", onUp);
  };
  document.addEventListener("mousemove", onMove);
  document.addEventListener("mouseup", onUp);
});

document.getElementById("fieldFilter").onchange = renderForm;
document.getElementById("fieldSearch").oninput = renderForm;
document.getElementById("btnCollapseAll").onclick = () => {
  snapshot && collectPaths(snapshot).forEach(p => collapsedPaths.add(p));
  renderForm();
};
document.getElementById("btnExpandAll").onclick = () => {
  collapsedPaths.clear();
  renderForm();
};

document.getElementById("wzPluginSearch").oninput = renderWizardPlugins;
document.getElementById("wzCancel").onclick = () => closeModal("wizardModal");
document.getElementById("wzBackIcon").onclick = () => goWizardStep(wizard.step - 1);
document.getElementById("wzBackIcon2").onclick = () => goWizardStep(wizard.step - 1);
document.getElementById("wzName").oninput = () => { wizard.autoName = false; };
document.getElementById("wzCreate").onclick = submitCreate;
document.getElementById("btnMoveOk").onclick = () => {
  if (!ctxConfig) return;
  const target = document.getElementById("moveWorkspace").value;
  send("config:move", { configId: ctxConfig.cfg.id, workspaceId: target }, (d) => {
    if (!okOr(d)) return;
    closeModal("moveModal");
    if (currentConfig && ctxConfig.cfg.id === currentConfig.id) {
      currentWorkspaceId = target;
      applyOpenData({ config: currentConfig, snapshot, sourceText, errors, versionChanged: false });
    }
    loadNav();
    showToast("已移动配置");
  });
};
document.getElementById("btnMoveCancel").onclick = () => closeModal("moveModal");
document.getElementById("btnSnapshot").onclick = () => {
  const note = document.getElementById("versionNote").value.trim() || undefined;
  send("version:snapshot", { note }, (d) => {
    if (okOr(d)) { document.getElementById("versionNote").value = ""; refreshVersionsList(); showToast("已留档"); }
  });
};

document.getElementById("promptOk").onclick = () => {
  const value = document.getElementById("promptInput").value.trim();
  if (value === "") {
    document.getElementById("promptHint").style.display = "";
    document.getElementById("promptInput").focus();
    return;
  }
  const cb = promptCallback;
  promptCallback = null;
  closeModal("promptModal");
  if (cb) cb(value);
};
document.getElementById("promptCancel").onclick = () => { promptCallback = null; closeModal("promptModal"); };
document.getElementById("promptInput").addEventListener("keydown", (e) => {
  if (e.key === "Enter") document.getElementById("promptOk").click();
});
document.getElementById("confirmOk").onclick = () => {
  const cb = confirmCallback;
  confirmCallback = null;
  closeModal("confirmModal");
  if (cb) cb();
};
document.getElementById("confirmCancel").onclick = () => { confirmCallback = null; closeModal("confirmModal"); };

document.querySelectorAll(".modal-close").forEach(btn => {
  btn.onclick = () => closeModal(btn.dataset.close);
});
document.addEventListener("click", (e) => {
  if (e.target.classList && e.target.classList.contains("ferry-modal-overlay") && e.target.classList.contains("open")) {
    if (loadLocal("ferry.closeOutside", true)) e.target.classList.remove("open");
  }
  if (!e.target.closest("#bellBtn") && !e.target.closest("#notifyPanel")) {
    document.getElementById("notifyPanel").style.display = "none";
  }
  if (!e.target.closest(".ctx-menu") && !e.target.closest(".menu-btn")) hideCtxMenu();
  if (!e.target.closest(".project-selector")) toggleProjectMenu(false);
});
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    hideCtxMenu();
    closeModal("wizardModal");
    closeModal("moveModal");
    closeModal("versionsModal");
    closeModal("promptModal");
    closeModal("confirmModal");
  }
});

// ================= 初始化 =================
function init() {
  send("app:dataDir", null, (d) => { if (d.ok) dataDir = d.path; });
  FerryNotifications.onChange(renderNotifications);
  document.getElementById("bellBtn").style.display = loadLocal("ferry.notifyEnabled", true) ? "" : "none";
  const unassigned = document.getElementById("unassignedTree");
  unassigned.addEventListener("dragover", (e) => {
    if (!dragCfg) return;
    e.preventDefault();
    unassigned.classList.add("drag-over");
  });
  unassigned.addEventListener("dragleave", (e) => {
    if (e.relatedTarget && unassigned.contains(e.relatedTarget)) return;
    unassigned.classList.remove("drag-over");
  });
  unassigned.addEventListener("drop", (e) => {
    e.preventDefault();
    unassigned.classList.remove("drag-over");
    const from = dragCfg;
    dragCfg = null;
    hideDragPreview();
    setWsDropZoneVisible(false);
    clearDropMarks();
    if (!from || from.ws === "") return;
    pendingDrop = { configId: from.id, targetWs: "", targetCfgId: null, before: true };
    send("config:move", { configId: from.id, workspaceId: "" }, (d) => {
      if (!okOr(d)) { pendingDrop = null; loadNav(); return; }
      loadNav(applyPendingDropOrder);
    });
  });

  const wsTree = document.getElementById("workspaceTree");
  wsTree.addEventListener("dragover", (e) => {
    if (!dragCfg) return;
    e.preventDefault();
    setWsDropZoneVisible(true);
  });
  wsTree.addEventListener("dragleave", (e) => {
    if (e.relatedTarget && wsTree.contains(e.relatedTarget)) return;
    setWsDropZoneVisible(false);
  });
  const wsSection = document.getElementById("wsSection");
  wsSection.addEventListener("dragover", (e) => {
    if (!dragCfg) return;
    e.preventDefault();
    setWsDropZoneVisible(true);
  });
  wsSection.addEventListener("dragleave", (e) => {
    if (e.relatedTarget && wsSection.contains(e.relatedTarget)) return;
    setWsDropZoneVisible(false);
  });

  const zone = document.getElementById("wsDropZone");
  zone.addEventListener("dragover", (e) => {
    if (!dragCfg) return;
    e.preventDefault();
    e.stopPropagation();
    setWsDropZoneVisible(true);
    zone.classList.add("drag-over");
  });
  zone.addEventListener("dragleave", () => zone.classList.remove("drag-over"));
  zone.addEventListener("drop", (e) => {
    e.preventDefault();
    e.stopPropagation();
    zone.classList.remove("drag-over");
    const pending = dragCfg;
    dragCfg = null;
    hideDragPreview();
    setWsDropZoneVisible(false);
    clearDropMarks();
    if (!pending) return;
    showPrompt("工作空间名称", "", (name) => {
      if (!name) return;
      send("workspace:create", { projectId: currentProjectId, name }, (d) => {
        if (!okOr(d)) return;
        const wsId = d.workspace.id;
        send("config:move", { configId: pending.id, workspaceId: wsId }, (r) => {
          if (!okOr(r)) { loadNav(); return; }
          loadNav(() => openConfig({ id: pending.id }, wsId));
          showToast("已创建并移入工作空间");
        });
      });
    });
  });
  document.addEventListener("dragover", (e) => moveDragPreview(e.clientX, e.clientY));
  document.addEventListener("dragend", () => {
    dragCfg = null;
    hideDragPreview();
    setWsDropZoneVisible(false);
    clearDropMarks();
  });
  send("bootstrap", null, (data) => {
    plugins = data.plugins || [];
    if (data.loadErrors && data.loadErrors.length) {
      setStatus("插件加载 " + data.loadErrors.length + " 个失败：" + data.loadErrors[0], true);
    }
    refreshProjects();
  });
}

// ================= 自检（全链路） =================
function runSpike() {
  const steps = [];
  let failed = false;
  function step(name, action, payload) {
    return new Promise((resolve) => {
      const t0 = performance.now();
      send(action, payload || {}, (data) => {
        if (!data.ok) failed = true;
        steps.push({ name, ms: performance.now() - t0, ok: !!data.ok, error: (data.errors || [])[0] || "" });
        resolve(data);
      });
    });
  }
  (async () => {
    const boot = await step("bootstrap", "bootstrap");
    const projectId = boot.projects[0].id;
    const wsData = await step("workspace:create", "workspace:create", { projectId, name: "自检工作空间" });
    const wsId = wsData.workspace.id;
    const cfgData = await step("config:create", "config:create",
      { projectId, workspaceId: wsId, pluginKey: "Nginx", name: "selfcheck.conf" });
    const cfgId = cfgData.configId;
    const openData = await step("config:open", "config:open", { workspaceId: wsId, configId: cfgId });
    const typeOk = (openData.snapshot || []).every(n => typeof n.type === "string");
    steps.push({ name: "type-check", ms: 0, ok: typeOk, error: typeOk ? "" : "字段类型不是字符串" });
    if (!typeOk) failed = true;
    await step("form:toggle", "form:toggle", { path: "http.upstreams", enabled: false });
    await step("form:toggle", "form:toggle", { path: "http.upstreams", enabled: true });
    await step("form:toggle-scalar", "form:toggle", { path: "user", enabled: false });
    await step("form:toggle-scalar", "form:toggle", { path: "user", enabled: true });
    await step("form:addItem", "form:addItem", { path: "http.upstreams" });
    await step("form:setValue", "form:setValue", { path: "http.upstreams[0].upstream_name", value: "backend" });
    await step("form:render", "form:render");
    await step("version:snapshot", "version:snapshot", { note: "自检" });
    const ucfg = await step("config:create-unassigned", "config:create",
      { projectId, workspaceId: "", pluginKey: "Nginx", name: "unassigned.conf" });
    await step("configs:unassigned", "configs:unassigned", { projectId });
    await step("config:move", "config:move", { configId: ucfg.configId, workspaceId: wsId });
    await step("archive:exportWs", "archive:exportWorkspace", { workspaceId: wsId, path: "SELFCHECK" });
    await step("archive:import", "archive:import", { path: "SELFCHECK" });
    await step("versions:list", "versions:list", { workspaceId: wsId, configId: cfgId });

    const worst = Math.max(...steps.map(s => s.ms));
    const interactiveMs = steps.filter(s => !s.name.startsWith("archive:")).map(s => s.ms);
    const worstInteractive = Math.max(...interactiveMs);
    window.external.sendMessage(JSON.stringify({
      action: "spike:result",
      ok: !failed && worstInteractive < 50,
      failed,
      worstMs: worst,
      worstInteractiveMs: worstInteractive,
      steps
    }));
  })();
}

init();
