import { getIpc } from './ipc';
import type { IpcAction } from './ipc/types';

interface SpikeStep {
  name: string;
  ms: number;
  ok: boolean;
  error: string;
}

/**
 * 与旧 UI 等价的 19 步自检：后端在 FERRY_SPIKE_SELFCHECK=1 时下发 spike:run，
 * 这里按顺序跑完整链路并把结果以 spike:result 回传（后端写文件后关闭窗口）。
 */
export async function runSpikeSelfCheck(): Promise<void> {
  const steps: SpikeStep[] = [];
  let failed = false;

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  async function step(name: string, action: IpcAction, payload?: Record<string, unknown>): Promise<any> {
    const t0 = performance.now();
    try {
      const data = await getIpc().send(action, (payload ?? {}) as never);
      if (!data.ok) {
        failed = true;
      }
      steps.push({
        name,
        ms: performance.now() - t0,
        ok: !!data.ok,
        error: (data.errors ?? [])[0] ?? ''
      });
      return data;
    } catch (error) {
      failed = true;
      steps.push({
        name,
        ms: performance.now() - t0,
        ok: false,
        error: (error as Error).message
      });
      return null;
    }
  }

  const boot = await step('bootstrap', 'bootstrap');
  const projectId = boot?.projects[0]?.id ?? '';
  const wsData = await step('workspace:create', 'workspace:create', {
    projectId,
    name: '自检工作空间'
  });
  const wsId = wsData?.workspace.id ?? '';
  const cfgData = await step('config:create', 'config:create', {
    projectId,
    workspaceId: wsId,
    pluginKey: 'Nginx',
    name: 'selfcheck.conf'
  });
  const cfgId = cfgData?.configId ?? '';
  const openData = await step('config:open', 'config:open', {
    workspaceId: wsId,
    configId: cfgId
  });
  const typeOk = (openData?.snapshot ?? []).every(
    (node: { type?: unknown }) => typeof node.type === 'string'
  );
  steps.push({ name: 'type-check', ms: 0, ok: typeOk, error: typeOk ? '' : '字段类型不是字符串' });
  if (!typeOk) {
    failed = true;
  }

  await step('form:toggle', 'form:toggle', { path: 'http.upstreams', enabled: false });
  await step('form:toggle', 'form:toggle', { path: 'http.upstreams', enabled: true });
  await step('form:toggle-scalar', 'form:toggle', { path: 'user', enabled: false });
  await step('form:toggle-scalar', 'form:toggle', { path: 'user', enabled: true });
  await step('form:addItem', 'form:addItem', { path: 'http.upstreams' });
  await step('form:setValue', 'form:setValue', {
    path: 'http.upstreams[0].upstream_name',
    value: 'backend'
  });
  await step('form:render', 'form:render');
  await step('version:snapshot', 'version:snapshot', { note: '自检' });
  const ucfg = await step('config:create-unassigned', 'config:create', {
    projectId,
    workspaceId: '',
    pluginKey: 'Nginx',
    name: 'unassigned.conf'
  });
  await step('configs:unassigned', 'configs:unassigned', { projectId });
  await step('config:move', 'config:move', {
    configId: ucfg?.configId ?? '',
    workspaceId: wsId
  });
  await step('archive:exportWs', 'archive:exportWorkspace', {
    workspaceId: wsId,
    path: 'SELFCHECK'
  });
  await step('archive:import', 'archive:import', { path: 'SELFCHECK' });
  await step('versions:list', 'versions:list', { workspaceId: wsId, configId: cfgId });

  const worst = Math.max(0, ...steps.map((s) => s.ms));
  const interactive = steps.filter((s) => !s.name.startsWith('archive:')).map((s) => s.ms);
  const worstInteractive = Math.max(0, ...interactive);
  await getIpc().send(
    'spike:result',
    {
      ok: !failed && worstInteractive < 50,
      failed,
      worstMs: worst,
      worstInteractiveMs: worstInteractive,
      steps
    },
    { fireAndForget: true }
  );
}
