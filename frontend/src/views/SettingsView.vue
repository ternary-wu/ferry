<script setup lang="ts">
import { computed } from 'vue';
import { useUiStore } from '../stores/ui';

const ui = useUiStore();
const titles: Record<string, string> = {
  general: '常规',
  appearance: '外观',
  plugins: '插件管理',
  modules: '模块管理',
  storage: '存储',
  notifications: '通知'
};
const title = computed(() => titles[ui.settingsCategory] ?? '设置');
</script>

<template>
  <div class="flex h-full flex-col p-6">
    <div class="text-lg font-semibold">{{ title }}</div>
    <div class="mt-10 text-sm text-[var(--ferry-text-dim)]">设置内容（P8 实现）</div>
  </div>
</template>
