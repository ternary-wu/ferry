import { ref } from 'vue';
import { defineStore } from 'pinia';
import type { ConfigInfo } from '../ipc/types';

export interface ConfigTarget {
  config: ConfigInfo;
  workspaceId: string;
}

export interface ContextMenuItem {
  text: string;
  danger?: boolean;
  disabled?: boolean;
  onClick?: () => void;
}

export const useUiStore = defineStore('ui', () => {
  const menuOpen = ref(false);
  const menuItems = ref<ContextMenuItem[]>([]);
  const menuX = ref(0);
  const menuY = ref(0);

  const promptOpen = ref(false);
  const promptTitle = ref('');
  const promptValue = ref('');
  const promptPlaceholder = ref('');
  let promptResolver: ((value: string | null) => void) | null = null;

  const confirmOpen = ref(false);
  const confirmTitle = ref('');
  const confirmMessage = ref('');
  let confirmResolver: ((value: boolean) => void) | null = null;

  const settingsCategory = ref('general');

  const moveOpen = ref(false);
  const moveTarget = ref<ConfigTarget | null>(null);
  const historyOpen = ref(false);
  const historyTarget = ref<ConfigTarget | null>(null);
  const renameOpen = ref(false);
  const renameTarget = ref<ConfigTarget | null>(null);
  const exportProjectOpen = ref(false);
  const pushModalOpen = ref(false);
  const pushModalTarget = ref<ConfigTarget | null>(null);
  const pushTargetModalOpen = ref(false);
  const pushTargetEditIndex = ref<number | null>(null);

  function openMenu(items: ContextMenuItem[], x: number, y: number) {
    menuItems.value = items;
    menuX.value = x;
    menuY.value = y;
    menuOpen.value = true;
  }

  function closeMenu() {
    menuOpen.value = false;
  }

  function prompt(opts: { title: string; defaultValue?: string; placeholder?: string }): Promise<string | null> {
    promptTitle.value = opts.title;
    promptValue.value = opts.defaultValue ?? '';
    promptPlaceholder.value = opts.placeholder ?? '';
    promptOpen.value = true;
    return new Promise((resolve) => {
      promptResolver = resolve;
    });
  }

  function resolvePrompt(value: string | null) {
    promptOpen.value = false;
    const resolver = promptResolver;
    promptResolver = null;
    resolver?.(value);
  }

  function confirm(opts: { title: string; message: string }): Promise<boolean> {
    confirmTitle.value = opts.title;
    confirmMessage.value = opts.message;
    confirmOpen.value = true;
    return new Promise((resolve) => {
      confirmResolver = resolve;
    });
  }

  function resolveConfirm(value: boolean) {
    confirmOpen.value = false;
    const resolver = confirmResolver;
    confirmResolver = null;
    resolver?.(value);
  }

  function openMove(config: ConfigInfo, workspaceId: string) {
    moveTarget.value = { config, workspaceId };
    moveOpen.value = true;
  }

  function closeMove() {
    moveOpen.value = false;
    moveTarget.value = null;
  }

  function openHistory(config: ConfigInfo, workspaceId: string) {
    historyTarget.value = { config, workspaceId };
    historyOpen.value = true;
  }

  function closeHistory() {
    historyOpen.value = false;
    historyTarget.value = null;
  }

  function openRename(config: ConfigInfo, workspaceId: string) {
    renameTarget.value = { config, workspaceId };
    renameOpen.value = true;
  }

  function closeRename() {
    renameOpen.value = false;
    renameTarget.value = null;
  }

  function openExportProject() {
    exportProjectOpen.value = true;
  }

  function closeExportProject() {
    exportProjectOpen.value = false;
  }

  function openPushModal(config: ConfigInfo, workspaceId: string) {
    pushModalTarget.value = { config, workspaceId };
    pushModalOpen.value = true;
  }

  function closePushModal() {
    pushModalOpen.value = false;
    pushModalTarget.value = null;
  }

  function openPushTargetModal(editIndex: number | null = null) {
    pushTargetEditIndex.value = editIndex;
    pushTargetModalOpen.value = true;
  }

  function closePushTargetModal() {
    pushTargetModalOpen.value = false;
    pushTargetEditIndex.value = null;
  }

  return {
    menuOpen,
    menuItems,
    menuX,
    menuY,
    openMenu,
    closeMenu,
    promptOpen,
    promptTitle,
    promptValue,
    promptPlaceholder,
    prompt,
    resolvePrompt,
    confirmOpen,
    confirmTitle,
    confirmMessage,
    confirm,
    resolveConfirm,
    settingsCategory,
    moveOpen,
    moveTarget,
    openMove,
    closeMove,
    historyOpen,
    historyTarget,
    openHistory,
    closeHistory,
    renameOpen,
    renameTarget,
    openRename,
    closeRename,
    exportProjectOpen,
    openExportProject,
    closeExportProject,
    pushModalOpen,
    pushModalTarget,
    openPushModal,
    closePushModal,
    pushTargetModalOpen,
    pushTargetEditIndex,
    openPushTargetModal,
    closePushTargetModal
  };
});
