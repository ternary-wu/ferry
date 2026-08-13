<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useProjectStore } from '../stores/project';
import { useConfigStore } from '../stores/config';
import { useSettingsStore } from '../stores/settings';
import { useUiStore, type ContextMenuItem } from '../stores/ui';
import { useAppStore } from '../stores/app';
import { useNotificationStore } from '../stores/notification';
import { useWizardStore } from '../stores/wizard';
import { getIpc } from '../ipc';
import { splitName } from '../utils/nameParts';
import type { ConfigInfo, NavWorkspace, ProjectInfo } from '../ipc/types';

const route = useRoute();
const router = useRouter();
const projectStore = useProjectStore();
const configStore = useConfigStore();
const settingsStore = useSettingsStore();
const ui = useUiStore();
const app = useAppStore();
const notifications = useNotificationStore();
const wizardStore = useWizardStore();

const projectMenuOpen = ref(false);
const wsCollapsed = ref(false);
const cfgCollapsed = ref(false);
const wsOpen = ref<Record<string, boolean>>({});
type DragSessionState =
  | { kind: 'config'; config: ConfigInfo; sourceWorkspaceId: string }
  | { kind: 'workspace'; workspace: NavWorkspace };

const dragSession = ref<DragSessionState | null>(null);
const dropTarget = ref<DropTargetState | null>(null);

const isSettings = computed(() => route.name === 'settings');
const showExt = computed(() => settingsStore.settings.showFileExtension === true);
const currentProject = computed(() =>
  projectStore.projects.find((p) => p.id === projectStore.currentProjectId)
);

function displayName(config: ConfigInfo): string {
  return showExt.value ? config.name : splitName(config.name).file;
}

const categories = [
  { id: 'general', name: '常规' },
  { id: 'appearance', name: '外观' },
  { id: 'plugins', name: '插件管理' },
  { id: 'modules', name: '模块管理' },
  { id: 'storage', name: '存储' },
  { id: 'notifications', name: '通知' }
];

function isWsOpen(id: string): boolean {
  return wsOpen.value[id] !== false;
}

function toggleWsOpen(id: string) {
  wsOpen.value = { ...wsOpen.value, [id]: !isWsOpen(id) };
}

async function selectProject(project: ProjectInfo) {
  projectMenuOpen.value = false;
  if (project.id === projectStore.currentProjectId) {
    return;
  }
  projectStore.selectProject(project.id);
  void settingsStore.save({ lastProjectId: project.id });
  configStore.close();
  await projectStore.loadNav();
  if (configStore.isOpen) {
    void router.push('/');
  }
}

async function createProject() {
  projectMenuOpen.value = false;
  const name = await ui.prompt({ title: '项目名称', placeholder: '输入项目名称' });
  if (!name) {
    return;
  }
  await projectStore.createProject(name);
  await projectStore.loadNav();
}

async function renameProject() {
  const project = currentProject.value;
  if (!project) {
    return;
  }
  projectMenuOpen.value = false;
  const name = await ui.prompt({ title: '项目名称', defaultValue: project.name });
  if (!name || name === project.name) {
    return;
  }
  await projectStore.renameProject(project.id, name);
}

async function deleteProject() {
  const project = currentProject.value;
  if (!project) {
    return;
  }
  projectMenuOpen.value = false;
  const ok = await ui.confirm({
    title: '删除项目',
    message: `确定删除「${project.name}」及其全部工作空间、配置与版本？`
  });
  if (!ok) {
    return;
  }
  await projectStore.deleteProject(project.id);
  await projectStore.loadNav();
}

async function openConfig(config: ConfigInfo, workspaceId: string) {
  await configStore.open(workspaceId, config.id);
  await router.push('/editor');
}

async function renameWorkspace(workspace: NavWorkspace) {
  const name = await ui.prompt({ title: '工作空间名称', defaultValue: workspace.name });
  if (!name || name === workspace.name) {
    return;
  }
  await projectStore.renameWorkspace(workspace.id, name);
  await projectStore.loadNav();
}

