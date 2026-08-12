<script setup lang="ts">
import { ref, watch } from 'vue';
import { useUiStore } from '../stores/ui';
import { useProjectStore } from '../stores/project';
import { useConfigStore } from '../stores/config';
import { useAppStore } from '../stores/app';
import { useNotificationStore } from '../stores/notification';

const ui = useUiStore();
const projectStore = useProjectStore();
const configStore = useConfigStore();
const app = useAppStore();
const notifications = useNotificationStore();
const target = ref('');

watch(
  () => ui.moveOpen,
  (open) => {
    if (open) {
      target.value = ui.moveTarget?.workspaceId ?? '';
    }
  }
);

async function submit() {
  const targetConfig = ui.moveTarget;
  if (!targetConfig) {
    return;
  }
  try {
    await projectStore.moveConfig(targetConfig.config.id, target.value);
    if (configStore.current?.id === targetConfig.config.id) {
      await configStore.open(target.value, targetConfig.config.id);
    }
    await projectStore.loadNav();
    notifications.add('ok', `已移动「${targetConfig.config.name}」`);
    ui.closeMove();
  } catch (error) {
    app.setStatus('移动失败：' + (error as Error).message, true);
  }
}
</script>

<template>
  <Teleport to="body">
    <div v-if="ui.moveOpen" class="ferry-overlay" @mousedown.self="ui.closeMove()">
      <div class="ferry-modal">
        <div class="ferry-modal-title">移动「{{ ui.moveTarget?.config.name }}」到：</div>
        <select v-model="target" class="ferry-input ferry-move-select">
          <option value="">（未归类配置）</option>
          <option v-for="ws in projectStore.nav.workspaces" :key="ws.id" :value="ws.id">
            {{ ws.name }}
          </option>
        </select>
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.closeMove()">取消</button>
          <button class="ferry-btn primary" @click="submit">移动</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
