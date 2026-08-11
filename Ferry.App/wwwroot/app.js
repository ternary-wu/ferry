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
let selectedTemplate = null;
let latencySamples = [];
let requestSeq = 0;
let moduleTreeFilter = "";
let previewWidth = 420;
const inflight = new Map();

// ---------- IPC（requestId 配对，容忍 Photino 乱序/重复交付） ----------
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
  const req = Object.assign({ action, requestId }, payload || {});
  window.external.sendMessage(JSON.stringify(req));
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
    const reqId = data.requestId;
    const item = reqId ? inflight.get(reqId) : null;
    if (!item) return;
    clearTimeout(item.timer);
    inflight.delete(reqId);
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

// ---------- 通用 UI ----------
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
  showToast._timer = setTimeout(() => toast.classList.remove("show"), 2000);
}

function textSpan(text, cls) {
  const el = document.createElement("span");
  el.className = cls || "hint";
  el.textContent = text;
  return el;
}

// 结构性变更：应用快照并全量重渲染
function applyFormUpdate(data) {
  if (!data) return;
  if (data.snapshot) { snapshot = data.snapshot; renderForm(); renderModuleTree(); }
  if (data.text !== undefined && data.text !== null) { sourceText = data.text; renderPreview(); }
  if (data.errors) {
    errors = data.errors;
    setStatus(errors.length === 0 ? "校验通过" : `校验：${errors.length} 个错误`, errors.length > 0);
  }
  if (data.unrecognized) unrecognized = data.unrecognized;
}

// 值修改：更新状态与预览，不重建输入框（避免失焦）
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

// ---------- 工作空间 / 配置 ----------
function refreshWorkspaces() {
  send("workspaces:list", null, (data) => {
    workspaces = data.workspaces || [];
    const sel = document.getElementById("workspaceSelect");
    sel.innerHTML = "";
    for (const ws of workspaces) {
      const opt = document.createElement("option");
      opt.value = ws.id;
      opt.textContent = ws.name;
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
    const ws = workspaces.find(w => w.id === currentWorkspaceId);
    document.getElementById("crumbWs").textContent = ws ? ws.name : "—";
    refreshConfigs();
  });
}

function refreshConfigs() {
  if (!currentWorkspaceId) return;
  send("configs:list", { workspaceId: currentWorkspaceId }, (data) => {
    configs = data.configs || [];
    renderConfigList();
  });
}

function renderConfigList() {
  const el = document.getElementById("configList");
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
    el.appendChild(textSpan(pluginName));
    for (const cfg of items) {
      const item = document.createElement("div");
      item.className = "config-item" + (currentConfig && cfg.id === currentConfig.id ? " active" : "");
      const name = document.createElement("span");
      name.className = "name";
      name.textContent = cfg.name;
      name.title = cfg.name;
      item.appendChild(name);
      const badge = document.createElement("span");
      badge.className = "badge" + (cfg.pluginMissing ? " missing" : "");
      badge.textContent = cfg.pluginMissing ? "插件缺失" : cfg.pluginVersion;
      item.appendChild(badge);
      const del = document.createElement("button");
      del.className = "icon-btn danger";
      del.textContent = "✕";
      del.onclick = (e) => { e.stopPropagation(); deleteConfig(cfg); };
      item.appendChild(del);
      item.onclick = () => openConfig(cfg);
      el.appendChild(item);
    }
  }
}

function deleteConfig(cfg) {
  if (!window.confirm(`删除配置「${cfg.name}」？`)) return;
  send("config:delete", { workspaceId: currentWorkspaceId, configId: cfg.id }, (data) => {
    okOr(data);
    currentConfig = null; snapshot = null; sourceText = "";
    renderForm(); renderModuleTree(); refreshConfigs();
  });
}

function openConfig(cfg) {
  send("config:open", { workspaceId: currentWorkspaceId, configId: cfg.id }, (data) => {
    if (!okOr(data)) return;
    currentConfig = data.config;
    snapshot = data.snapshot || [];
    sourceText = data.sourceText || "";
    errors = data.errors || [];
    unrecognized = data.unrecognized || [];
    templates = data.templates || [];
    document.getElementById("crumbConfig").textContent =
      `${data.config.name} · ${data.config.pluginName}`;
    document.getElementById("formTitle").textContent = data.config.name;
    document.getElementById("formSubtitle").textContent =
      `插件 ${data.config.pluginName} v${data.config.pluginVersion}${data.versionChanged ? "（字段可能有增减）" : ""}`;
    renderConfigList(); renderForm(); renderModuleTree(); renderPreview(); renderTemplates(); refreshVersions();
    setStatus(data.pluginMissing ? "插件缺失：仅可查看/导出源码" : `已打开：${data.config.name}`, !!data.pluginMissing);
  });
}

