<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useUiStore } from '../stores/ui';
import { useProjectStore } from '../stores/project';
import { useAppStore } from '../stores/app';
import { useNotificationStore } from '../stores/notification';
import { useSettingsStore } from '../stores/settings';
import { getIpc } from '../ipc';

const ui = useUiStore();
const projectStore = useProjectStore();
const app = useAppStore();
const notifications = useNotificationStore();
const settingsStore = useSettingsStore();
const projectId = ref('');

function outsideClose() {
  return settingsStore.settings.closeOutside !== false;
}

watch(
  () => ui.exportProjectOpen,
  (open) => {
    if (open) {
      projectId.value = projectStore.currentProjectId || projectStore.projects[0]?.id || '';
    }
  }
);

const selected = computed(
  () => projectStore.projects.find((p) => p.id === projectId.value) ?? null
);

async function submit() {
  if (!selected.value) {
    return;
  }
  const picked = await getIpc().send('file:saveDialog', {
    title: '导出项目存档',
    defaultName: selected.value.name + '.ferry',
    filterName: 'Ferry 存档',
    patterns: ['*.ferry'],
    defaultExt: 'ferry'
  });
  if (!picked.path) {
    return;
  }
  try {
    const res = await getIpc().send('archive:exportProject', {
      projectId: selected.value.id,
      path: picked.path
    });
    notifications.add('ok', `已导出：${res.path}`);
    app.setStatus(`已导出：${res.path}`);
    ui.closeExportProject();
  } catch (error) {
    app.setStatus('导出失败：' + (error as Error).message, true);
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="ui.exportProjectOpen"
      class="ferry-overlay"
      @mousedown.self="outsideClose() && ui.closeExportProject()"
    >
      <div class="ferry-modal">
        <div class="ferry-modal-title">导出项目</div>
        <select v-model="projectId" class="ferry-input ferry-move-select">
          <option v-for="p in projectStore.projects" :key="p.id" :value="p.id">
            {{ p.name }}
          </option>
        </select>
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.closeExportProject()">取消</button>
          <button class="ferry-btn primary" @click="submit">导出</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
