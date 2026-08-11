"use strict";

// ---------- 状态 ----------
let plugins = [];
let workspaces = [];
let currentWorkspaceId = "";
let configs = [];
let templates = [];
let currentConfig = null;
let snapshot = null;
let sourceText = "";
let errors = [];
let unrecognized = [];
let previewTab = "preview";
let collapsedPaths = new Set();
let requestSeq = 0;
let latencySamples = [];
const inflight = new Map();

const wizard = { pluginKey: "", templateId: "__blank", step: 1 };

// ---------- IPC（requestId 配对） ----------
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

// 结构性变更：应用快照并重渲染
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

// 值修改：更新状态与预览，不重建输入框
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

// ---------- 工作空间 / 配置树 ----------
function refreshWorkspaces() {
  send("workspaces:list", null, (data) => {
    workspaces = data.workspaces || [];
    const sel = document.getElementById("workspaceSelect");
    sel.innerHTML = "";
    for (const ws of workspaces) {
      const opt = document.createElement("option");
      opt.value = ws.id;
      opt.textContent = "工作空间：" + ws.name;
      sel.appendChild(opt);
    }
    if (workspaces.length === 0) {
      send("workspace:create", { name: "默认工作空间" }, () => refreshWorkspaces());
      return;
    }
    if (!currentWorkspaceId || !workspaces.some(w => w.id === currentWorkspaceId)) {
      currentWorkspaceId = workspaces[0].id;
    }
    sel.value = currentWorkspaceId;
    fillWizardWorkspace();
    refreshConfigs();
  });
}

function fillWizardWorkspace() {
  const sel = document.getElementById("wzWorkspace");
  sel.innerHTML = "";
  for (const ws of workspaces) {
    const opt = document.createElement("option");
    opt.value = ws.id;
    opt.textContent = ws.name;
    sel.appendChild(opt);
  }
  sel.value = currentWorkspaceId;
}

function refreshConfigs() {
  if (!currentWorkspaceId) return;
  send("configs:list", { workspaceId: currentWorkspaceId }, (data) => {
    configs = data.configs || [];
    renderConfigTree();
  });
}

function renderConfigTree() {
  const el = document.getElementById("configTree");
  el.textContent = "";
  if (configs.length === 0) {
    el.appendChild(textSpan("该工作空间暂无配置"));
    return;
  }
  const groups = new Map();
  for (const cfg of configs) {
    const key = cfg.pluginName || cfg.pluginKey;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(cfg);
  }
  for (const [pluginName, items] of groups) {
    const header = document.createElement("div");
    header.className = "tree";
    const label = document.createElement("span");
    label.textContent = "▾ " + pluginName;
    const plus = document.createElement("span");
    plus.className = "plus";
    plus.textContent = "＋";
    plus.title = "以该插件新建配置";
    plus.onclick = (e) => { e.stopPropagation(); openWizard(items[0].pluginKey); };
    header.appendChild(label);
    header.appendChild(plus);
    header.onclick = () => { group.style.display = group.style.display === "none" ? "" : "none"; };
    el.appendChild(header);
    const group = document.createElement("div");
    for (const cfg of items) {
      const item = document.createElement("div");
      item.className = "item child" + (currentConfig && cfg.id === currentConfig.id ? " active" : "");
      const icon = document.createElement("span");
      icon.textContent = "🌐";
      const name = document.createElement("span");
      name.className = "name";
      name.textContent = cfg.name;
      name.title = cfg.name;
      item.appendChild(icon);
      item.appendChild(name);
      const badge = document.createElement("span");
      badge.className = "badge" + (cfg.pluginMissing ? " missing" : "");
      badge.textContent = cfg.pluginMissing ? "缺插件" : "";
      if (cfg.pluginMissing) item.appendChild(badge);
      const del = document.createElement("span");
      del.className = "del";
      del.textContent = "✕";
      del.title = "删除配置";
      del.onclick = (e) => { e.stopPropagation(); deleteConfig(cfg); };
      item.appendChild(del);
      item.onclick = () => openConfig(cfg);
      group.appendChild(item);
    }
    el.appendChild(group);
  }
}

function textSpan(text) {
  const el = document.createElement("span");
  el.style.color = "#999";
  el.style.fontSize = "13px";
  el.textContent = text;
  return el;
}

