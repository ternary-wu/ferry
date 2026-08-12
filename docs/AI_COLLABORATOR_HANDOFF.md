# Ferry 项目协作 AI 交接文档

> 生成时间：2026-08-12。适用对象：无法直接阅读整个仓库、只能依赖本文档理解项目的协作 AI。
> 代码事实以 `D:\Program\C#program\Ferry` 当前工作树为准；本文档与当前代码核对过。
> 仓库内 `DEV_HANDOFF.md`、`docs/design.md`、`docs/developer-guide.md` 均严重滞后，**不要作为事实**；
> `docs/backend-enhancements.md` 是当前的后端增强记录，可以读。

---

## 1. 项目是什么

Ferry 是一款**插件化运维配置生成工具**，面向非专业运维用户：通过插件定义「表单 + 语法规则」，
动态渲染配置编辑界面，生成 json / yaml / ini / 自定义 layout 格式的配置文件，并支持导入导出、
版本留档/回滚、工作空间管理、可移植存档包。

产品模型是严格三层：

```
项目 Project
└── 工作空间 Workspace
    └── 配置 Config（绑定一个插件，如 nginx.conf / redis.conf）
        └── 版本 Version（源码快照，可留档/回滚）
```

核心语义（不要破坏）：

- **源码为权威**：配置存档主体是 `SourceText`；`Values/Enabled` 只是打开配置时由源码解析出的表单缓存。
- **实时保存**：每次表单变更命令（`form:*`）后端立即渲染并落盘，没有“保存”按钮。
- **配置只能同时打开一个**；未分配工作空间的配置进入“未分类”区。
- **勾选语义 v3**：勾选子模块级联启用祖先；取消父模块保留子状态与值；必填字段锁定。

---

## 2. 技术栈总览

| 层 | 技术 |
|---|---|
| 桌面宿主 | Photino.NET 3.0.14（WebView2 消息桥，C# / .NET 10 / net10.0-windows） |
| 后端领域 | Ferry.Core（纯逻辑库，只依赖 YamlDotNet；不感知 UI/传输） |
| 基础设施 | Ferry.Infrastructure（本地 JSON 存储实现、本地目录推送实现） |
| 前端 | Vue 3.5 + TypeScript strict + Vite 7 + Tailwind CSS v4 + Pinia 3 + vue-router（hash 模式） |
| 前端测试 | Vitest（当前 34 个用例） |
| 后端测试 | xUnit（当前 69 个用例，13 个测试文件） |

架构分层（六边形）：

```
Ferry.App（宿主：Photino + IPC 分发）
    │  调用
Ferry.Core（领域 + 用例：WorkspaceService / FormSession / 插件 / 渲染 / 校验 / 存档）
    │  依赖端口
Ports: IWorkspaceStore / IPluginSource / IPushService
    ▲
Ferry.Infrastructure（适配器：LocalWorkspaceStore / DirectoryPluginSource / LocalDirectoryPushService）
```

- `Ferry.Core` 零引用 `Ferry.App` / `Ferry.Infrastructure`（可用 grep 验证）。
- `DirectoryPluginSource`（文件扫描实现）目前放在 Core 内，是已知的小瑕疵。
- `Ferry.Infrastructure` 只引用 Core。
- `Ferry.App` 引用 Core + Infrastructure，同时承载旧 `wwwroot` 与新 `frontend/dist`。

---

## 3. 仓库结构

```
Ferry.slnx                        解决方案（4 个项目）
Ferry.Core/                       领域模型、服务、端口、会话引擎
Ferry.Infrastructure/             本地 JSON 存储、本地推送
Ferry.App/                        桌面宿主：Program.cs、Window/WindowController.cs、wwwroot（旧 UI）、Properties/launchSettings.json
frontend/                         Vue 新前端（唯一真实 UI）
frontend/src/                     App.vue / components / stores / views / ipc / utils / styles / router
frontend/dist/                    Vite 构建产物（由 Ferry.App.csproj 拷到输出目录 ui/）
tests/Ferry.Core.Tests/           xUnit 测试
Plugins/                          插件资产（Nginx、App-config、Dockerfile、Redis、Docker-compose 等，YAML 三文件）
docs/                             backend-enhancements.md（可信）；design/developer-guide/plugin-development（部分滞后）
scripts/frontend-ci.ps1           前端 CI 辅助脚本（镜像目录方案）
```

