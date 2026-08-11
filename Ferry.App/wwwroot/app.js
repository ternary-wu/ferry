"use strict";

// ---------- 状态 ----------
let plugins = [];
let projects = [];
let currentProjectId = "";
let nav = { workspaces: [], unassigned: [] };
let currentConfig = null;
let currentWorkspaceId = "";
let snapshot = null;
let sourceText = "";
let errors = [];
let unrecognized = [];
let previewTab = "preview";
let collapsedPaths = new Set();
let ctxConfig = null;
let requestSeq = 0;
let latencySamples = [];
const inflight = new Map();
const wizard = { pluginKey: "", templateId: "__blank", step: 1 };

function loadLocal(key, fallback) {
  try {
    const v = localStorage.getItem(key);
    return v === null ? fallback : JSON.parse(v);
  } catch (e) { return fallback; }
}
function saveLocal(key, value) {
  try { localStorage.setItem(key, JSON.stringify(value)); } catch (e) {}
}

// ---------- IPC ----------
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
    latencySamples.push({ action: item.action, ms: performance.now() - item.t0 });
    if (data.latencyMs !== undefined) {
      document.getElementById("latency").textContent =
        `IPC ${data.latencyMs.toFixed(1)}ms · 最近 ${(performance.now() - item.t0).toFixed(1)}ms`;
    }
    if (item.onOk) item.onOk(data);
  } catch (e) {
    log("receive-error:" + e.message);
  }
});

// ---------- 通用 ----------
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
  el.style.color = "#999";
  el.style.fontSize = "13px";
  el.textContent = text;
  return el;
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
    setStatus(errors.length === 0 ? "校验通过" : `校验：${errors.length} 个错误`, errors.length > 0);
  }
  if (data.unrecognized) unrecognized = data.unrecognized;
}
function applyLightUpdate(data) {
  if (!data) return;
  if (data.snapshot) snapshot = data.snapshot;
  if (data.text !== undefined && data.text !== null) { sourceText = data.text; renderPreview(); }
  if (data.errors) {
    errors = data.errors;
    setStatus(errors.length === 0 ? "校验通过" : `校验：${errors.length} 个错误`, errors.length > 0);
  }
  updateModuleStatus();
}

