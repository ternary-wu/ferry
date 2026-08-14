<script setup lang="ts">
import { ref, watch } from 'vue';
import { useUiStore } from '../stores/ui';
import { useSettingsStore } from '../stores/settings';
import type { PushTarget } from '../ipc/types';

const ui = useUiStore();
const settingsStore = useSettingsStore();
const name = ref('');
const type = ref<'local' | 'git' | 'ssh'>('git');
const remotePath = ref('');
const branch = ref('main');

watch(
  () => ui.pushTargetModalOpen,
  (open) => {
    if (!open) {
      return;
    }
    const index = ui.pushTargetEditIndex;
    const targets = settingsStore.settings.pushTargets ?? [];
    const editing = index !== null ? targets[index] : undefined;
    name.value = editing?.name ?? '';
    type.value = editing?.type ?? 'git';
    remotePath.value = editing?.remotePath ?? '';
    branch.value = editing?.branch ?? 'main';
  }
);

function outsideClose() {
  return settingsStore.settings.closeOutside !== false;
}

async function save() {
  if (!name.value.trim() || !remotePath.value.trim()) {
    return;
  }
  const targets = [...(settingsStore.settings.pushTargets ?? [])];
  const index = ui.pushTargetEditIndex;
  const item: PushTarget = {
    id:
      index !== null && targets[index]
        ? targets[index].id
        : Date.now() + '' + Math.round(Math.random() * 1e6),
    name: name.value.trim(),
    type: type.value,
    remotePath: remotePath.value.trim(),
    branch: type.value === 'git' ? branch.value.trim() || 'main' : undefined
  };
  if (index !== null && targets[index]) {
    targets[index] = item;
  } else {
    targets.push(item);
  }
  await settingsStore.save({ pushTargets: targets });
  ui.closePushTargetModal();
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="ui.pushTargetModalOpen"
      class="ferry-overlay"
      @mousedown.self="outsideClose() && ui.closePushTargetModal()"
    >
      <div class="ferry-modal">
        <div class="ferry-modal-title">
          {{ ui.pushTargetEditIndex === null ? '新增推送目标' : '编辑推送目标' }}
        </div>
        <input v-model="name" class="ferry-input" placeholder="目标名称" />
        <select v-model="type" class="ferry-input ferry-move-select">
          <option value="local">本地目录</option>
          <option value="git">Git 仓库</option>
          <option value="ssh">SSH</option>
        </select>
        <input
          v-model="remotePath"
          class="ferry-input ferry-move-select"
          placeholder="本地目录路径 / Git 仓库路径 / user@host:/目录"
        />
        <input
          v-if="type === 'git'"
          v-model="branch"
          class="ferry-input ferry-move-select"
          placeholder="分支（默认 main）"
        />
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.closePushTargetModal()">取消</button>
          <button
            class="ferry-btn primary"
            :disabled="!name.trim() || !remotePath.trim()"
            @click="save"
          >
            保存
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
