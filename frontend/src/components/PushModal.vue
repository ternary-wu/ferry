<script setup lang="ts">
import { ref, watch } from 'vue';
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

watch(
  () => ui.pushModalOpen,
  (open) => {
    if (open) {
      const targets = settingsStore.settings.pushTargets ?? [];
      targetId.value = targets[0]?.id ?? '';
      note.value = '';
    }
  }
);

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
      note: note.value || undefined
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
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.closePushModal()">取消</button>
          <button class="ferry-btn primary" :disabled="!targetId" @click="submit">推送</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
