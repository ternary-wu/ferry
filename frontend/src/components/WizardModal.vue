<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { useAppStore } from '../stores/app';
import { useProjectStore } from '../stores/project';
import { useConfigStore } from '../stores/config';
import { useWizardStore } from '../stores/wizard';
import { useSettingsStore } from '../stores/settings';
import { getIpc } from '../ipc';
import { loadLocal, saveLocal } from '../utils/storage';
import { splitName, joinName } from '../utils/nameParts';
import type { PluginDescriptor, PluginTemplateDto } from '../ipc/types';

const router = useRouter();
const appStore = useAppStore();
const projectStore = useProjectStore();
const configStore = useConfigStore();
const wizard = useWizardStore();
const settingsStore = useSettingsStore();

function isPluginEnabled(key: string): boolean {
  return settingsStore.settings.pluginDisabled?.[key] !== true;
}

const filteredPlugins = computed(() => {
  const query = wizard.search.trim().toLowerCase();
  return appStore.plugins.filter(
    (plugin) =>
      isPluginEnabled(plugin.key) &&
      (!query ||
        plugin.name.toLowerCase().includes(query) ||
        plugin.key.toLowerCase().includes(query) ||
        (plugin.description || '').toLowerCase().includes(query))
  );
});

const recentPlugins = computed(() => {
  if (wizard.search.trim()) {
    return [];
  }
  return loadLocal<string[]>('ferry.recentPlugins', [])
    .map((key) => appStore.plugins.find((plugin) => plugin.key === key))
    .filter((plugin): plugin is PluginDescriptor => Boolean(plugin))
    .filter((plugin) => isPluginEnabled(plugin.key))
    .slice(0, 4);
});

const currentPlugin = computed(
  () => appStore.plugins.find((plugin) => plugin.key === wizard.pluginKey) ?? null
);
const extUnlocked = ref(false);

const fileNameInput = computed({
  get: () => splitName(wizard.name).file,
  set: (value: string) => {
    wizard.name = joinName(value, splitName(wizard.name).ext);
  }
});

const fileExtInput = computed({
  get: () => splitName(wizard.name).ext,
  set: (value: string) => {
    wizard.name = joinName(splitName(wizard.name).file, value);
  }
});

watch(
  () => wizard.open,
  (open) => {
    if (open) {
      extUnlocked.value = false;
    }
  }
);

const templateOptions = computed<Array<PluginTemplateDto & { id: string }>>(() => {
  const blank: PluginTemplateDto & { id: string } = {
    id: '__blank',
    name: '默认模板',
    description: '空白默认配置'
  };
  return [blank, ...(currentPlugin.value?.templates ?? [])];
});

watch(
  () => wizard.step,
  (step) => {
    if (step === 3 && wizard.autoName && !wizard.name) {
      const defaultName = currentPlugin.value?.defaultFileName;
      if (defaultName) {
        wizard.name = defaultName;
      }
      wizard.autoName = false;
    }
  }
);

function goStep(target: number) {
  if (target >= 1 && target <= wizard.step) {
    wizard.step = target;
  }
}

function wsDotClass(n: number): string {
  if (n < wizard.step) return 'done clickable';
  if (n === wizard.step) return 'active';
  return 'future';
}

function selectPlugin(plugin: PluginDescriptor) {
  wizard.pluginKey = plugin.key;
  wizard.templateId = loadLocal<string>(`ferry.tpl.${plugin.key}`, '') || '__blank';
  wizard.step = 2;
}

function selectTemplate(template: PluginTemplateDto) {
  wizard.templateId = template.id;
  if (wizard.pluginKey) {
    saveLocal(`ferry.tpl.${wizard.pluginKey}`, template.id);
  }
  wizard.step = 3;
}

async function createConfig() {
  const pluginKey = wizard.pluginKey;
  if (!pluginKey) {
    appStore.setStatus('请选择插件', true);
    return;
  }
  try {
    const created = await getIpc().send('config:create', {
      projectId: projectStore.currentProjectId,
      workspaceId: wizard.workspaceId,
      pluginKey,
      name: wizard.name.trim() || undefined
    });
    await configStore.open(wizard.workspaceId, created.configId);

    if (wizard.templateId !== '__blank') {
      const applied = await getIpc().send('form:applyPreset', { preset: wizard.templateId });
      if (applied.ok) {
        configStore.applyFormResult(applied);
      } else {
        appStore.setStatus((applied.errors ?? ['应用模板失败']).join('；'), true);
      }
    }

    const template = currentPlugin.value?.templates.find((t) => t.id === wizard.templateId);
    if (template) {
      saveLocal(`ferry.tplCfg.${created.configId}`, template.name);
    }
    const recents = loadLocal<string[]>('ferry.recentPlugins', []);
    saveLocal('ferry.recentPlugins', [pluginKey, ...recents.filter((k) => k !== pluginKey)].slice(0, 5));

    await projectStore.loadNav();
    wizard.close();
    await router.push('/editor');
  } catch (error) {
    appStore.setStatus('创建失败：' + (error as Error).message, true);
  }
}
</script>

