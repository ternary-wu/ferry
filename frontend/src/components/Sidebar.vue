<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useProjectStore } from '../stores/project';
import { useConfigStore } from '../stores/config';
import { useSettingsStore } from '../stores/settings';
import { useUiStore, type ContextMenuItem } from '../stores/ui';
import { useWizardStore } from '../stores/wizard';
import type { ConfigInfo, NavWorkspace, ProjectInfo } from '../ipc/types';

const route = useRoute();
const router = useRouter();
const projectStore = useProjectStore();
const configStore = useConfigStore();
const settingsStore = useSettingsStore();
const ui = useUiStore();
const wizardStore = useWizardStore();

const projectMenuOpen = ref(false);
const wsCollapsed = ref(false);
const cfgCollapsed = ref(false);
const wsOpen = ref<Record<string, boolean>>({});

const isSettings = computed(() => route.name === 'settings');
const currentProject = computed(() =>
  projectStore.projects.find((p) => p.id === projectStore.currentProjectId)
);

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

function workspaceMenuItems(workspace: NavWorkspace): ContextMenuItem[] {
  return [
    { text: '快速新建配置', onClick: () => wizardStore.openWizard({ workspaceId: workspace.id }) },
    { text: '重命名', onClick: () => void renameWorkspace(workspace) },
    { text: '导出存档', disabled: true },
    { text: '删除', danger: true, disabled: true }
  ];
}

function configMenuItems(config: ConfigInfo, workspaceId: string): ContextMenuItem[] {
  return [
    { text: '查看', onClick: () => void openConfig(config, workspaceId) },
    { text: '导出', disabled: true },
    { text: '复制', disabled: true },
    { text: '移动', disabled: true },
    { text: '历史', disabled: true },
    { text: '回滚', disabled: true },
    { text: '恢复全部默认配置', disabled: true },
    { text: '推送', disabled: true },
    { text: '删除', danger: true, disabled: true }
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
</script>

<template>
  <aside class="ferry-sidebar flex w-[270px] shrink-0 flex-col overflow-y-auto border-r border-[var(--ferry-border-soft)] bg-[var(--ferry-surface)] p-4">
    <div class="mb-4 text-xl font-semibold">Ferry</div>

    <template v-if="!isSettings">
      <div class="relative">
        <button
          class="ferry-project-btn flex w-full items-center gap-2 rounded-xl border border-transparent bg-[var(--ferry-control)] px-3.5 py-3 text-sm hover:border-[#555]"
          @click="projectMenuOpen = !projectMenuOpen"
          @contextmenu.prevent="openProjectContextMenu"
        >
          <span class="flex-1 truncate text-left">{{ currentProject?.name ?? '选择项目' }}</span>
          <span class="text-[11px] text-[var(--ferry-text-muted)]">▼</span>
        </button>
        <div
          v-if="projectMenuOpen"
          class="ferry-project-menu absolute left-0 right-0 top-full z-50 mt-1.5 rounded-xl border border-[var(--ferry-border)] bg-[#252525] p-1.5 shadow-lg"
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
              @click="toggleWsOpen(ws.id)"
              @contextmenu.prevent="openWorkspaceMenu($event, ws)"
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
                :class="{ active: configStore.current?.id === cfg.id }"
                @click="openConfig(cfg, ws.id)"
                @contextmenu.prevent="openConfigMenu($event, cfg, ws.id)"
              >
                <span>🌐</span>
                <span class="name flex-1 truncate">{{ cfg.name }}</span>
                <span v-if="cfg.pluginMissing" class="ferry-badge missing">缺插件</span>
                <span class="ferry-hover-op" @click.stop="openConfigMenu($event, cfg, ws.id)">⋯</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section class="mt-6">
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
            :class="{ active: configStore.current?.id === cfg.id }"
            @click="openConfig(cfg, '')"
            @contextmenu.prevent="openConfigMenu($event, cfg, '')"
          >
            <span>🌐</span>
            <span class="name flex-1 truncate">{{ cfg.name }}</span>
            <span v-if="cfg.pluginMissing" class="ferry-badge missing">缺插件</span>
            <span class="ferry-hover-op" @click.stop="openConfigMenu($event, cfg, '')">⋯</span>
          </div>
        </div>
      </section>

      <div class="flex-1"></div>
      <div class="ferry-nav-row" @click="goSettings">⚙ 设置</div>
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
      <div class="flex-1"></div>
      <div class="ferry-nav-row" @click="goHome">← 返回</div>
    </template>
  </aside>
</template>
