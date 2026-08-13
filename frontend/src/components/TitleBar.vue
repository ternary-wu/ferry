<script setup lang="ts">
import { computed } from 'vue';
import { useWindowStore } from '../stores/window';
import { useProjectStore } from '../stores/project';
import { useConfigStore } from '../stores/config';
import NotificationPanel from './NotificationPanel.vue';

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

let mouseDown = false;
let dragStarted = false;
let dragStartX = 0;
let dragStartY = 0;

function onBarMouseDown(event: MouseEvent) {
  if ((event.target as HTMLElement).closest('button')) return;
  if (event.button !== 0) return;
  mouseDown = true;
  dragStarted = false;
  dragStartX = event.clientX;
  dragStartY = event.clientY;
}

function onBarMouseMove(event: MouseEvent) {
  if (!mouseDown || dragStarted) {
    return;
  }
  const dx = event.clientX - dragStartX;
  const dy = event.clientY - dragStartY;
  if (Math.abs(dx) + Math.abs(dy) > 4) {
    dragStarted = true;
    void windowStore.beginDrag();
  }
}

function onBarMouseUp() {
  mouseDown = false;
  dragStarted = false;
}

function onBarDblClick(event: MouseEvent) {
  if ((event.target as HTMLElement).closest('button')) return;
  void windowStore.toggleMaximize();
}
</script>

<template>
  <header
    class="ferry-titlebar relative flex h-8 shrink-0 select-none items-center justify-between border-b border-[var(--ferry-border-soft)] bg-[var(--ferry-surface)]"
    @mousedown="onBarMouseDown"
    @mousemove="onBarMouseMove"
    @mouseup="onBarMouseUp"
    @dblclick="onBarDblClick"
  >
    <span class="text-xs text-[var(--ferry-text-muted)]">Ferry</span>
    <span
      class="pointer-events-none absolute left-1/2 top-1/2 max-w-[45%] -translate-x-1/2 -translate-y-1/2 truncate text-center text-xs text-[#bbb]"
    >
      {{ breadcrumb }}
    </span>
    <span class="flex shrink-0 items-center gap-0.5">
      <NotificationPanel />
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
