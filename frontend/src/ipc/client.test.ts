import { afterEach, describe, expect, it, vi } from 'vitest';
import { createIpcClient, IpcError } from './client';
import { createMockTransport } from './mock';

describe('createIpcClient', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('resolves matched response by requestId', async () => {
    const mock = createMockTransport();
    const client = createIpcClient(mock.transport);

    const pending = client.send('app:dataDir', {});
    await Promise.resolve();

    expect(mock.sent).toHaveLength(1);
    const requestId = mock.sent[0].requestId;
    mock.respond(requestId, { path: 'C:\\Ferry' });

    await expect(pending).resolves.toMatchObject({ ok: true, path: 'C:\\Ferry' });
  });

  it('rejects with IpcError on ok:false', async () => {
    const mock = createMockTransport();
    const client = createIpcClient(mock.transport);

    const pending = client.send('settings:get', {});
    await Promise.resolve();

    mock.respondError(mock.sent[0].requestId, ['设置读取失败'], 'validation');

    await expect(pending).rejects.toBeInstanceOf(IpcError);
    await expect(pending).rejects.toThrow('设置读取失败');
  });

  it('ignores unknown requestId and rejects on timeout', async () => {
    vi.useFakeTimers();
    const mock = createMockTransport();
    const client = createIpcClient(mock.transport, 10000);

    const pending = client.send('app:dataDir', {});
    await Promise.resolve();

    mock.respond('unknown-id', { path: 'ignored' });
    vi.advanceTimersByTime(10001);

    await expect(pending).rejects.toThrow('IPC 超时');
  });

  it('fireAndForget sends message and resolves immediately', async () => {
    const mock = createMockTransport();
    const client = createIpcClient(mock.transport);

    await expect(client.send('window:close', {}, { fireAndForget: true })).resolves.toMatchObject({
      ok: true
    });
    expect(mock.sent).toHaveLength(1);
    expect(mock.sent[0].action).toBe('window:close');
  });
});
