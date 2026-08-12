<script setup lang="ts">
import { useConfigStore } from '../stores/config';

const configStore = useConfigStore();
</script>

<template>
  <div class="flex h-full flex-col p-6">
    <div class="text-lg font-semibold">{{ configStore.current?.name ?? '配置编辑器' }}</div>
    <p class="mt-1 text-sm text-[var(--ferry-text-muted)]">
      <template v-if="configStore.current">
        {{ configStore.current.pluginName }} · v{{ configStore.current.pluginVersion }}
        <template v-if="configStore.versionChanged">（字段可能有增减）</template>
      </template>
      <template v-else>请从侧栏选择配置</template>
    </p>
    <div class="mt-10 text-sm text-[var(--ferry-text-dim)]">字段树编辑器（P5 实现）</div>
  </div>
</template>
