"use strict";

// Window Shell 客户端：仅负责自定义 TitleBar 的窗口控制（窗口层），不含任何业务逻辑。
// 窗口拖动走 Windows 原生（window:drag → WindowController.BeginNativeDrag），
// 鼠标与窗口 1:1 跟随，自动适配 DPI 缩放与多显示器。
(function () {
  const titleBar = document.getElementById("titleBar");
  if (!titleBar) return;

  function send(action) {
    window.external.sendMessage(JSON.stringify({ action, requestId: "win" + Date.now() }));
  }

  titleBar.addEventListener("mousedown", (e) => {
    if (e.target.closest("button")) return;
    if (e.button !== 0) return;
    send("window:drag");
  });

  titleBar.addEventListener("dblclick", (e) => {
    if (e.target.closest("button")) return;
    send("window:maximize");
  });

  document.getElementById("winMin").onclick = () => send("window:minimize");
  document.getElementById("winMax").onclick = () => send("window:maximize");
  document.getElementById("winClose").onclick = () => send("window:close");
})();