旧 `Ferry.App/wwwroot/*`（index.html / styles.css / app.js / window.js / modules.js）是**旧 UI，已不是默认加载**，
只作行为参考，最终阶段会退役。

---

## 4. 构建 / 运行 / 测试（协作 AI 必读）

### 4.1 环境关键约束（踩过坑，务必遵守）

1. **仓库路径含 `#`**（`D:\Program\C#program\Ferry`）：Vite / Vitest / Rollup 在该路径下无法运行。
   前端检查必须在**无 `#` 的镜像目录**里做。
2. **系统 PATH 没有 node/npm**：使用绝对路径
   `C:\Users\Wu\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe`。
3. **现有镜像目录**（已装好 node_modules）：
   `C:\Users\Wu\.codex\visualizations\2026\08\12\019ff53d-4684-77b2-8aa2-b237da5fe901\ferry-frontend-ci`
   每次改前端后把 `frontend/src` 同步进镜像再跑检查；检查完把镜像 `dist` 拷回 `frontend/dist`。

### 4.2 后端命令（在仓库根目录，PowerShell）

```powershell
dotnet build .\Ferry.slnx -v q --nologo     # 必须 0 警告 0 错误
dotnet test .\Ferry.slnx -v q --nologo      # 当前 69/69
```

### 4.3 前端检查（镜像流程）

```powershell
$mirror = 'C:\Users\Wu\.codex\visualizations\2026\08\12\019ff53d-4684-77b2-8aa2-b237da5fe901\ferry-frontend-ci'
$node = 'C:\Users\Wu\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'

# 1) 同步源码（用 Copy-Item 把 frontend/src 内容合并进 $mirror\src）
Copy-Item .\frontend\src\* -Destination (Join-Path $mirror 'src') -Recurse -Force

Push-Location $mirror
& $node node_modules\vue-tsc\bin\vue-tsc.js --noEmit       # 类型检查
& $node node_modules\vitest\vitest.mjs run                 # 单测（当前 34/34）
& $node node_modules\vite\bin\vite.js build                # 生产构建（输出 dist/）
Pop-Location

# 2) 把镜像 dist 拷回仓库 frontend/dist（删旧再拷）
# 3) dotnet build .\Ferry.slnx  // 把 dist 以 ui/ 前缀拷进 Ferry.App/bin/.../
```

也可用 `.\scripts\frontend-ci.ps1 -Task build|test`（它会重建临时镜像并离线安装依赖，较慢）。

### 4.4 启动

```powershell
dotnet run --project Ferry.App          # 或直接运行 Ferry.App\bin\Debug\net10.0-windows\Ferry.App.exe
```

- **默认加载新 UI**（`ui/index.html`）；只有设置环境变量 `FERRY_UI_OLD=1` 才加载旧 `wwwroot`。
- Visual Studio F5 调试：`Ferry.App/Properties/launchSettings.json` 已配置 `FERRY_UI_NEW=1`（与默认一致）。
- 启动日志：`Ferry.App\bin\Debug\net10.0-windows\ferry-spike-log.txt`，最后一行会记录实际加载的 HTML 路径
  （`loaded: ...\ui\index.html` = 新 UI）。业务日志在 `ferry.log`（5MB 轮转）。

---

## 5. 前后端通信（IPC）

### 5.1 传输

- WebView2 同进程消息桥，不是 HTTP：JS 调 `window.external.sendMessage(JSON)`，
  C# 调 `window.SendWebMessage(JSON)`。
- 前端只在 `frontend/src/ipc/client.ts` 的 `createWebViewTransport()` 接触 `window.external`；
  `mock.ts` 提供测试传输；`index.ts` 的 `getIpc()` 运行时自动选择，`setIpcClientForTesting()` 供测试注入。