function refreshVersions() {
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
      item.className = "version-item";
      const t = document.createElement("span");
      t.className = "t";
      t.textContent = `${v.timestamp}${v.note ? " · " + v.note : ""}（${v.length} 字符）`;
      item.appendChild(t);
      item.title = v.preview;
      item.onclick = () => {
        if (!window.confirm("回滚到该版本？当前表单将被该版本源码重建。")) return;
        send("version:restore", { workspaceId: currentWorkspaceId, configId: currentConfig.id, versionId: v.id }, (data) => {
          if (!okOr(data)) return;
          currentConfig = data.config;
          snapshot = data.snapshot || [];
          sourceText = data.sourceText || "";
          errors = data.errors || [];
          renderForm(); renderModuleTree(); renderPreview(); refreshVersions();
          setStatus("已回滚到该版本", false);
        });
      };
      const del = document.createElement("button");
      del.className = "icon-btn danger";
      del.textContent = "✕";
      del.onclick = (e) => {
        e.stopPropagation();
        send("version:delete", { workspaceId: currentWorkspaceId, configId: currentConfig.id, versionId: v.id }, () => refreshVersions());
      };
      item.appendChild(del);
      el.appendChild(item);
    }
  });
}

// ---------- 表单（卡片式） ----------
function renderForm() {
  const root = document.getElementById("formRoot");
  root.textContent = "";
  if (!snapshot) return;
  for (const node of snapshot) root.appendChild(renderCard(node, 0));
  updateModuleStatus();
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

function renderCard(node, depth) {
  if (!node.isVisible) return document.createDocumentFragment();
  if (node.isModule || node.type === "Object") {
    const card = document.createElement("div");
    card.className = "form-card" + (node.isEnabled ? "" : " disabled");
    card.dataset.path = node.path;

    const head = document.createElement("div");
    head.className = "form-card-header";
    const group = document.createElement("div");
    group.className = "form-card-title-group";
    const icon = document.createElement("span");
    icon.className = "form-card-icon";
    icon.textContent = node.isModule ? "☰" : "▤";
    group.appendChild(icon);
    const titleBox = document.createElement("div");
    titleBox.style.flex = "1";
    const title = document.createElement("div");
    title.className = "form-card-title";
    title.textContent = node.label || node.id;
    titleBox.appendChild(title);
    if (node.description) {
      const sub = document.createElement("div");
      sub.className = "form-card-subtitle";
      sub.textContent = node.description;
      titleBox.appendChild(sub);
    }
    group.appendChild(titleBox);
    head.appendChild(group);

    if (!node.required) {
      head.appendChild(renderEnableControl(node));
      if (node.enabledChildModulesText) {
        const count = document.createElement("span");
        count.className = "form-card-count";
        count.textContent = node.enabledChildModulesText;
        head.appendChild(count);
      }
    } else {
      head.appendChild(renderLockBadge());
    }
    const toggle = document.createElement("span");
    toggle.className = "form-card-toggle";
    toggle.textContent = "▶";
    head.appendChild(toggle);
    head.onclick = () => card.classList.toggle("collapsed");
    card.appendChild(head);

    const body = document.createElement("div");
    body.className = "form-card-body";
    for (const child of node.children || []) body.appendChild(renderCard(child, depth + 1));
    card.appendChild(body);
    return card;
  }

  if (node.type === "Array") {
    const box = document.createElement("div");
    const arrayHead = document.createElement("div");
    arrayHead.style.display = "flex";
    arrayHead.style.alignItems = "center";
    arrayHead.style.gap = "8px";
    const label = document.createElement("div");
    label.className = "form-field-name";
    label.textContent = node.label || node.id;
    label.title = node.description || "";
    arrayHead.appendChild(node.required ? renderLockBadge() : renderEnableControl(node));
    arrayHead.appendChild(label);
    box.appendChild(arrayHead);
    for (const item of node.children || []) {
      const itemBox = document.createElement("div");
      itemBox.className = "array-item";
      const itemHead = document.createElement("div");
      itemHead.className = "array-item-head";
      const title = document.createElement("span");
      title.className = "title";
      title.textContent = item.label || item.id;
      itemHead.appendChild(title);
      const remove = document.createElement("button");
      remove.className = "icon-btn danger";
      remove.textContent = "✕";
      remove.onclick = () => send("form:removeItem", { path: item.path }, applyFormUpdate);
      itemHead.appendChild(remove);
      itemHead.onclick = () => itemBox.classList.toggle("collapsed");
      itemBox.appendChild(itemHead);
      const body = document.createElement("div");
      body.className = "array-item-body";
      for (const child of item.children || []) body.appendChild(renderCard(child, depth + 1));
      itemBox.appendChild(body);
      box.appendChild(itemBox);
    }
    const add = document.createElement("button");
    add.className = "array-add-btn";
    add.textContent = "＋ 添加项";
    add.onclick = () => send("form:addItem", { path: node.path }, applyFormUpdate);
    box.appendChild(add);
    return box;
  }

  return renderField(node);
}

function renderField(node) {
  const field = document.createElement("div");
  field.className = "form-field" + (node.isEnabled ? "" : " disabled");
  const row = document.createElement("div");
  row.className = "form-field-row";
  row.appendChild(node.required ? renderLockBadge() : renderEnableControl(node));
  const label = document.createElement("div");
  label.className = "form-field-label";
  const name = document.createElement("div");
  name.className = "form-field-name";
  name.textContent = node.label || node.id;
  label.appendChild(name);
  if (node.description) {
    const desc = document.createElement("div");
    desc.className = "form-field-desc";
    desc.textContent = node.description;
    label.appendChild(desc);
  }
  row.appendChild(label);
  const control = document.createElement("div");
  control.className = "form-field-control";

  const setValue = (value) => send("form:setValue", { path: node.path, value }, applyLightUpdate);

  switch (node.type) {
    case "String": {
      const input = document.createElement("input");
      input.className = "text-input";
      input.type = "text";
      input.value = node.value ?? "";
      input.onchange = () => setValue(input.value);
      control.appendChild(input);
      break;
    }
    case "Number": {
      const input = document.createElement("input");
      input.className = "number-input";
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
      toggle.className = "toggle-switch" + (node.value === true ? " active" : "");
      toggle.onclick = () => setValue(node.value !== true);
      control.appendChild(toggle);
      break;
    }
    case "Enum": {
      const select = document.createElement("select");
      select.className = "select-input";
      for (const opt of node.enumOptions || []) {
        const el = document.createElement("option");
        el.value = opt.value;
        el.textContent = opt.value + (opt.description ? `（${opt.description}）` : "");
        select.appendChild(el);
      }
      if (node.allowCustomValue) {
        const custom = document.createElement("input");
        custom.className = "text-input";
        custom.type = "text";
        custom.placeholder = "自定义值";
        custom.value = (node.value ?? "").toString();
        custom.onchange = () => setValue(custom.value);
        select.onchange = () => setValue(select.value);
        control.appendChild(select);
        custom.style.marginTop = "4px";
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
    err.className = "field-error";
    err.textContent = node.validationError;
    control.appendChild(err);
  }
  row.appendChild(control);
  field.appendChild(row);
  return field;
}

/// 字段启用勾选框：所有非必填字段（含标量）默认可取消；必填字段显示锁定标记。
function renderEnableControl(node) {
  const box = document.createElement("span");
  box.className = "tree-checkbox" + (node.isEnabled ? " checked" : "");
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

function renderLockBadge() {
  const lock = document.createElement("span");
  lock.className = "tree-checkbox locked";
  lock.textContent = "🔒";
  lock.title = "必填字段不可取消";
  return lock;
}

// ---------- 模块树 ----------
function renderModuleTree() {
  const el = document.getElementById("moduleTree");
  el.textContent = "";
  if (!snapshot) {
    el.appendChild(textSpan("打开配置后显示模块树"));
    return;
  }
  const filter = moduleTreeFilter.trim().toLowerCase();
  for (const node of snapshot) {
    el.appendChild(renderTreeBranch(node, filter, 0, filter !== ""));
  }
}

function renderTreeBranch(node, filter, depth, forceOpen) {
  const wrap = document.createElement("div");
  wrap.className = "tree-node";
  const hasModules = (node.children || []).some(c => c.isModule || (c.children || []).length > 0);
  const selfMatch = filter && (node.label || "").toLowerCase().includes(filter) || (node.id || "").toLowerCase().includes(filter);
  const childMatch = (node.children || []).some(c => c.isModule || (c.children || []).length > 0);
  const show = !filter || selfMatch || childMatch;
  if (!node.isModule && !show) return wrap;
  if (!node.isModule && !(node.children || []).some(c => c.isModule)) return wrap;

  const label = document.createElement("div");
  label.className = "tree-node-label" + (node.isEnabled ? "" : " disabled");
  const toggle = document.createElement("span");
  toggle.className = "tree-toggle" + (hasModules ? "" : " leaf");
  toggle.textContent = "▶";
  label.appendChild(toggle);
  if (node.isModule) {
    const box = document.createElement("span");
    box.className = "tree-checkbox" + (node.isEnabled ? " checked" : "");
    box.onclick = (e) => {
      e.stopPropagation();
      if (!node.canToggleEnabled) return;
      send("form:toggle", { path: node.path, enabled: !node.isEnabled }, applyFormUpdate);
    };
    label.appendChild(box);
  }
  const text = document.createElement("span");
  text.className = "tree-label-text";
  text.textContent = (node.isModule ? "☰ " : "▤ ") + (node.label || node.id);
  label.appendChild(text);
  if (node.isModule && node.enabledChildModulesText) {
    const count = document.createElement("span");
    count.className = "tree-count";
    count.textContent = node.enabledChildModulesText;
    label.appendChild(count);
  }
  wrap.appendChild(label);

  const children = document.createElement("div");
  children.className = "tree-children" + (forceOpen ? "" : " collapsed");
  label.onclick = () => children.classList.toggle("collapsed");
  for (const child of node.children || []) {
    const branch = renderTreeBranch(child, filter, depth + 1, forceOpen);
    if (branch.childElementCount > 0) children.appendChild(branch);
  }
  wrap.appendChild(children);
  return wrap;
}

// ---------- 预览 / 源码 ----------
function renderPreview() {
  if (previewTab === "preview") {
    document.getElementById("previewCode").innerHTML = highlightConfig(sourceText || "");
  }
}

function highlightConfig(text) {
  let html = text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  html = html.replace(/\b(http|server|location|events|upstream|stream|mail|if)\b/g, '<span class="kw-block">$1</span>');
  html = html.replace(/\b(\d+(?:[a-z]+)?)\b/g, '<span class="kw-number">$1</span>');
  html = html.replace(/(#[^\n]*)/g, '<span class="kw-comment">$1</span>');
  return html;
}

function switchPreviewTab(tab) {
  previewTab = tab;
  document.querySelectorAll(".preview-tab").forEach(t => t.classList.toggle("active", t.dataset.tab === tab));
  document.getElementById("pane-preview").classList.toggle("hidden", tab !== "preview");
  document.getElementById("pane-source").classList.toggle("hidden", tab !== "source");
  if (tab === "source") {
    document.getElementById("sourceEditor").value = sourceText;
  } else {
    renderPreview();
  }
}

// ---------- 模板弹窗 ----------
function renderTemplates() {
  const grid = document.getElementById("templateGrid");
  grid.textContent = "";
  const blank = document.createElement("div");
  blank.className = "template-card" + (selectedTemplate === "__blank" ? " selected" : "");
  blank.dataset.template = "__blank";
  blank.innerHTML = "<h3>空白开始</h3><p>不套用任何模板，手动配置</p>";
  blank.onclick = () => selectTemplateCard(blank);
  grid.appendChild(blank);
  for (const t of templates) {
    const card = document.createElement("div");
    card.className = "template-card" + (selectedTemplate === t.id ? " selected" : "");
    card.dataset.template = t.id;
    const h3 = document.createElement("h3");
    h3.textContent = t.name;
    const p = document.createElement("p");
    p.textContent = t.description || "场景模板";
    card.appendChild(h3);
    card.appendChild(p);
    card.onclick = () => selectTemplateCard(card);
    grid.appendChild(card);
  }
}

function selectTemplateCard(card) {
  selectedTemplate = card.dataset.template;
  document.querySelectorAll(".template-card").forEach(c => c.classList.toggle("selected", c === card));
}

function openTemplateModal() {
  selectedTemplate = null;
  renderTemplates();
  document.getElementById("templateModal").classList.add("open");
}

function closeTemplateModal() {
  document.getElementById("templateModal").classList.remove("open");
}

// ---------- 事件绑定 ----------
document.querySelectorAll(".sidebar-tab").forEach(tab => {
  tab.onclick = () => {
    document.querySelectorAll(".sidebar-tab").forEach(t => t.classList.toggle("active", t === tab));
    ["configs", "modules", "versions", "archive"].forEach(id =>
      document.getElementById("tab-" + id).hidden = tab.dataset.tab !== id);
  };
});

document.getElementById("workspaceSelect").onchange = (e) => {
  currentWorkspaceId = e.target.value;
  currentConfig = null; snapshot = null; sourceText = "";
  document.getElementById("crumbConfig").textContent = "（未打开配置）";
  document.getElementById("formTitle").textContent = "Ferry";
  document.getElementById("formSubtitle").textContent = "从左侧选择配置开始编辑";
  renderForm(); renderModuleTree(); renderPreview();
  const ws = workspaces.find(w => w.id === currentWorkspaceId);
  document.getElementById("crumbWs").textContent = ws ? ws.name : "—";
  refreshConfigs();
};

document.getElementById("btnNewWs").onclick = () => {
  const name = window.prompt("工作空间名称：");
  if (!name) return;
  send("workspace:create", { name }, (d) => {
    if (okOr(d)) { currentWorkspaceId = d.workspace.id; refreshWorkspaces(); }
  });
};

document.getElementById("btnRenameWs").onclick = () => {
  if (!currentWorkspaceId) return;
  const ws = workspaces.find(w => w.id === currentWorkspaceId);
  const name = window.prompt("新的工作空间名称：", ws ? ws.name : "");
  if (!name) return;
  send("workspace:rename", { id: currentWorkspaceId, name }, (d) => { if (okOr(d)) refreshWorkspaces(); });
};

document.getElementById("btnDeleteWs").onclick = () => {
  if (!currentWorkspaceId) return;
  if (!window.confirm("删除整个工作空间及其全部配置与版本？")) return;
  send("workspace:delete", { id: currentWorkspaceId }, (d) => {
    okOr(d);
    currentWorkspaceId = ""; currentConfig = null; snapshot = null; sourceText = "";
    refreshWorkspaces();
  });
};

document.getElementById("btnNewConfig").onclick = () => {
  if (!currentWorkspaceId) return;
  const pluginKey = document.getElementById("newConfigPlugin").value;
  const name = document.getElementById("newConfigName").value.trim() || undefined;
  if (!pluginKey) { setStatus("请选择插件", true); return; }
  send("config:create", { workspaceId: currentWorkspaceId, pluginKey, name }, (d) => {
    if (!okOr(d)) return;
    document.getElementById("newConfigName").value = "";
    showToast("已创建配置");
    refreshConfigs();
  });
};

document.getElementById("btnTemplates").onclick = openTemplateModal;
document.getElementById("btnTemplateCancel").onclick = closeTemplateModal;
document.getElementById("templateModal").addEventListener("click", (e) => {
  if (e.target.id === "templateModal") closeTemplateModal();
});
document.getElementById("btnTemplateApply").onclick = () => {
  if (!selectedTemplate) { setStatus("请先选择一个模板", true); return; }
  if (selectedTemplate === "__blank") {
    send("config:reset", {}, (d) => {
      if (okOr(d)) { applyFormUpdate(d); closeTemplateModal(); showToast("已重置为空白配置"); }
    });
  } else {
    send("form:applyPreset", { preset: selectedTemplate }, (d) => {
      if (okOr(d)) { applyFormUpdate(d); closeTemplateModal(); showToast("已应用模板"); }
    });
  }
};

document.getElementById("btnReset").onclick = () => {
  if (!currentConfig) { setStatus("当前没有打开的配置", true); return; }
  if (!window.confirm("清空当前配置并恢复默认值？")) return;
  send("config:reset", {}, (d) => {
    if (okOr(d)) { applyFormUpdate(d); showToast("已清空配置并恢复默认值"); }
  });
};

document.getElementById("btnValidate").onclick = () =>
  send("form:validate", null, (d) => { if (okOr(d)) applyFormUpdate(d); });

document.getElementById("btnPreview").onclick = () =>
  send("form:render", null, (d) => {
    if (!okOr(d)) return;
    sourceText = d.text || "";
    renderPreview();
    setStatus("已生成预览", false);
  });

document.querySelectorAll(".preview-tab").forEach(tab => {
  tab.onclick = () => switchPreviewTab(tab.dataset.tab);
});

document.getElementById("btnApplyEdit").onclick = () => {
  const text = document.getElementById("sourceEditor").value;
  send("form:importText", { text }, (d) => {
    if (!okOr(d)) return;
    applyFormUpdate(d);
    const report = document.getElementById("unrecognizedReport");
    report.textContent = d.report && d.report.unrecognizedLines > 0
      ? `未识别内容 ${d.report.unrecognizedLines} 行（已保留，导出时可选追加）`
      : "";
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
  send("config:exportTo", { path }, (d) => {
    if (okOr(d)) showToast(`已导出：${d.path}`);
  });
};

document.getElementById("btnSnapshot").onclick = () => {
  if (!currentConfig) { setStatus("当前没有打开的配置", true); return; }
  const note = document.getElementById("versionNote").value.trim() || undefined;
  send("version:snapshot", { note }, (d) => {
    if (okOr(d)) { document.getElementById("versionNote").value = ""; showToast("已留档"); refreshVersions(); }
  });
};

document.getElementById("btnExportWs").onclick = () => exportArchive("archive:exportWorkspace");
document.getElementById("btnExportCfg").onclick = () => exportArchive("archive:exportConfig");

function exportArchive(action) {
  const path = document.getElementById("archivePath").value;
  if (!path) { setStatus("请填写存档包路径", true); return; }
  const payload = { path, workspaceId: currentWorkspaceId };
  if (action === "archive:exportConfig") {
    if (!currentConfig) { setStatus("当前没有打开的配置", true); return; }
    payload.configId = currentConfig.id;
  }
  send(action, payload, (d) => {
    if (okOr(d)) {
      document.getElementById("archiveResult").textContent = `已导出：${d.path}`;
      showToast("存档导出完成");
    }
  });
}

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

document.getElementById("btnLogs").onclick = () => send("logs:open", null, (d) => { okOr(d); });
document.getElementById("moduleSearch").oninput = (e) => {
  moduleTreeFilter = e.target.value;
  renderModuleTree();
};

// 预览宽度拖拽
document.getElementById("previewResizer").addEventListener("mousedown", (e) => {
  e.preventDefault();
  const startX = e.clientX;
  const startWidth = document.getElementById("previewPanel").offsetWidth;
  const onMove = (ev) => {
    const width = Math.max(320, Math.min(window.innerWidth * 0.6, startWidth - (ev.clientX - startX)));
    document.getElementById("previewPanel").style.width = width + "px";
  };
  const onUp = () => {
    document.removeEventListener("mousemove", onMove);
    document.removeEventListener("mouseup", onUp);
  };
  document.addEventListener("mousemove", onMove);
  document.addEventListener("mouseup", onUp);
});

// ---------- 初始化 ----------
function init() {
  send("bootstrap", null, (data) => {
    plugins = data.plugins || [];
    const sel = document.getElementById("newConfigPlugin");
    for (const p of plugins) {
      const opt = document.createElement("option");
      opt.value = p.key;
      opt.textContent = p.name + (p.version ? ` v${p.version}` : "");
      sel.appendChild(opt);
    }
    sel.onchange = () => {
      const plugin = plugins.find(p => p.key === sel.value);
      if (plugin) document.getElementById("newConfigName").placeholder = `配置名（默认=${plugin.defaultFileName}）`;
    };
    if (data.loadErrors && data.loadErrors.length) {
      setStatus(`插件加载 ${data.loadErrors.length} 个失败：${data.loadErrors[0]}`, true);
    }
    send("logs:path", null, (d) => {
      if (d.ok) document.getElementById("logPath").textContent = d.path;
    });
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
    window.external.sendMessage(JSON.stringify({
      action: "spike:result",
      ok: !failed && worst < 50,
      failed,
      worstMs: worst,
      steps
    }));
  })();
}

init();
