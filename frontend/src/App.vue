<script setup lang="ts">
import { onMounted } from 'vue';
import TitleBar from './components/TitleBar.vue';
import StatusBar from './components/StatusBar.vue';
import { useAppStore } from './stores/app';

const app = useAppStore();

onMounted(async () => {
  try {
    const res = await app.bootstrap();
    if (res.loadErrors.length > 0) {
      app.setStatus(`插件加载 ${res.loadErrors.length} 个失败：${res.loadErrors[0]}`, true);
    } else {
      app.setStatus('就绪');
    }
  } catch (error) {
    app.setStatus('初始化失败：' + (error as Error).message, true);
  }
});
</script>

<template>
  <div class="ferry-shell flex h-full flex-col overflow-hidden">
    <TitleBar />
    <div class="flex min-h-0 flex-1">
      <aside class="ferry-sidebar w-[270px] shrink-0 overflow-y-auto border-r border-[var(--ferry-border-soft)] bg-[var(--ferry-surface)] p-4">
        <div class="text-xl font-semibold">Ferry</div>
        <p class="mt-10 text-sm text-[var(--ferry-text-muted)]">Sidebar（P3 实现）</p>
      </aside>
      <main class="min-w-0 flex-1 overflow-hidden bg-[var(--ferry-bg)]">
        <RouterView />
      </main>
      <aside class="ferry-dock hidden w-[42%] shrink-0 border-l border-[var(--ferry-border-soft)] bg-[#181818]"></aside>
    </div>
    <StatusBar />
  </div>
</template>
