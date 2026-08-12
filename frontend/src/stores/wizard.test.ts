import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useWizardStore } from './wizard';

describe('wizard store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('opens at step 1 by default', () => {
    const wizard = useWizardStore();
    wizard.openWizard();
    expect(wizard.open).toBe(true);
    expect(wizard.step).toBe(1);
    expect(wizard.templateId).toBe('__blank');
  });

  it('opens at step 2 when plugin preselected', () => {
    const wizard = useWizardStore();
    wizard.openWizard({ pluginKey: 'Nginx', workspaceId: 'ws1' });
    expect(wizard.step).toBe(2);
    expect(wizard.pluginKey).toBe('Nginx');
    expect(wizard.workspaceId).toBe('ws1');
    expect(wizard.templateId).toBe('__blank');
  });

  it('close resets open flag', () => {
    const wizard = useWizardStore();
    wizard.openWizard();
    wizard.close();
    expect(wizard.open).toBe(false);
  });
});
