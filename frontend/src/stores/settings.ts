import { ref } from 'vue';
import { defineStore } from 'pinia';
import { getIpc } from '../ipc';
import type { AppSettings } from '../ipc/types';

export const useSettingsStore = defineStore('settings', () => {
  const settings = ref<AppSettings>({});
  const loaded = ref(false);

  async function load() {
    const res = await getIpc().send('settings:get', {});
    settings.value = res.settings ?? {};
    loaded.value = true;
    return res.settings;
  }

  async function save(patch: Partial<AppSettings>) {
    const res = await getIpc().send('settings:save', { settings: patch });
    settings.value = res.settings ?? { ...settings.value, ...patch };
    return res.settings;
  }

  return { settings, loaded, load, save };
});
