"use strict";

// 动态模块注册表与通知中心（UI 层预留）：
// 未来模块（Git/SSH/推送等）注册后即出现在"工具"分组与配置 ⋯ 菜单，无需改 Sidebar。
window.FerryModules = (function () {
  const registry = [];
  function register(module) { registry.push(module); }
  function list() { return registry.slice(); }
  return { register, list };
})();

window.FerryNotifications = (function () {
  let items = [];
  const listeners = [];

  function load() {
    try { items = JSON.parse(localStorage.getItem("ferry.notifications") || "[]"); }
    catch (e) { items = []; }
    return items;
  }
  function save() {
    localStorage.setItem("ferry.notifications", JSON.stringify(items));
  }
  function add(type, text) {
    items.unshift({ id: Date.now() + "" + Math.round(Math.random() * 1e6), type, text, time: Date.now() });
    if (items.length > 50) items.length = 50;
    save();
    listeners.forEach(f => f(items));
  }
  function consume(id) {
    items = items.filter(i => i.id !== id);
    save();
    listeners.forEach(f => f(items));
  }
  function clearAll() {
    items = [];
    save();
    listeners.forEach(f => f(items));
  }
  function get() { return items.slice(); }
  function onChange(fn) { listeners.push(fn); }
  return { load, add, consume, clearAll, onChange, get };
})();
