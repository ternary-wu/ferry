export type ThemeName = 'dark' | 'light' | 'system';

export function resolveTheme(theme?: string): 'dark' | 'light' {
  if (theme === 'light') {
    return 'light';
  }
  if (theme === 'dark') {
    return 'dark';
  }
  return window.matchMedia?.('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

export function applyTheme(theme?: string, animations?: boolean): void {
  const root = document.documentElement;
  root.dataset.theme = resolveTheme(theme);
  root.dataset.animations = animations === false ? 'off' : 'on';
}

export function onSystemThemeChange(callback: () => void): void {
  const mq = window.matchMedia?.('(prefers-color-scheme: light)');
  mq?.addEventListener?.('change', callback);
}
