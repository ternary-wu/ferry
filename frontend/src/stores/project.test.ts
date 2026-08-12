import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { createIpcClient } from '../ipc/client';
import { createMockTransport } from '../ipc/mock';
import { setIpcClientForTesting } from '../ipc';
import { useProjectStore } from './project';

describe('project store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('loads projects and selects preferred/first', async () => {
    const mock = createMockTransport();
    setIpcClientForTesting(createIpcClient(mock.transport));
    const store = useProjectStore();

    const pending = store.loadProjects('p2');
    await Promise.resolve();
    mock.respond(mock.sent[0].requestId, {
      projects: [
        { id: 'p1', name: 'A', createdAt: '', updatedAt: '' },
        { id: 'p2', name: 'B', createdAt: '', updatedAt: '' }
      ]
    });
    await pending;

    expect(store.projects).toHaveLength(2);
    expect(store.currentProjectId).toBe('p2');
  });

  it('creates default project when none exists', async () => {
    const mock = createMockTransport();
    setIpcClientForTesting(createIpcClient(mock.transport));
    const store = useProjectStore();

    const pending = store.loadProjects();
    await Promise.resolve();
    mock.respond(mock.sent[0].requestId, { projects: [] });
    await Promise.resolve();
    mock.respond(mock.sent[1].requestId, {
      project: { id: 'p1', name: '默认项目', createdAt: '', updatedAt: '' }
    });
    await pending;

    expect(mock.sent[1].action).toBe('project:create');
    expect(store.currentProjectId).toBe('p1');
  });

  it('createProject appends and selects new project', async () => {
    const mock = createMockTransport();
    setIpcClientForTesting(createIpcClient(mock.transport));
    const store = useProjectStore();

    const pending = store.createProject('生产');
    await Promise.resolve();
    mock.respond(mock.sent[0].requestId, {
      project: { id: 'p9', name: '生产', createdAt: '', updatedAt: '' }
    });
    await pending;

    expect(store.projects).toHaveLength(1);
    expect(store.currentProjectId).toBe('p9');
  });
});
