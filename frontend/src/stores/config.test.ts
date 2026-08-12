import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { createIpcClient } from '../ipc/client';
import { createMockTransport } from '../ipc/mock';
import { setIpcClientForTesting } from '../ipc';
import { useConfigStore } from './config';

describe('config store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('open stores snapshot and seeds collapsed paths', async () => {
    const mock = createMockTransport();
    setIpcClientForTesting(createIpcClient(mock.transport));
    const store = useConfigStore();

    const pending = store.open('ws1', 'cfg1');
    await Promise.resolve();
    mock.respond(mock.sent[0].requestId, {
      config: { id: 'cfg1', name: 'nginx.conf', pluginKey: 'Nginx', pluginVersion: '1.0', pluginName: 'Nginx' },
      snapshot: [
        {
          path: 'http',
          id: 'http',
          label: 'http',
          description: '',
          type: 'Object',
          value: null,
          isEnabled: true,
          isVisible: true,
          isModule: true,
          isArrayItem: false,
          isSelectable: true,
          canToggleEnabled: true,
          totalChildModulesCount: 1,
          enabledChildModulesCount: 1,
          enabledChildModulesText: '1/1',
          required: false,
          allowCustomValue: false,
          integerOnly: false,
          enumOptions: [],
          children: [
            {
              path: 'http.upstreams',
              id: 'upstreams',
              label: 'upstreams',
              description: '',
              type: 'Array',
              value: null,
              isEnabled: true,
              isVisible: true,
              isModule: true,
              isArrayItem: false,
              isSelectable: true,
              canToggleEnabled: true,
              totalChildModulesCount: 0,
              enabledChildModulesCount: 0,
              enabledChildModulesText: '',
              required: false,
              allowCustomValue: false,
              integerOnly: false,
              enumOptions: [],
              children: [
                {
                  path: 'http.upstreams[0]',
                  id: 'http.upstreams_item_1',
                  label: '项目 1',
                  description: '',
                  type: 'Object',
                  value: null,
                  isEnabled: true,
                  isVisible: true,
                  isModule: false,
                  isArrayItem: true,
                  isSelectable: true,
                  canToggleEnabled: true,
                  totalChildModulesCount: 0,
                  enabledChildModulesCount: 0,
                  enabledChildModulesText: '',
                  required: false,
                  allowCustomValue: false,
                  integerOnly: false,
                  enumOptions: [],
                  children: []
                }
              ]
            }
          ]
        }
      ],
      sourceText: 'http {}\n',
      errors: [],
      unrecognized: [],
      versionChanged: false,
      templates: []
    });
    await pending;

    expect(store.current?.id).toBe('cfg1');
    expect(store.collapsed['http']).toBe(true);
    expect(store.collapsed['http.upstreams']).toBe(true);
  });

  it('setValue sends form:setValue and applies response', async () => {
    const mock = createMockTransport();
    setIpcClientForTesting(createIpcClient(mock.transport));
    const store = useConfigStore();
    store.snapshot = [
      {
        path: 'user',
        id: 'user',
        label: 'user',
        description: '',
        type: 'String',
        value: null,
        isEnabled: true,
        isVisible: true,
        isModule: false,
        isArrayItem: false,
        isSelectable: true,
        canToggleEnabled: true,
        totalChildModulesCount: 0,
        enabledChildModulesCount: 0,
        enabledChildModulesText: '',
        required: false,
        allowCustomValue: false,
        integerOnly: false,
        enumOptions: [],
        children: []
      }
    ];

    const pending = store.setValue('user', 'nginx');
    await Promise.resolve();
    mock.respond(mock.sent[0].requestId, {
      snapshot: [],
      text: 'user nginx;',
      errors: [],
      unrecognized: []
    });
    await pending;

    expect(mock.sent[0].action).toBe('form:setValue');
    expect(mock.sent[0].payload.path).toBe('user');
    expect(mock.sent[0].payload.value).toBe('nginx');
    expect(store.sourceText).toBe('user nginx;');
  });
});
