import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useUiStore } from './ui';

describe('ui store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('opens and closes context menu', () => {
    const ui = useUiStore();
    ui.openMenu([{ text: '查看' }], 100, 200);
    expect(ui.menuOpen).toBe(true);
    expect(ui.menuItems[0].text).toBe('查看');
    expect(ui.menuX).toBe(100);
    expect(ui.menuY).toBe(200);
    ui.closeMenu();
    expect(ui.menuOpen).toBe(false);
  });

  it('prompt resolves with value or null', async () => {
    const ui = useUiStore();
    const pending = ui.prompt({ title: '项目名称' });
    expect(ui.promptOpen).toBe(true);
    ui.resolvePrompt('生产');
    await expect(pending).resolves.toBe('生产');

    const cancelled = ui.prompt({ title: '工作空间名称' });
    ui.resolvePrompt(null);
    await expect(cancelled).resolves.toBeNull();
  });

  it('confirm resolves with boolean', async () => {
    const ui = useUiStore();
    const pending = ui.confirm({ title: '删除', message: '确认？' });
    expect(ui.confirmOpen).toBe(true);
    ui.resolveConfirm(true);
    await expect(pending).resolves.toBe(true);
  });

  it('opens and closes move modal', () => {
    const ui = useUiStore();
    ui.openMove(
      {
        id: 'c1',
        name: 'nginx.conf',
        pluginKey: 'Nginx',
        pluginVersion: '1.0',
        pluginName: 'Nginx',
        pluginMissing: false,
        updatedAt: ''
      },
      'ws1'
    );
    expect(ui.moveOpen).toBe(true);
    expect(ui.moveTarget?.config.id).toBe('c1');
    expect(ui.moveTarget?.workspaceId).toBe('ws1');
    ui.closeMove();
    expect(ui.moveOpen).toBe(false);
    expect(ui.moveTarget).toBeNull();
  });

  it('opens and closes history modal', () => {
    const ui = useUiStore();
    ui.openHistory(
      {
        id: 'c1',
        name: 'nginx.conf',
        pluginKey: 'Nginx',
        pluginVersion: '1.0',
        pluginName: 'Nginx',
        pluginMissing: false,
        updatedAt: ''
      },
      ''
    );
    expect(ui.historyOpen).toBe(true);
    expect(ui.historyTarget?.workspaceId).toBe('');
    ui.closeHistory();
    expect(ui.historyOpen).toBe(false);
  });

  it('opens and closes rename modal', () => {
    const ui = useUiStore();
    ui.openRename(
      {
        id: 'c1',
        name: 'nginx.conf',
        pluginKey: 'Nginx',
        pluginVersion: '1.0',
        pluginName: 'Nginx',
        pluginMissing: false,
        updatedAt: ''
      },
      'ws1'
    );
    expect(ui.renameOpen).toBe(true);
    expect(ui.renameTarget?.config.name).toBe('nginx.conf');
    ui.closeRename();
    expect(ui.renameOpen).toBe(false);
    expect(ui.renameTarget).toBeNull();
  });
});