// ---------- 项目 ----------
function refreshProjects(selectProjectId) {
  send("projects:list", null, (data) => {
    projects = data.projects || [];
    if (projects.length === 0) {
      send("project:create", { name: "默认项目" }, () => refreshProjects(selectProjectId));
      return;
    }
    currentProjectId = selectProjectId
      || loadLocal("ferry.projectId", "")
      || projects[0].id;
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
    const name = document.createElement("span");
    name.textContent = p.name;
    item.appendChild(name);
    if (p.id === currentProjectId) {
      const check = document.createElement("span");
      check.className = "check";
      check.textContent = "✓";
      item.appendChild(check);
    }
    item.onclick = () => {
      if (p.id !== currentProjectId) {
        currentProjectId = p.id;
        saveLocal("ferry.projectId", currentProjectId);
        clearOpenConfig();
        loadNav();
      }
      toggleProjectMenu(false);
    };
    list.appendChild(item);
  }
}

function toggleProjectMenu(open) {
  const menu = document.getElementById("projectMenu");
  menu.classList.toggle("open", open !== undefined ? open : !menu.classList.contains("open"));
}

function loadNav() {
  send("nav:tree", { projectId: currentProjectId }, (data) => {
    if (!okOr(data)) return;
    nav = { workspaces: data.workspaces || [], unassigned: data.unassigned || [] };
    renderWorkspaceTree();
    renderUnassignedTree();
    renderWelcome();
  });
}

function renderWorkspaceTree() {
  const el = document.getElementById("workspaceTree");
  el.textContent = "";
  if (nav.workspaces.length === 0) {
    el.appendChild(textSpan("暂无工作空间"));
  }
  for (const ws of nav.workspaces) {
    const header = document.createElement("div");
    header.className = "tree";
    const label = document.createElement("span");
    label.textContent = "▾ " + ws.name;
    const plus = document.createElement("span");
    plus.className = "plus";
    plus.textContent = "＋";
    plus.title = "新建工作空间";
    plus.onclick = (e) => { e.stopPropagation(); newWorkspace(ws.id); };
    header.appendChild(label);
    header.appendChild(plus);
    const group = document.createElement("div");
    group.className = "tree-group open";
    header.onclick = () => group.classList.toggle("open");
    el.appendChild(header);
    for (const cfg of ws.configs || []) group.appendChild(renderConfigItem(cfg, ws.id));
    el.appendChild(group);
  }
}

function renderUnassignedTree() {
  const el = document.getElementById("unassignedTree");
  el.textContent = "";
  if (nav.unassigned.length === 0) return;
  const header = document.createElement("div");
  header.className = "tree";
  const label = document.createElement("span");
  label.textContent = "▾ 未归类配置";
  const plus = document.createElement("span");
  plus.className = "plus";
  plus.textContent = "＋";
  plus.title = "新建未归类配置";
  plus.onclick = (e) => { e.stopPropagation(); openWizard("", ""); };
  header.appendChild(label);
  header.appendChild(plus);
  const group = document.createElement("div");
  group.className = "tree-group open";
  header.onclick = () => group.classList.toggle("open");
  el.appendChild(header);
  for (const cfg of nav.unassigned) group.appendChild(renderConfigItem(cfg, ""));
  el.appendChild(group);
}

function renderConfigItem(cfg, workspaceId) {
  const item = document.createElement("div");
  item.className = "item child" + (currentConfig && cfg.id === currentConfig.id ? " active" : "");
  item.dataset.cfg = cfg.id;
  const icon = document.createElement("span");
  icon.textContent = "🌐";
  const name = document.createElement("span");
  name.className = "name";
  name.textContent = cfg.name;
  name.title = cfg.name;
  item.appendChild(icon);
  item.appendChild(name);
  if (cfg.pluginMissing) {
    const badge = document.createElement("span");
    badge.className = "badge missing";
    badge.textContent = "缺插件";
    item.appendChild(badge);
  }
  const menuBtn = document.createElement("span");
  menuBtn.className = "menu-btn";
  menuBtn.textContent = "⋯";
  menuBtn.onclick = (e) => {
    e.stopPropagation();
    showCtxMenu(e, cfg, workspaceId);
  };
  item.appendChild(menuBtn);
  item.onclick = () => openConfig(cfg, workspaceId);
  return item;
}

// ---------- 配置菜单 ----------
function showCtxMenu(e, cfg, workspaceId) {
  ctxConfig = { cfg, workspaceId };
  const menu = document.getElementById("ctxMenu");
  menu.textContent = "";
  const items = [
    { text: "快速创建（同插件）", fn: () => openWizard(cfg.pluginKey, "", workspaceId) },
    { text: "复制配置", fn: () => openWizard(cfg.pluginKey, cfg.name + " - 副本", workspaceId) },
    { text: "移动工作空间", fn: () => openMoveModal() },
    { text: "版本历史", fn: () => openVersions() },
    { text: "导出存档", fn: () => exportConfigArchive(cfg, workspaceId) },
    { text: "删除", danger: true, fn: () => deleteConfig(cfg, workspaceId) }
  ];
  for (const it of items) {
    const row = document.createElement("div");
    row.className = "menu-item";
    row.textContent = it.text;
    if (it.danger) row.style.color = "#f85149";
    row.onclick = () => { hideCtxMenu(); it.fn(); };
    menu.appendChild(row);
  }
  menu.classList.add("open");
  const x = Math.min(e.clientX, window.innerWidth - 190);
  const y = Math.min(e.clientY, window.innerHeight - items.length * 34 - 20);
  menu.style.left = x + "px";
  menu.style.top = y + "px";
}
function hideCtxMenu() { document.getElementById("ctxMenu").classList.remove("open"); }
document.addEventListener("click", (e) => {
  if (!e.target.closest(".ctx-menu")) hideCtxMenu();
  if (!e.target.closest(".project-selector")) toggleProjectMenu(false);
});

function newWorkspace(projectId) {
  const name = window.prompt("工作空间名称：");
  if (!name) return;
  send("workspace:create", { projectId, name }, (d) => {
    if (okOr(d)) { loadNav(); showToast("已创建工作空间"); }
  });
}

function deleteConfig(cfg, workspaceId) {
  if (!window.confirm(`删除配置「${cfg.name}」？`)) return;
  send("config:delete", { workspaceId, configId: cfg.id }, (data) => {
    okOr(data);
    if (currentConfig && cfg.id === currentConfig.id) clearOpenConfig();
    loadNav();
    showToast("已删除配置");
  });
}

function exportConfigArchive(cfg, workspaceId) {
  const path = window.prompt("存档包导出路径：", cfg.name + ".zip");
  if (!path) return;
  send("archive:exportConfig", { workspaceId, configId: cfg.id, path }, (d) => {
    if (okOr(d)) showToast(`已导出：${d.path}`);
  });
}

// ---------- 打开配置 ----------
function clearOpenConfig() {
  currentConfig = null; snapshot = null; sourceText = "";
  document.getElementById("welcome").style.display = "";
  document.getElementById("editor").classList.remove("show");
  document.getElementById("crumbText").innerHTML = "";
  document.getElementById("moduleStatus").textContent = "";
  document.getElementById("formRoot").textContent = "";
  renderPreview();
}

function applyOpenData(data) {
  currentConfig = data.config;
  snapshot = data.snapshot || [];
  sourceText = data.sourceText || "";
  errors = data.errors || [];
  unrecognized = data.unrecognized || [];
  collapsedPaths = new Set();
  const project = projects.find(p => p.id === currentProjectId);
  const ws = nav.workspaces.find(w => w.id === currentWorkspaceId);
  const crumb = `${escapeHtml(project ? project.name : "项目")} / ${escapeHtml(ws ? ws.name : "未归类")} / <b>${escapeHtml(data.config.name)}</b>`;
  document.getElementById("crumbText").innerHTML = crumb;
  document.getElementById("formTitle").textContent = data.config.name;
  document.getElementById("formSubtitle").textContent =
    `${data.config.pluginName} v${data.config.pluginVersion}${data.versionChanged ? "（字段可能有增减）" : ""}`;
  document.getElementById("welcome").style.display = "none";
  document.getElementById("editor").classList.add("show");
  seedCollapsed();
  renderForm(); renderPreview();
  renderNavActive();
  setStatus(data.pluginMissing ? "插件缺失：仅可查看/导出源码" : `已打开：${data.config.name}`, !!data.pluginMissing);
  updateRecents(data.config.pluginKey, "");
}

function seedCollapsed() {
  const collect = (nodes) => {
    for (const n of nodes) {
      if ((n.isModule || n.type === "Object") && (n.children || []).length > 4) collapsedPaths.add(n.path);
      collect(n.children || []);
    }
  };
  collect(snapshot || []);
}

function renderNavActive() {
  document.querySelectorAll("#workspaceTree .item, #unassignedTree .item").forEach(el => {
    el.classList.remove("active");
  });
  if (currentConfig) {
    const sel = `#workspaceTree .item[data-cfg="${currentConfig.id}"], #unassignedTree .item[data-cfg="${currentConfig.id}"]`;
    const el = document.querySelector(sel);
    if (el) el.classList.add("active");
  }
}

function openConfig(cfg, workspaceId) {
  send("config:open", { workspaceId, configId: cfg.id }, (data) => {
    if (!okOr(data)) return;
    currentWorkspaceId = workspaceId;
    applyOpenData(data);
  });
}

// ---------- 表单（卡片 + 行） ----------
function renderForm() {
  const root = document.getElementById("formRoot");
  root.textContent = "";
  if (!snapshot) return;
  const scalars = snapshot.filter(n => !n.isModule && n.type !== "Object" && n.type !== "Array");
  const blocks = snapshot.filter(n => n.isModule || n.type === "Object" || n.type === "Array");
  if (scalars.length > 0) root.appendChild(renderBaseCard(scalars));
  for (const node of blocks) root.appendChild(renderCard(node, 0));
  updateModuleStatus();
}

function renderBaseCard(nodes) {
  const card = el("div", "card");
  const head = el("div", "card-head");
  head.appendChild(el("span", "title", "基础设置"));
  const chev = el("span", "chev", "▼");
  head.appendChild(chev);
  head.onclick = () => card.classList.toggle("collapsed");
  card.appendChild(head);
  const body = el("div", "card-body");
  for (const node of nodes) body.appendChild(renderRow(node));
  card.appendChild(body);
  return card;
}

function renderCard(node, depth) {
  if (!node.isVisible) return document.createDocumentFragment();
  if (node.type === "Array") return renderArrayCard(node);
  if (node.isModule || node.type === "Object") {
    const card = el("div", "card" + (node.isEnabled ? "" : " disabled"));
    card.dataset.path = node.path;
    const head = el("div", "card-head");
    head.appendChild(node.required ? renderLock() : renderCheck(node));
    const title = el("span", "title", node.label || node.id);
    title.title = node.description || "";
    head.appendChild(title);
    if (node.isModule && node.enabledChildModulesText) {
      head.appendChild(el("span", "count", node.enabledChildModulesText + " 子模块"));
    }
    const chev = el("span", "chev", "▼");
    head.appendChild(chev);
    if (collapsedPaths.has(node.path)) card.classList.add("collapsed");
    head.onclick = () => {
      const was = card.classList.toggle("collapsed");
      if (was) collapsedPaths.add(node.path); else collapsedPaths.delete(node.path);
    };
    card.appendChild(head);
    const body = el("div", "card-body");
    for (const child of node.children || []) body.appendChild(renderCard(child, depth + 1));
    card.appendChild(body);
    return card;
  }
  return renderRow(node);
}

function renderArrayCard(node) {
  const card = el("div", "card");
  const head = el("div", "card-head");
  head.appendChild(node.required ? renderLock() : renderCheck(node));
  head.appendChild(el("span", "title", node.label || node.id));
  head.appendChild(el("span", "count", `${(node.children || []).length} 项`));
  const chev = el("span", "chev", "▼");
  head.appendChild(chev);
  if (collapsedPaths.has(node.path)) card.classList.add("collapsed");
  head.onclick = () => {
    const was = card.classList.toggle("collapsed");
    if (was) collapsedPaths.add(node.path); else collapsedPaths.delete(node.path);
  };
  card.appendChild(head);
  const body = el("div", "card-body");
  for (const item of node.children || []) {
    const itemBox = el("div", "array-item collapsed");
    const itemHead = el("div", "array-item-head");
    itemHead.appendChild(el("span", "t", item.label || item.id));
    const del = el("span", "del", "✕ 删除");
    del.onclick = (e) => { e.stopPropagation(); send("form:removeItem", { path: item.path }, applyFormUpdate); };
    itemHead.appendChild(del);
    itemHead.onclick = () => itemBox.classList.toggle("collapsed");
    itemBox.appendChild(itemHead);
    const itemBody = el("div", "array-item-body");
    for (const child of item.children || []) itemBody.appendChild(renderCard(child, 1));
    itemBox.appendChild(itemBody);
    body.appendChild(itemBox);
  }
  const add = el("div", "array-add", "＋ 添加项");
  add.onclick = () => send("form:addItem", { path: node.path }, applyFormUpdate);
  body.appendChild(add);
  card.appendChild(body);
  return card;
}

function renderRow(node) {
  const row = el("div", "row" + (node.isEnabled ? "" : " disabled"));
  row.dataset.path = node.path;
  const wrap = el("div");
  wrap.style.cssText = "display:flex;gap:10px;align-items:flex-start;flex:1";
  wrap.appendChild(node.required ? renderLock() : renderCheck(node));
  const label = el("div", "row-label");
  label.appendChild(el("div", "n", node.label || node.id));
  if (node.description) label.appendChild(el("div", "d", node.description));
  wrap.appendChild(label);
  const control = el("div", "row-control");
  const setValue = (value) => send("form:setValue", { path: node.path, value }, applyLightUpdate);
  switch (node.type) {
    case "String": {
      const input = document.createElement("input");
      input.type = "text";
      input.value = node.value ?? "";
      input.onchange = () => setValue(input.value);
      control.appendChild(input);
      break;
    }
    case "Number": {
      const input = document.createElement("input");
      input.type = "number";
      input.value = node.value ?? "";
      input.min = node.min ?? "";
      input.max = node.max ?? "";
      input.step = node.integerOnly ? "1" : "any";
      input.onchange = () => setValue(input.value);
      control.appendChild(input);
      break;
    }
    case "Boolean": {
      const toggle = el("div", "toggle" + (node.value === true ? " active" : ""));
      toggle.style.alignSelf = "flex-start";
      toggle.onclick = () => setValue(node.value !== true);
      control.appendChild(toggle);
      break;
    }
    case "Enum": {
      const select = document.createElement("select");
      for (const opt of node.enumOptions || []) {
        const o = document.createElement("option");
        o.value = opt.value;
        o.textContent = opt.value + (opt.description ? `（${opt.description}）` : "");
        select.appendChild(o);
      }
      if (node.allowCustomValue) {
        const custom = document.createElement("input");
        custom.type = "text";
        custom.placeholder = "自定义值";
        custom.value = (node.value ?? "").toString();
        custom.onchange = () => setValue(custom.value);
        select.onchange = () => setValue(select.value);
        control.appendChild(select);
        control.appendChild(custom);
      } else {
        select.value = (node.value ?? "").toString();
        select.onchange = () => setValue(select.value);
        control.appendChild(select);
      }
      break;
    }
  }
  if (node.validationError) control.appendChild(el("div", "row-error", node.validationError));
  wrap.appendChild(control);
  row.appendChild(wrap);
  return row;
}

function renderCheck(node) {
  const box = el("span", "check" + (node.isEnabled ? " checked" : ""));
  if (!node.isSelectable) {
    box.style.cursor = "not-allowed";
    box.title = "父级未启用时锁定";
  } else {
    box.title = "取消勾选后该项不写入输出";
  }
  box.onclick = (e) => {
    e.stopPropagation();
    if (!node.canToggleEnabled) return;
    send("form:toggle", { path: node.path, enabled: !node.isEnabled }, applyFormUpdate);
  };
  return box;
}

function renderLock() {
  const lock = el("span", "check lock", "🔒");
  lock.title = "必填字段不可取消";
  return lock;
}

function el(tag, cls, text) {
  const node = document.createElement(tag);
  if (cls) node.className = cls;
  if (text !== undefined) node.textContent = text;
  return node;
}

function updateModuleStatus() {
  document.getElementById("moduleStatus").textContent =
    snapshot ? `${countEnabled(snapshot)}/${countTotal(snapshot)} 模块已启用` : "";
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

// ---------- 欢迎页 ----------
function renderWelcome() {
  const chips = document.getElementById("recentChips");
  chips.textContent = "";
  const recents = loadLocal("ferry.recentPlugins", []);
  for (const key of recents) {
    const p = plugins.find(x => x.key === key);
    if (!p) continue;
    const chip = el("div", "chip", p.name);
    chip.onclick = () => openWizard(p.key, "", "");
    chips.appendChild(chip);
  }
  if (chips.childElementCount === 0) {
    chips.appendChild(el("span", "recent-label", "创建配置后这里会显示最近使用的插件"));
  }
}

function updateRecents(pluginKey, templateId) {
  const recents = loadLocal("ferry.recentPlugins", []);
  const next = [pluginKey, ...recents.filter(k => k !== pluginKey)].slice(0, 5);
  saveLocal("ferry.recentPlugins", next);
  if (templateId && templateId !== "__blank") {
    const tpls = loadLocal("ferry.recentTemplates", []);
    saveLocal("ferry.recentTemplates", [templateId, ...tpls.filter(t => t !== templateId)].slice(0, 5));
  }
  renderWelcome();
}

// ---------- 源码面板 ----------
function renderPreview() {
  if (previewTab === "preview") {
    document.getElementById("previewCode").textContent = sourceText || "（点击右上角「源码」打开）";
  }
}
function switchSourceTab(tab) {
  previewTab = tab;
  document.querySelectorAll(".source-tab").forEach(t => t.classList.toggle("active", t.dataset.tab === tab));
  document.getElementById("previewCode").style.display = tab === "preview" ? "" : "none";
  document.getElementById("sourceEditor").style.display = tab === "edit" ? "" : "none";
  if (tab === "edit") document.getElementById("sourceEditor").value = sourceText;
  else renderPreview();
}
function setSourceOpen(open) {
  const panel = document.getElementById("sourcePanel");
  panel.classList.toggle("open", open);
  document.getElementById("btnSource").classList.toggle("active", open);
}

// ---------- 向导 ----------
function openWizard(pluginKey, presetName, workspaceId) {
  wizard.pluginKey = pluginKey || "";
  wizard.templateId = "__blank";
  wizard.step = 1;
  document.getElementById("wzName").value = presetName || "";
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
  document.getElementById("wzBack").style.display = n === 1 ? "none" : "";
  document.getElementById("wzCreate").style.display = n === 3 ? "" : "none";
}
function fillWizardWorkspace(selected) {
  const sel = document.getElementById("wzWorkspace");
  sel.innerHTML = "";
  const optNone = document.createElement("option");
  optNone.value = "";
  optNone.textContent = "（未归类配置）";
  sel.appendChild(optNone);
  for (const ws of nav.workspaces) {
    const opt = document.createElement("option");
    opt.value = ws.id;
    opt.textContent = ws.name;
    sel.appendChild(opt);
  }
  sel.value = selected !== undefined ? selected : (currentWorkspaceId || "");
}
function renderWizardPlugins() {
  const recentsEl = document.getElementById("wzRecentList");
  const listEl = document.getElementById("wzPluginList");
  recentsEl.textContent = "";
  listEl.textContent = "";
  const filter = document.getElementById("wzPluginSearch").value.trim().toLowerCase();
  const recents = loadLocal("ferry.recentPlugins", []);
  const recentPlugins = recents.map(k => plugins.find(p => p.key === k)).filter(Boolean);
  if (!filter && recentPlugins.length > 0) {
    recentsEl.appendChild(el("div", "mini-label", "最近使用"));
    for (const p of recentPlugins) recentsEl.appendChild(renderPluginOption(p));
  }
  const visible = plugins.filter(p => !filter || p.name.toLowerCase().includes(filter) || p.key.toLowerCase().includes(filter));
  for (const p of visible) listEl.appendChild(renderPluginOption(p));
  if (visible.length === 0) listEl.appendChild(textSpan("没有匹配的插件"));
}
function renderPluginOption(p) {
  const opt = el("div", "option");
  opt.innerHTML = `🌐 ${escapeHtml(p.name)} <small>v${escapeHtml(p.version)}</small><div class="desc">${escapeHtml(p.description || p.rendererType)}</div>`;
  opt.onclick = () => {
    wizard.pluginKey = p.key;
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
  const blank = el("div", "option" + (wizard.templateId === "__blank" ? " active" : ""));
  blank.innerHTML = "默认模板<div class='desc'>空白默认配置</div>";
  blank.onclick = () => { wizard.templateId = "__blank"; selectWizardTemplate(blank); goWizardStep(3); };
  elW.appendChild(blank);
  const plugin = plugins.find(p => p.key === wizard.pluginKey);
  for (const t of (plugin && plugin.templates) || []) {
    const opt = el("div", "option" + (wizard.templateId === t.id ? " active" : ""));
    opt.innerHTML = `${escapeHtml(t.name)}<div class="desc">${escapeHtml(t.description || "场景模板")}</div>`;
    opt.onclick = () => { wizard.templateId = t.id; selectWizardTemplate(opt); goWizardStep(3); };
    elW.appendChild(opt);
  }
}
function selectWizardTemplate(target) {
  document.querySelectorAll("#wzTemplateList .option").forEach(o => o.classList.remove("active"));
  target.classList.add("active");
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

// ---------- 移动 ----------
function openMoveModal() {
  if (!ctxConfig) return;
  const sel = document.getElementById("moveWorkspace");
  sel.innerHTML = "";
  const optNone = document.createElement("option");
  optNone.value = "";
  optNone.textContent = "（未归类配置）";
  sel.appendChild(optNone);
  for (const ws of nav.workspaces) {
    const opt = document.createElement("option");
    opt.value = ws.id;
    opt.textContent = ws.name;
    sel.appendChild(opt);
  }
  sel.value = ctxConfig.workspaceId;
  document.getElementById("moveTargetLabel").textContent = `移动「${ctxConfig.cfg.name}」到：`;
  openModal("moveModal");
}
document.getElementById("btnMoveOk").onclick = () => {
  if (!ctxConfig) return;
  const target = document.getElementById("moveWorkspace").value;
  send("config:move", { configId: ctxConfig.cfg.id, workspaceId: target }, (d) => {
    if (!okOr(d)) return;
    closeModal("moveModal");
    if (currentConfig && ctxConfig.cfg.id === currentConfig.id) {
      currentWorkspaceId = target;
      applyOpenData({ config: currentConfig, snapshot, sourceText, errors, unrecognized, versionChanged: false });
    }
    loadNav();
    showToast("已移动配置");
  });
};
document.getElementById("btnMoveCancel").onclick = () => closeModal("moveModal");

// ---------- 版本 ----------
function openVersions() {
  if (!ctxConfig) { setStatus("请选择配置", true); return; }
  document.getElementById("versionNote").value = "";
  refreshVersionsList();
  openModal("versionsModal");
}
function refreshVersionsList() {
  const elV = document.getElementById("versionList");
  elV.textContent = "";
  if (!ctxConfig) { elV.appendChild(textSpan("请选择配置")); return; }
  send("versions:list", { workspaceId: ctxConfig.workspaceId, configId: ctxConfig.cfg.id }, (data) => {
    const versions = data.versions || [];
    if (versions.length === 0) { elV.appendChild(textSpan("暂无留档")); return; }
    for (const v of versions) {
      const item = el("div", "list-item");
      item.appendChild(el("span", "t", `${v.timestamp}${v.note ? " · " + v.note : ""}（${v.length} 字符）`));
      const acts = el("span", "acts");
      const restore = el("span", "", "回滚");
      restore.onclick = () => {
        if (!window.confirm("回滚到该版本？当前表单将被该版本源码重建。")) return;
        send("version:restore", { workspaceId: ctxConfig.workspaceId, configId: ctxConfig.cfg.id, versionId: v.id }, (data) => {
          if (!okOr(data)) return;
          currentWorkspaceId = ctxConfig.workspaceId;
          applyOpenData(data);
          refreshVersionsList();
          showToast("已回滚到该版本");
        });
      };
      const del = el("span", "del", "删除");
      del.onclick = () => send("version:delete", { workspaceId: ctxConfig.workspaceId, configId: ctxConfig.cfg.id, versionId: v.id }, () => refreshVersionsList());
      acts.appendChild(restore);
      acts.appendChild(del);
      item.appendChild(acts);
      elV.appendChild(item);
    }
  });
}

// ---------- 设置 ----------
function openSettings() {
  renderSettingsPlugins();
  renderSettingsTemplates();
  send("logs:path", null, (d) => {
    if (d.ok) document.getElementById("settingsLogPath").textContent = d.path;
  });
  openModal("settingsModal");
}
function renderSettingsPlugins() {
  const elS = document.getElementById("settingsPlugins");
  elS.textContent = "";
  for (const p of plugins) {
    const row = el("div", "plugin-row");
    row.appendChild(el("span", "n", `🌐 ${p.name}`));
    row.appendChild(el("span", "v", `v${p.version}`));
    if (p.loadErrors && p.loadErrors.length) {
      row.appendChild(el("span", "e", p.loadErrors[0]));
    }
    elS.appendChild(row);
  }
}
function renderSettingsTemplates() {
  const elS = document.getElementById("settingsTemplates");
  elS.textContent = "";
  for (const p of plugins) {
    const head = el("div", "plugin-row");
    head.appendChild(el("span", "n", p.name));
    elS.appendChild(head);
    for (const t of (p.templates || [])) {
      elS.appendChild(el("div", "list-item", t.name + (t.description ? "　—　" + t.description : "")));
    }
    if (!p.templates || p.templates.length === 0) {
      elS.appendChild(textSpan("该插件未定义模板（将使用默认模板）"));
    }
  }
}

// ---------- 事件绑定 ----------
document.getElementById("projectBtn").onclick = () => toggleProjectMenu();
document.getElementById("btnNewProject").onclick = () => {
  const name = window.prompt("项目名称：");
  if (!name) return;
  send("project:create", { name }, (d) => {
    if (okOr(d)) { currentProjectId = d.project.id; saveLocal("ferry.projectId", currentProjectId); refreshProjects(currentProjectId); showToast("已创建项目"); }
  });
};
document.getElementById("btnNewConfig").onclick = () => openWizard();
document.getElementById("btnWelcomeNew").onclick = () => openWizard();
document.getElementById("wzPluginSearch").oninput = renderWizardPlugins;
document.getElementById("wzCancel").onclick = () => closeModal("wizardModal");
document.getElementById("wzBack").onclick = () => goWizardStep(wizard.step - 1);
document.getElementById("wzCreate").onclick = submitCreate;

document.getElementById("btnSource").onclick = () => {
  const panel = document.getElementById("sourcePanel");
  setSourceOpen(!panel.classList.contains("open"));
  if (panel.classList.contains("open")) {
    document.getElementById("sourceName").textContent = currentConfig ? currentConfig.name : "未打开配置";
    renderPreview();
  }
};
document.getElementById("btnSourceClose").onclick = () => setSourceOpen(false);
document.getElementById("btnSourceFull").onclick = () => {
  const panel = document.getElementById("sourcePanel");
  panel.classList.toggle("full");
  document.getElementById("btnSourceFull").textContent = panel.classList.contains("full") ? "⤢" : "⛶";
};
document.querySelectorAll(".source-tab").forEach(tab => {
  tab.onclick = () => switchSourceTab(tab.dataset.tab);
});
document.getElementById("btnApplyEdit").onclick = () => {
  const text = document.getElementById("sourceEditor").value;
  send("form:importText", { text }, (d) => {
    if (!okOr(d)) return;
    applyFormUpdate(d);
    document.getElementById("unrecognizedReport").textContent =
      d.report && d.report.unrecognizedLines > 0 ? `未识别内容 ${d.report.unrecognizedLines} 行（已保留）` : "";
    showToast("已应用修改到表单");
  });
};
document.getElementById("btnImportFile").onclick = () => document.getElementById("importFile").click();
document.getElementById("importFile").onchange = (e) => {
  const file = e.target.files[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    send("form:importText", { text: String(reader.result) }, (d) => {
      if (!okOr(d)) return;
      applyFormUpdate(d);
      showToast(`已导入：${file.name}`);
    });
  };
  reader.readAsText(file);
};
document.getElementById("btnExport").onclick = () => {
  const path = document.getElementById("exportPath").value;
  if (!path) { setStatus("请填写导出路径", true); return; }
  send("config:exportTo", { path }, (d) => { if (okOr(d)) showToast(`已导出：${d.path}`); });
};

document.getElementById("btnSnapshot").onclick = () => {
  const note = document.getElementById("versionNote").value.trim() || undefined;
  send("version:snapshot", { note }, (d) => {
    if (okOr(d)) { document.getElementById("versionNote").value = ""; refreshVersionsList(); showToast("已留档"); }
  });
};

document.getElementById("btnSettings").onclick = openSettings;
document.querySelectorAll(".settings-tab").forEach(tab => {
  tab.onclick = () => {
    document.querySelectorAll(".settings-tab").forEach(t => t.classList.toggle("active", t === tab));
    document.querySelectorAll(".settings-pane").forEach(p => p.classList.toggle("active", p.id === "pane-" + tab.dataset.pane));
  };
});
document.getElementById("btnOpenLogs").onclick = () => send("logs:open", null, (d) => { okOr(d); });
document.getElementById("btnImportArchive").onclick = () => {
  const path = document.getElementById("archivePath").value;
  if (!path) { setStatus("请填写存档包路径", true); return; }
  send("archive:import", { path }, (d) => {
    if (!okOr(d)) return;
    const parts = [
      `导入 ${d.imported} 个配置`,
      d.skipped ? `跳过 ${d.skipped} 个` : "",
      d.packagedPlugins.length ? `随包插件：${d.packagedPlugins.join("、")}` : "",
      d.missingPlugins.length ? `缺插件：${d.missingPlugins.join("、")}` : ""
    ].filter(Boolean);
    document.getElementById("archiveResult").textContent = parts.join("；");
    refreshProjects();
    showToast("存档导入完成");
  });
};

document.getElementById("btnCollapseAll").onclick = () => {
  snapshot && collectPaths(snapshot).forEach(p => collapsedPaths.add(p));
  renderForm();
};
document.getElementById("btnExpandAll").onclick = () => {
  collapsedPaths.clear();
  renderForm();
};
function collectPaths(nodes) {
  const out = [];
  for (const n of nodes) {
    if (n.isModule || n.type === "Object" || n.type === "Array") out.push(n.path);
    out.push(...collectPaths(n.children || []));
  }
  return out;
}

// 源码面板宽度拖拽（420 基础宽度）
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    hideCtxMenu();
    closeModal("wizardModal");
    closeModal("moveModal");
    closeModal("versionsModal");
    closeModal("settingsModal");
  }
});

// ---------- 初始化 ----------
function init() {
  send("bootstrap", null, (data) => {
    plugins = data.plugins || [];
    if (data.loadErrors && data.loadErrors.length) {
      setStatus(`插件加载 ${data.loadErrors.length} 个失败：${data.loadErrors[0]}`, true);
    }
    refreshProjects();
  });
}

// ---------- 自检（全链路） ----------
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