### 5.2 协议

请求：`{ "action": "...", "requestId": "...", ...payload }`

响应：`{ "ok": true, "action": "...", "requestId": "...", "latencyMs": number, ...data }`
失败：`{ "ok": false, "errors": string[], "errorCode"?: string, "requestId": "..." }`

- 前端客户端：requestId 配对、10 秒超时、容忍乱序/重复；`fireAndForget` 选项只发不收。
- `window:close` 后端提前 return 不回包；`window:drag` 是同步模态拖拽循环，前端两者都 fire-and-forget。
- `spike:run` 只下发不回包；`spike:result` 回写文件后关窗（自检专用）。
- **类型契约**：`frontend/src/ipc/types.ts` 的 `ActionMap` 是后端 `Program.cs` switch 的**手写镜像**，
  新增命令要同步改：后端 handler、types.ts、store 方法、必要时测试——共 4 处。

### 5.3 当前 IPC 命令全量清单

引导/插件：`bootstrap`、`plugins:reload`

项目：`projects:list`、`project:create`、`project:rename`、`project:delete`

工作空间：`workspaces:list`、`workspace:create`、`workspace:rename`、`workspace:delete`

导航/配置：`nav:tree`、`configs:list`、`configs:unassigned`、`config:create`、`config:duplicate`、
`config:open`、`config:delete`、`config:move`、`config:reorder`、`config:reset`、`config:saveSource`、`config:exportTo`

表单：`form:snapshot`、`form:validate`、`form:render`、`form:setValue`、`form:toggle`、
`form:addItem`、`form:removeItem`、`form:applyPreset`、`form:importText`

版本：`versions:list`、`version:snapshot`、`version:restore`、`version:delete`

存档/路径：`archive:exportWorkspace`、`archive:exportConfig`、`archive:import`、
`logs:path`、`logs:open`、`app:dataDir`

回收站：`trash:list`、`trash:delete`

窗口：`window:minimize`、`window:maximize`、`window:close`、`window:drag`

设置：`settings:get`、`settings:save`

调试：`log`

---

## 6. 后端领域模型

### 6.1 核心对象（Ferry.Core/Ports/IWorkspaceStore.cs）

- `ProjectInfo(Id, Name, CreatedAt, UpdatedAt)`
- `WorkspaceInfo(Id, ProjectId, Name, ...)`（扁平，不允许嵌套）
- `ConfigInfo(Id, WorkspaceId, Name, PluginKey, PluginVersion, UpdatedAt, CurrentVersionId)`
- `ConfigData`：`Id / ProjectId / WorkspaceId / Name / PluginKey / PluginVersion /
  SourceText（权威）/ Values / Enabled / Unrecognized / VersionId`
- `VersionSnapshot(Id, ConfigId, SourceText, Timestamp, Note)`
- `IWorkspaceStore`：项目/工作空间/配置/版本/settings/configOrder 的读写端口。

### 6.2 WorkspaceService（Ferry.Core/Services/WorkspaceService.cs）

主要方法：

- 项目：`CreateProject / RenameProject / DeleteProject / ListProjects / EnsureDefaultProject`
- 工作空间：`CreateWorkspace / RenameWorkspace / DeleteWorkspace / ListWorkspaces`
- 配置：`CreateConfig / DuplicateConfig / MoveConfig / DeleteConfig / LoadConfig / SaveConfig /
  ListConfigs / ListUnassignedConfigs`
- 排序：`ReorderConfigs`（严格：必须恰好包含全部配置且不重复）、`ApplyConfigOrder`（宽容，存档导入用）
- 版本：`SnapshotVersion / RestoreVersion / ListVersions / DeleteVersion`
- 设置：`LoadSettings / SaveSettings`（merge 语义）
- 静态：`ResolvePlugin`、`IsPluginVersionChanged`

注意：

