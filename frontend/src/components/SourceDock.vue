<script setup lang="ts">
import { computed, ref } from 'vue';
import { useConfigStore } from '../stores/config';
import { DOCK_MIN, useDockStore } from '../stores/dock';

const configStore = useConfigStore();
const dock = useDockStore();

const dragging = ref(false);
const dockRef = ref<HTMLElement | null>(null);

const sourceLines = computed(() => (configStore.sourceText ?? '').split('\n'));
const inCloseZone = computed(() => !dock.maximized && dock.width < DOCK_MIN);

function onResizePointerDown(event: PointerEvent) {
  if (event.button !== 0) {
    return;
  }
  dragging.value = true;
  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
  event.preventDefault();
}

function onResizePointerMove(event: PointerEvent) {
  if (!dragging.value) {
    return;
  }
  const host = dockRef.value?.parentElement as HTMLElement | null;
  if (!host) {
    return;
  }
  const rect = host.getBoundingClientRect();
  if (rect.width <= 0) {
    return;
  }
  dock.resizeTo(((rect.right - event.clientX) / rect.width) * 100);
}

function onResizePointerUp(event: PointerEvent) {
  if (!dragging.value) {
    return;
  }
  dragging.value = false;
  try {
    (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId);
  } catch {
    // 指针捕获已释放时忽略
  }
  dock.finishResize();
}
</script>

<template>
  <aside
    ref="dockRef"
    class="ferry-dock"
    :class="{ maximized: dock.maximized, dragging }"
    :style="{ width: dock.maximized ? '100%' : dock.width + '%' }"
  >
    <div
      v-if="!dock.maximized"
      class="ferry-dock-resizer"
      :class="{ dragging }"
      title="拖拽调整宽度（松开低于 35% 关闭）"
      @pointerdown="onResizePointerDown"
      @pointermove="onResizePointerMove"
      @pointerup="onResizePointerUp"
    ></div>
    <header class="ferry-dock-header">
      <button
        class="ferry-dock-btn"
        :title="dock.lineNumbers ? '隐藏行号' : '显示行号'"
        :class="{ active: dock.lineNumbers }"
        @click="dock.toggleLineNumbers()"
      >
        #
      </button>
      <span class="flex-1"></span>
      <button class="ferry-dock-btn" title="仅占满主工作区" @click="dock.toggleMaximize()">
        {{ dock.maximized ? '还原' : '全占' }}
      </button>
      <button class="ferry-dock-btn close" title="关闭源码 Dock" @click="dock.closeDock()">×</button>
    </header>
    <div v-if="inCloseZone" class="ferry-dock-hint">松开关闭</div>
    <div class="ferry-dock-code" aria-readonly="true">
      <div v-for="(line, index) in sourceLines" :key="index" class="ferry-dock-line">
        <span v-if="dock.lineNumbers" class="ferry-dock-ln">{{ index + 1 }}</span>
        <span class="ferry-dock-text">{{ line || ' ' }}</span>
      </div>
    </div>
  </aside>
</template>
