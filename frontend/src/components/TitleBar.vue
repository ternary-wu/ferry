<script setup lang="ts">
import { computed } from 'vue';
import { useWindowStore } from '../stores/window';
import { useProjectStore } from '../stores/project';
import { useConfigStore } from '../stores/config';

const windowStore = useWindowStore();
const projectStore = useProjectStore();
const configStore = useConfigStore();

const breadcrumb = computed(() => {
  const project = projectStore.projects.find((p) => p.id === projectStore.currentProjectId);
  if (!configStore.current) {
    return project?.name ?? '';
  }
  const workspace = projectStore.nav.workspaces.find((w) => w.id === configStore.workspaceId);
  return `${project?.name ?? '项目'} / ${workspace?.name ?? '未归类'} / ${configStore.current.name}`;
});

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
    class="ferry-titlebar flex h-8 shrink-0 select-none items-center justify-between border-b border-[var(--ferry-border-soft)] bg-[var(--ferry-surface)]"
    @mousedown="onBarMouseDown"
    @dblclick="onBarDblClick"
  >
    <span class="text-xs text-[var(--ferry-text-muted)]">Ferry</span>
    <span class="min-w-0 flex-1 truncate px-2 text-center text-xs text-[#bbb]">{{ breadcrumb }}</span>
    <span class="flex shrink-0 items-center gap-0.5">
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