- `MoveConfig` 跨工作空间移动时**版本历史不随迁**（已知限制，旧工作空间下的版本仍留在旧桶）。
- `DeleteConfig` 连版本一起删；`RemoveConfig` 只移桶不删版本（移动用）。
- 排序持久化：`workspace.json` 根节点 `configOrder`（workspaceId → 有序 ID 数组）。
- Settings 持久化：`workspace.json` 根节点 `settings` 对象；`SaveSettings` 只覆盖传入 key，`null` 删除 key。
  固定 key：`theme / animations / restoreProject / lastProjectId / defaultPath /
  notifyEnabled / notifyStyle / moduleEnabled / pluginDisabled / tooltipDelay /
  trashDays / trashSizeMB / closeOutside`。

### 6.3 FormSession（会话引擎，Ferry.Core/Services/Session/）

- 命令协议 DTO：`SetValueCommand / ToggleEnabledCommand / AddItemCommand / RemoveItemCommand /
  ApplyPresetCommand / ImportCommand / ValidateCommand / RenderCommand / SnapshotCommand`。
- 实例入口：`FormSession.Create(plugin, state?)` + `Apply(command)`（单机/Photino 用）。
- 无状态入口：`FormSession.Execute(plugin, state, command, expectedVersion?)`（服务器用，带乐观锁）。
- 输出：`GetSnapshot()` → `List<FormFieldSnapshot>`（只读渲染树，不暴露事件树）；
  `GetState()` → `ConfigState`（可序列化）；`Render()`；`Validate()`；`Import(text)`。
- 路径规则：静态路径如 `http.servers`，数组项用稳定序号 `http.servers[0]`（PathResolver）。
- 打开配置 = 源码 → `Import` 解析 → 表单；layout/ini 用宽松解析并保留 `Unrecognized`。

### 6.4 插件系统

- `PluginManager` + `DirectoryPluginSource`（`IPluginSource`）：扫描插件根目录一级子目录，
  **PluginKey = 目录名**。
- 插件三文件：`plugin.yaml`（元数据/renderer）、`schema.yaml`（字段定义）、`templates.yaml`（场景模板，
  回退 schema 旧 presets）。
- 字段类型：String / Number / Boolean / Enum / Array / Object；支持依赖显隐、required、min/max、
  integerOnly、allowCustomValue、validations.pattern、render 布局声明。
- 渲染：`RendererFactory` → json / yaml / ini / layout 四个渲染器。
- 导入：`ConfigImporter`（json/yaml 精确）+ `ConfigReverseParser`（layout/ini 宽松，未识别内容保留）。
- 校验：`ConfigValidator`（required / min/max / integerOnly / 枚举 / pattern）。
- 插件缺失：`pluginMissing=true` 时仅可查看/导出源码；版本变化返回 `versionChanged`。

### 6.5 存档包 / 回收站

- `PortableArchiveService`：zip 容器（manifest + configs + versions + 插件三文件）。
  `ExportWorkspace / ExportConfig / Import`；导入按同名项目/工作空间/配置复用，支持随包插件只读加载。
  manifest 携带 `configOrder`，导入后恢复排序。
