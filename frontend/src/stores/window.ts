import { defineStore } from 'pinia';
import { getIpc } from '../ipc';

export const useWindowStore = defineStore('window', () => {
  function minimize() {
    return getIpc().send('window:minimize', {});
  }

  function toggleMaximize() {
    return getIpc().send('window:maximize', {});
  }

  /** 后端对 window:close 提前返回不回包，因此 fire-and-forget，避免 10s 超时等待。 */
  function close() {
    return getIpc().send('window:close', {}, { fireAndForget: true });
  }

  function beginDrag() {
    return getIpc().send('window:drag', {});
  }

  return { minimize, toggleMaximize, close, beginDrag };
});
