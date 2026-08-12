import type { IpcAction, IpcPayload, IpcResponse, IpcResult } from './types';

export interface IpcTransport {
  send(message: string): void;
  onReceive(handler: (json: string) => void): void;
}

export interface IpcSendOptions {
  fireAndForget?: boolean;
  timeoutMs?: number;
}

export class IpcError extends Error {
  readonly action?: string;
  readonly requestId?: string;
  readonly errorCode?: string;
  readonly errors: string[];

  constructor(response: {
    ok: boolean;
    action?: string;
    requestId?: string;
    errors?: string[];
    errorCode?: string;
  }) {
    super((response.errors ?? ['IPC 请求失败']).join('；'));
    this.name = 'IpcError';
    this.action = response.action;
    this.requestId = response.requestId;
    this.errorCode = response.errorCode;
    this.errors = response.errors ?? [];
  }
}

export interface IpcClient {
  send<K extends IpcAction>(
    action: K,
    payload: IpcPayload<K>,
    options?: IpcSendOptions
  ): Promise<IpcResult<K>>;
}

interface Inflight {
  resolve: (data: IpcResponse) => void;
  reject: (error: IpcError) => void;
  timer: ReturnType<typeof setTimeout>;
}

export function createWebViewTransport(): IpcTransport {
  const external = window.external as unknown as FerryExternal;
  return {
    send(message: string) {
      external.sendMessage(message);
    },
    onReceive(handler: (json: string) => void) {
      external.receiveMessage(handler);
    }
  };
}

export function createIpcClient(
  transport: IpcTransport,
  defaultTimeoutMs = 10000
): IpcClient {
  let seq = 0;
  const inflight = new Map<string, Inflight>();

  transport.onReceive((json: string) => {
    let data: IpcResponse;
    try {
      data = JSON.parse(json) as IpcResponse;
    } catch {
      return;
    }
    if (data.action === 'spike:run' || data.action === 'spike:result') {
      return;
    }
    if (!data.requestId) {
      return;
    }
    const item = inflight.get(data.requestId);
    if (!item) {
      return;
    }
    clearTimeout(item.timer);
    inflight.delete(data.requestId);
    if (data.ok) {
      item.resolve(data);
    } else {
      item.reject(new IpcError(data));
    }
  });

  return {
    send<K extends IpcAction>(
      action: K,
      payload: IpcPayload<K>,
      options: IpcSendOptions = {}
    ): Promise<IpcResult<K>> {
      const timeoutMs = options.timeoutMs ?? defaultTimeoutMs;
      const requestId = 'r' + ++seq;
      const message = JSON.stringify({ action, requestId, ...(payload as object) });

      if (options.fireAndForget) {
        transport.send(message);
        return Promise.resolve({ ok: true } as IpcResult<K>);
      }

      return new Promise<IpcResult<K>>((resolve, reject) => {
        const timer = setTimeout(() => {
          inflight.delete(requestId);
          reject(new IpcError({ ok: false, action, requestId, errors: ['IPC 超时'] }));
        }, timeoutMs);
        inflight.set(requestId, {
          resolve: (data) => resolve(data as IpcResult<K>),
          reject,
          timer
        });
        transport.send(message);
      });
    }
  };
}
