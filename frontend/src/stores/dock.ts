import { ref } from 'vue';
import { defineStore } from 'pinia';
import { loadLocal, saveLocal } from '../utils/storage';

/** 常规拖拽宽度下限（35%）。低于该值松开即关闭。 */
export const DOCK_MIN = 35;
/** 拖拽宽度上限（60%）。 */
export const DOCK_MAX = 60;
/** 拖拽过程中允许进入的关闭区下限（30%），避免阈值附近手抖。 */
export const DOCK_DRAG_LOWER = 30;
/** 默认宽度（42%）。 */
export const DOCK_DEFAULT_WIDTH = 42;

export const useDockStore = defineStore('dock', () => {
  const open = ref(false);
  const width = ref(clamp(loadLocal<number>('ferry.dock.width', DOCK_DEFAULT_WIDTH)));
  const maximized = ref(false);
  const lineNumbers = ref(loadLocal<boolean>('ferry.dock.lineNumbers', true));
  /** 全占前记住的宽度，还原时恢复。 */
  const restoredWidth = ref(clamp(width.value));

  function clamp(percent: number): number {
    if (Number.isFinite(percent)) {
      return Math.min(DOCK_MAX, Math.max(DOCK_MIN, percent));
    }
    return DOCK_DEFAULT_WIDTH;
  }

  function openDock() {
    open.value = true;
  }

  function closeDock() {
    open.value = false;
    maximized.value = false;
  }

  function toggle() {
    if (open.value) {
      closeDock();
    } else {
      openDock();
    }
  }

  /** 拖拽中实时更新宽度：允许进入 30%–35% 关闭区，但不超过 60%。 */
  function resizeTo(percent: number) {
    width.value = Number.isFinite(percent)
      ? Math.min(DOCK_MAX, Math.max(DOCK_DRAG_LOWER, percent))
      : width.value;
  }

  /** 松开拖拽：低于 35% 关闭，否则保留并持久化。 */
  function finishResize() {
    if (width.value < DOCK_MIN) {
      closeDock();
      return;
    }
    restoredWidth.value = width.value;
    saveLocal('ferry.dock.width', width.value);
  }

  /** 「全占」仅占满 Main Workspace（不隐藏 Sidebar、不覆盖任务栏），再点还原。 */
  function toggleMaximize() {
    if (maximized.value) {
      maximized.value = false;
      width.value = restoredWidth.value;
    } else {
      restoredWidth.value = width.value;
      maximized.value = true;
    }
  }

  function toggleLineNumbers() {
    lineNumbers.value = !lineNumbers.value;
    saveLocal('ferry.dock.lineNumbers', lineNumbers.value);
  }

  return {
    open,
    width,
    maximized,
    lineNumbers,
    restoredWidth,
    openDock,
    closeDock,
    toggle,
    resizeTo,
    finishResize,
    toggleMaximize,
    toggleLineNumbers
  };
});
