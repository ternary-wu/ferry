<script setup lang="ts">
import { useWindowStore } from '../stores/window';

const windowStore = useWindowStore();

function onBarMouseDown(event: MouseEvent) {
  if ((event.target as HTMLElement).closest('button')) return;
  if (event.button !== 0) return;
  void windowStore.beginDrag();
}

function onBarDblClick(event: MouseEvent) {
  if ((event.target as HTMLElement).closest('button')) return;
  void windowStore.toggleMaximize();
}
</script>

<template>
  <header
    class="ferry-titlebar flex h-8 shrink-0 select-none items-center justify-between border-b border-[var(--ferry-border-soft)] bg-[var(--ferry-surface)] px-3"
    @mousedown="onBarMouseDown"
    @dblclick="onBarDblClick"
  >
    <span class="text-xs text-[var(--ferry-text-muted)]">Ferry</span>
    <span class="flex items-center gap-0.5">
      <button class="win-btn" title="最小化" @click="windowStore.minimize()">
        <svg viewBox="0 0 12 12"><path d="M1 6h10" stroke="currentColor" stroke-width="1.2" /></svg>
      </button>
      <button class="win-btn" title="最大化 / 还原" @click="windowStore.toggleMaximize()">
        <svg viewBox="0 0 12 12">
          <rect x="1.5" y="1.5" width="9" height="9" fill="none" stroke="currentColor" stroke-width="1.2" />
        </svg>
      </button>
      <button class="win-btn close" title="关闭" @click="windowStore.close()">
        <svg viewBox="0 0 12 12">
          <path d="M1.5 1.5l9 9M10.5 1.5l-9 9" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" />
        </svg>
      </button>
    </span>
  </header>
</template>
