import { ref } from 'vue';
import { defineStore } from 'pinia';
import { getIpc } from '../ipc';
import type { ConfigMeta, FormFieldSnapshot } from '../ipc/types';

export const useConfigStore = defineStore('config', () => {
  const current = ref<ConfigMeta | null>(null);
  const workspaceId = ref('');
  const snapshot = ref<FormFieldSnapshot[]>([]);
  const sourceText = ref('');
  const errors = ref<string[]>([]);

  async function open(workspaceIdArg: string, configId: string) {
    const res = await getIpc().send('config:open', { workspaceId: workspaceIdArg, configId });
    workspaceId.value = workspaceIdArg;
    current.value = res.config ?? null;
    snapshot.value = res.snapshot ?? [];
    sourceText.value = res.sourceText ?? '';
    errors.value = res.errors ?? [];
    return res;
  }

  function close() {
    current.value = null;
    workspaceId.value = '';
    snapshot.value = [];
    sourceText.value = '';
    errors.value = [];
  }

  return { current, workspaceId, snapshot, sourceText, errors, open, close };
});
