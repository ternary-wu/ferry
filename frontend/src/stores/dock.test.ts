import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { DOCK_MAX, DOCK_MIN, useDockStore } from './dock';

describe('dock store', () => {
  beforeEach(() => {
    const store = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => {
        store.set(key, value);
      },
      removeItem: (key: string) => {
        store.delete(key);
      },
      clear: () => store.clear()
    });
    setActivePinia(createPinia());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('defaults hidden with 42% width and line numbers on', () => {
    const dock = useDockStore();
    expect(dock.open).toBe(false);
    expect(dock.width).toBe(42);
    expect(dock.maximized).toBe(false);
    expect(dock.lineNumbers).toBe(true);
  });

  it('toggles open/close', () => {
    const dock = useDockStore();
    dock.toggle();
    expect(dock.open).toBe(true);
    dock.toggle();
    expect(dock.open).toBe(false);
  });

  it('clamps resize to 35–60 and persists width on finish', () => {
    const dock = useDockStore();
    dock.openDock();
    dock.resizeTo(90);
    expect(dock.width).toBe(DOCK_MAX);
    dock.resizeTo(10);
    expect(dock.width).toBe(DOCK_MIN);
    expect(dock.closeZone).toBe(true);
    dock.resizeTo(45);
    expect(dock.width).toBe(45);
    expect(dock.closeZone).toBe(false);
    dock.finishResize();
    expect(dock.open).toBe(true);
    expect(JSON.parse(localStorage.getItem('ferry.dock.width')!)).toBe(45);
  });

  it('closes when released in close zone and resets width to threshold', () => {
    const dock = useDockStore();
    dock.openDock();
    dock.resizeTo(33);
    expect(dock.width).toBe(DOCK_MIN);
    expect(dock.closeZone).toBe(true);
    expect(dock.open).toBe(true);
    dock.finishResize();
    expect(dock.open).toBe(false);
    dock.openDock();
    expect(dock.width).toBe(DOCK_MIN);
    expect(dock.closeZone).toBe(false);
  });

  it('keeps width when released at minimum threshold', () => {
    const dock = useDockStore();
    dock.openDock();
    dock.resizeTo(DOCK_MIN);
    expect(dock.closeZone).toBe(false);
    dock.finishResize();
    expect(dock.open).toBe(true);
    expect(dock.width).toBe(DOCK_MIN);
  });

  it('maximize fills main workspace and restore keeps previous width', () => {
    const dock = useDockStore();
    dock.openDock();
    dock.resizeTo(50);
    dock.finishResize();
    dock.toggleMaximize();
    expect(dock.maximized).toBe(true);
    dock.toggleMaximize();
    expect(dock.maximized).toBe(false);
    expect(dock.width).toBe(50);
  });

  it('line numbers toggle persists', () => {
    const dock = useDockStore();
    dock.toggleLineNumbers();
    expect(dock.lineNumbers).toBe(false);
    expect(JSON.parse(localStorage.getItem('ferry.dock.lineNumbers')!)).toBe(false);
  });

  it('close resets maximized state', () => {
    const dock = useDockStore();
    dock.openDock();
    dock.toggleMaximize();
    dock.closeDock();
    expect(dock.open).toBe(false);
    expect(dock.maximized).toBe(false);
  });
});
