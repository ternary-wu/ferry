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
let latencySamples = [];
let requestSeq = 0;
const inflight = new Map();

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
    if (!item) return; // 孤儿/重复响应：忽略
    clearTimeout(item.timer);
    inflight.delete(reqId);
    const elapsed = performance.now() - item.t0;
    latencySamples.push({ action: item.action, ms: elapsed });
    if (data.latencyMs !== undefined) {
      document.getElementById("latency").textContent =
        `IPC ${data.latencyMs.toFixed(1)}ms · 最近 ${elapsed.toFixed(1)}ms`;
    }
    if (item.onOk) item.onOk(data);
  } catch (e) {
    log("receive-error:" + e.message);
  }
});

function okOr(data, fallback) {
  if (!data.ok) {
    setStatus((data.errors || ["操作失败"]).join("；"), true);
    return fallback;
  }
  return data;
}

function setStatus(text, isError) {
  const el = document.getElementById("statusText");
  el.textContent = text;
  el.className = isError ? "bad" : "ok";
}

// ---------- 工作空间 / 配置 ----------
function refreshWorkspaces() {
  send("workspaces:list", null, (data) => {
    workspaces = data.workspaces || [];
    const sel = document.getElementById("workspaceSelect");
    sel.innerHTML = "";
    for (const ws of workspaces) {
      const opt = document.createElement("option");
      opt.value = ws.id; opt.textContent = ws.name;
      sel.appendChild(opt);
    }
    if (workspaces.length === 0) {
      send("workspace:create", { name: "默认工作空间" }, (d) => {
        if (d.ok) refreshWorkspaces();
      });
      return;
    }
    if (!currentWorkspaceId || !workspaces.some(w => w.id === currentWorkspaceId)) {
      currentWorkspaceId = workspaces[0].id;
    }
    sel.value = currentWorkspaceId;
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
  el.innerHTML = "";
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
    const title = document.createElement("div");
    title.className = "hint";
    title.textContent = pluginName;
    el.appendChild(title);
    for (const cfg of items) {
      const item = document.createElement("div");
      item.className = "config-item" + (currentConfig && cfg.id === currentConfig.id ? " active" : "");
      const name = document.createElement("span");
      name.className = "name";
      name.textContent = cfg.name;
      name.title = cfg.name;
      item.appendChild(name);
      if (cfg.pluginMissing) {
        const badge = document.createElement("span");
        badge.className = "badge missing";
        badge.textContent = "插件缺失";
        item.appendChild(badge);
      } else {
        const badge = document.createElement("span");
        badge.className = "badge";
        badge.textContent = cfg.pluginVersion;
        item.appendChild(badge);
      }
      const del = document.createElement("button");
      del.textContent = "✕";
      del.className = "danger";
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
    refreshConfigs();
    renderForm();
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
    renderConfigList();
    renderForm();
    renderPreview();
    renderTemplates();
    refreshVersions();
    setStatus(data.versionChanged
      ? `该配置创建于插件 v${currentConfig.pluginVersion}，当前为 v${currentConfig.pluginVersion}，字段可能有增减`
      : `已打开：${currentConfig.name}`, false);
    if (data.pluginMissing) {
      setStatus("插件缺失：仅可查看/导出源码", true);
    }
  });
}

function refreshVersions() {
  if (!currentConfig) {
    document.getElementById("versionList").textContent = "打开配置后可留档";
    return;
  }
  send("versions:list", { workspaceId: currentWorkspaceId, configId: currentConfig.id }, (data) => {
    const el = document.getElementById("versionList");
    el.innerHTML = "";
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
        send("version:restore", { workspaceId: currentWorkspaceId, configId: currentConfig.id, versionId: v.id },
          (data) => {
            if (!okOr(data)) return;
            currentConfig = data.config;
            snapshot = data.snapshot || [];
            sourceText = data.sourceText || "";
            errors = data.errors || [];
            renderForm(); renderPreview(); refreshVersions();
            setStatus("已回滚到该版本", false);
          });
      };
      const del = document.createElement("button");
      del.textContent = "✕";
      del.className = "danger";
      del.onclick = (e) => {
        e.stopPropagation();
        send("version:delete", { workspaceId: currentWorkspaceId, configId: currentConfig.id, versionId: v.id },
          () => refreshVersions());
      };
      item.appendChild(del);
      el.appendChild(item);
    }
  });
}

