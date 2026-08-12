<script setup lang="ts">
import { nextTick, ref, watch } from 'vue';
import { useUiStore, type ContextMenuItem } from '../stores/ui';

const ui = useUiStore();
const menuEl = ref<HTMLDivElement | null>(null);
const left = ref(0);
const top = ref(0);

watch(
  () => ui.menuOpen,
  async (open) => {
    if (!open) {
      return;
    }
    left.value = ui.menuX;
    top.value = ui.menuY;
    await nextTick();
    if (!menuEl.value) {
      return;
    }
    const rect = menuEl.value.getBoundingClientRect();
    if (left.value + rect.width > window.innerWidth - 8) {
      left.value = Math.max(8, window.innerWidth - rect.width - 8);
    }
    if (top.value + rect.height > window.innerHeight - 8) {
      top.value = Math.max(8, window.innerHeight - rect.height - 8);
    }
  }
);

function onItemClick(item: ContextMenuItem) {
  ui.closeMenu();
  item.onClick?.();
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="ui.menuOpen"
      ref="menuEl"
      class="ferry-ctx-menu"
      :style="{ left: left + 'px', top: top + 'px' }"
    >
      <div
        v-for="item in ui.menuItems"
        :key="item.text"
        class="ferry-ctx-item"
        :class="{ danger: item.danger, disabled: item.disabled }"
        @click="onItemClick(item)"
      >
        {{ item.text }}
      </div>
    </div>
  </Teleport>
</template>
