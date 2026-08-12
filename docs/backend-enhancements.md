# 后端增强规划（2026-08-12）

> 本文档记录「前端绿地重构」附带的最小后端增强范围。目标：保持 Core 领域语义
> （SourceText 权威、实时保存、FormSession 命令契约）不变，只补齐前端需要的持久化能力。

## 一、本次已实施（最小集）

### 1. 配置排序持久化

背景：旧 UI 的配置排序只存在 `localStorage`（`ferry.order.<project>.<ws>`），清缓存即丢失；
新前端需要排序跨重启保持，并由 Core 负责持久化。

实施内容：

- `IWorkspaceStore` 新增 `GetConfigOrder(workspaceId)` / `SaveConfigOrder(workspaceId, configIds)`。
- `LocalWorkspaceStore` 在 `workspace.json` 根节点新增 `configOrder`（workspaceId → 有序配置 ID 数组）；
  保存配置时自动追加到所属工作空间顺序末尾，删除/移动/级联删除时自动清理顺序条目。
- `WorkspaceService`：
  - `ListConfigs(workspaceId)` / `ListUnassignedConfigs(projectId)` 按存储顺序返回；
  - `ReorderConfigs(workspaceId, configIds)`（严格入口：必须恰好包含全部配置且不重复）；
  - `ApplyConfigOrder(workspaceId, orderedIds)`（宽容入口：存档导入等场景，只保留已存在配置的相对顺序）。
  - `MoveConfig` 跨工作空间移动时：源顺序移除、目标顺序追加末尾。
- 新增 IPC：`config:reorder { workspaceId, configIds }`。
- 存档包：`ExportWorkspace` 的 manifest 携带 `configOrder`；导入后按相对顺序恢复。

### 2. Settings 持久化

背景：旧 UI 的 Settings 全部只写 `localStorage`（`ferry.theme`、`ferry.trashDays` 等），
大量项“只存不生效”；新前端需要统一持久化并真实接线。

实施内容：

- `IWorkspaceStore` 新增 `LoadSettings()` / `SaveSettings(settings)`。
- `LocalWorkspaceStore` 在 `workspace.json` 根节点新增 `settings` 对象；`SaveSettings` 为 merge 语义
  （只覆盖传入 key，其余保留；`null` 值删除 key）。
- `WorkspaceService` 提供透传的 `LoadSettings` / `SaveSettings`。
- 新增 IPC：`settings:get` / `settings:save { settings: {...} }`。
- 固定 key 集合（前端 Settings 阶段接线）：
  `theme`、`animations`、`restoreProject`、`lastProjectId`、`defaultPath`、
  `notifyEnabled`、`notifyStyle`、`moduleEnabled`、`pluginDisabled`、
  `tooltipDelay`、`trashDays`、`trashSizeMB`、`closeOutside`。

## 二、明确列为后续（本轮不做）

- **回收站真软删除**：当前“删除”= 先导出 zip 到 `%AppData%\Ferry\trash` 再真删；
  后续改为存储层软删除标记 + 后台定时清理（保留时间/最大空间），并支持还原不依赖 zip 导入。
- **模块系统后端化**：当前 `FerryModules` 只是前端存根；后续定义 Core 模块契约、
  发现/加载机制与扩展点，前端只消费模块注册表。
- **数组项内模块状态持久化**：`FormNode` 注释已明确该限制；后续把数组项内模块的
  enabled 状态纳入 `ConfigState.Enabled` 持久化。
- **Settings 扩展 key**：后续新增能力（推送凭据、协作账号、加密信息等）时按同一机制扩展。