// ---------- 表单渲染 ----------
function renderForm() {
  const root = document.getElementById("form");
  root.textContent = "";
  if (!snapshot) {
    root.appendChild(textSpan("从左侧选择配置开始编辑"));
    return;
  }
  for (const node of snapshot) root.appendChild(renderNode(node, 0));
  const enabledCount = countEnabled(snapshot);
  const totalCount = countTotal(snapshot);
  document.getElementById("moduleStatus").textContent =
    `${enabledCount}/${totalCount} 模块已启用`;
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

function renderNode(node, depth) {
  if (!node.isVisible) return document.createDocumentFragment();
  const wrap = document.createElement("div");
  wrap.className = "field" + (node.isEnabled ? "" : " disabled");
  wrap.dataset.path = node.path;

  if (node.isModule) {
    const box = document.createElement("input");
    box.type = "checkbox";
    box.checked = node.isEnabled;
    box.disabled = !node.canToggleEnabled;
    box.title = node.canToggleEnabled ? "" : "父级未启用时锁定";
    box.onchange = () => send("form:toggle", { path: node.path, enabled: box.checked });
    wrap.appendChild(box);
  }

  const label = document.createElement("label");
  label.textContent = node.label || node.id;
  label.title = node.description || "";
  wrap.appendChild(label);

  if (node.validationError) {
    const err = document.createElement("span");
    err.className = "error";
    err.textContent = node.validationError;
    wrap.appendChild(err);
  }

  switch (node.type) {
    case "String": {
      const input = document.createElement("input");
      input.type = "text";
      input.value = node.value ?? "";
      input.onchange = () => send("form:setValue", { path: node.path, value: input.value });
      wrap.appendChild(input);
      break;
    }
    case "Number": {
      const input = document.createElement("input");
      input.type = "number";
      input.value = node.value ?? "";
      input.min = node.min ?? "";
      input.max = node.max ?? "";
      input.step = node.integerOnly ? "1" : "any";
      input.onchange = () => send("form:setValue", { path: node.path, value: input.value });
      wrap.appendChild(input);
      break;
    }
    case "Boolean": {
      const box = document.createElement("input");
      box.type = "checkbox";
      box.checked = node.value === true;
      box.onchange = () => send("form:setValue", { path: node.path, value: box.checked });
      wrap.appendChild(box);
      break;
    }
    case "Enum": {
      const select = document.createElement("select");
      const options = node.enumOptions || [];
      for (const opt of options) {
        const el = document.createElement("option");
        el.value = opt.value;
        el.textContent = opt.value + (opt.description ? `（${opt.description}）` : "");
        select.appendChild(el);
      }
      const apply = (v) => send("form:setValue", { path: node.path, value: v });
      if (node.allowCustomValue) {
        const custom = document.createElement("input");
        custom.type = "text";
        custom.placeholder = "自定义值";
        custom.value = (node.value ?? "").toString();
        custom.onchange = () => apply(custom.value);
        select.onchange = () => apply(select.value);
        wrap.appendChild(select);
        wrap.appendChild(custom);
      } else {
        select.value = (node.value ?? "").toString();
        select.onchange = () => apply(select.value);
        wrap.appendChild(select);
      }
      break;
    }
    case "Array": {
      const add = document.createElement("button");
      add.textContent = "＋ 添加项";
      add.onclick = () => send("form:addItem", { path: node.path });
      wrap.appendChild(add);
      for (const child of node.children || []) {
        const item = document.createElement("div");
        item.className = "item";
        const remove = document.createElement("button");
        remove.textContent = "✕";
        remove.onclick = () => send("form:removeItem", { path: child.path });
        item.appendChild(remove);
        item.appendChild(renderNode(child, depth + 1));
        wrap.appendChild(item);
      }
      break;
    }
    case "Object":
      for (const child of node.children || []) wrap.appendChild(renderNode(child, depth + 1));
      break;
  }
  return wrap;
}

// ---------- 预览 / 源码 ----------
function renderPreview() {
  if (previewTab === "preview") {
    document.getElementById("text").textContent = sourceText || "（点击 预览 生成）";
  }
}

function switchTab(tab) {
  previewTab = tab;
  document.getElementById("tabPreview").classList.toggle("active", tab === "preview");
  document.getElementById("tabSource").classList.toggle("active", tab === "source");
  document.getElementById("text").hidden = tab !== "preview";
  document.getElementById("editor").hidden = tab !== "source";
  if (tab === "source") {
    document.getElementById("editor").value = sourceText;
  }
}

function renderTemplates() {
  const sel = document.getElementById("presetSelect");
  sel.innerHTML = '<option value="">模板…</option>';
  for (const t of templates) {
    const opt = document.createElement("option");
    opt.value = t.id;
    opt.textContent = t.name;
    opt.title = t.description || "";
    sel.appendChild(opt);
  }
}

function textSpan(text) {
  const el = document.createElement("span");
  el.className = "hint";
  el.textContent = text;
  return el;
}

// ---------- 事件绑定 ----------
document.getElementById("workspaceSelect").onchange = (e) => {
  currentWorkspaceId = e.target.value;
  currentConfig = null; snapshot = null; sourceText = "";
  renderForm(); refreshConfigs();
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
  send("workspace:rename", { id: currentWorkspaceId, name }, (d) => {
    if (okOr(d)) refreshWorkspaces();
  });
};

document.getElementById("btnDeleteWs").onclick = () => {
  if (!currentWorkspaceId) return;
  if (!window.confirm("删除整个工作空间及其全部配置与版本？")) return;
  send("workspace:delete", { id: currentWorkspaceId }, (d) => {
    okOr(d);
    currentWorkspaceId = "";
    currentConfig = null; snapshot = null; sourceText = "";
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
    setStatus("已创建配置", false);
    refreshConfigs();
  });
};

document.getElementById("btnApplyPreset").onclick = () => {
  const id = document.getElementById("presetSelect").value;
  if (!id) return;
  if (!window.confirm("应用模板将覆盖当前配置，是否继续？")) return;
  send("form:applyPreset", { preset: id }, (d) => {
    if (okOr(d)) {
      snapshot = d.snapshot || [];
      errors = d.errors || [];
      renderForm(); renderPreview();
      setStatus("已应用模板", false);
    }
  });
};

document.getElementById("btnReset").onclick = () => {
  if (!currentConfig) { setStatus("当前没有打开的配置", true); return; }
  if (!window.confirm("清空当前配置并恢复默认值？")) return;
  send("config:reset", {}, (d) => {
    if (!okOr(d)) return;
    snapshot = d.snapshot || [];
    sourceText = d.sourceText || "";
    renderForm(); renderPreview();
    setStatus("已清空配置并恢复默认值", false);
  });
};

document.getElementById("btnValidate").onclick = () =>
  send("form:validate", null, (d) => {
    if (!okOr(d)) return;
    errors = d.errors || [];
    setStatus(errors.length === 0 ? "校验：✓ 全部通过" : `校验：${errors.length} 个错误`, errors.length > 0);
    if (d.snapshot) { snapshot = d.snapshot; renderForm(); }
  });

document.getElementById("btnPreview").onclick = () =>
  send("form:render", null, (d) => {
    if (!okOr(d)) return;
    sourceText = d.text || "";
    renderPreview();
    setStatus("已生成预览", false);
  });

document.getElementById("tabPreview").onclick = () => switchTab("preview");
document.getElementById("tabSource").onclick = () => switchTab("source");

document.getElementById("btnApplyEdit").onclick = () => {
  const text = document.getElementById("editor").value;
  send("form:importText", { text }, (d) => {
    if (!okOr(d)) return;
    snapshot = d.snapshot || [];
    errors = d.errors || [];
    unrecognized = d.unrecognized || [];
    renderForm(); renderPreview();
    const report = document.getElementById("unrecognizedReport");
    report.textContent = d.report && d.report.unrecognizedLines > 0
      ? `未识别内容 ${d.report.unrecognizedLines} 行（已保留，导出时可选追加）`
      : "";
    setStatus("已应用修改到表单", false);
  });
};

document.getElementById("btnExport").onclick = () => {
  const path = document.getElementById("exportPath").value;
  if (!path) { setStatus("请填写导出路径", true); return; }
  send("config:exportTo", { path }, (d) => {
    if (okOr(d)) setStatus(`已导出：${d.path}`, false);
  });
};

document.getElementById("btnImportFile").onclick = () => {
  document.getElementById("importFile").click();
};

document.getElementById("importFile").onchange = (e) => {
  const file = e.target.files[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    send("form:importText", { text: String(reader.result) }, (d) => {
      if (!okOr(d)) return;
      snapshot = d.snapshot || [];
      errors = d.errors || [];
      renderForm(); renderPreview();
      setStatus(`已导入：${file.name}`, false);
    });
  };
  reader.readAsText(file);
};

document.getElementById("btnSnapshot").onclick = () => {
  if (!currentConfig) { setStatus("当前没有打开的配置", true); return; }
  const note = document.getElementById("versionNote").value.trim() || undefined;
  send("version:snapshot", { note }, (d) => {
    if (okOr(d)) {
      setStatus("已留档", false);
      refreshVersions();
    }
  });
};

document.getElementById("btnExportWs").onclick = () => exportArchive("archive:exportWorkspace");
document.getElementById("btnExportCfg").onclick = () => exportArchive("archive:exportConfig");

function exportArchive(action) {
  const path = document.getElementById("archivePath").value;
  if (!path) { setStatus("请填写存档包路径", true); return; }
  const payload = { path };
  if (action === "archive:exportConfig") {
    if (!currentConfig) { setStatus("当前没有打开的配置", true); return; }
    payload.workspaceId = currentWorkspaceId;
    payload.configId = currentConfig.id;
  } else {
    payload.workspaceId = currentWorkspaceId;
  }
  send(action, payload, (d) => {
    if (okOr(d)) {
      document.getElementById("archiveResult").textContent = `已导出：${d.path}`;
      setStatus("存档导出完成", false);
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
    setStatus("存档导入完成", false);
  });
};

document.getElementById("btnLogs").onclick = () => {
  send("logs:open", null, (d) => { okOr(d); });
};

// ---------- 初始化 ----------
function init() {
  document.getElementById("newConfigPlugin").innerHTML = "";
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

// ---------- 自检（M7 全链路） ----------
function runSpike() {
  log("spike-run-start");
  const steps = [];
  let failed = false;
  function step(name, action, payload) {
    return new Promise((resolve) => {
      const t0 = performance.now();
      send(action, payload || {}, (data) => {
        const ms = performance.now() - t0;
        if (!data.ok) failed = true;
        steps.push({ name, ms, ok: !!data.ok, error: (data.errors || [])[0] || "" });
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
    await step("config:open", "config:open", { workspaceId: wsId, configId: cfgId });
    await step("form:toggle", "form:toggle", { path: "http.upstreams", enabled: false });
    await step("form:toggle", "form:toggle", { path: "http.upstreams", enabled: true });
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