function joinPath(dir: string, name: string): string {
  if (!dir) {
    return name;
  }
  return dir.endsWith('\\') || dir.endsWith('/') ? dir + name : dir + '\\' + name;
}

async function exportConfig(config: ConfigInfo, workspaceId: string) {
  const defaultName = joinPath(settingsStore.settings.defaultPath ?? '', config.name);
  const picked = await getIpc().send('file:saveDialog', {
    title: '导出配置',
    defaultName,
    patterns: ['*.*']
  });
  if (!picked.path) {
    return;
  }
  try {
    const res = await getIpc().send('config:exportFile', {
      workspaceId,
      configId: config.id,
      path: picked.path
    });
    notifications.add('ok', `已导出：${res.path}`);
    app.setStatus(`已导出：${res.path}`);
  } catch (error) {
    app.setStatus('导出失败：' + (error as Error).message, true);
  }
}

async function duplicateConfig(config: ConfigInfo, workspaceId: string) {
  try {
    const res = await projectStore.duplicateConfig(config.id, workspaceId);
    await projectStore.loadNav();
    notifications.add('ok', `已复制为「${res.name}」`);
  } catch (error) {
    app.setStatus('复制失败：' + (error as Error).message, true);
  }
}

async function renameConfig(config: ConfigInfo, workspaceId: string) {
  ui.openRename(config, workspaceId);
}

async function resetConfig(config: ConfigInfo, workspaceId: string) {
  const ok = await ui.confirm({
    title: '恢复默认配置',
    message: `确定将「${config.name}」恢复为插件默认？`
  });
  if (!ok) {
    return;
  }
  if (configStore.current?.id !== config.id) {
    await openConfig(config, workspaceId);
  }
  try {
    await configStore.resetCurrent();
    app.setStatus(`已恢复默认配置：${config.name}`);
  } catch (error) {
    app.setStatus('恢复默认失败：' + (error as Error).message, true);
  }
}

async function deleteConfig(config: ConfigInfo, workspaceId: string) {
  const ok = await ui.confirm({
    title: '删除配置',
    message: `确定删除「${config.name}」？将先存档到回收站，可还原。`
  });
  if (!ok) {
    return;
  }
  try {
    const dirRes = await getIpc().send('app:dataDir', {});
    const zipPath = joinPath(
      joinPath(dirRes.path, 'trash'),
      `${config.name}-${Date.now()}.zip`
    );
    await getIpc().send('archive:exportConfig', {
      workspaceId,
      configId: config.id,
      path: zipPath
    });
    await getIpc().send('config:delete', { workspaceId, configId: config.id });
    const wasCurrent = configStore.current?.id === config.id;
    if (wasCurrent) {
      configStore.close();
      await router.push('/');
    }
    await projectStore.loadNav();
    notifications.add('ok', `配置「${config.name}」已移入回收站`);
  } catch (error) {
    app.setStatus('删除失败：' + (error as Error).message, true);
  }
}

async function exportWorkspace(workspace: NavWorkspace) {
  const defaultName = joinPath(settingsStore.settings.defaultPath ?? '', workspace.name + '.zip');
  const picked = await getIpc().send('file:saveDialog', {
    title: '导出工作空间存档',
    defaultName,
    patterns: ['*.zip']
  });
  if (!picked.path) {
    return;
  }
  try {
    const res = await getIpc().send('archive:exportWorkspace', {
      workspaceId: workspace.id,
      path: picked.path
    });
    notifications.add('ok', `已导出：${res.path}`);
  } catch (error) {
    app.setStatus('导出失败：' + (error as Error).message, true);
  }
}

