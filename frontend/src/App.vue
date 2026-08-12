<script setup lang="ts">
import { onMounted } from 'vue';
import TitleBar from './components/TitleBar.vue';
import StatusBar from './components/StatusBar.vue';
import Sidebar from './components/Sidebar.vue';
import ContextMenu from './components/ContextMenu.vue';
import ModalHost from './components/ModalHost.vue';
import WizardModal from './components/WizardModal.vue';
import { useAppStore } from './stores/app';
import { useProjectStore } from './stores/project';
import { useSettingsStore } from './stores/settings';
import { useUiStore } from './stores/ui';
import { setIpcLatencyListener } from './ipc';

const app = useAppStore();
const projectStore = useProjectStore();
const settingsStore = useSettingsStore();
const ui = useUiStore();

onMounted(async () => {
  setIpcLatencyListener((ms) => app.setLatency(ms));
  document.addEventListener('contextmenu', (event) => event.preventDefault());
  document.addEventListener('click', () => ui.closeMenu());
  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') {
      return;
    }
    ui.closeMenu();
    if (ui.promptOpen) {
      ui.resolvePrompt(null);
    }
    if (ui.confirmOpen) {
      ui.resolveConfirm(false);
    }
  });
  try {
    const res = await app.bootstrap();
    if (res.loadErrors.length > 0) {
      app.setStatus(`插件加载 ${res.loadErrors.length} 个失败：${res.loadErrors[0]}`, true);
    } else {
      app.setStatus('就绪');
    }
    await settingsStore.load();
    const restoreProject = settingsStore.settings.restoreProject !== false;
    const preferredId =
      restoreProject && settingsStore.settings.lastProjectId
        ? settingsStore.settings.lastProjectId
        : undefined;
    await projectStore.loadProjects(preferredId);
    await projectStore.loadNav();
  } catch (error) {
    app.setStatus('初始化失败：' + (error as Error).message, true);
  }
});
</script>

<template>
  <div class="ferry-shell flex h-full flex-col overflow-hidden">
    <TitleBar />
    <div class="flex min-h-0 flex-1">
      <Sidebar />
      <main class="min-w-0 flex-1 overflow-hidden bg-[var(--ferry-bg)]">
        <RouterView />
      </main>
      <aside class="ferry-dock hidden w-[42%] shrink-0 border-l border-[var(--ferry-border-soft)] bg-[#181818]"></aside>
    </div>
    <StatusBar />
    <ContextMenu />
    <ModalHost />
    <WizardModal />
  </div>
</template>