<template>
  <Teleport to="body">
    <div v-if="wizard.open" class="ferry-overlay" @mousedown.self="wizard.close()">
      <div class="ferry-wizard">
        <button class="ferry-wizard-close" title="关闭" @click="wizard.close()">
          <svg viewBox="0 0 14 14">
            <path d="M2 2l10 10M12 2L2 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
          </svg>
        </button>

        <div class="ferry-wizard-steps">
          <span class="ferry-ws-dot" :class="wsDotClass(1)" @click="goStep(1)"></span>
          <span class="ferry-ws-line"></span>
          <span class="ferry-ws-dot" :class="wsDotClass(2)" @click="goStep(2)"></span>
          <span class="ferry-ws-line"></span>
          <span class="ferry-ws-dot" :class="wsDotClass(3)" @click="goStep(3)"></span>
        </div>

        <div v-if="wizard.step === 1">
          <h2 class="ferry-wizard-title">选择插件</h2>
          <p class="ferry-wizard-sub">选择要创建配置的能力</p>
          <input v-model="wizard.search" class="ferry-input" placeholder="搜索插件…" />
          <div v-if="recentPlugins.length" class="ferry-mini-label">最近使用</div>
          <div v-if="recentPlugins.length" class="ferry-wizard-grid">
            <div
              v-for="plugin in recentPlugins"
              :key="plugin.key"
              class="ferry-option"
              @click="selectPlugin(plugin)"
            >
              <div class="ferry-option-name">🌐 {{ plugin.name }} <small>v{{ plugin.version }}</small></div>
              <div class="ferry-option-desc">{{ plugin.description || plugin.rendererType }}</div>
            </div>
          </div>
          <div class="ferry-mini-label">全部插件</div>
          <div class="ferry-wizard-grid">
            <div
              v-for="plugin in filteredPlugins"
              :key="plugin.key"
              class="ferry-option"
              @click="selectPlugin(plugin)"
            >
              <div class="ferry-option-name">🌐 {{ plugin.name }} <small>v{{ plugin.version }}</small></div>
              <div class="ferry-option-desc">{{ plugin.description || plugin.rendererType }}</div>
            </div>
          </div>
          <div v-if="filteredPlugins.length === 0" class="ferry-hint">没有找到匹配的插件</div>
        </div>

        <div v-else-if="wizard.step === 2">
          <h2 class="ferry-wizard-title">选择模板</h2>
          <p class="ferry-wizard-sub">模板由插件定义；未选择则使用默认模板</p>
          <div class="ferry-wizard-grid">
            <div
              v-for="template in templateOptions"
              :key="template.id"
              class="ferry-option"
              :class="{ active: wizard.templateId === template.id }"
              @click="selectTemplate(template)"
            >
              <div class="ferry-option-name">{{ template.name }}</div>
              <div class="ferry-option-desc">{{ template.description || '场景模板' }}</div>
            </div>
          </div>
          <div class="ferry-wizard-footer">
            <button class="ferry-btn" @click="goStep(1)">‹ 返回</button>
          </div>
        </div>

        <div v-else>
          <h2 class="ferry-wizard-title">配置信息</h2>
          <div class="ferry-wizard-name-row">
            <input v-model="fileNameInput" class="ferry-input" placeholder="文件名" />
            <span class="ferry-wizard-dot">.</span>
            <input
              v-model="fileExtInput"
              class="ferry-input ferry-wizard-ext"
              :disabled="!extUnlocked"
              placeholder="扩展名"
            />
          </div>
          <label class="ferry-wizard-ext-toggle">
            <input v-model="extUnlocked" type="checkbox" />
            <span>允许修改扩展名（如果改变扩展名，可能导致文件不可用）</span>
          </label>
          <select
            v-model="wizard.workspaceId"
            class="ferry-input ferry-wizard-select"
            :disabled="projectStore.nav.workspaces.length === 0"
          >
            <option value="">--- 选择工作空间（未归类）---</option>
            <option v-for="ws in projectStore.nav.workspaces" :key="ws.id" :value="ws.id">
              {{ ws.name }}
            </option>
          </select>
          <div class="ferry-wizard-footer">
            <button class="ferry-btn" @click="goStep(2)">‹ 返回</button>
            <div class="flex-1"></div>
            <button class="ferry-btn" @click="wizard.close()">取消</button>
            <button class="ferry-btn primary" @click="createConfig">创建</button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>
