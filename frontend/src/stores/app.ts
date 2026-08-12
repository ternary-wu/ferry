import { ref } from 'vue';
import { defineStore } from 'pinia';
import { getIpc } from '../ipc';
import type { PluginDescriptor } from '../ipc/types';

export const useAppStore = defineStore('app', () => {
  const plugins = ref<PluginDescriptor[]>([]);
  const loadErrors = ref<string[]>([]);
  const status = ref('就绪');
  const statusIsError = ref(false);
  const latencyMs = ref<number | null>(null);
  const bootstrapped = ref(false);

  async function bootstrap() {
    const res = await getIpc().send('bootstrap', {});
    plugins.value = res.plugins ?? [];
    loadErrors.value = res.loadErrors ?? [];
    bootstrapped.value = true;
    return res;
  }

  function setStatus(text: string, isError = false) {
    status.value = text;
    statusIsError.value = isError;
  }

  function setLatency(ms: number | null) {
    latencyMs.value = ms;
  }

  return { plugins, loadErrors, status, statusIsError, latencyMs, bootstrapped, bootstrap, setStatus, setLatency };
});
