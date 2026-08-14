export type FieldType = 'String' | 'Number' | 'Boolean' | 'Enum' | 'Array' | 'Object';

export interface EnumOption {
  value: string;
  description: string;
}

export interface PluginTemplateDto {
  id: string;
  name: string;
  description: string;
}

export interface PluginDescriptor {
  key: string;
  name: string;
  version: string;
  description: string;
  rendererType: string;
  defaultFileName: string;
  canImport: boolean;
  targetName: string;
  targetVersion: string;
  loadErrors: string[];
  templates: PluginTemplateDto[];
}

export interface ProjectInfo {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
}

export interface WorkspaceInfo {
  id: string;
  projectId: string;
  name: string;
  createdAt: string;
  updatedAt: string;
}

export interface ConfigInfo {
  id: string;
  name: string;
  pluginKey: string;
  pluginVersion: string;
  pluginName: string;
  pluginMissing: boolean;
  updatedAt: string;
  currentVersionId?: string | null;
}

export interface NavWorkspace {
  id: string;
  name: string;
  configs: ConfigInfo[];
}

export interface NavTree {
  workspaces: NavWorkspace[];
  unassigned: ConfigInfo[];
}

export interface FormFieldSnapshot {
  path: string;
  id: string;
  label: string;
  description: string;
  type: FieldType;
  value: unknown;
  isEnabled: boolean;
  isVisible: boolean;
  isModule: boolean;
  isArrayItem: boolean;
  isSelectable: boolean;
  canToggleEnabled: boolean;
  validationError?: string | null;
  totalChildModulesCount: number;
  enabledChildModulesCount: number;
  enabledChildModulesText: string;
  required: boolean;
  allowCustomValue: boolean;
  min?: number | null;
  max?: number | null;
  integerOnly: boolean;
  enumOptions: EnumOption[];
  children: FormFieldSnapshot[];
}

export interface ConfigMeta {
  id: string;
  name: string;
  pluginKey: string;
  pluginVersion: string;
  pluginName: string;
}

export interface ConfigOpenResult {
  config?: ConfigMeta;
  snapshot: FormFieldSnapshot[];
  sourceText: string;
  errors: string[];
  unrecognized: string[];
  versionChanged: boolean;
  templates: PluginTemplateDto[];
  pluginMissing?: boolean;
}

export interface FormResultData {
  snapshot: FormFieldSnapshot[];
  text?: string | null;
  errors: string[];
  newItemPath?: string | null;
  unrecognized: string[];
}

export interface VersionDto {
  id: string;
  note?: string | null;
  timestamp: string;
  length: number;
  preview: string;
}

export interface TrashItem {
  name: string;
  path: string;
  size: number;
  modified: string;
}

export interface PushTarget {
  id: string;
  name: string;
  type: 'local' | 'git' | 'ssh';
  remotePath: string;
  branch?: string;
  groupIds?: string[];
  sshUser?: string;
  remoteDir?: string;
  keyFile?: string;
  userName?: string;
  userEmail?: string;
}

export interface GitCommitDto {
  id: string;
  message: string;
  timestamp: string;
}

export interface HostGroup {
  id: string;
  name: string;
}

export interface HostEntry {
  id: string;
  ip: string;
  hostname?: string;
  port: number;
  groupId: string;
}

export interface ArchiveImportResult {
  imported: number;
  skipped: number;
  packagedPlugins: string[];
  localPlugins: string[];
  missingPlugins: string[];
  workspaceId: string | null;
}

export interface AppSettings {
  theme?: 'dark' | 'light' | 'system';
  animations?: boolean;
  restoreProject?: boolean;
  lastProjectId?: string;
  defaultPath?: string;
  notifyEnabled?: boolean;
  notifyStyle?: 'panel' | 'toast';
  moduleEnabled?: Record<string, boolean>;
  pluginDisabled?: Record<string, boolean>;
  tooltipDelay?: number;
  tooltipDelayEnabled?: boolean;
  tooltipEnabled?: boolean;
  tooltipShowDelay?: number;
  tooltipShowDelayEnabled?: boolean;
  showFileExtension?: boolean;
  trashDays?: number;
  trashSizeMB?: number;
  closeOutside?: boolean;
  pushTargets?: PushTarget[];
  hostGroups?: HostGroup[];
  hostInventory?: HostEntry[];
  [key: string]: unknown;
}

export type IpcResponse<T = Record<string, unknown>> = {
  ok: boolean;
  action?: string;
  requestId?: string;
  latencyMs?: number;
  errors?: string[];
  errorCode?: string;
} & T;

