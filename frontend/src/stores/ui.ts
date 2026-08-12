import { ref } from 'vue';
import { defineStore } from 'pinia';

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
    settingsCategory
  };
});
