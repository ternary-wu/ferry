<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useUiStore } from '../stores/ui';
import { useAppStore } from '../stores/app';
import { useSettingsStore } from '../stores/settings';
import { useProjectStore } from '../stores/project';
import { getIpc } from '../ipc';
import type { TrashItem } from '../ipc/types';

const ui = useUiStore();
const appStore = useAppStore();
const settingsStore = useSettingsStore();
const projectStore = useProjectStore();

const titles: Record<string, string> = {
  general: '常规',
  appearance: '外观',
  plugins: '插件管理',
  modules: '模块管理',
  storage: '存储',
  notifications: '通知'
};
const title = computed(() => titles[ui.settingsCategory] ?? '设置');

const trashItems = ref<TrashItem[]>([]);
const trashLoading = ref(false);
const importPath = ref('');
const logPath = ref('');

// ---------- 常规 ----------

const restoreProject = computed({
  get: () => settingsStore.settings.restoreProject !== false,
  set: (value: boolean) => void settingsStore.save({ restoreProject: value })
});

const closeOutside = computed({
  get: () => settingsStore.settings.closeOutside !== false,
  set: (value: boolean) => void settingsStore.save({ closeOutside: value })
});

const defaultPath = computed({
  get: () => settingsStore.settings.defaultPath ?? '',
  set: (value: string) => void settingsStore.save({ defaultPath: value })
});

async function importArchive() {
  const path = importPath.value.trim();
  if (!path) {
    return;
  }
  try {
    const res = await getIpc().send('archive:import', { path });
    importPath.value = '';
    await projectStore.loadProjects();
    await projectStore.loadNav();
    appStore.setStatus(`存档导入完成：${res.imported} 个配置`);
  } catch (error) {
    appStore.setStatus('导入失败：' + (error as Error).message, true);
  }
}

async function pickImportFile() {
  const res = await getIpc().send('file:openDialog', {
    title: '选择存档包',
    patterns: ['*.zip']
  });
  if (res.path) {
    importPath.value = res.path;
  }
}

async function loadLogPath() {
  try {
    const res = await getIpc().send('logs:path', {});
    logPath.value = res.path;
  } catch {
    // 日志路径读取失败不阻塞设置页
  }
}

function openLog() {
  void getIpc().send('logs:open', {});
}

// ---------- 外观 ----------

const theme = computed({
  get: () => settingsStore.settings.theme ?? 'dark',
  set: (value: string) =>
    void settingsStore.save({ theme: value as 'dark' | 'light' | 'system' })
});

const animations = computed({
  get: () => settingsStore.settings.animations !== false,
  set: (value: boolean) => void settingsStore.save({ animations: value })
});

const tooltipDelay = computed({
  get: () => settingsStore.settings.tooltipDelay ?? 250,
  set: (value: number) => void settingsStore.save({ tooltipDelay: value })
});

const tooltipEnabled = computed({
  get: () => settingsStore.settings.tooltipEnabled !== false,
  set: (value: boolean) => void settingsStore.save({ tooltipEnabled: value })
});

const tooltipDelayEnabled = computed({
  get: () => settingsStore.settings.tooltipDelayEnabled !== false,
  set: (value: boolean) => void settingsStore.save({ tooltipDelayEnabled: value })
});

const tooltipShowDelay = computed({
  get: () => settingsStore.settings.tooltipShowDelay ?? 250,
  set: (value: number) => void settingsStore.save({ tooltipShowDelay: value })
});

const tooltipShowDelayEnabled = computed({
  get: () => settingsStore.settings.tooltipShowDelayEnabled === true,
  set: (value: boolean) => void settingsStore.save({ tooltipShowDelayEnabled: value })
});

const showFileExtension = computed({
  get: () => settingsStore.settings.showFileExtension === true,
  set: (value: boolean) => void settingsStore.save({ showFileExtension: value })
});

// ---------- 插件管理 ----------

function isPluginEnabled(key: string): boolean {
  return settingsStore.settings.pluginDisabled?.[key] !== true;
}

async function togglePlugin(key: string, enabled: boolean) {
  const current = { ...(settingsStore.settings.pluginDisabled ?? {}) };
  if (enabled) {
    delete current[key];
  } else {
    current[key] = true;
  }
  await settingsStore.save({ pluginDisabled: current });
}