function deleteConfig(cfg) {
  if (!window.confirm(`删除配置「${cfg.name}」？`)) return;
  send("config:delete", { workspaceId: currentWorkspaceId, configId: cfg.id }, (data) => {
    okOr(data);
    if (currentConfig && cfg.id === currentConfig.id) clearOpenConfig();
    refreshConfigs();
    showToast("已删除配置");
  });
}

function clearOpenConfig() {
  currentConfig = null; snapshot = null; sourceText = "";
  document.getElementById("topTitle").textContent = "Ferry";
  document.getElementById("formTitle").textContent = "Ferry";
  document.getElementById("formSubtitle").textContent = "从左侧选择配置开始编辑";
  document.getElementById("formRoot").textContent = "";
  document.getElementById("moduleStatus").textContent = "";
  renderPreview();
}

function applyOpenData(data) {
  currentConfig = data.config;
  snapshot = data.snapshot || [];
  sourceText = data.sourceText || "";
  errors = data.errors || [];
  unrecognized = data.unrecognized || [];
  templates = data.templates || [];
  collapsedPaths = new Set();
  const ws = workspaces.find(w => w.id === currentWorkspaceId);
  document.getElementById("topTitle").textContent = data.config.name;
  document.getElementById("formTitle").textContent = data.config.name;
  document.getElementById("formSubtitle").textContent =
    `${ws ? ws.name : ""} / ${data.config.pluginName} v${data.config.pluginVersion}` +
    (data.versionChanged ? "（字段可能有增减）" : "");
  renderForm(); renderPreview();
  setStatus(data.pluginMissing ? "插件缺失：仅可查看/导出源码" : `已打开：${data.config.name}`, !!data.pluginMissing);
}

function openConfig(cfg) {
  send("config:open", { workspaceId: currentWorkspaceId, configId: cfg.id }, (data) => {
    if (!okOr(data)) return;
    applyOpenData(data);
    renderConfigTree();
  });
}

// ---------- 表单（卡片 + 行） ----------
function renderForm() {
  const root = document.getElementById("formRoot");
  root.textContent = "";
  if (!snapshot) {
    root.appendChild(textSpan("从左侧选择配置开始编辑"));
    return;
  }
  const scalars = snapshot.filter(n => !n.isModule && n.type !== "Object" && n.type !== "Array");
  const blocks = snapshot.filter(n => n.isModule || n.type === "Object" || n.type === "Array");
  if (scalars.length > 0) {
    root.appendChild(renderBaseCard(scalars));
  }
  for (const node of blocks) root.appendChild(renderCard(node, 0));
  root.appendChild(renderQuickActionsCard());
  updateModuleStatus();
}

function renderBaseCard(nodes) {
  const card = document.createElement("div");
  card.className = "card";
  const head = document.createElement("div");
  head.className = "card-head";
  const title = document.createElement("span");
  title.className = "title";
  title.textContent = "基础设置";
  head.appendChild(title);
  const chev = document.createElement("span");
  chev.className = "chev";
  chev.textContent = "▼";
  head.appendChild(chev);
  head.onclick = () => card.classList.toggle("collapsed");
  card.appendChild(head);
  const body = document.createElement("div");
  body.className = "card-body";
  for (const node of nodes) body.appendChild(renderRow(node));
  card.appendChild(body);
  return card;
}

function renderCard(node, depth) {
  if (!node.isVisible) return document.createDocumentFragment();
  if (node.type === "Array") return renderArrayCard(node);
  if (node.isModule || node.type === "Object") {
    const card = document.createElement("div");
    card.className = "card" + (node.isEnabled ? "" : " disabled");
    card.dataset.path = node.path;
    const head = document.createElement("div");
    head.className = "card-head";
    if (!node.required) {
      head.appendChild(renderCheck(node));
    } else {
      head.appendChild(renderLock());
    }
    const title = document.createElement("span");
    title.className = "title";
    title.textContent = (node.label || node.id) + (node.description ? "　—　" + node.description : "");
    title.title = node.description || "";
    head.appendChild(title);
    if (node.isModule && node.enabledChildModulesText) {
      const count = document.createElement("span");
      count.className = "count";
      count.textContent = node.enabledChildModulesText + " 子模块";
      head.appendChild(count);
    }
    const chev = document.createElement("span");
    chev.className = "chev";
    chev.textContent = "▼";
    head.appendChild(chev);
    const collapsed = collapsedPaths.has(node.path);
    if (collapsed) card.classList.add("collapsed");
    head.onclick = () => {
      const was = card.classList.toggle("collapsed");
      if (was) collapsedPaths.add(node.path); else collapsedPaths.delete(node.path);
    };
    card.appendChild(head);
    const body = document.createElement("div");
    body.className = "card-body";
    for (const child of node.children || []) body.appendChild(renderCard(child, depth + 1));
    card.appendChild(body);
    return card;
  }
  return renderRow(node);
}

