"use strict";

let snapshot = null;
let pending = null;
let previewText = "";
let lastLatency = 0;
let latencySamples = [];

function send(action, payload, onOk) {
  const req = Object.assign({ action }, payload || {});
  pending = { t0: performance.now(), action, onOk };
  window.external.sendMessage(JSON.stringify(req));
}

function log(text) {
  try { window.external.sendMessage(JSON.stringify({ action: "log", text })); } catch (e) {}
}

window.onerror = function (msg, src, line) {
  log("js-error:" + msg + " @" + (src || "") + ":" + line);
};

window.external.receiveMessage(function (json) {
  try {
    const data = JSON.parse(json);
    if (data.action === "spike:run") {
      log("spike-run-received");
      runSpike();
      return;
    }
    const elapsed = pending ? performance.now() - pending.t0 : 0;
    lastLatency = elapsed;
    latencySamples.push({ action: pending ? pending.action : "?", ms: elapsed });
    document.getElementById("latency").textContent =
      `最近 ${elapsed.toFixed(1)}ms · 峰值 ${Math.max(...latencySamples.map(s => s.ms)).toFixed(1)}ms`;
    if (data.ok && data.snapshot) snapshot = data.snapshot;
    if (data.ok && data.text) previewText = data.text;
    if (data.ok && data.errors && data.errors.length > 0) {
      document.getElementById("status").firstChild.textContent = `校验：${data.errors.length} 个错误`;
    }
    if (pending && pending.onOk) pending.onOk(data);
    pending = null;
    render();
  } catch (e) {
    log("receive-error:" + e.message);
  }
});

function render() {
  const root = document.getElementById("form");
  root.textContent = "";
  if (!snapshot) return;
  for (const node of snapshot) root.appendChild(renderNode(node));
  document.getElementById("text").textContent = previewText || "（点击 预览 生成）";
}

function renderNode(node) {
  const wrap = document.createElement("div");
  wrap.className = "field" + (node.isEnabled ? "" : " disabled");
  wrap.dataset.path = node.path;

  if (node.isModule) {
    const box = document.createElement("input");
    box.type = "checkbox";
    box.checked = node.isEnabled;
    box.disabled = !node.canToggleEnabled;
    box.onchange = () => send("toggle", { path: node.path, enabled: box.checked },
      () => send("snapshot"));
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

  const onChanged = (value) => send("setValue", { path: node.path, value },
    () => send("snapshot"));

  switch (node.type) {
    case "String":
      const input = document.createElement("input");
      input.type = "text";
      input.value = node.value ?? "";
      input.onchange = () => onChanged(input.value);
      wrap.appendChild(input);
      break;
    case "Number":
      const num = document.createElement("input");
      num.type = "number";
      num.value = node.value ?? "";
      num.min = node.min ?? "";
      num.max = node.max ?? "";
      num.step = node.integerOnly ? "1" : "any";
      num.onchange = () => onChanged(num.value);
      wrap.appendChild(num);
      break;
    case "Boolean":
      const boolBox = document.createElement("input");
      boolBox.type = "checkbox";
      boolBox.checked = node.value === true;
      boolBox.onchange = () => onChanged(boolBox.checked);
      wrap.appendChild(boolBox);
      break;
    case "Enum":
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
        custom.placeholder = "自定义";
        custom.value = (node.value ?? "").toString();
        custom.onchange = () => onChanged(custom.value);
        select.onchange = () => onChanged(select.value);
        wrap.appendChild(select);
        wrap.appendChild(custom);
      } else {
        select.value = (node.value ?? "").toString();
        select.onchange = () => onChanged(select.value);
        wrap.appendChild(select);
      }
      break;
    case "Array":
      const add = document.createElement("button");
      add.textContent = "＋ 添加";
      add.onclick = () => send("addItem", { path: node.path }, () => send("snapshot"));
      wrap.appendChild(add);
      for (const child of node.children || []) {
        const item = document.createElement("div");
        item.className = "item";
        const remove = document.createElement("button");
        remove.textContent = "✕";
        remove.onclick = () => send("removeItem", { path: child.path }, () => send("snapshot"));
        item.appendChild(remove);
        item.appendChild(renderNode(child));
        wrap.appendChild(item);
      }
      break;
    case "Object":
      for (const child of node.children || []) wrap.appendChild(renderNode(child));
      break;
  }
  return wrap;
}

document.getElementById("btnValidate").onclick = () =>
  send("validate", null, (data) => {
    const status = document.getElementById("status");
    status.firstChild.textContent =
      data.ok ? "校验：✓ 全部通过" : `校验：${data.errors.length} 个错误`;
  });

document.getElementById("btnRender").onclick = () =>
  send("render", null, (data) => {
    if (data.ok) previewText = data.text;
  });

// ---------- 自检：测量每步端到端延迟（含 JS→.NET→JS IPC 往返） ----------
function runSpike() {
  log("spike-run-start");
  const steps = [];
  function step(name, fn) {
    return new Promise((resolve) => {
      const t0 = performance.now();
      fn(() => {
        steps.push({ name, ms: performance.now() - t0 });
        resolve();
      });
    });
  }
  (async () => {
    await step("snapshot", (done) => send("snapshot", null, done));
    await step("toggle-http-off", (done) => send("toggle", { path: "http", enabled: false }, done));
    await step("toggle-http-on", (done) => send("toggle", { path: "http", enabled: true }, done));
    await step("add-item", (done) => send("addItem", { path: "http.upstreams" }, done));
    await step("set-value", (done) =>
      send("setValue", { path: "http.upstreams[0].upstream_name", value: "backend" }, done));
    await step("remove-item", (done) => send("removeItem", { path: "http.upstreams[0]" }, done));
    await step("render", (done) => send("render", null, done));
    await step("validate", (done) => send("validate", null, done));
    const worst = Math.max(...steps.map(s => s.ms));
    window.external.sendMessage(JSON.stringify({
      action: "spike:result",
      ok: worst < 50,
      worstMs: worst,
      steps
    }));
  })();
}

send("snapshot", null, () => send("render"));
