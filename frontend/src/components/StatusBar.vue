<script setup lang="ts">
import { computed } from 'vue';
import { useAppStore } from '../stores/app';
import { useConfigStore } from '../stores/config';
import type { FormFieldSnapshot } from '../ipc/types';

const app = useAppStore();
const configStore = useConfigStore();

function countModules(nodes: FormFieldSnapshot[]): [number, number] {
  let enabled = 0;
  let total = 0;
  for (const node of nodes) {
    if (node.isModule) {
      total++;
      if (node.isEnabled) {
        enabled++;
      }
    }
    const [childEnabled, childTotal] = countModules(node.children);
    enabled += childEnabled;
    total += childTotal;
  }
  return [enabled, total];
}

const moduleText = computed(() => {
  if (configStore.snapshot.length === 0) {
    return '';
  }
  const [enabled, total] = countModules(configStore.snapshot);
  return `${enabled}/${total} 模块已启用`;
});
</script>

<template>
  <footer class="ferry-statusbar flex h-[30px] shrink-0 items-center gap-4 border-t border-[var(--ferry-border-soft)] px-3.5 text-xs text-[var(--ferry-text-muted)]">
    <span :class="app.statusIsError ? 'text-[var(--ferry-danger)]' : 'text-[var(--ferry-ok)]'">
      {{ app.status }}
    </span>
    <span v-if="moduleText">{{ moduleText }}</span>
    <span class="flex-1"></span>
    <span v-if="app.latencyMs !== null">IPC {{ app.latencyMs.toFixed(1) }}ms</span>
  </footer>
</template>