export interface ActionMap {
  bootstrap: {
    payload: Record<string, never>;
    data: {
      plugins: PluginDescriptor[];
      projects: ProjectInfo[];
      workspaces: WorkspaceInfo[];
      loadErrors: string[];
    };
  };
  'plugins:reload': {
    payload: Record<string, never>;
    data: { plugins: PluginDescriptor[]; loadErrors: string[] };
  };
  'projects:list': { payload: Record<string, never>; data: { projects: ProjectInfo[] } };
  'project:create': { payload: { name: string }; data: { project: ProjectInfo } };
  'project:rename': { payload: { id: string; name: string }; data: { project: ProjectInfo } };
  'project:delete': { payload: { id: string }; data: Record<string, never> };
  'workspaces:list': { payload: Record<string, never>; data: { workspaces: WorkspaceInfo[] } };
  'workspace:create': {
    payload: { projectId?: string; name: string };
    data: { workspace: WorkspaceInfo };
  };
  'workspace:rename': { payload: { id: string; name: string }; data: { workspace: WorkspaceInfo } };
  'workspace:delete': { payload: { id: string }; data: Record<string, never> };
  'workspace:reorder': {
    payload: { projectId: string; workspaceIds: string[] };
    data: Record<string, never>;
  };
  'nav:tree': { payload: { projectId: string }; data: NavTree };
  'configs:list': { payload: { workspaceId: string }; data: { configs: ConfigInfo[] } };
  'configs:unassigned': { payload: { projectId: string }; data: { configs: ConfigInfo[] } };
  'config:create': {
    payload: { projectId: string; workspaceId: string; pluginKey: string; name?: string };
    data: { configId: string };
  };
  'config:duplicate': {
    payload: { workspaceId: string; configId: string; name?: string };
    data: { configId: string; name: string };
  };
  'config:rename': {
    payload: { workspaceId: string; configId: string; name: string };
    data: { configId: string; name: string };
  };
  'config:open': { payload: { workspaceId: string; configId: string }; data: ConfigOpenResult };
  'config:delete': { payload: { workspaceId: string; configId: string }; data: Record<string, never> };
  'config:move': { payload: { configId: string; workspaceId: string }; data: { configId: string } };
  'config:reorder': {
    payload: { workspaceId: string; configIds: string[] };
    data: Record<string, never>;
  };
  'config:reset': {
    payload: Record<string, never>;
    data: { snapshot: FormFieldSnapshot[]; sourceText: string };
  };
  'config:saveSource': { payload: { text: string }; data: Record<string, never> };
  'config:exportTo': { payload: { path: string }; data: { path: string } };
  'config:exportFile': {
    payload: { workspaceId: string; configId: string; path: string };
    data: { path: string };
  };
  'form:snapshot': { payload: Record<string, never>; data: FormResultData };
  'form:validate': { payload: Record<string, never>; data: FormResultData };
  'form:render': { payload: Record<string, never>; data: FormResultData };
  'form:setValue': { payload: { path: string; value: unknown }; data: FormResultData };
  'form:toggle': { payload: { path: string; enabled?: boolean }; data: FormResultData };
  'form:addItem': { payload: { path: string }; data: FormResultData };
  'form:removeItem': { payload: { path: string }; data: FormResultData };
  'form:applyPreset': { payload: { preset: string }; data: FormResultData };
  'form:importText': {
    payload: { text: string };
    data: FormResultData & { report: { unrecognizedLines: number; canImport: boolean } };
  };
  'versions:list': {
    payload: { workspaceId: string; configId: string };
    data: { versions: VersionDto[] };
  };
  'version:snapshot': { payload: { note?: string }; data: { versionId: string } };
  'version:restore': { payload: { workspaceId: string; configId: string; versionId: string }; data: ConfigOpenResult };
  'version:delete': {
    payload: { workspaceId: string; configId: string; versionId: string };
    data: Record<string, never>;
  };
  'archive:exportWorkspace': { payload: { workspaceId: string; path: string }; data: { path: string } };
  'archive:exportConfig': {
    payload: { workspaceId: string; configId: string; path: string };
    data: { path: string };
  };
  'archive:exportProject': { payload: { projectId: string; path: string }; data: { path: string } };
  'archive:import': { payload: { path: string }; data: ArchiveImportResult };
  'file:openDialog': {
    payload: { title?: string; patterns?: string[]; filterName?: string };
    data: { path: string | null };
  };
  'file:saveDialog': {
    payload: {
      title?: string;
      defaultName?: string;
      patterns?: string[];
      defaultExt?: string;
      filterName?: string;
    };
    data: { path: string | null };
  };
  'logs:path': { payload: Record<string, never>; data: { path: string } };
  'logs:open': { payload: Record<string, never>; data: Record<string, never> };
  'app:dataDir': { payload: Record<string, never>; data: { path: string } };
  'trash:list': { payload: Record<string, never>; data: { items: TrashItem[] } };
  'trash:delete': { payload: { path: string }; data: Record<string, never> };
  'window:minimize': { payload: Record<string, never>; data: Record<string, never> };
  'window:maximize': { payload: Record<string, never>; data: Record<string, never> };
  'window:isMaximized': { payload: Record<string, never>; data: { maximized: boolean } };
  'window:close': { payload: Record<string, never>; data: Record<string, never> };
  'window:drag': { payload: Record<string, never>; data: Record<string, never> };
  'settings:get': { payload: Record<string, never>; data: { settings: AppSettings } };
  'settings:save': { payload: { settings: Partial<AppSettings> }; data: { settings: AppSettings } };
  'push:run': {
    payload: {
      workspaceId: string;
      configId: string;
      targetId: string;
      note?: string;
      hostId?: string;
    };
    data: { message: string };
  };
  'push:gitLog': {
    payload: { targetId: string; workspaceId: string; configId: string };
    data: { commits: GitCommitDto[] };
  };
  'push:gitRestore': {
    payload: { targetId: string; workspaceId: string; configId: string; commitId: string };
    data: { message: string; snapshotId?: string | null };
  };
  'hosts:import': {
    payload: { path: string; groupId?: string };
    data: { imported: number; skipped: number; entries: HostEntry[] };
  };
  'hosts:export': {
    payload: { path: string; format: 'txt' | 'yaml'; groupId?: string };
    data: { path: string };
  };
  log: { payload: { text: string }; data: Record<string, never> };
  'spike:result': {
    payload: {
      ok: boolean;
      failed: boolean;
      worstMs: number;
      worstInteractiveMs: number;
      steps: Array<{ name: string; ms: number; ok: boolean; error: string }>;
    };
    data: Record<string, never>;
  };
}

export type IpcAction = keyof ActionMap;
export type IpcPayload<K extends IpcAction> = ActionMap[K]['payload'];
export type IpcData<K extends IpcAction> = ActionMap[K]['data'];
export type IpcResult<K extends IpcAction> = IpcResponse<IpcData<K>>;