async function deleteWorkspace(workspace: NavWorkspace) {
  const ok = await ui.confirm({
    title: '删除工作空间',
    message: `确定删除「${workspace.name}」？将先存档到回收站，可还原。`
  });
  if (!ok) {
    return;
  }
  try {
    const dirRes = await getIpc().send('app:dataDir', {});
    const zipPath = joinPath(
      joinPath(dirRes.path, 'trash'),
      `${workspace.name}-${Date.now()}.zip`
    );
    await getIpc().send('archive:exportWorkspace', {
      workspaceId: workspace.id,
      path: zipPath
    });
    if (configStore.workspaceId === workspace.id) {
      configStore.close();
      await router.push('/');
    }
    await projectStore.deleteWorkspace(workspace.id);
    await projectStore.loadNav();
    notifications.add('ok', `工作空间「${workspace.name}」已移入回收站`);
  } catch (error) {
    app.setStatus('删除失败：' + (error as Error).message, true);
  }
}

function workspaceMenuItems(workspace: NavWorkspace): ContextMenuItem[] {
  return [
    { text: '快速新建配置', onClick: () => wizardStore.openWizard({ workspaceId: workspace.id }) },
    { text: '重命名', onClick: () => void renameWorkspace(workspace) },
    { text: '导出存档', onClick: () => void exportWorkspace(workspace) },
    { text: '删除', danger: true, onClick: () => void deleteWorkspace(workspace) }
  ];
}

function configMenuItems(config: ConfigInfo, workspaceId: string): ContextMenuItem[] {
  return [
    { text: '查看', onClick: () => void openConfig(config, workspaceId) },
    { text: '重命名', onClick: () => void renameConfig(config, workspaceId) },
    { text: '导出', onClick: () => void exportConfig(config, workspaceId) },
    { text: '复制', onClick: () => void duplicateConfig(config, workspaceId) },
    { text: '移动', onClick: () => ui.openMove(config, workspaceId) },
    { text: '历史', onClick: () => ui.openHistory(config, workspaceId) },
    { text: '回滚', onClick: () => ui.openHistory(config, workspaceId) },
    { text: '恢复全部默认配置', onClick: () => void resetConfig(config, workspaceId) },
    { text: '推送', disabled: true },
    { text: '动态模块项', disabled: true },
    { text: '删除', danger: true, onClick: () => void deleteConfig(config, workspaceId) }
  ];
}

function openWorkspaceMenu(event: MouseEvent, workspace: NavWorkspace) {
  ui.openMenu(workspaceMenuItems(workspace), event.clientX, event.clientY);
}

function openConfigMenu(event: MouseEvent, config: ConfigInfo, workspaceId: string) {
  ui.openMenu(configMenuItems(config, workspaceId), event.clientX, event.clientY);
}

function openProjectContextMenu(event: MouseEvent) {
  ui.openMenu(
    [
      { text: '新建项目', onClick: () => void createProject() },
      { text: '重命名', onClick: () => void renameProject() },
      { text: '删除项目', danger: true, onClick: () => void deleteProject() }
    ],
    event.clientX,
    event.clientY
  );
}

function goSettings() {
  ui.settingsCategory = 'general';
  void router.push('/settings');
}

function goHome() {
  void router.push('/');
}

// ---------- 拖拽（统一 Drag Session） ----------

type DropMode =
  | 'workspace'
  | 'workspace-sort'
  | 'workspace-reorder'
  | 'unassigned'
  | 'create-workspace';

interface DropTargetState {
  mode: DropMode;
  workspaceId?: string;
  configId?: string;
  before?: boolean;
}

function configsOf(workspaceId: string): ConfigInfo[] {
  return workspaceId === ''
    ? projectStore.nav.unassigned
    : projectStore.nav.workspaces.find((w) => w.id === workspaceId)?.configs ?? [];
}

function applyLocalOrder(workspaceId: string, ids: string[]) {
  const byId = new Map(configsOf(workspaceId).map((c) => [c.id, c]));
  const ordered = ids
    .map((id) => byId.get(id))
    .filter((c): c is ConfigInfo => Boolean(c));
  if (workspaceId === '') {
    projectStore.nav.unassigned = ordered;
  } else {
    const ws = projectStore.nav.workspaces.find((w) => w.id === workspaceId);
    if (ws) {
      ws.configs = ordered;
    }
  }
}

function onConfigDragStart(event: DragEvent, config: ConfigInfo, workspaceId: string) {
  dragSession.value = { kind: 'config', config, sourceWorkspaceId: workspaceId };
  dropTarget.value = null;
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', config.id);
  }
}

