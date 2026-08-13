<script setup lang="ts">
import { computed } from 'vue';
import { useAppStore } from '../stores/app';
import { useConfigStore } from '../stores/config';

const app = useAppStore();
const configStore = useConfigStore();

const leftText = computed(() => {
  if (!configStore.isOpen) {
    return '就绪';
  }
  if (configStore.saving) {
    return '保存中…';
  }
  if (configStore.errors.length > 0) {
    return `✗ ${configStore.errors.length} 个错误`;
  }
  return '✓ 校验通过 · 已保存';
});

const leftClass = computed(() => {
  if (!configStore.isOpen || configStore.saving) {
    return '';
  }
  return configStore.errors.length > 0
    ? 'text-[var(--ferry-danger)]'
    : 'text-[var(--ferry-ok)]';
});

const rightText = computed(() => (app.statusIsError ? app.status : '正常'));
const rightClass = computed(() =>
  app.statusIsError ? 'text-[var(--ferry-danger)]' : 'text-[var(--ferry-ok)]'
);
</script>

<template>
  <footer class="ferry-statusbar flex h-[30px] shrink-0 items-center justify-between border-t border-[var(--ferry-border-soft)] px-5 text-xs text-[var(--ferry-text-muted)]">
    <span :class="leftClass">{{ leftText }}</span>
    <span :class="rightClass">{{ rightText }}</span>
  </footer>
</template>
