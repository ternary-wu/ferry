<script setup lang="ts">
import { ref, watch } from 'vue';
import { useUiStore } from '../stores/ui';
import { useProjectStore } from '../stores/project';
import { useAppStore } from '../stores/app';
import { useConfigStore } from '../stores/config';
import { useSettingsStore } from '../stores/settings';
import { splitName, joinName } from '../utils/nameParts';

const ui = useUiStore();
const projectStore = useProjectStore();
const app = useAppStore();
const configStore = useConfigStore();
const settingsStore = useSettingsStore();

function outsideClose() {
  return settingsStore.settings.closeOutside !== false;
}

const file = ref('');
const ext = ref('');
const extUnlocked = ref(false);

watch(
  () => ui.renameOpen,
  (open) => {
    if (!open || !ui.renameTarget) {
      return;
    }
    const parts = splitName(ui.renameTarget.config.name);
    file.value = parts.file;
    ext.value = parts.ext;
    extUnlocked.value = false;
  }
);

async function submit() {
  const target = ui.renameTarget;
  if (!target) {
    return;
  }
  const name = joinName(file.value, ext.value).trim();
  if (!name || name === target.config.name) {
    ui.closeRename();
    return;
  }
  try {
    const res = await projectStore.renameConfig(target.config.id, target.workspaceId, name);
    await projectStore.loadNav();
    if (configStore.current?.id === target.config.id && configStore.current) {
      configStore.current.name = res.name;
    }
    ui.closeRename();
  } catch (error) {
    app.setStatus('重命名失败：' + (error as Error).message, true);
  }
}
</script>

<template>
  <Teleport to="body">
    <div v-if="ui.renameOpen" class="ferry-overlay" @mousedown.self="outsideClose() && ui.closeRename()">
      <div class="ferry-modal">
        <div class="ferry-modal-title">重命名「{{ ui.renameTarget?.config.name }}」</div>
        <div class="ferry-wizard-name-row">
          <input v-model="file" class="ferry-input" placeholder="文件名" />
          <span class="ferry-wizard-dot">.</span>
          <input
            v-model="ext"
            class="ferry-input ferry-wizard-ext"
            :disabled="!extUnlocked"
            placeholder="扩展名"
          />
        </div>
        <label class="ferry-wizard-ext-toggle">
          <input v-model="extUnlocked" type="checkbox" />
          <span>允许修改扩展名（如果改变扩展名，可能导致文件不可用）</span>
        </label>
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.closeRename()">取消</button>
          <button class="ferry-btn primary" @click="submit">重命名</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