function onWorkspaceDragStart(event: DragEvent, workspace: NavWorkspace) {
  dragSession.value = { kind: 'workspace', workspace };
  dropTarget.value = null;
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', workspace.id);
  }
}

function onConfigDragOver(event: DragEvent, workspaceId: string, configId: string) {
  if (dragSession.value?.kind !== 'config') {
    return;
  }
  event.preventDefault();
  event.stopPropagation();
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
  dropTarget.value = {
    mode: workspaceId === '' ? 'unassigned' : 'workspace-sort',
    workspaceId,
    configId,
    before: event.clientY < rect.top + rect.height / 2
  };
}

function onConfigDragLeave(event: DragEvent, workspaceId: string, configId: string) {
  if (event.relatedTarget && (event.currentTarget as HTMLElement).contains(event.relatedTarget as Node)) {
    return;
  }
  if (
    (dropTarget.value?.mode === 'workspace-sort' || dropTarget.value?.mode === 'unassigned') &&
    dropTarget.value?.workspaceId === workspaceId &&
    dropTarget.value?.configId === configId
  ) {
    dropTarget.value = null;
  }
}

function onWsDragOver(event: DragEvent, workspaceId: string) {
  const session = dragSession.value;
  if (!session) {
    return;
  }
  if (session.kind === 'config') {
    dropTarget.value = { mode: workspaceId === '' ? 'unassigned' : 'workspace', workspaceId };
    return;
  }
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
  dropTarget.value = {
    mode: 'workspace-reorder',
    workspaceId,
    before: event.clientY < rect.top + rect.height / 2
  };
}

function onWsDragLeave(event: DragEvent, workspaceId: string) {
  if (event.relatedTarget && (event.currentTarget as HTMLElement).contains(event.relatedTarget as Node)) {
    return;
  }
  const target = dropTarget.value;
  if (
    target &&
    (target.mode === 'workspace' || target.mode === 'workspace-reorder') &&
    target.workspaceId === workspaceId
  ) {
    dropTarget.value = null;
  }
}

function resetDrag() {
  dragSession.value = null;
  dropTarget.value = null;
}

async function onConfigDrop(workspaceId: string, targetCfgId: string) {
  const from = dragSession.value;
  const target = dropTarget.value;
  resetDrag();
  if (!from || !target || from.kind !== 'config') {
    return;
  }
  if (from.sourceWorkspaceId === workspaceId && from.config.id === targetCfgId) {
    return;
  }
  const ids = configsOf(workspaceId)
    .map((c) => c.id)
    .filter((id) => !(from.sourceWorkspaceId === workspaceId && id === from.config.id));
  const toIdx = ids.indexOf(targetCfgId);
  const insertAt = toIdx < 0 ? ids.length : target.before ? toIdx : toIdx + 1;
  ids.splice(insertAt, 0, from.config.id);
  await commitDrop(workspaceId, ids, from);
}

async function onWorkspaceDrop(workspaceId: string) {
  const session = dragSession.value;
  const target = dropTarget.value;
  resetDrag();
  if (!session) {
    return;
  }
  if (session.kind === 'config') {
    const ids = configsOf(workspaceId)
      .map((c) => c.id)
      .filter((id) => !(session.sourceWorkspaceId === workspaceId && id === session.config.id));
    ids.push(session.config.id);
    await commitDrop(workspaceId, ids, {
      config: session.config,
      sourceWorkspaceId: session.sourceWorkspaceId
    });
    return;
  }
  await reorderWorkspacesDrop(session.workspace.id, workspaceId, target);
}

async function reorderWorkspacesDrop(
  fromWsId: string,
  targetWsId: string,
  target: DropTargetState | null
) {
  if (fromWsId === targetWsId) {
    return;
  }
  const ids = projectStore.nav.workspaces.map((w) => w.id).filter((id) => id !== fromWsId);
  const toIdx = ids.indexOf(targetWsId);
  const insertAt = toIdx < 0 ? ids.length : target?.before ? toIdx : toIdx + 1;
  ids.splice(insertAt, 0, fromWsId);
  try {
    await projectStore.reorderWorkspaces(projectStore.currentProjectId, ids);
    await projectStore.loadNav();
  } catch (error) {
    app.setStatus('工作空间排序失败：' + (error as Error).message, true);
    await projectStore.loadNav();
  }
}

