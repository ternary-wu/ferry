<script setup lang="ts">
import { computed } from 'vue';
import FieldControl from './FieldControl.vue';
import { useConfigStore } from '../../stores/config';
import {
  nodeMatchesSearch,
  nodePassesFilter,
  subtreeMatchesSearch,
  subtreePassesFilter
} from '../../utils/fieldTree';
import type { FormFieldSnapshot } from '../../ipc/types';

const props = defineProps<{ node: FormFieldSnapshot; depth: number }>();
const configStore = useConfigStore();

const visible = computed(() => {
  if (!props.node.isVisible) {
    return false;
  }
  const query = configStore.search.trim().toLowerCase();
  if (query && !nodeMatchesSearch(props.node, query) && !subtreeMatchesSearch(props.node, query)) {
    return false;
  }
  const filter = configStore.filter;
  if (!nodePassesFilter(props.node, filter) && !subtreePassesFilter(props.node, filter)) {
    return false;
  }
  return true;
});

const hasChildren = computed(() => props.node.children.length > 0);
const isExpandable = computed(
  () =>
    hasChildren.value &&
    (props.node.type === 'Object' || props.node.type === 'Array' || props.node.isModule)
);
const expanded = computed(() => {
  if (!isExpandable.value) {
    return false;
  }
  if (configStore.search.trim()) {
    return true;
  }
  return !configStore.collapsed[props.node.path];
});

const isArray = computed(() => props.node.type === 'Array');
const isArrayItem = computed(() => props.node.isArrayItem);

function toggleCollapse() {
  if (isExpandable.value) {
    configStore.toggleCollapsed(props.node.path);
  }
}

async function onToggleEnabled() {
  if (!props.node.canToggleEnabled) {
    return;
  }
  await configStore.toggle(props.node.path, !props.node.isEnabled);
}
</script>

<template>
  <div
    v-if="visible"
    class="ferry-field-row"
    :class="{ disabled: !props.node.isEnabled }"
    :style="{ paddingLeft: depth * 22 + 8 + 'px' }"
  >
    <div class="ferry-field-head">
      <span v-if="isExpandable" class="ferry-field-arrow" @click.stop="toggleCollapse">
        {{ expanded ? '⌄' : '›' }}
      </span>
      <span v-else class="ferry-field-arrow"></span>

      <span
        v-if="!props.node.canToggleEnabled"
        class="ferry-field-check lock"
        data-tip="必填字段不可取消"
      >
        🔒
      </span>
      <span
        v-else
        class="ferry-field-check"
        :class="{ checked: props.node.isEnabled }"
        :data-tip="props.node.isEnabled ? '取消勾选后该项不写入输出（值保留）' : '启用该项'"
        @click="onToggleEnabled"
      >
        {{ props.node.isEnabled ? '☑' : '☐' }}
      </span>

      <span class="ferry-field-label" :data-tip="props.node.description || props.node.label">
        {{ props.node.label || props.node.id }}
      </span>

      <span v-if="props.node.isModule && props.node.enabledChildModulesText" class="ferry-field-count">
        {{ props.node.enabledChildModulesText }}
      </span>
      <span v-if="isArray" class="ferry-field-count">{{ props.node.children.length }} 项</span>
      <span v-if="isArray" class="ferry-field-add" @click.stop="configStore.addItem(props.node.path)">
        ＋ 添加项
      </span>
      <span v-if="isArrayItem" class="ferry-field-remove" @click.stop="configStore.removeItem(props.node.path)">
        移除
      </span>

      <FieldControl v-if="!isExpandable && !isArrayItem" :node="props.node" />
    </div>

    <div v-if="isExpandable && expanded" class="ferry-field-children">
      <FieldNode
        v-for="child in props.node.children"
        :key="child.path"
        :node="child"
        :depth="depth + 1"
      />
    </div>

    <div
      v-if="props.node.validationError"
      class="ferry-field-error"
      :style="{ paddingLeft: depth * 22 + 28 + 'px' }"
    >
      {{ props.node.validationError }}
    </div>
  </div>
</template>
