import { createIpcClient, createWebViewTransport } from './client';
import type { IpcClient } from './client';

export * from './client';
export * from './mock';
export * from './types';

let activeClient: IpcClient | null = null;

/** 获取应用级 IPC 单例；非浏览器环境（Vitest）回退为无操作传输，测试可显式替换。 */
export function getIpc(): IpcClient {
  if (!activeClient) {
    activeClient =
      typeof window !== 'undefined' && window.external
        ? createIpcClient(createWebViewTransport())
        : createIpcClient({
            send() {
              /* 无操作 */
            },
            onReceive() {
              /* 无操作 */
            }
          });
  }
  return activeClient;
}

/** 测试注入点：替换全局 IPC 客户端。 */
export function setIpcClientForTesting(client: IpcClient): void {
  activeClient = client;
}
