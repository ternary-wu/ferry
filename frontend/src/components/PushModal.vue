<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useUiStore } from '../stores/ui';
import { useSettingsStore } from '../stores/settings';
import { useAppStore } from '../stores/app';
import { useNotificationStore } from '../stores/notification';
import { getIpc } from '../ipc';

const ui = useUiStore();
const settingsStore = useSettingsStore();
const app = useAppStore();
const notifications = useNotificationStore();
const targetId = ref('');
const note = ref('');
const hostId = ref('');

const selectedTarget = computed(
  () => (settingsStore.settings.pushTargets ?? []).find((t) => t.id === targetId.value) ?? null
);

const availableHosts = computed(() => {
  const target = selectedTarget.value;
  if (!target || target.type !== 'ssh') {
    return [];
  }
  const all = settingsStore.settings.hostInventory ?? [];
  if (!target.groupIds || target.groupIds.length === 0) {
    return all;
  }
  return all.filter((h) => target.groupIds!.includes(h.groupId));
});

watch(targetId, () => {
  hostId.value = availableHosts.value[0]?.id ?? '';
});

watch(
  () => ui.pushModalOpen,
  (open) => {
    if (open) {
      const targets = settingsStore.settings.pushTargets ?? [];
      targetId.value = targets[0]?.id ?? '';
      note.value = '';
      hostId.value = '';
    }
  }
);

watch(availableHosts, () => {
  if (hostId.value && !availableHosts.value.some((h) => h.id === hostId.value)) {
    hostId.value = availableHosts.value[0]?.id ?? '';
  }
});

function outsideClose() {
  return settingsStore.settings.closeOutside !== false;
}

async function submit() {
  const targetConfig = ui.pushModalTarget;
  if (!targetConfig || !targetId.value) {
    return;
  }
  try {
    const res = await getIpc().send('push:run', {
      workspaceId: targetConfig.workspaceId,
      configId: targetConfig.config.id,
      targetId: targetId.value,
      note: note.value || undefined,
      hostId: selectedTarget.value?.type === 'ssh' ? hostId.value || undefined : undefined
    });
    notifications.add('ok', res.message);
    app.setStatus(res.message);
    ui.closePushModal();
  } catch (error) {
    app.setStatus((error as Error).message, true);
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="ui.pushModalOpen"
      class="ferry-overlay"
      @mousedown.self="outsideClose() && ui.closePushModal()"
    >
      <div class="ferry-modal">
        <div class="ferry-modal-title">推送「{{ ui.pushModalTarget?.config.name }}」</div>
        <select v-model="targetId" class="ferry-input ferry-move-select">
          <option value="" disabled>请选择推送目标</option>
          <option v-for="t in settingsStore.settings.pushTargets ?? []" :key="t.id" :value="t.id">
            {{ t.name }}（{{ t.type }}）
          </option>
        </select>
        <input
          v-model="note"
          class="ferry-input ferry-move-select"
          placeholder="备注 / 提交信息（可选）"
        />
        <template v-if="selectedTarget?.type === 'ssh'">
          <select v-model="hostId" class="ferry-input ferry-move-select">
            <option value="" disabled>请选择要推送的主机</option>
            <option v-for="h in availableHosts" :key="h.id" :value="h.id">
              {{ h.hostname || h.ip }}{{ h.hostname ? '（' + h.ip + '）' : '' }}
            </option>
          </select>
          <div v-if="availableHosts.length === 0" class="ferry-hint">
            目标分组下暂无主机，请先在设置中导入主机
          </div>
        </template>
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.closePushModal()">取消</button>
          <button
            class="ferry-btn primary"
            :disabled="!targetId || (selectedTarget?.type === 'ssh' && !hostId)"
            @click="submit"
          >
            推送
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