- **回收站不是真软删除**：删除前先 `archive:exportConfig/exportWorkspace` 导出 zip 到
  `%AppData%\Ferry\trash\`，再真删；还原 = `archive:import` 该 zip（可能同名合并）。真软删除是后续项。

### 6.6 已知后端限制（明确不在本轮）

- 数组项内模块状态不持久化（Core 注释明确）。
- 回收站真软删除 + 自动清理（保留时间/最大空间）未实现。
- 模块系统后端化未实现（当前仅前端存根）。
- 推送：`IPushService` 契约与 `LocalDirectoryPushService` 存在但未接入 UI/IPC。
- `LegacyWorkspaceMigrator` 是旧数据迁移遗留，仍存在。
- 配置移动后版本历史不随迁。

---

## 7. 前端架构

### 7.1 目录与职责

```
frontend/src/
├── main.ts                 入口：createApp + Pinia + router
├── App.vue                 全局壳：TitleBar + Sidebar + Main(RouterView + SourceDock) + StatusBar + 弹窗
├── router/index.ts         hash 路由：/ Welcome、/editor Editor、/settings Settings
├── ipc/                    client.ts（传输抽象+客户端）、types.ts（ActionMap）、mock.ts、index.ts（单例）
├── stores/                 app / project / config / settings / ui / dock / window / notification / wizard
├── components/             TitleBar、Sidebar、StatusBar、ContextMenu、ModalHost、MoveModal、
│                           HistoryModal、WizardModal、SourceDock、field/FieldNode、field/FieldControl
├── views/                  WelcomeView、EditorView、SettingsView
├── utils/                  storage.ts（localStorage 封装）、fieldTree.ts（折叠路径收集）
├── styles/main.css         Tailwind v4 + 全部自定义样式 + --ferry-* 主题 token
└── types/external.d.ts     window.external 类型
```

### 7.2 Pinia store 职责

- `app`：plugins、loadErrors、status、latencyMs、bootstrap。
- `project`：projects、currentProjectId、nav（workspaces + unassigned）、项目/工作空间 CRUD、
  moveConfig、duplicateConfig、reorderConfigs、loadNav。
- `config`：当前配置快照（current/workspaceId/snapshot/sourceText/errors/unrecognized/...）、
  filter/search/collapsed；open/close/applyFormResult/setValue/toggle/addItem/removeItem/resetCurrent。
- `settings`：settings、loaded、load/save（merge 到后端）。
- `ui`：右键菜单、prompt/confirm、settingsCategory、Move 弹窗状态、History 弹窗状态。
- `dock`：open、width（35%–60%，拖拽中 30%–60%）、maximized、lineNumbers；
  openDock/closeDock/toggle/resizeTo/finishResize/toggleMaximize/toggleLineNumbers。
- `window`：minimize / toggleMaximize / close / beginDrag（close、drag 为 fire-and-forget）。
- `notification`：本地通知列表（localStorage `ferry.notifications`，上限 50）；面板/Toast 接线在 P9。
- `wizard`：三步向导状态（插件→模板→名称/工作空间）；模板记忆 `ferry.tpl.<key>`、
  配置模板记忆 `ferry.tplCfg.<configId>`、最近插件 `ferry.recentPlugins`。

### 7.3 页面

- **WelcomeView**：Logo、说明、新建配置按钮、最近使用插件（最多 4 个，动态居中）。
- **EditorView**：配置编辑页。头部（配置名/元信息 + 右上角「源码」按钮，Dock 打开时同排出现「全占/还原」）；
  工具栏（筛选/搜索/折叠全部/展开全部）；字段树（FieldNode/FieldControl，六类控件、模块勾选语义 v3、
  搜索自动展开、数组增删、前端即时校验 + 后端错误回显）。
- **SettingsView**：六分类（常规/外观/插件管理/模块管理/存储/通知）导航已建，**内容为占位（P8 实现）**。

### 7.4 样式

- Tailwind v4（`@import "tailwindcss"`）+ 自定义 CSS。深色主题 token（`--ferry-bg:#212121`、
  `--ferry-surface:#171717`、`--ferry-primary:#2f6feb` 等）当前唯一生效；
  Light/System 主题切换未实现（P8）。
- `html/body/#app` 高度 100%、`margin:0`；body 深色背景、`overflow:hidden`。

### 7.5 数据/偏好存储

- 业务状态（排序、设置）已迁后端 `workspace.json`。
- localStorage 只保留 UI 偏好：模板记忆、最近使用、Dock 宽度/行号、通知列表。

---

## 8. Window Shell（重点，容易踩坑）

### 8.1 启动流程（Ferry.App/Program.cs）

1. `WindowController.EnablePerMonitorV2Dpi()`（强制 PerMonitorV2，兜底 manifest）。
2. 组装 `PhotinoWindow`：默认 1280×800、最小 1200×720、Chromeless。
3. `WindowController.Initialize()` 注册窗口事件。
4. `HandleMessage` 是全部 IPC 分发（一个大的 switch + JsonObject 手拼 DTO）。
5. 默认加载 `ui/index.html`（新前端）；`FERRY_UI_OLD=1` 才加载 `wwwroot/index.html`。

