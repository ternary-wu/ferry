interface FerryExternal {
  sendMessage(message: string): void;
  receiveMessage(handler: (json: string) => void): void;
}