// ---------- 存储 / 回收站 ----------

const trashDays = computed({
  get: () => settingsStore.settings.trashDays ?? 30,
  set: (value: number) => void settingsStore.save({ trashDays: value })
});

const trashSizeMB = computed({
  get: () => settingsStore.settings.trashSizeMB ?? 2048,
  set: (value: number) => void settingsStore.save({ trashSizeMB: value })
});

async function refreshTrash() {
  trashLoading.value = true;
  try {
    const res = await getIpc().send('trash:list', {});
    let items = res.items ?? [];
    const days = Number(settingsStore.settings.trashDays ?? 30) || 30;
    const maxMB = Number(settingsStore.settings.trashSizeMB ?? 2048) || 2048;
    const now = Date.now();
    const expired = items.filter(
      (item) => now - new Date(item.modified).getTime() > days * 86400000
    );
    for (const item of expired) {
      await getIpc().send('trash:delete', { path: item.path }).catch(() => {});
    }
    items = items.filter((item) => !expired.includes(item));

    let total = items.reduce((sum, item) => sum + item.size, 0);
    const sorted = [...items].sort(
      (a, b) => new Date(a.modified).getTime() - new Date(b.modified).getTime()
    );
    while (total > maxMB * 1048576 && sorted.length > 0) {
      const oldest = sorted.shift()!;
      await getIpc().send('trash:delete', { path: oldest.path }).catch(() => {});
      total -= oldest.size;
      items = items.filter((item) => item !== oldest);
    }
    trashItems.value = items;
  } catch (error) {
    appStore.setStatus('回收站读取失败：' + (error as Error).message, true);
  } finally {
    trashLoading.value = false;
  }
}

async function restoreTrash(item: TrashItem) {
  try {
    const res = await getIpc().send('archive:import', { path: item.path });
    // 还原成功后从回收站移除该文件，避免重复点击产生多份副本
    await getIpc().send('trash:delete', { path: item.path });
    await projectStore.loadProjects();
    await projectStore.loadNav();
    appStore.setStatus(`已还原：${res.imported} 个配置`);
    await refreshTrash();
  } catch (error) {
    appStore.setStatus('还原失败：' + (error as Error).message, true);
  }
}

async function permanentDelete(item: TrashItem) {
  const ok = await ui.confirm({
    title: '永久删除',
    message: `确定永久删除「${item.name}」？此操作不可恢复。`
  });
  if (!ok) {
    return;
  }
  try {
    await getIpc().send('trash:delete', { path: item.path });
    await refreshTrash();
  } catch (error) {
    appStore.setStatus('删除失败：' + (error as Error).message, true);
  }
}

// ---------- 通知 ----------

const notifyEnabled = computed({
  get: () => settingsStore.settings.notifyEnabled !== false,
  set: (value: boolean) => void settingsStore.save({ notifyEnabled: value })
});

const notifyStyle = computed({
  get: () => settingsStore.settings.notifyStyle ?? 'panel',
  set: (value: string) =>
    void settingsStore.save({ notifyStyle: value as 'panel' | 'toast' })
});

onMounted(() => {
  void loadLogPath();
  void refreshTrash();
});
</script>

