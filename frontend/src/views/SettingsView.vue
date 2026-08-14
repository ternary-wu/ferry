<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useUiStore } from '../stores/ui';
import { useAppStore } from '../stores/app';
import { useSettingsStore } from '../stores/settings';
import { useProjectStore } from '../stores/project';
import { getIpc } from '../ipc';
import type { HostEntry, HostGroup, TrashItem } from '../ipc/types';

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
  notifications: '通知',
  push: '推送'
};
const title = computed(() => titles[ui.settingsCategory] ?? '设置');

const trashItems = ref<TrashItem[]>([]);
const trashLoading = ref(false);
const logPath = ref('');
const hostGroupFilter = ref('');
const importGroupId = ref('default');
const exportFormat = ref<'txt' | 'yaml'>('txt');

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
  const picked = await getIpc().send('file:openDialog', {
    title: '选择 Ferry 存档'
  });
  if (!picked.path) {
    // 用户取消选择，不产生错误
    return;
  }
  const path = picked.path;
  try {
    const res = await getIpc().send('archive:import', { path });
    await projectStore.loadProjects();
    await projectStore.loadNav();
    appStore.setStatus(`存档导入完成：${res.imported} 个配置`);
  } catch (error) {
    appStore.setStatus((error as Error).message, true);
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

async function deletePushTarget(index: number) {
  const targets = [...(settingsStore.settings.pushTargets ?? [])];
  const target = targets[index];
  if (!target) {
    return;
  }
  const ok = await ui.confirm({
    title: '删除推送目标',
    message: `确定删除「${target.name}」？`
  });
  if (!ok) {
    return;
  }
  targets.splice(index, 1);
  await settingsStore.save({ pushTargets: targets });
}

const hostGroups = computed<HostGroup[]>(() => {
  const list = settingsStore.settings.hostGroups ?? [];
  return list.length === 0 ? [{ id: 'default', name: '默认分组' }] : list;
});

const filteredHosts = computed(() => {
  const hosts = settingsStore.settings.hostInventory ?? [];
  if (!hostGroupFilter.value) {
    return hosts;
  }
  return hosts.filter((h) => h.groupId === hostGroupFilter.value);
});

function saveHostGroups(next: HostGroup[]) {
  void settingsStore.save({ hostGroups: next });
}

function saveHosts(next: HostEntry[]) {
  void settingsStore.save({ hostInventory: next });
}

async function createHostGroup() {
  const name = await ui.prompt({ title: '分组名称', placeholder: '输入分组名称' });
  if (!name) {
    return;
  }
  saveHostGroups([
    ...hostGroups.value,
    { id: Date.now() + '' + Math.round(Math.random() * 1e6), name }
  ]);
}

async function renameHostGroup(group: HostGroup) {
  const name = await ui.prompt({ title: '分组名称', defaultValue: group.name });
  if (!name || name === group.name) {
    return;
  }
  saveHostGroups(hostGroups.value.map((g) => (g.id === group.id ? { ...g, name } : g)));
}

async function deleteHostGroup(group: HostGroup) {
  if (group.id === 'default') {
    appStore.setStatus('默认分组不可删除', true);
    return;
  }
  const ok = await ui.confirm({
    title: '删除分组',
    message: `确定删除「${group.name}」？其主机将移到默认分组。`
  });
  if (!ok) {
    return;
  }
  saveHostGroups(hostGroups.value.filter((g) => g.id !== group.id));
  const hosts = settingsStore.settings.hostInventory ?? [];
  saveHosts(hosts.map((h) => (h.groupId === group.id ? { ...h, groupId: 'default' } : h)));
  if (hostGroupFilter.value === group.id) {
    hostGroupFilter.value = '';
  }
}

async function importHosts() {
  const picked = await getIpc().send('file:openDialog', {
    title: '选择主机清单',
    filterName: '主机清单',
    patterns: ['*.txt', '*.yaml', '*.yml']
  });
  if (!picked.path) {
    return;
  }
  try {
    const res = await getIpc().send('hosts:import', {
      path: picked.path,
      groupId: importGroupId.value || 'default'
    });
    await settingsStore.load();
    appStore.setStatus(`导入完成：${res.imported} 台主机，跳过 ${res.skipped} 条`);
  } catch (error) {
    appStore.setStatus((error as Error).message, true);
  }
}

async function exportHosts() {
  const ext = exportFormat.value;
  const defaultName = ext === 'yaml' ? 'hosts.yaml' : 'hosts.txt';
  const picked = await getIpc().send('file:saveDialog', {
    title: '导出主机清单',
    defaultName,
    filterName: ext === 'yaml' ? 'YAML 文件' : '文本文件',
    patterns: [ext === 'yaml' ? '*.yaml' : '*.txt'],
    defaultExt: ext
  });
  if (!picked.path) {
    return;
  }
  try {
    const res = await getIpc().send('hosts:export', {
      path: picked.path,
      format: exportFormat.value,
      groupId: hostGroupFilter.value || undefined
    });
    appStore.setStatus(`已导出：${res.path}`);
  } catch (error) {
    appStore.setStatus((error as Error).message, true);
  }
}

async function addHost() {
  const ip = await ui.prompt({ title: '主机 IP', placeholder: '例如 192.168.1.10' });
  if (!ip) {
    return;
  }
  const hosts = [...(settingsStore.settings.hostInventory ?? [])];
  hosts.push({
    id: Date.now() + '' + Math.round(Math.random() * 1e6),
    ip: ip.trim(),
    port: 22,
    groupId: hostGroupFilter.value || 'default'
  });
  saveHosts(hosts);
}

function updateHost(host: HostEntry, patch: Partial<HostEntry>) {
  saveHosts(
    (settingsStore.settings.hostInventory ?? []).map((h) => (h.id === host.id ? { ...h, ...patch } : h))
  );
}

async function removeHost(host: HostEntry) {
  const ok = await ui.confirm({
    title: '移除主机',
    message: `确定移除 ${host.ip}？`
  });
  if (!ok) {
    return;
  }
  saveHosts((settingsStore.settings.hostInventory ?? []).filter((h) => h.id !== host.id));
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
    <div v-else-if="ui.settingsCategory === 'notifications'" class="ferry-settings-body">
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

    <!-- 推送 -->
    <div v-else class="ferry-settings-body">
      <div class="ferry-settings-section-title">主机清单</div>
      <div class="ferry-settings-row">
        <span class="ferry-settings-label">查看分组</span>
        <select v-model="hostGroupFilter" class="ferry-input ferry-settings-input">
          <option value="">全部</option>
          <option v-for="g in hostGroups" :key="g.id" :value="g.id">{{ g.name }}</option>
        </select>
      </div>
      <div class="ferry-settings-row">
        <span class="ferry-settings-label">导入到分组</span>
        <select v-model="importGroupId" class="ferry-input ferry-settings-input">
          <option v-for="g in hostGroups" :key="g.id" :value="g.id">{{ g.name }}</option>
        </select>
        <button class="ferry-btn small" @click="importHosts">导入</button>
      </div>
      <div class="ferry-settings-row">
        <span class="ferry-settings-label">导出格式</span>
        <select v-model="exportFormat" class="ferry-input ferry-settings-input">
          <option value="txt">txt</option>
          <option value="yaml">yaml</option>
        </select>
        <button class="ferry-btn small" @click="exportHosts">导出</button>
      </div>
      <div class="ferry-settings-row">
        <span class="ferry-settings-label">分组管理</span>
        <button class="ferry-btn small" @click="createHostGroup">＋ 新建</button>
        <button
          v-for="g in hostGroups"
          :key="g.id"
          class="ferry-btn small"
          :title="g.name"
          @click="renameHostGroup(g)"
        >
          ✎ {{ g.name }}
        </button>
        <button
          v-for="g in hostGroups.filter((x) => x.id !== 'default')"
          :key="'del-' + g.id"
          class="ferry-btn small danger"
          @click="deleteHostGroup(g)"
        >
          删除 {{ g.name }}
        </button>
      </div>
      <div class="ferry-settings-row">
        <span class="ferry-settings-label">主机</span>
        <button class="ferry-btn small" @click="addHost">＋ 添加</button>
      </div>
      <div v-if="filteredHosts.length === 0" class="ferry-hint">暂无主机</div>
      <div v-for="host in filteredHosts" :key="host.id" class="ferry-settings-row">
        <input :value="host.ip" class="ferry-input ferry-settings-input" disabled />
        <input
          :value="host.hostname ?? ''"
          class="ferry-input ferry-settings-input"
          placeholder="hostname"
          @change="updateHost(host, { hostname: ($event.target as HTMLInputElement).value })"
        />
        <input
          type="number"
          :value="host.port"
          class="ferry-input ferry-settings-input"
          @change="updateHost(host, { port: Number(($event.target as HTMLInputElement).value) || 22 })"
        />
        <select
          :value="host.groupId"
          class="ferry-input ferry-settings-input"
          @change="updateHost(host, { groupId: ($event.target as HTMLSelectElement).value })"
        >
          <option v-for="g in hostGroups" :key="g.id" :value="g.id">{{ g.name }}</option>
        </select>
        <button class="ferry-btn small danger" @click="removeHost(host)">移除</button>
      </div>

      <div class="ferry-settings-section-title">推送目标</div>
      <div class="ferry-settings-row">
        <span class="ferry-settings-label">推送目标</span>
        <button class="ferry-btn small" @click="ui.openPushTargetModal()">＋ 新增</button>
      </div>
      <div v-if="(settingsStore.settings.pushTargets ?? []).length === 0" class="ferry-hint">
        暂无推送目标
      </div>
      <div
        v-for="(target, index) in settingsStore.settings.pushTargets ?? []"
        :key="target.id"
        class="ferry-settings-row"
      >
        <span class="ferry-settings-value">
          {{ target.name }}（{{ target.type }} · {{
            target.type === 'ssh' ? target.remoteDir || target.remotePath : target.remotePath
          }}{{ target.branch ? ' · ' + target.branch : '' }}）
        </span>
        <button class="ferry-btn small" @click="ui.openPushTargetModal(index)">编辑</button>
        <button class="ferry-btn small danger" @click="deletePushTarget(index)">删除</button>
      </div>
    </div>
  </div>
</template>