### 8.2 无边框窗口实现（Ferry.App/Window/WindowController.cs）

- 样式：`WS_POPUP | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX`，去掉 `WS_CAPTION`。
- **白边修复**：`WM_NCCALCSIZE` 返回 0，客户区=窗口区（去掉 DWM 透明边框透出桌面的 7px 白边）；
  另有 `DWMNCRP_DISABLED` 兜底。
- **圆角**：`DWMWA_WINDOW_CORNER_PREFERENCE(33)`，普通状态 `DWMWCP_ROUND(2)`（Win11 8px），
  最大化时 `DWMWCP_DONOTROUND(1)` 切直角。仅 Win11 生效。
- **DPI**：PerMonitorV2（manifest + 运行时兜底）；默认/最小尺寸按物理像素，不再乘 DPI 系数。
- **工作区最大化**：不是 `SetMaximized`，而是手动 `SetWindowPos` 到当前显示器 `rcWork`
  （结构上不可能盖任务栏）；还原恢复最大化前矩形。
- **窗口拖动**：`ReleaseCapture()` + `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)`；
  前端 `window:drag` fire-and-forget（拖拽是同步模态循环）。
- **关闭**：`WindowClosingHandler` 必须返回 `false`（Photino 3.0.14 中 `true` = 取消关闭，
  曾导致关不掉）；`window:close` 后端提前 return 不回包。
- **边缘 Resize**：`WM_NCHITTEST` 自绘边框命中（6px×DPI）；最大化时返回 HTCLIENT。
- **状态持久化**：`%AppData%\Ferry\window.json`（位置/尺寸/是否最大化）；`EnsureVisible` 会把
  尺寸钳制到工作区、把位置拉回可见范围。

### 8.3 Window Shell 已知限制

- `WM_NCCALCSIZE=0` 会去掉系统窗口阴影；边缘拖拽手感依赖自定义命中测试，需真机确认。
- Win10 不支持 DWM 圆角（会保持直角）。
- `GetPixel` 截屏采样对 WebView2（DirectComposition）不可靠，验证白边要用 `PrintWindow` 或人眼。

---

## 9. 拖拽系统（统一 Drag Session）

全部在 `frontend/src/components/Sidebar.vue`，使用 HTML5 DnD。

### 9.1 状态

```ts
dragSession  = { config: ConfigInfo, sourceWorkspaceId: string } | null
dropTarget   = { mode: 'workspace' | 'workspace-sort' | 'unassigned' | 'create-workspace',
                 workspaceId?, configId?, before? } | null
```

- `workspace`：拖到工作空间标题 → 加入该工作空间末尾。
- `workspace-sort`：拖到工作空间内某配置行 → 加入并插入指定位置（before/after 插入线）。
- `unassigned`：拖到「配置」区域 → 移入未分类（带 configId/before 时为插入）。
- `create-workspace`：拖到「＋ 创建工作空间」→ 弹输入框 → 创建工作空间 → 自动移入 → 展开 → 打开配置。

### 9.2 关键实现要点

- 同空间排序：等 `config:reorder` 成功后才应用本地顺序（避免异步竞态）。
- 跨空间：先 `config:move` 再 `config:reorder` 到目标位置；若移动的是当前打开的配置，
  之后重新 `config:open`（否则后端会话仍指向旧工作空间，后续保存会写错桶）。
- Drop Zone 由 `dragSession` 直接驱动（`v-if="dragSession"`），位于 Sidebar 底部 `flex-1` 弹性区之前；
  这样出现时弹性区吸收高度，**不会推挤正在拖动的配置行**（此前推挤导致未分类配置拖不动）。
- 光标保持默认箭头：树/配置行/Drop Zone 均不设 `cursor: grab/pointer`。

---

## 10. 已完成功能状态

按阶段（均在 `codex/greenfield-ui` 分支）：