<template>
  <div class="flex h-full flex-col overflow-y-auto p-6">
    <div class="text-lg font-semibold">{{ title }}</div>

    <!-- 常规 -->
    <div v-if="ui.settingsCategory === 'general'" class="ferry-settings-body">
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">启动时恢复上次项目</span>
        <input v-model="restoreProject" type="checkbox" class="ferry-check" />
      </label>
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">点击弹窗外部关闭</span>
        <input v-model="closeOutside" type="checkbox" class="ferry-check" />
      </label>
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">默认路径（导出存档时使用）</span>
        <input v-model="defaultPath" class="ferry-input ferry-settings-input" placeholder="例如 D:\configs" />
      </label>
      <div class="ferry-settings-row">
        <span class="ferry-settings-label">导入存档包</span>
        <button class="ferry-btn small" @click="pickImportFile">选择文件…</button>
        <span class="ferry-settings-value">{{ importPath || '未选择' }}</span>
        <button class="ferry-btn small" @click="importArchive">导入</button>
      </div>
      <div class="ferry-settings-row">
        <span class="ferry-settings-label">日志文件</span>
        <span class="ferry-settings-value">{{ logPath }}</span>
        <button class="ferry-btn small" @click="openLog">打开</button>
      </div>
    </div>

    <!-- 外观 -->
    <div v-else-if="ui.settingsCategory === 'appearance'" class="ferry-settings-body">
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">主题</span>
        <select v-model="theme" class="ferry-input ferry-settings-input">
          <option value="dark">深色</option>
          <option value="light">浅色</option>
          <option value="system">跟随系统</option>
        </select>
      </label>
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">动画</span>
        <input v-model="animations" type="checkbox" class="ferry-check" />
      </label>
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">悬停显示字段说明</span>
        <input v-model="tooltipEnabled" type="checkbox" class="ferry-check" />
      </label>
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">显示配置文件扩展名</span>
        <input v-model="showFileExtension" type="checkbox" class="ferry-check" />
      </label>
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">悬停延迟显示（ms）</span>
        <input v-model="tooltipShowDelayEnabled" type="checkbox" class="ferry-check" />
        <input
          v-model.number="tooltipShowDelay"
          type="number"
          class="ferry-input ferry-settings-input"
          :disabled="!tooltipShowDelayEnabled"
        />
      </label>
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">移开延迟关闭（ms）</span>
        <input v-model="tooltipDelayEnabled" type="checkbox" class="ferry-check" />
        <input
          v-model.number="tooltipDelay"
          type="number"
          class="ferry-input ferry-settings-input"
          :disabled="!tooltipDelayEnabled"
        />
      </label>
    </div>

    <!-- 插件管理 -->
    <div v-else-if="ui.settingsCategory === 'plugins'" class="ferry-settings-body">
      <div v-if="appStore.plugins.length === 0" class="ferry-hint">暂无插件</div>
      <label v-for="plugin in appStore.plugins" :key="plugin.key" class="ferry-settings-row">
        <span class="ferry-settings-label">
          🌐 {{ plugin.name }}
          <small class="ferry-settings-sub">v{{ plugin.version }}</small>
          <span v-if="plugin.loadErrors.length" class="ferry-badge missing">{{ plugin.loadErrors[0] }}</span>
        </span>
        <input
          :checked="isPluginEnabled(plugin.key)"
          type="checkbox"
          class="ferry-check"
          @change="togglePlugin(plugin.key, ($event.target as HTMLInputElement).checked)"
        />
      </label>
    </div>

    <!-- 模块管理 -->
    <div v-else-if="ui.settingsCategory === 'modules'" class="ferry-settings-body">
      <div class="ferry-hint">暂无已安装模块（未来动态模块在此显示）</div>
    </div>

    <!-- 存储 -->
    <div v-else-if="ui.settingsCategory === 'storage'" class="ferry-settings-body">
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">回收站保留时间（天）</span>
        <input v-model.number="trashDays" type="number" class="ferry-input ferry-settings-input" />
      </label>
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">回收站最大空间（MB）</span>
        <input v-model.number="trashSizeMB" type="number" class="ferry-input ferry-settings-input" />
      </label>
      <div class="ferry-settings-section-title">回收站</div>
      <div v-if="trashLoading" class="ferry-hint">加载中…</div>
      <div v-else-if="trashItems.length === 0" class="ferry-hint">回收站为空</div>
      <div v-for="item in trashItems" :key="item.path" class="ferry-settings-row">
        <span class="ferry-settings-value">
          {{ item.name }}（{{ (item.size / 1024).toFixed(0) }}KB · {{ item.modified }}）
        </span>
        <button class="ferry-btn small" @click="restoreTrash(item)">还原</button>
        <button class="ferry-btn small danger" @click="permanentDelete(item)">永久删除</button>
      </div>
    </div>

    <!-- 通知 -->
    <div v-else class="ferry-settings-body">
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">启用通知</span>
        <input v-model="notifyEnabled" type="checkbox" class="ferry-check" />
      </label>
      <label class="ferry-settings-row">
        <span class="ferry-settings-label">提示方式</span>
        <select v-model="notifyStyle" class="ferry-input ferry-settings-input">
          <option value="panel">通知面板</option>
          <option value="toast">轻提示</option>
        </select>
      </label>
      <div class="ferry-hint">通知面板与 Toast 将在下一阶段接线；此处持久化开关与样式。</div>
    </div>
  </div>
</template>
