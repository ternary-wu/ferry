import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { createIpcClient } from '../ipc/client';
import { createMockTransport } from '../ipc/mock';
import { setIpcClientForTesting } from '../ipc';
import { useSettingsStore } from './settings';

describe('settings store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('loads settings via ipc', async () => {
    const mock = createMockTransport();
    setIpcClientForTesting(createIpcClient(mock.transport));
    const store = useSettingsStore();

    const pending = store.load();
    await Promise.resolve();
    mock.respond(mock.sent[0].requestId, { settings: { theme: 'dark' } });
    await pending;

    expect(store.settings.theme).toBe('dark');
    expect(store.loaded).toBe(true);
  });

  it('saves settings and merges response', async () => {
    const mock = createMockTransport();
    setIpcClientForTesting(createIpcClient(mock.transport));
    const store = useSettingsStore();

    const pending = store.save({ theme: 'light' });
    await Promise.resolve();
    mock.respond(mock.sent[0].requestId, {
      settings: { theme: 'light', animations: false }
    });
    await pending;

    expect(store.settings.theme).toBe('light');
    expect(store.settings.animations).toBe(false);
  });
});
