import { createIpcClient, createWebViewTransport } from './client';
import type { IpcClient } from './client';

export * from './client';
export * from './mock';
export * from './types';

let activeClient: IpcClient | null = null;
let latencyListener: ((ms: number) => void) | null = null;

/** 注册全局 IPC 延迟回调（状态栏显示用）。 */
export function setIpcLatencyListener(listener: ((ms: number) => void) | null): void {
  latencyListener = listener;
}

/** 获取应用级 IPC 单例；非浏览器环境（Vitest）回退为无操作传输，测试可显式替换。 */
export function getIpc(): IpcClient {
  if (!activeClient) {
    const onLatency = (ms: number) => latencyListener?.(ms);
    activeClient =
      typeof window !== 'undefined' && window.external
        ? createIpcClient(createWebViewTransport(), 10000, onLatency)
        : createIpcClient({
            send() {
              /* 无操作 */
            },
            onReceive() {
              /* 无操作 */
            }
          }, 10000, onLatency);
  }
  return activeClient;
}

/** 测试注入点：替换全局 IPC 客户端。 */
export function setIpcClientForTesting(client: IpcClient): void {
  activeClient = client;
}