| 阶段 | 内容 | 提交 |
|---|---|---|
| P0 | 基线提交 | 552b343 |
| P1 | 后端最小增强（排序持久化、Settings 持久化） | 14f37a0 |
| P2 | 前端工程地基（Vite+Vue+TS+Tailwind+Pinia、类型化 IPC、AppShell） | f8f6f23 |
| P3 | 布局与导航（Sidebar/三视图/面包屑/右键菜单） | 89c5e7d、7dd184f |
| P4 | Welcome + 三步新建向导 | 2052094 |
| P5 | 配置编辑页字段树 | 804f558 |
| 窗口修复 | 关闭按钮（WindowClosingHandler 返回 false） | 3262881 |
| P6 | 只读源码 Dock（实时刷新、35–60% 拖拽、阈值关闭、全占/还原） | 59e05ec |
| P7 | 拖拽排序/移动、完整菜单、移动/历史弹窗、删除先进回收站、config:duplicate | a559c7e |
| 定点修复 | 窗口拖动/白边/工作区最大化 | bdb622e |
| 定点修复 | DWM/DPI 去白边、默认 1280×800、Drag Session、按钮位置 | 6870c85 |
| 样式 | Win11 DWM 圆角（8px，最大化直角） | 70e5ce9 |
| 定点修复 | Drop Zone 稳定显示 | cb1c2d5 |
| 定点修复 | 未分类拖不动 + 默认箭头光标 | 1ea761e |
| 定点修复 | VS 调试默认新 UI（launchSettings + 默认反转） | 412227b |

自动检查现状：后端 69/69；前端 34/34；`dotnet build` 0 警告 0 错误；Vite 构建通过。

---

## 11. 未完成 / 待办

### P8（设置与主题）

- Settings 六分类真实接线：主题 Dark/Light/System 三态切换并持久化；
  动画开关（根节点 class 控制 transition）；启动恢复上次项目；默认路径用于导出；
  通知开关与样式；模块启用（对存根生效）；插件启用/禁用（不再仅 localStorage）；
  回收站列表/还原/永久删除/保留时间与空间清理；导入存档；日志路径与打开。

### P9（通知与收尾）

- 通知面板（50 条 + 消费/清空）与 Toast 按 `notifyStyle` 接线；删除/移动配置等操作产生通知。
- 新前端实现等价 19 步 `spike:run/result` 自检。
- 窗口 TitleBar 行为最终回归（拖动/关闭/最大化/白边/DPI）。
- 经用户确认后：退役旧 `wwwroot`（git 历史可恢复）、更新 README（Node/pnpm 前置、构建命令）。

### 后端后续项（明确不在本轮）

- 回收站真软删除 + 自动清理。
- 模块系统后端化。
- 数组项内模块状态持久化。
- 配置移动后版本历史随迁。

---

## 12. 已知问题与限制（如实记录）

1. Window Shell：`WM_NCCALCSIZE=0` 去掉了系统阴影；边缘 Resize 手感未在真实桌面全面验证；
   Win10 无 DWM 圆角。
2. 回收站是 zip 方案：还原靠 `archive:import`，同名配置可能合并/跳过，不是真软删除。
3. 配置被移动后，原工作空间下的版本历史不随迁（新位置历史为空）。
4. Settings 多数项目前“只存不生效”，P8 才接线；主题只有深色。
5. 模块系统是前端存根（`FerryModules` 对应物在旧 wwwroot，新前端尚未实现注册表 UI）。
6. 通知只有本地 localStorage 记录，面板/Toast 未接线（P9）。
7. 前后端协议契约是手写镜像，加命令容易漏改。
8. `Program.cs` 的 IPC 分发是单体 switch + JsonObject，未拆成独立 Dispatcher（架构债，非本轮范围）。
9. 旧文档（DEV_HANDOFF、docs/design.md 等）与代码脱节，不要作为事实。

---

## 13. Git 现状与协作约定

### 13.1 现状

