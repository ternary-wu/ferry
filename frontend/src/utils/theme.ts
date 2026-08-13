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

/** 字段说明 tooltip 移开后的消失延迟（毫秒）；延迟关闭时为 0。 */
export function applyTooltipDelay(delay?: number, delayEnabled?: boolean): void {
  document.documentElement.style.setProperty(
    '--ferry-tooltip-delay',
    delayEnabled === false ? '0ms' : `${delay ?? 250}ms`
  );
}

/** 字段说明 tooltip 悬停后的显示延迟（毫秒）；延迟关闭时为 0（立即显示）。 */
export function applyTooltipShowDelay(delay?: number, delayEnabled?: boolean): void {
  document.documentElement.style.setProperty(
    '--ferry-tooltip-show-delay',
    delayEnabled === true ? `${delay ?? 250}ms` : '0ms'
  );
}

/** 悬停显示字段说明的总开关。 */
export function applyTooltipEnabled(enabled?: boolean): void {
  document.documentElement.dataset.tooltip = enabled === false ? 'off' : 'on';
}

export function onSystemThemeChange(callback: () => void): void {
  const mq = window.matchMedia?.('(prefers-color-scheme: light)');
  mq?.addEventListener?.('change', callback);
}
