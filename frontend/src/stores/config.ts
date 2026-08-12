import { computed, ref } from 'vue';
import { defineStore } from 'pinia';
import { getIpc } from '../ipc';
import type { ConfigMeta, FormFieldSnapshot, PluginTemplateDto } from '../ipc/types';

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
    isOpen,
    open,
    close
  };
});
