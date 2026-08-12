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
});
