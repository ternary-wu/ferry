import { defineStore } from 'pinia';
import { getIpc } from '../ipc';

export const useWindowStore = defineStore('window', () => {
  function minimize() {
    return getIpc().send('window:minimize', {});
  }

  function toggleMaximize() {
    return getIpc().send('window:maximize', {});
  }

  function isMaximized() {
    return getIpc().send('window:isMaximized', {});
  }

  /** 后端对 window:close 提前返回不回包，因此 fire-and-forget，避免 10s 超时等待。 */
  function close() {
    return getIpc().send('window:close', {}, { fireAndForget: true });
  }

  function beginDrag() {
    // BeginNativeDrag 是同步模态拖拽循环，拖拽期间不回包，fire-and-forget 避免 10s 超时误报
    return getIpc().send('window:drag', {}, { fireAndForget: true });
  }

  return { minimize, toggleMaximize, isMaximized, close, beginDrag };
});