function renderArrayCard(node) {
  const card = document.createElement("div");
  card.className = "card";
  card.dataset.path = node.path;
  const head = document.createElement("div");
  head.className = "card-head";
  if (!node.required) head.appendChild(renderCheck(node)); else head.appendChild(renderLock());
  const title = document.createElement("span");
  title.className = "title";
  title.textContent = node.label || node.id;
  head.appendChild(title);
  const count = document.createElement("span");
  count.className = "count";
  count.textContent = `${(node.children || []).length} 项`;
  head.appendChild(count);
  const chev = document.createElement("span");
  chev.className = "chev";
  chev.textContent = "▼";
  head.appendChild(chev);
  const collapsed = collapsedPaths.has(node.path);
  if (collapsed) card.classList.add("collapsed");
  head.onclick = () => {
    const was = card.classList.toggle("collapsed");
    if (was) collapsedPaths.add(node.path); else collapsedPaths.delete(node.path);
  };
  card.appendChild(head);
  const body = document.createElement("div");
  body.className = "card-body";
  for (const item of node.children || []) {
    const itemBox = document.createElement("div");
    itemBox.className = "array-item collapsed";
    const itemHead = document.createElement("div");
    itemHead.className = "array-item-head";
    const t = document.createElement("span");
    t.className = "t";
    t.textContent = item.label || item.id;
    itemHead.appendChild(t);
    const del = document.createElement("span");
    del.className = "del";
    del.textContent = "✕ 删除";
    del.onclick = (e) => { e.stopPropagation(); send("form:removeItem", { path: item.path }, applyFormUpdate); };
    itemHead.appendChild(del);
    itemHead.onclick = () => itemBox.classList.toggle("collapsed");
    itemBox.appendChild(itemHead);
    const itemBody = document.createElement("div");
    itemBody.className = "array-item-body";
    for (const child of item.children || []) itemBody.appendChild(renderCard(child, 1));
    itemBox.appendChild(itemBody);
    body.appendChild(itemBox);
  }
  const add = document.createElement("div");
  add.className = "array-add";
  add.textContent = "＋ 添加项";
  add.onclick = () => send("form:addItem", { path: node.path }, applyFormUpdate);
  body.appendChild(add);
  card.appendChild(body);
  return card;
}

