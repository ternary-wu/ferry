import { ref } from 'vue';
import { defineStore } from 'pinia';
import { loadLocal } from '../utils/storage';

export interface WizardOptions {
  pluginKey?: string;
  workspaceId?: string;
  presetName?: string;
}

export const useWizardStore = defineStore('wizard', () => {
  const open = ref(false);
  const step = ref(1);
  const pluginKey = ref('');
  const templateId = ref('__blank');
  const name = ref('');
  const workspaceId = ref('');
  const autoName = ref(true);
  const search = ref('');

  function openWizard(options: WizardOptions = {}) {
    pluginKey.value = options.pluginKey ?? '';
    templateId.value = pluginKey.value
      ? (loadLocal<string>(`ferry.tpl.${pluginKey.value}`, '') || '__blank')
      : '__blank';
    step.value = pluginKey.value ? 2 : 1;
    autoName.value = !options.presetName;
    name.value = options.presetName ?? '';
    workspaceId.value = options.workspaceId ?? '';
    search.value = '';
    open.value = true;
  }

  function close() {
    open.value = false;
  }

  return {
    open,
    step,
    pluginKey,
    templateId,
    name,
    workspaceId,
    autoName,
    search,
    openWizard,
    close
  };
});
