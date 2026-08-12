<script setup lang="ts">
import { useAppStore } from '../stores/app';

const app = useAppStore();
</script>

<template>
  <footer class="ferry-statusbar flex h-[30px] shrink-0 items-center gap-4 border-t border-[var(--ferry-border-soft)] px-3.5 text-xs text-[var(--ferry-text-muted)]">
    <span :class="app.statusIsError ? 'text-[var(--ferry-danger)]' : 'text-[var(--ferry-ok)]'">
      {{ app.status }}
    </span>
    <span class="flex-1"></span>
    <span v-if="app.latencyMs !== null">IPC {{ app.latencyMs.toFixed(1) }}ms</span>
  </footer>
</template>