async function commitDrop(
  workspaceId: string,
  ids: string[],
  from: { config: ConfigInfo; sourceWorkspaceId: string }
) {
  try {
    const isCross = from.sourceWorkspaceId !== workspaceId;
    if (isCross) {
      await projectStore.moveConfig(from.config.id, workspaceId);
      if (configStore.current?.id === from.config.id) {
        await configStore.open(workspaceId, from.config.id);
      }
    }
    await projectStore.reorderConfigs(workspaceId, ids);
    if (isCross) {
      // 跨工作空间移动后以服务端为准刷新导航，避免本地旧 nav 丢失被移动的配置
      await projectStore.loadNav();
    } else {
      applyLocalOrder(workspaceId, ids);
    }
  } catch (error) {
    app.setStatus('拖拽操作失败：' + (error as Error).message, true);
    await projectStore.loadNav();
  }
}

function onCreateZoneDragOver() {
  if (dragSession.value?.kind !== 'config') {
    return;
  }
  dropTarget.value = { mode: 'create-workspace' };
}

function onCreateZoneDragLeave() {
  if (dropTarget.value?.mode === 'create-workspace') {
    dropTarget.value = null;
  }
}

async function onCreateWorkspaceDrop() {
  const from = dragSession.value;
  resetDrag();
  if (!from || from.kind !== 'config') {
    return;
  }
  const name = await ui.prompt({ title: '新建工作空间', placeholder: '输入工作空间名称' });
  if (!name) {
    return;
  }
  try {
    const ws = await projectStore.createWorkspace(name);
    await projectStore.moveConfig(from.config.id, ws.id);
    if (configStore.current?.id === from.config.id) {
      await configStore.open(ws.id, from.config.id);
    }
    await projectStore.loadNav();
    wsOpen.value = { ...wsOpen.value, [ws.id]: true };
    const moved = configsOf(ws.id).find((c) => c.id === from.config.id);
    if (moved) {
      await openConfig(moved, ws.id);
    }
    notifications.add('ok', `已创建并移入「${ws.name}」`);
  } catch (error) {
    app.setStatus('创建/移动失败：' + (error as Error).message, true);
    await projectStore.loadNav();
  }
}

function workspaceHeaderClass(ws: NavWorkspace) {
  const target = dropTarget.value;
  if (target?.mode === 'workspace' && target.workspaceId === ws.id) {
    return { 'drag-over': true };
  }
  if (target?.mode === 'workspace-reorder' && target.workspaceId === ws.id) {
    return {
      'drag-over': true,
      'drop-before': Boolean(target.before),
      'drop-after': !target.before
    };
  }
  return {
    'drag-over': false,
    'drop-before': false,
    'drop-after': false
  };
}

function unassignedSectionClass() {
  return {
    'drag-over': dropTarget.value?.mode === 'unassigned' && !dropTarget.value?.configId
  };
}

function configRowClass(config: ConfigInfo, workspaceId: string) {
  const isSortTarget =
    dropTarget.value?.configId === config.id &&
    dropTarget.value?.workspaceId === workspaceId &&
    (dropTarget.value?.mode === 'workspace-sort' || dropTarget.value?.mode === 'unassigned');
  return {
    active: configStore.current?.id === config.id,
    'dragging-source':
      dragSession.value?.kind === 'config' && dragSession.value.config.id === config.id,
    'drop-before': Boolean(isSortTarget && dropTarget.value?.before),
    'drop-after': Boolean(isSortTarget && !dropTarget.value?.before)
  };
}
</script>