- 分支：`codex/greenfield-ui`（本地分支，最新提交 `412227b`）；`dev` 为旧基线；`main` 为初始提交。
- **仓库只有本地，云端无此仓库**（用户已知悉，不构成阻塞）；默认不 push，除非用户明确要求。
- 工作树当前干净。

### 13.2 协作约定（重要）

- 全程中文交流；提交信息用中文（feat:/fix:/style:/docs:/test:）。
- 分阶段推进，每阶段结束**暂停**，等用户人工验证通过后才进入下一阶段。
- 每阶段结束由 Codex 代用户启动应用（默认新 UI）。
- 不要修改整体架构（Vue/Tailwind/Photino 分层）与后端领域语义；只做定点修复或按计划实施。
- 前端改动必须走镜像检查流程（见第 4.3 节）。
- 构建底线：`dotnet build` 0 警告 0 错误；后端/前端测试全绿。

---

## 14. 关键文件索引

| 文件 | 作用 |
|---|---|
| `Ferry.App/Program.cs` | 宿主入口 + IPC 全量分发 + 新/旧 UI 选择 |
| `Ferry.App/Window/WindowController.cs` | 无边框窗口、圆角、DPI、最大化、拖动、关闭、状态持久化 |
| `Ferry.App/Properties/launchSettings.json` | VS F5 环境变量（FERRY_UI_NEW=1） |
| `Ferry.Core/Services/WorkspaceService.cs` | 项目/工作空间/配置/版本/排序/Settings 用例 |
| `Ferry.Core/Services/Session/FormSession.cs` | 表单会话引擎（实例 + 静态 Execute） |
| `Ferry.Core/Services/Archive/PortableArchiveService.cs` | 存档包导出/导入 |
| `Ferry.Infrastructure/LocalWorkspaceStore.cs` | workspace.json 存储实现 |
| `frontend/src/ipc/client.ts` | IPC 客户端（传输抽象、requestId、超时） |
| `frontend/src/ipc/types.ts` | ActionMap 类型化协议（手写镜像） |
| `frontend/src/App.vue` | 全局壳（TitleBar/Sidebar/Main/SourceDock/StatusBar/弹窗） |
| `frontend/src/components/Sidebar.vue` | 导航树 + 全部拖拽逻辑 + 菜单 |
| `frontend/src/components/SourceDock.vue` | 源码 Dock（只读、实时、全占/还原、行号） |
| `frontend/src/stores/dock.ts` | Dock 状态（宽度/阈值/全占/行号持久化） |
| `frontend/src/stores/config.ts` | 当前配置快照与表单命令 |
| `frontend/src/views/EditorView.vue` | 配置编辑页（字段树 + 源码/全占按钮） |
| `frontend/src/styles/main.css` | 全部样式与 --ferry-* token |
| `scripts/frontend-ci.ps1` | 前端镜像 CI 辅助脚本 |

数据文件位置：

- `%AppData%\Ferry\v2\workspace.json`（项目/工作空间/配置/版本/排序/settings）
- `%AppData%\Ferry\window.json`（窗口状态）
- `%AppData%\Ferry\trash\`（删除前导出的 zip）

---

## 15. 给协作 AI 的工作建议

1. 动手前先读本文档第 4、5、8、9 节（环境、IPC、窗口、拖拽）。
2. 修改前端：先改 `frontend/src`，同步到镜像跑 typecheck/vitest/build，再拷回 `dist`，
   最后 `dotnet build` 让 `bin/ui` 更新。
3. 修改后端：保持 `SourceText 权威 / 实时保存 / FormSession 契约 / IPC 响应结构` 不变；
   新增命令时同步更新 `frontend/src/ipc/types.ts`。
4. 验证 UI：启动后用 `ferry-spike-log.txt` 确认加载的是 `ui/index.html`。
5. 遇到窗口/拖拽问题：先复现并记录现象（哪一步、什么反馈），再读
   `WindowController.cs` / `Sidebar.vue` 对应段落；不要凭旧文档猜测。
6. 每完成一项定点修复：跑全部自动检查，提交（中文 message），然后启动应用请用户验证。