function renderRow(node) {
  const row = document.createElement("div");
  row.className = "row" + (node.isEnabled ? "" : " disabled");
  row.dataset.path = node.path;
  const label = document.createElement("div");
  label.className = "row-label";
  const n = document.createElement("div");
  n.className = "n";
  n.textContent = node.label || node.id;
  label.appendChild(n);
  if (node.description) {
    const d = document.createElement("div");
    d.className = "d";
    d.textContent = node.description;
    label.appendChild(d);
  }
  const control = document.createElement("div");
  control.className = "row-control";
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
      const toggle = document.createElement("div");
      toggle.className = "toggle" + (node.value === true ? " active" : "");
      toggle.style.alignSelf = "flex-start";
      toggle.onclick = () => setValue(node.value !== true);
      control.appendChild(toggle);
      break;
    }
    case "Enum": {
      const select = document.createElement("select");
      for (const opt of node.enumOptions || []) {
        const el = document.createElement("option");
        el.value = opt.value;
        el.textContent = opt.value + (opt.description ? `（${opt.description}）` : "");
        select.appendChild(el);
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
  if (node.validationError) {
    const err = document.createElement("div");
    err.className = "row-error";
    err.textContent = node.validationError;
    control.appendChild(err);
  }

  const wrap = document.createElement("div");
  wrap.style.display = "flex";
  wrap.style.gap = "10px";
  wrap.style.alignItems = "flex-start";
  wrap.style.flex = "1";
  wrap.appendChild(node.required ? renderLock() : renderCheck(node));
  wrap.appendChild(label);
  wrap.appendChild(control);
  row.appendChild(wrap);
  return row;
}

function renderCheck(node) {
  const box = document.createElement("span");
  box.className = "check" + (node.isEnabled ? " checked" : "");
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
  const lock = document.createElement("span");
  lock.className = "check lock";
  lock.textContent = "🔒";
  lock.title = "必填字段不可取消";
  return lock;
}

function renderQuickActionsCard() {
  const card = document.createElement("div");
  card.className = "card";
  const head = document.createElement("div");
  head.className = "card-head";
  const title = document.createElement("span");
  title.className = "title";
  title.textContent = "快速操作";
  head.appendChild(title);
  const chev = document.createElement("span");
  chev.className = "chev";
  chev.textContent = "▼";
  head.appendChild(chev);
  head.onclick = () => card.classList.toggle("collapsed");
  card.appendChild(head);
  const body = document.createElement("div");
  body.className = "card-body";
  const rows = [
    { text: "基于当前插件创建配置", fn: () => currentConfig && openWizard(currentConfig.pluginKey) },
    { text: "复制当前配置", fn: () => currentConfig && openWizard(currentConfig.pluginKey, currentConfig.name + " - 副本") }
  ];
  for (const r of rows) {
    const row = document.createElement("div");
    row.className = "quick-row";
    row.textContent = r.text;
    const arrow = document.createElement("span");
    arrow.className = "arrow";
    arrow.textContent = "›";
    row.appendChild(arrow);
    row.onclick = r.fn;
    body.appendChild(row);
  }
  card.appendChild(body);
  return card;
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

// ---------- 源码面板 ----------
function renderPreview() {
  if (previewTab === "preview") {
    document.getElementById("previewCode").textContent = sourceText || "（点击右上角「源码」后选择「预览」）";
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

// ---------- 新建配置向导 ----------
function openWizard(pluginKey, presetName) {
  wizard.pluginKey = pluginKey || "";
  wizard.templateId = "__blank";
  wizard.step = 1;
  document.getElementById("wzName").value = presetName || "";
  document.getElementById("wzPluginSearch").value = "";
  fillWizardWorkspace();
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

function renderWizardPlugins() {
  const el = document.getElementById("wzPluginList");
  el.textContent = "";
  const filter = document.getElementById("wzPluginSearch").value.trim().toLowerCase();
  for (const p of plugins) {
    if (filter && !(p.name.toLowerCase().includes(filter) || p.key.toLowerCase().includes(filter))) continue;
    const opt = document.createElement("div");
    opt.className = "option";
    opt.innerHTML = `🌐 ${p.name} <small>v${p.version}</small><div class="desc">${p.description || p.rendererType}</div>`;
    opt.onclick = () => {
      wizard.pluginKey = p.key;
      document.querySelectorAll("#wzPluginList .option").forEach(o => o.classList.remove("active"));
      opt.classList.add("active");
      renderWizardTemplates();
      goWizardStep(2);
    };
    el.appendChild(opt);
  }
}

function renderWizardTemplates() {
  const el = document.getElementById("wzTemplateList");
  el.textContent = "";
  const blank = document.createElement("div");
  blank.className = "option" + (wizard.templateId === "__blank" ? " active" : "");
  blank.innerHTML = "默认模板<div class='desc'>空白默认配置</div>";
  blank.onclick = () => { wizard.templateId = "__blank"; selectWizardTemplate(blank); goWizardStep(3); };
  el.appendChild(blank);
  const plugin = plugins.find(p => p.key === wizard.pluginKey);
  const tpls = plugin ? templatesForPlugin(plugin) : [];
  for (const t of tpls) {
    const opt = document.createElement("div");
    opt.className = "option" + (wizard.templateId === t.id ? " active" : "");
    opt.innerHTML = `${t.name}<div class="desc">${t.description || "场景模板"}</div>`;
    opt.onclick = () => { wizard.templateId = t.id; selectWizardTemplate(opt); goWizardStep(3); };
    el.appendChild(opt);
  }
}

function templatesForPlugin(plugin) {
  return plugin.templates || [];
}

function selectWizardTemplate(el) {
  document.querySelectorAll("#wzTemplateList .option").forEach(o => o.classList.remove("active"));
  el.classList.add("active");
}

function submitCreate() {
  const pluginKey = wizard.pluginKey;
  if (!pluginKey) { setStatus("请选择插件", true); return; }
  const workspaceId = document.getElementById("wzWorkspace").value || currentWorkspaceId;
  const name = document.getElementById("wzName").value.trim() || undefined;
  const templateId = wizard.templateId;
  send("config:create", { workspaceId, pluginKey, name }, (d) => {
    if (!okOr(d)) return;
    const cfgId = d.configId;
    send("config:open", { workspaceId, configId: cfgId }, (data) => {
      if (!okOr(data)) { closeModal("wizardModal"); refreshConfigs(); return; }
      const finish = () => {
        closeModal("wizardModal");
        currentWorkspaceId = workspaceId;
        document.getElementById("workspaceSelect").value = workspaceId;
        applyOpenData(data);
        refreshConfigs();
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

// ---------- 模板应用（当前配置） ----------
function renderTemplateList() {
  const el = document.getElementById("templateList");
  el.textContent = "";
  const blank = document.createElement("div");
  blank.className = "option";
  blank.innerHTML = "空白开始<div class='desc'>不套用模板，恢复默认配置</div>";
  blank.onclick = () => {
    if (!window.confirm("应用模板将覆盖当前配置，是否继续？")) return;
    send("config:reset", {}, (d) => { if (okOr(d)) { applyFormUpdate(d); closeModal("templateModal"); showToast("已重置为空白配置"); } });
  };
  el.appendChild(blank);
  for (const t of templates) {
    const opt = document.createElement("div");
    opt.className = "option";
    opt.innerHTML = `${t.name}<div class="desc">${t.description || "场景模板"}</div>`;
    opt.onclick = () => {
      if (!window.confirm("应用模板将覆盖当前配置，是否继续？")) return;
      send("form:applyPreset", { preset: t.id }, (d) => {
        if (okOr(d)) { applyFormUpdate(d); closeModal("templateModal"); showToast("已应用模板"); }
      });
    };
    el.appendChild(opt);
  }
}

// ---------- 版本 ----------
function refreshVersionsList() {
  const el = document.getElementById("versionList");
  el.textContent = "";
  if (!currentConfig) {
    el.appendChild(textSpan("打开配置后可留档"));
    return;
  }
  send("versions:list", { workspaceId: currentWorkspaceId, configId: currentConfig.id }, (data) => {
    const versions = data.versions || [];
    if (versions.length === 0) {
      el.appendChild(textSpan("暂无留档"));
      return;
    }
    for (const v of versions) {
      const item = document.createElement("div");
      item.className = "list-item";
      const t = document.createElement("span");
      t.className = "t";
      t.textContent = `${v.timestamp}${v.note ? " · " + v.note : ""}（${v.length} 字符）`;
      t.title = v.preview;
      item.appendChild(t);
      const acts = document.createElement("span");
      acts.className = "acts";
      const restore = document.createElement("span");
      restore.textContent = "回滚";
      restore.title = "回滚到该版本";
      restore.onclick = () => {
        if (!window.confirm("回滚到该版本？当前表单将被该版本源码重建。")) return;
        send("version:restore", { workspaceId: currentWorkspaceId, configId: currentConfig.id, versionId: v.id }, (data) => {
          if (!okOr(data)) return;
          applyOpenData(data);
          renderConfigTree();
          refreshVersionsList();
          showToast("已回滚到该版本");
        });
      };
      const del = document.createElement("span");
      del.className = "del";
      del.textContent = "删除";
      del.onclick = () => send("version:delete", { workspaceId: currentWorkspaceId, configId: currentConfig.id, versionId: v.id }, () => refreshVersionsList());
      acts.appendChild(restore);
      acts.appendChild(del);
      item.appendChild(acts);
      el.appendChild(item);
    }
  });
}

// ---------- 事件绑定 ----------
document.getElementById("workspaceSelect").onchange = (e) => {
  currentWorkspaceId = e.target.value;
  clearOpenConfig();
  renderConfigTree();
  refreshConfigs();
  fillWizardWorkspace();
};

document.getElementById("btnNewConfig").onclick = () => openWizard();
document.getElementById("wzPluginSearch").oninput = renderWizardPlugins;
document.getElementById("wzCancel").onclick = () => closeModal("wizardModal");
document.getElementById("wzBack").onclick = () => goWizardStep(wizard.step - 1);
document.getElementById("wzCreate").onclick = submitCreate;

document.getElementById("btnTemplates").onclick = () => {
  if (!currentConfig) { setStatus("请先打开配置", true); return; }
  renderTemplateList();
  openModal("templateModal");
};
document.getElementById("btnTemplateCancel").onclick = () => closeModal("templateModal");

document.getElementById("btnVersions").onclick = () => {
  if (!currentConfig) { setStatus("请先打开配置", true); return; }
  refreshVersionsList();
  openModal("versionsModal");
};
document.getElementById("btnSnapshot").onclick = () => {
  const note = document.getElementById("versionNote").value.trim() || undefined;
  send("version:snapshot", { note }, (d) => {
    if (okOr(d)) { document.getElementById("versionNote").value = ""; refreshVersionsList(); showToast("已留档"); }
  });
};

document.getElementById("btnArchive").onclick = () => openModal("archiveModal");
document.getElementById("btnExportWs").onclick = () => exportArchive("archive:exportWorkspace");
document.getElementById("btnExportCfg").onclick = () => exportArchive("archive:exportConfig");
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
    refreshWorkspaces();
    showToast("存档导入完成");
  });
};

function exportArchive(action) {
  const path = document.getElementById("archivePath").value;
  if (!path) { setStatus("请填写存档包路径", true); return; }
  const payload = { path, workspaceId: currentWorkspaceId };
  if (action === "archive:exportConfig") {
    if (!currentConfig) { setStatus("当前没有打开的配置", true); return; }
    payload.configId = currentConfig.id;
  }
  send(action, payload, (d) => {
    if (okOr(d)) { document.getElementById("archiveResult").textContent = `已导出：${d.path}`; showToast("存档导出完成"); }
  });
}

document.getElementById("btnReset").onclick = () => {
  if (!currentConfig) { setStatus("当前没有打开的配置", true); return; }
  if (!window.confirm("清空当前配置并恢复默认值？")) return;
  send("config:reset", {}, (d) => { if (okOr(d)) { applyFormUpdate(d); showToast("已清空配置并恢复默认值"); } });
};

document.getElementById("btnValidate").onclick = () =>
  send("form:validate", null, (d) => { if (okOr(d)) applyFormUpdate(d); });

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

document.getElementById("btnSettings").onclick = () => {
  send("logs:path", null, (d) => {
    if (d.ok) document.getElementById("settingsLogPath").textContent = d.path;
  });
  openModal("settingsModal");
};
document.getElementById("btnOpenLogs").onclick = () => send("logs:open", null, (d) => { okOr(d); });
document.getElementById("btnSettingsClose").onclick = () => closeModal("settingsModal");

document.getElementById("btnCollapseAll").onclick = () => {
  snapshot && collectPaths(snapshot).forEach(p => collapsedPaths.add(p));
  renderForm();
};
document.getElementById("btnExpandAll").onclick = () => {
  collapsedPaths.clear();
  renderForm();
};
document.getElementById("btnCollapseDisabled").onclick = () => {
  snapshot && collectPaths(snapshot).forEach(p => {
    const node = findNodeByPath(snapshot, p);
    if (node && !node.isEnabled) collapsedPaths.add(p);
  });
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

function findNodeByPath(nodes, path) {
  for (const n of nodes) {
    if (n.path === path) return n;
    const found = findNodeByPath(n.children || [], path);
    if (found) return found;
  }
  return null;
}

// ---------- 初始化 ----------
function init() {
  send("bootstrap", null, (data) => {
    plugins = data.plugins || [];
    if (data.loadErrors && data.loadErrors.length) {
      setStatus(`插件加载 ${data.loadErrors.length} 个失败：${data.loadErrors[0]}`, true);
    }
    refreshWorkspaces();
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
    await step("bootstrap", "bootstrap");
    const wsData = await step("workspace:create", "workspace:create", { name: "自检工作空间" });
    const wsId = wsData.workspace.id;
    const cfgData = await step("config:create", "config:create",
      { workspaceId: wsId, pluginKey: "Nginx", name: "selfcheck.conf" });
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
    await step("archive:exportWs", "archive:exportWorkspace", { workspaceId: wsId, path: "SELFCHECK" });
    await step("archive:import", "archive:import", { path: "SELFCHECK" });
    await step("versions:list", "versions:list", { workspaceId: wsId, configId: cfgId });

    const worst = Math.max(...steps.map(s => s.ms));
    const interactiveMs = steps
      .filter(s => !s.name.startsWith("archive:"))
      .map(s => s.ms);
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