<template>
  <aside class="ferry-sidebar flex w-[270px] shrink-0 flex-col overflow-hidden border-r border-[var(--ferry-border-soft)] bg-[var(--ferry-surface)]">
    <div class="ferry-sidebar-header">
      <div class="mb-4 pl-1 text-xl font-semibold">Ferry</div>
      <template v-if="!isSettings">
        <div class="relative">
          <button
            class="ferry-project-btn flex w-full items-center gap-2 rounded-xl border text-sm"
            :class="{ open: projectMenuOpen }"
            @click="projectMenuOpen = !projectMenuOpen"
            @contextmenu.prevent="openProjectContextMenu"
          >
            <span class="flex-1 truncate text-left">{{ currentProject?.name ?? '选择项目' }}</span>
            <span class="text-[11px] text-[var(--ferry-text-muted)]">▾</span>
          </button>
          <div
            v-if="projectMenuOpen"
          class="ferry-project-menu absolute left-0 right-0 top-full z-50 mt-1.5 rounded-xl border border-[var(--ferry-border)] bg-[var(--ferry-overlay)] p-1.5 shadow-lg"
          >
            <div
              v-for="p in projectStore.projects"
              :key="p.id"
              class="ferry-menu-row"
              :class="{ active: p.id === projectStore.currentProjectId }"
              @click="selectProject(p)"
            >
              <span class="flex-1 truncate">{{ p.name }}</span>
              <span v-if="p.id === projectStore.currentProjectId" class="text-[var(--ferry-primary)]">✓</span>
            </div>
            <div class="ferry-menu-sep"></div>
            <div class="ferry-menu-row" @click="createProject">＋ 新建项目</div>
            <div class="ferry-menu-row" @click="renameProject">重命名</div>
            <div class="ferry-menu-row danger" @click="deleteProject">删除项目</div>
          </div>
        </div>
        <button class="ferry-new-config-btn" @click="wizardStore.openWizard()">＋ 新建配置</button>
      </template>
    </div>

    <nav class="ferry-sidebar-nav min-h-0 flex-1 overflow-y-auto">
      <template v-if="!isSettings">
        <section class="mt-6">
          <div class="ferry-section-row" @click="wsCollapsed = !wsCollapsed">
            <span>工作空间</span>
            <span class="flex-1"></span>
            <span class="ferry-hover-op" title="新建配置" @click.stop="wizardStore.openWizard()">＋</span>
            <span class="text-[10px] text-[var(--ferry-text-dim)]">{{ wsCollapsed ? '▸' : '▾' }}</span>
          </div>
          <div v-if="!wsCollapsed" class="mt-1">
            <div v-if="projectStore.nav.workspaces.length === 0" class="ferry-hint">暂无工作空间</div>
            <div v-for="ws in projectStore.nav.workspaces" :key="ws.id" class="group">
            <div
              class="ferry-tree-row"
              :class="workspaceHeaderClass(ws)"
              draggable="true"
              @click="toggleWsOpen(ws.id)"
              @contextmenu.prevent="openWorkspaceMenu($event, ws)"
              @dragstart="onWorkspaceDragStart($event, ws)"
              @dragover.prevent="onWsDragOver($event, ws.id)"
              @dragleave="onWsDragLeave($event, ws.id)"
              @drop.prevent.stop="onWorkspaceDrop(ws.id)"
              @dragend="resetDrag"
            >
                <span class="text-[10px] text-[var(--ferry-text-dim)]">{{ isWsOpen(ws.id) ? '▾' : '▸' }}</span>
                <span class="name flex-1 truncate">{{ ws.name }}</span>
                <span
                  class="ferry-hover-op"
                  title="快速新建配置"
                  @click.stop="wizardStore.openWizard({ workspaceId: ws.id })"
                >＋</span>
                <span class="ferry-hover-op" @click.stop="openWorkspaceMenu($event, ws)">⋯</span>
              </div>
              <div v-if="isWsOpen(ws.id)" class="pl-3.5">
                <div v-if="ws.configs.length === 0" class="ferry-hint">暂无配置</div>
                <div
                  v-for="cfg in ws.configs"
                  :key="cfg.id"
                  class="ferry-config-row"
                  :class="configRowClass(cfg, ws.id)"
                  draggable="true"
                  @click="openConfig(cfg, ws.id)"
                  @contextmenu.prevent="openConfigMenu($event, cfg, ws.id)"
                  @dragstart="onConfigDragStart($event, cfg, ws.id)"
                  @dragover="onConfigDragOver($event, ws.id, cfg.id)"
                  @dragleave="onConfigDragLeave($event, ws.id, cfg.id)"
                  @drop.prevent.stop="onConfigDrop(ws.id, cfg.id)"
                  @dragend="resetDrag"
                >
                  <span>🌐</span>
                <span class="name flex-1 truncate">{{ displayName(cfg) }}</span>
                  <span v-if="cfg.pluginMissing" class="ferry-badge missing">缺插件</span>
                  <span class="ferry-hover-op" @click.stop="openConfigMenu($event, cfg, ws.id)">⋯</span>
                </div>
              </div>
            </div>
          </div>
        </section>

        <section
          class="ferry-sidebar-drop-target mt-6 rounded-xl"
          :class="unassignedSectionClass()"
          @dragover.prevent="onWsDragOver($event, '')"
          @dragleave="onWsDragLeave($event, '')"
          @drop.prevent.stop="onWorkspaceDrop('')"
        >
          <div class="ferry-section-row" @click="cfgCollapsed = !cfgCollapsed">
            <span>配置</span>
            <span class="ferry-count">{{ projectStore.nav.unassigned.length }}</span>
            <span class="flex-1"></span>
            <span class="ferry-hover-op" title="新建未归类配置" @click.stop="wizardStore.openWizard()">＋</span>
            <span class="text-[10px] text-[var(--ferry-text-dim)]">{{ cfgCollapsed ? '▸' : '▾' }}</span>
          </div>
          <div v-if="!cfgCollapsed" class="mt-1">
            <div v-if="projectStore.nav.unassigned.length === 0" class="ferry-hint">暂无未归类配置</div>
            <div
              v-for="cfg in projectStore.nav.unassigned"
              :key="cfg.id"
              class="ferry-config-row"
              :class="configRowClass(cfg, '')"
              draggable="true"
              @click="openConfig(cfg, '')"
              @contextmenu.prevent="openConfigMenu($event, cfg, '')"
              @dragstart="onConfigDragStart($event, cfg, '')"
              @dragover="onConfigDragOver($event, '', cfg.id)"
              @dragleave="onConfigDragLeave($event, '', cfg.id)"
              @drop.prevent.stop="onConfigDrop('', cfg.id)"
              @dragend="resetDrag"
            >
              <span>🌐</span>
            <span class="name flex-1 truncate">{{ displayName(cfg) }}</span>
              <span v-if="cfg.pluginMissing" class="ferry-badge missing">缺插件</span>
              <span class="ferry-hover-op" @click.stop="openConfigMenu($event, cfg, '')">⋯</span>
            </div>
          </div>
        </section>

        <div
          v-if="dragSession?.kind === 'config'"
          class="ferry-ws-drop-zone"
          :class="{ 'drag-over': dropTarget?.mode === 'create-workspace' }"
          @dragover.prevent="onCreateZoneDragOver"
          @dragleave="onCreateZoneDragLeave"
          @drop.prevent.stop="onCreateWorkspaceDrop"
        >
          ＋ 创建工作空间（拖到此处创建并移入）
        </div>
      </template>

      <template v-else>
        <div class="mb-3 text-sm font-semibold text-[var(--ferry-text-muted)]">设置</div>
        <div
          v-for="cat in categories"
          :key="cat.id"
          class="ferry-nav-row"
          :class="{ active: ui.settingsCategory === cat.id }"
          @click="ui.settingsCategory = cat.id"
        >
          {{ cat.name }}
        </div>
      </template>
    </nav>

    <footer class="ferry-sidebar-footer">
      <div v-if="!isSettings" class="ferry-nav-row" @click="goSettings">⚙ 设置</div>
      <div v-else class="ferry-nav-row" @click="goHome">← 返回</div>
    </footer>
  </aside>
</template>
