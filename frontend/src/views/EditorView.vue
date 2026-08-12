<script setup lang="ts">
import { computed, watch } from 'vue';
import FieldNode from '../components/field/FieldNode.vue';
import { useAppStore } from '../stores/app';
import { useConfigStore } from '../stores/config';
import { loadLocal } from '../utils/storage';

const configStore = useConfigStore();
const appStore = useAppStore();

const templateName = computed(() =>
  configStore.current ? loadLocal<string>(`ferry.tplCfg.${configStore.current.id}`, '') : ''
);

watch(
  () => configStore.errors,
  (errors) => {
    appStore.setStatus(errors.length ? `校验：${errors.length} 个错误` : '校验通过', errors.length > 0);
  },
  { immediate: true }
);
</script>

<template>
  <div v-if="configStore.isOpen" class="flex h-full flex-col">
    <header class="shrink-0 border-b border-[var(--ferry-border-soft)] px-6 pb-2 pt-5">
      <h2 class="text-[21px]">{{ configStore.current?.name }}</h2>
      <p class="text-[13px] text-[var(--ferry-text-muted)]">
        {{ configStore.current?.pluginName }} · v{{ configStore.current?.pluginVersion }}
        <template v-if="configStore.versionChanged">（字段可能有增减）</template>
        <template v-if="templateName"> · 模板：{{ templateName }}</template>
        <template v-if="configStore.pluginMissing"> · 插件缺失：仅可查看/导出源码</template>
      </p>
    </header>

    <div class="flex shrink-0 items-center gap-2 border-b border-[var(--ferry-border-soft)] px-6 py-2">
      <select v-model="configStore.filter" class="ferry-field-tool">
        <option value="all">全部</option>
        <option value="selected">已选择</option>
        <option value="unselected">未选择</option>
      </select>
      <input
        v-model="configStore.search"
        class="ferry-field-tool ferry-field-search"
        placeholder="搜索字段…"
      />
      <button class="ferry-btn small" @click="configStore.collapseAll()">折叠全部</button>
      <button class="ferry-btn small" @click="configStore.expandAll()">展开全部</button>
    </div>

    <div class="min-h-0 flex-1 overflow-y-auto px-6 py-3">
      <FieldNode
        v-for="node in configStore.snapshot"
        :key="node.path"
        :node="node"
        :depth="0"
      />
    </div>
  </div>
  <div
    v-else
    class="flex h-full items-center justify-center text-sm text-[var(--ferry-text-muted)]"
  >
    请从侧栏选择配置
  </div>
</template>
