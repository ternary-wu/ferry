<script setup lang="ts">
import { computed } from 'vue';
import { useAppStore } from '../stores/app';
import { useWizardStore } from '../stores/wizard';
import { loadLocal } from '../utils/storage';
import type { PluginDescriptor } from '../ipc/types';

const appStore = useAppStore();
const wizardStore = useWizardStore();

const recentPlugins = computed(() =>
  loadLocal<string[]>('ferry.recentPlugins', [])
    .map((key) => appStore.plugins.find((plugin) => plugin.key === key))
    .filter((plugin): plugin is PluginDescriptor => Boolean(plugin))
    .slice(0, 4)
);
</script>

<template>
  <div class="flex h-full flex-col overflow-y-auto p-10">
    <div class="flex flex-1 flex-col items-center justify-center gap-3.5">
      <div class="ferry-welcome-icon">F</div>
      <h1 class="text-[26px]">Ferry</h1>
      <p class="text-[var(--ferry-text-muted)]">从模板开始，快速创建运维配置</p>
      <button class="ferry-btn primary ferry-welcome-btn" @click="wizardStore.openWizard()">
        ＋ 新建配置
      </button>
    </div>
    <div class="flex flex-col items-center gap-2.5 pt-6">
      <div class="text-xs text-[var(--ferry-text-dim)]">最近使用</div>
      <div class="flex flex-wrap justify-center gap-2.5">
        <button
          v-for="plugin in recentPlugins"
          :key="plugin.key"
          class="ferry-recent-chip"
          @click="wizardStore.openWizard({ pluginKey: plugin.key })"
        >
          {{ plugin.name }}
        </button>
        <span v-if="recentPlugins.length === 0" class="text-xs text-[var(--ferry-text-dim)]">
          创建配置后这里会显示最近使用的插件
        </span>
      </div>
    </div>
  </div>
</template>
