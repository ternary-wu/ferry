import type { IpcTransport } from './client';

export interface SentMessage {
  action: string;
  requestId: string;
  payload: Record<string, unknown>;
}

export function createMockTransport() {
  const listeners: Array<(json: string) => void> = [];
  const sent: SentMessage[] = [];

  const transport: IpcTransport = {
    send(message: string) {
      const parsed = JSON.parse(message) as Record<string, unknown>;
      sent.push({
        action: String(parsed.action ?? ''),
        requestId: String(parsed.requestId ?? ''),
        payload: parsed
      });
    },
    onReceive(handler: (json: string) => void) {
      listeners.push(handler);
    }
  };

  return {
    transport,
    sent,
    respond(requestId: string, data: Record<string, unknown>) {
      const json = JSON.stringify({ ok: true, requestId, ...data });
      listeners.forEach((handler) => handler(json));
    },
    respondError(requestId: string, errors: string[], errorCode?: string) {
      const json = JSON.stringify({ ok: false, requestId, errors, errorCode });
      listeners.forEach((handler) => handler(json));
    }
  };
}
