import { computed, ref } from 'vue';
import { defineStore } from 'pinia';
import { getIpc } from '../ipc';
import type { ConfigMeta, FormFieldSnapshot, PluginTemplateDto } from '../ipc/types';
import type { FieldFilter } from '../utils/fieldTree';
import { collectCollapsiblePaths } from '../utils/fieldTree';

export const useConfigStore = defineStore('config', () => {
  const current = ref<ConfigMeta | null>(null);
  const workspaceId = ref('');
  const snapshot = ref<FormFieldSnapshot[]>([]);
  const sourceText = ref('');
  const errors = ref<string[]>([]);
  const unrecognized = ref<string[]>([]);
  const versionChanged = ref(false);
  const pluginMissing = ref(false);
  const templates = ref<PluginTemplateDto[]>([]);
  const saving = ref(false);
  const filter = ref<FieldFilter>('all');
  const search = ref('');
  const collapsed = ref<Record<string, boolean>>({});

  const isOpen = computed(() => current.value !== null);

  async function open(workspaceIdArg: string, configId: string) {
    const res = await getIpc().send('config:open', { workspaceId: workspaceIdArg, configId });
    workspaceId.value = workspaceIdArg;
    current.value = res.config ?? null;
    snapshot.value = res.snapshot ?? [];
    sourceText.value = res.sourceText ?? '';
    errors.value = res.errors ?? [];
    unrecognized.value = res.unrecognized ?? [];
    versionChanged.value = res.versionChanged ?? false;
    pluginMissing.value = res.pluginMissing ?? false;
    templates.value = res.templates ?? [];
    search.value = '';
    seedCollapsed();
    return res;
  }

  function close() {
    current.value = null;
    workspaceId.value = '';
    snapshot.value = [];
    sourceText.value = '';
    errors.value = [];
    unrecognized.value = [];
    versionChanged.value = false;
    pluginMissing.value = false;
    templates.value = [];
    search.value = '';
    collapsed.value = {};
  }

  function seedCollapsed() {
    const map: Record<string, boolean> = {};
    for (const path of collectCollapsiblePaths(snapshot.value)) {
      map[path] = true;
    }
    collapsed.value = map;
  }

  function toggleCollapsed(path: string) {
    collapsed.value = { ...collapsed.value, [path]: !collapsed.value[path] };
  }

  function collapseAll() {
    const map: Record<string, boolean> = {};
    for (const path of collectCollapsiblePaths(snapshot.value)) {
      map[path] = true;
    }
    collapsed.value = map;
  }

  function expandAll() {
    collapsed.value = {};
  }

  function applyFormResult(data: {
    snapshot: FormFieldSnapshot[];
    text?: string | null;
    errors: string[];
    unrecognized?: string[];
  }) {
    snapshot.value = data.snapshot;
    if (data.text !== undefined && data.text !== null) {
      sourceText.value = data.text;
    }
    errors.value = data.errors;
    if (data.unrecognized) {
      unrecognized.value = data.unrecognized;
    }
  }

  async function setValue(path: string, value: unknown) {
    saving.value = true;
    try {
      const res = await getIpc().send('form:setValue', { path, value });
      if (res.ok) {
        applyFormResult(res);
      }
      return res;
    } finally {
      saving.value = false;
    }
  }

  async function toggle(path: string, enabled?: boolean) {
    saving.value = true;
    try {
      const res = await getIpc().send('form:toggle', { path, enabled });
      if (res.ok) {
        applyFormResult(res);
      }
      return res;
    } finally {
      saving.value = false;
    }
  }

  async function addItem(path: string) {
    saving.value = true;
    try {
      const res = await getIpc().send('form:addItem', { path });
      if (res.ok) {
        applyFormResult(res);
      }
      return res;
    } finally {
      saving.value = false;
    }
  }

  async function removeItem(path: string) {
    saving.value = true;
    try {
      const res = await getIpc().send('form:removeItem', { path });
      if (res.ok) {
        applyFormResult(res);
      }
      return res;
    } finally {
      saving.value = false;
    }
  }

  async function resetCurrent() {
    saving.value = true;
    try {
      const res = await getIpc().send('config:reset', {});
      if (res.ok) {
        snapshot.value = res.snapshot;
        sourceText.value = res.sourceText;
        errors.value = [];
        try {
          const validation = await getIpc().send('form:validate', {});
          errors.value = validation.errors ?? [];
        } catch {
          // 校验失败不影响重置结果
        }
      }
      return res;
    } finally {
      saving.value = false;
    }
  }

  return {
    current,
    workspaceId,
    snapshot,
    sourceText,
    errors,
    unrecognized,
    versionChanged,
    pluginMissing,
    templates,
    saving,
    filter,
    search,
    collapsed,
    isOpen,
    open,
    close,
    applyFormResult,
    seedCollapsed,
    toggleCollapsed,
    collapseAll,
    expandAll,
    setValue,
    toggle,
    addItem,
    removeItem,
    resetCurrent
  };
});
