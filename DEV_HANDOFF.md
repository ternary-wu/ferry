# Ferry 正式开发交接文档（开发指南）

> **用途**：本文件是新线程（正式开发）的起点指南，由用户审查后再移交。
> **注意**：本文件**不要提交到 git**——当前是工作树内未跟踪文件，任何 `git add .` 之前请确认已排除它。
> **核对时间**：2026-08-11（基于 `dev` 155f49d / `master` 181ad97 / tag `mvp-1.0`）。

## 0. 阅读顺序（新线程必读）

1. 本文件（现状、决策、路线图、约束）
2. `docs/design.md`——尤其 **§6（勾选语义 v3、插件三文件、headless API、多人化评估）** 与 **§7（MVP 冻结与版本策略）**
3. `docs/developer-guide.md`——架构与核心 API 速查
4. `docs/plugin-development.md`——插件编写规范（写插件/改引擎时对照）
5. 代码本体：`Ferry.Core/Services/` 与 `Ferry.Ui/ViewModels/MainViewModel.cs`

---

## 1. 项目定位与当前阶段

Ferry 是插件化的运维配置生成工具：通过插件定义“表单 + 语法规则”，动态渲染配置界面，生成 **json / yaml / ini / 任意自定义格式（layout 声明式）** 的配置文件，并支持预览、导入导出、高级编辑（源码面板）、工作区持久化。

**当前阶段：MVP 已完成、冻结并发布（tag `mvp-1.0`）。正式开发尚未开始，本文件是正式开发的起点。**

### MVP 已实现

- 插件三文件结构 + 自动扫描（`Plugins/<name>/`）
- 动态表单：6 种字段类型（String/Number/Boolean/Enum/Array/Object）、嵌套数组、依赖显隐、枚举自定义值（`allowCustomValue`，如 worker_processes 的 auto/数字）
- 校验约束：`min` / `max` / `integerOnly` / `required`，错误实时显示并阻止生成/导出
- 4 种渲染器：json / yaml / ini / layout（纯 YAML 声明式布局，无模板语言）
- 可选模块（勾选）、模板预设（templates.yaml）、勾选语义 v3
- 工作区持久化（`%AppData%/Ferry/workspace.json`，500ms 防抖，仅“清空配置”显式删除）
- 源码预览 / 编辑 / 应用修改（AvalonEdit；json/yaml 可解析回表单）
- 导入导出、集中日志（`ferry.log` 自动轮转）、全局异常兜底
- 5 个插件：Nginx、App Config、Dockerfile、Redis、Docker Compose
- **77 项 xUnit 测试全过，构建 0 警告 0 错误**（2026-08-11 实测）

### 最终目标与未来方向（接口已预留，功能未实现）

1. 工作空间与配置管理：工作空间（项目）内含多个插件的配置，配置名作为导出文件名/标记；配置留档可查可回滚——**已列入开发计划（M1.5，依赖 M1.6，用户确认）**
2. 显示模式（全部/已选/未选）与搜索过滤
3. Git / SSH 推送（`Ferry.Push` 的 `IPushService` 契约已定，仅本地目录实现）
4. Web 前端或 UI 框架迁移（倾向 Photino + HTML/CSS/JS，见 §5.5）
5. 可选高级模板引擎回归（Scriban）、应用版本过滤、更细校验规则（`validations` 字典尚未执行）
6. 自定义格式导入（反向解析）：layout / ini 等任意格式解析回填表单，帮助新人理解存量配置——**已列入开发计划（M1.6，核心依赖，用户确认）**
7. 可移植存档包：工作空间 / 配置可导出为自包含文件（含插件定义与渲染文本），换机器 / 分发 / 拷贝他人存档时仍可查看与导出——**已列入开发计划（M1.7，用户确认）**

---

## 2. Git 与分支现状（已核对，开工先处理）

| 引用 | 提交 | 说明 |
|---|---|---|
| 本地 `dev` | 155f49d | 与 `origin/dev` 一致，**正式开发主线** |
| 本地 `master` | 181ad97 | **落后 `origin/master` 1 个提交** |
| `origin/master` | 677fcb4 | = 181ad97 + “Change license from MIT to Apache” |
| tag `mvp-1.0` | 181ad97 | MVP 冻结点 |
| `origin/HEAD` | 677fcb4 | 远端默认分支 = master |

要点：

- `dev` 已通过 merge 包含 license 变更（155f49d 的父之一是 677fcb4），**dev 与 origin/master 内容一致**。
- 开工第一件事：`git fetch` + `git pull origin master`（快进本地 master，消除 behind 1），避免本地 refs 误导。
- 已确认：**不建孤立分支、不建单独 MVP 仓库**。正式开发直接在 dev 上进行，里程碑打 tag；若未来需要 MVP 热修，从 tag `mvp-1.0` 拉分支。

---

## 3. 仓库结构与文档地图

```
Ferry.Core/         纯逻辑库（模型、插件加载、表单、校验、渲染、导入、工作区）
Ferry.Ui/           WPF 前端（MainViewModel、MainWindow.xaml 数据模板、AvalonEdit）
Ferry.Push/         推送层（IPushService 契约 + LocalDirectoryPushService）
tests/Ferry.Core.Tests/   11 个测试文件 / 77 项测试
docs/               design.md、developer-guide.md、plugin-development.md
Ferry.Ui/Plugins/   Nginx-test、App-config、Dockerfile、Redis、Docker-compose
```

文档地图：

- `README.md`：产品介绍（**其中测试数量 53 为旧值，最新为 77**，更新文档时顺手修正）
- `docs/design.md`：设计决策与已知限制（重点 §6、§7）
- `docs/developer-guide.md`：二开指南
- `docs/plugin-development.md`：插件编写指南
- 本文件：正式开发交接与路线图（不提交）

---

## 4. 架构、数据流与关键 API

### 依赖方向

```
Ferry.Ui（WPF） ──只依赖公开 API──> Ferry.Core
Ferry.Push（独立，未来被 UI 或 Web 后端调用）
Core 不依赖 WPF/UI 程序集（FormFieldViewModel 仅用 ICommand/ObservableCollection）
```

### 数据流

```
plugin.yaml + schema.yaml + templates.yaml
   → PluginManager.LoadAllPlugins()（YamlDotNet 解析，根目录可注入）
   → PluginContext（Schema / Templates / RendererConfig / PluginKey）
   → FormBuilder.Build(schema, values?)  ──递归──> FormFieldViewModel 树
   → ConfigValidator.Validate / ConfigValueCollector.Collect（按启用状态裁剪、类型强转）
   → Dictionary<string, object?> 值树
   → RendererFactory.Create(plugin) → IConfigRenderer.Render → 文本（预览/导出）
   → ConfigImporter.Parse（json/yaml）反向回填表单
   → WorkspaceStore（IWorkspaceStore）持久化：全量值树 + 启用状态
```

### 关键公开 API（headless 化将基于它们，先熟悉）

**Ferry.Core.Services**

| 类型 | 职责 / 常用成员 |
|---|---|
| `PluginManager(pluginRootPath?)` | 扫描插件；`LoadAllPlugins()`；`LoadErrors` |
| `PluginContext` | `PluginKey`（=插件目录名）、`CanImport`、`DefaultFileName`、`Templates`、`RendererConfig` |
| `FormBuilder` | `Build(schema, values?)`；`ApplyEnabledStates(roots, states)`（cascadeUp:false 精确恢复） |
| `FormFieldViewModel` | `Value`、`IsEnabled`、`SetEnabled(value, cascadeUp)`、`IsSelectable`、`CanToggleEnabled`、`ValidationError`、`Path`、`AddItemCommand`/`RemoveItemCommand`、`Children`、`TotalChildModulesCount`/`EnabledChildModulesText`、`IsArrayItem` |
| `ConfigValidator` | `Validate(roots)` → `List<string>` |
| `ConfigValueCollector` | `Collect(roots, includeDisabled: false)` |
| `ConfigValueConverter` | `Coerce(type, raw)` 类型强转 |
| `ConfigImporter` | `Parse(plugin, text)`（仅 json/yaml）、`NormalizeTree` |
| `WorkspaceStore` | `IWorkspaceStore.Load/Save/Clear(pluginKey)`；`WorkspaceEntry{Values, Enabled, Modules(旧)}` |
| `FerryLog` | `Configure(directory?, maxBytes?)`、`Info/Warn/Error` |

**Ferry.Core.Services.Rendering**

- `IConfigRenderer.Render(config)`；四个实现：Json / Yaml / Ini / Layout
- `RendererFactory.Create(PluginContext)`；`FieldRenderConfig`（line/open/close/item/itemOpen/itemClose/inline/keepEmpty/hidden）

**Ferry.Push**

- `IPushService`：`Name`、`Supports(PushTargetType)`、`PushAsync(PushRequest, CancellationToken)`
- `PushTargetType`：LocalDirectory / GitRepository / SshServer（Git/SSH 未实现）
- `PushRequest`：`ConfigName`、`Content`、`Target`、`Branch`、`CommitMessage`、`RemotePath`、`CredentialId`

### UI 与 Core 的当前耦合点（headless 化的重点改造对象）

`Ferry.Ui/ViewModels/MainViewModel.cs` 目前直接持有 `FormFieldViewModel` 树，并承担了本应属于 Core 的编排逻辑：

- 递归挂接/摘除 `PropertyChanged` + `Children.CollectionChanged`（`AttachVmEvents`/`DetachVmEvents`），任何变更触发 校验 → 预览 → 500ms 防抖保存
- 保存入口 `BuildEntry` / `CollectEnabledStates` 在 ViewModel 内
- 预设应用 `ApplyPresetModules`、占位预设、状态栏文案混在 UI 层

**改造目标**：这些逻辑下沉为 Core 的“会话”层（FormSession），MainViewModel 退化为会话客户端。详见 §7 M1。

---

## 5. 已确认的关键决策（新线程必须遵守）

### 5.1 勾选语义 v3（design.md §6.1）

- 勾选子模块 → **级联启用所有祖先**（仅用户/预设触发的启用）
- 勾选父模块 → **不级联勾选子模块**；子模块集合由模板文件（templates.yaml 的 `modules`）或用户手动定义
- 取消父模块 → **保留子模块状态与全部值**：UI 中保留（置灰、可展开检视），但**不写入源码/输出**；重新勾选父后原样恢复，不重置
- 块内标量不受“子模块”规则影响：父块启用后，标量按自身默认启用状态输出
- 新建配置初始状态：保持现状（所有模块默认启用），模板负责重塑场景
- 父勾选框显示**两态**（自身启用 + “N/M 子模块”小字），不用三态
- 实现要点：级联向上只作用于用户/预设触发的启用；工作区恢复用 `SetEnabled(cascadeUp: false)` 精确恢复，避免“父停用但子保留”的保存状态被恢复破坏

### 5.2 插件三文件结构（design.md §6.2）

```text
Plugins/<name>/
├─ plugin.yaml       # 元数据：名称、插件版本、target.name/version（应用版本范围）、renderer
├─ schema.yaml       # 字段定义（presets 已移出）
└─ templates.yaml    # 场景模板（反向代理/静态站点等）：modules + values（可含数组项）
```

- 兼容：`PluginManager` 优先读 `templates.yaml`，回退 `schema.yaml` 旧 `presets`
- 应用版本过滤（minAppVersion/maxAppVersion + 目标版本选择器）**不在 MVP 范围**；`target.version` 语义化字段已存在，字段级版本过滤留待后续

### 5.3 headless 零妥协架构：协议与传输分离（design.md §6.3 细化为标准解）

**目标**：同一套 Core 同时满足“单机零 HTTP / 零端口 / 零防火墙”与“服务器多人可扩展”。两者都不是妥协方案，而是**同一设计的两个适配器**。

**标准解 = 六边形架构（端口与适配器）+ 命令式协议**：

```text
┌─ 适配器层（传输/基础设施，Core 外）─────────────────────────────┐
│  Photino 宿主（同进程消息）│ HTTP/WS 端点（服务器）│ CLI │...  │
└──────────────┬───────────────────────────────────────┘
               │ 命令/查询 DTO（纯数据，JSON 可序列化）
┌──────────────▼───────────────────────────────────────┐
│ Ferry.Core：领域 + 用例（FormSession 引擎）              │
│  插件模型 / 表单树 / 校验 / 渲染 / 导入                   │
│  只依赖端口接口，不知道任何传输的存在                       │
└──┬────────────────┬─────────────────┬─────────────────┘
   │                │                 │
 IWorkspaceStore  IPluginSource   IPushService
 （端口，实现可换：本地 JSON / 数据库 / 服务端存储 / Git / SSH）
```

要点：

1. **协议与传输正交**：Core 只定义“命令/查询”协议（DTO 纯数据、可 JSON 序列化），不定义传输。单机适配器是**进程内分发器**（直接方法调用，不经过 HTTP、不监听端口、不涉及防火墙）；服务器适配器是 HTTP/WebSocket 端点（把请求反序列化为同一套命令再调用 Core）。Core 从不感知自己在哪个传输后面。
2. **命令式协议（单一事实源）**：所有操作收敛为 Command/Query：
   - `SetValue{Path, Value}`、`ToggleEnabled{Path, Enabled?}`、`AddItem{ArrayPath}`、`RemoveItem{Path}`、`ApplyPreset{PresetId}`、`Import{Text}`、`Validate`、`Render`、`Snapshot`
   - 文档状态 `ConfigState{PluginKey, Values, Enabled, Version?}` 可序列化——单机与服务器的状态模型完全一致
   - 结果统一为 `OperationResult{Ok, Errors[], Version?}`（数据，不靠异常传递业务错误）；服务器适配器据此映射 HTTP 状态码（400 校验失败 / 409 版本冲突 / 404 路径不存在）
3. **有状态与无状态是同一引擎的两个入口**：
   - 实例式 `FormSession`（单机/Photino 用）：宿主持有会话对象，命令直达
   - 静态 `FormSession.Execute(plugin, state, command)`（服务器用）：请求携带 ConfigState + 命令 → 返回新 ConfigState；无会话亲和、天然水平扩展
   - 底层共享同一套“重建表单树 → 执行变换 → 产出快照/新状态”逻辑，不存在两套实现
4. **会话上下文抽象**：命令/存储按 `Scope`（owner / 租户）隔离；单机 scope = 本地，服务器 scope = 认证后的用户/租户 ID。认证发生在适配器层（服务器中间件），Core 只接收 scope 字符串，不实现认证——认证是基础设施，不是领域。
5. **并发（乐观锁，可选参数）**：`ConfigState.Version` + 命令可选携带 `ExpectedVersion`；有则校验（冲突返回 409），无则跳过。单机客户端可不传，服务器客户端必传——数据模型统一，无妥协。
6. **端口最小集（M1 落地时引入）**：
   - `IWorkspaceStore`（已有）：按 `(WorkspaceId, ConfigId)` 存取文档状态（工作空间 = 项目，配置 = 插件 + 配置名，版本历史独立存储），实现可换（本地 JSON / 数据库 / 服务端存储）
   - `IPluginSource`（新增）：插件来源抽象，实现 = 现有本地目录扫描（DirectoryPluginSource）/ 未来服务端共享注册表（RemotePluginSource）；`PluginManager` 依赖该端口而非路径字符串
   - `IPushService`（已有）：推送端口，实现 = LocalDirectory / Git / SSH
7. **不暴露事件树**：Core 输出 DTO 快照，UI 变更后重新 `GetSnapshot()`；WPF 的 ObservableCollection / PropertyChanged 只存在于适配器层（当前 MainViewModel），不进协议。

### 5.4 多人化评估结论（design.md §6.4）

核心迁到服务器为多人服务时，**领域逻辑与 API 契约无需大改**，变化集中在外部基建（零妥协架构下这些全部是适配器层的替换，Core 契约不变）：

- 传输层：同进程消息 → HTTP/WebSocket 端点（直接映射同一套命令）
- 会话/用户：加认证、权限、按用户隔离配置；会话可改为“无状态文档”（前端随请求携带值树，天然水平扩展）
- 持久化：`IWorkspaceStore` 接口不变，实现从本地 JSON 换数据库/服务端存储
- 并发：文档模型上加版本号做乐观锁
- 插件：从本地目录扫描改为服务端共享注册表/缓存；`PluginManager` 插件根目录需可配置

因此 **headless FormSession 的输入输出必须是纯数据（DTO），不能绑定本地文件路径**。

### 5.5 UI 迁移倾向（design.md §6.3/7）

- 倾向：**Photino + 原生 HTML/CSS/JS（WebView2）**
- 顺序：先做 headless FormSession API → Photino spike（验证 IPC 延迟与可行性）→ 功能对等后正式迁移；**WPF 作为兜底保留在 mvp-1.0，不在 dev 早期删除**
- UI 参考（仅视觉/交互，原型 JS 是 demo 代码不可复用）：
  - `C:\Users\Wu\Downloads\Ferry_UI_设计规范.md`
  - `C:\Users\Wu\Downloads\Ferry_UI_Prototype.html`

### 5.6 版本管理策略（design.md §7.1）

- dev 为主线，里程碑打 tag；不建孤立分支/单独仓库（已确认）
- 正式开发与 MVP 热修并行的需求出现时，从 tag `mvp-1.0` 拉分支

---

## 6. 引擎技术陷阱（已踩坑，写插件/改引擎前必读）

1. `{{ .key }}` 是 layout 引擎保留占位符（指当前字段键名）——**子字段不能命名为 `key`**（compose 的 environment 变量因此改名 `name`）
2. 占位符正则 `[\w.]*` 不支持连字符——**字段 id 不能用连字符**（Redis 的 `protected-mode` 用 `protected_mode` id + 字段级 `line: "protected-mode {{ . }}"` 硬编码指令名）
3. Scriban 空字符串当真值 → 值收集时统一丢弃空字符串（JSON/YAML 也不输出 `""`）
4. 行形数组默认不输出键行，需要 `render.open` 输出键行（compose `ports:` 等）
5. `keepalive_timeout` 值用字符串 `75s` 而非 Number；Redis `port` 用 Number，但 `tcp-backlog` 等用字段级 `line`
6. 数组项内 module 状态不持久化（WorkspaceStore 只存静态路径的 Enabled；恢复时数组项保持默认启用）——已知限制
7. **PluginKey = 插件目录名**：Nginx 插件目录实际叫 `Nginx-test`（plugin.yaml 内 name: Nginx）。重命名目录会丢失已存工作区数据；正式开发建议统一目录名并做 key 迁移或接受丢失
8. 测试通过 `FindRepoRoot()` 向上查找 `Ferry.slnx` 读取真实插件做集成测试；新插件/字段语法要同步补测试
9. 文档中测试数量滞后（README/developer-guide 写 53、design 写 58，实际 77）——更新文档时顺手修正

---

## 7. 正式开发路线图

**执行顺序：M1 → M1.6 → M1.5 → M1.7 → M2 → M3**。

原因：M1.5 采用"源码为权威"存储后，打开任何 layout / ini 配置都要先做源码 → 表单解析，因此 M1.6（自定义格式导入）从"辅助导入"升级为**核心依赖**，必须先于 M1.5 完成。

### M1：Headless FormSession API（Core）——第一优先级

**目标**：把“会话 + 命令/状态”变成 Core 的一等公民，任何前端（WPF / Photino / Web / CLI）只依赖它。

**设计草案（待新线程细化敲定，尤其路径规则）**：

```text
Ferry.Core/Services/Session/
  FormSession.cs            // 会话引擎：实例式入口（单机）与静态 Execute（服务器）
  Protocol/Commands.cs      // Command/Query DTO（§5.3 协议）
  Protocol/ConfigState.cs   // 可序列化文档状态（SourceText 源码权威 + Values/Enabled 缓存 + Version）
  Protocol/OperationResult.cs
  FormFieldSnapshot.cs      // 只读快照树（供前端渲染）
  FormSessionOptions.cs     // 初始 values / enabledStates / preset
  PathResolver.cs           // 路径规则：静态路径 + 数组序号
```

命令协议（建议，与 §5.3 一致）：

- 创建：`FormSession.Create(plugin, options?)`（或从 `ConfigState` / `WorkspaceEntry` 恢复）
- 查询：`GetSnapshot()` → FormFieldSnapshot 树（值、启用、可选性、校验错误、N/M 计数、子项）
- 修改：`SetValue(path, value)`、`ToggleEnabled(path, enabled?)`、`AddItem(arrayPath)`（返回新项 path）、`RemoveItem(path)`
- 场景：`ApplyPreset(preset)`（模块集合 + 初始值）
- 校验/输出：`Validate()` → errors；`Render()` → 文本（不校验，与现 GeneratePreview 语义一致）
- 导入/导出：`Import(text)`（仅 CanImport）；导出即 Render
- 持久化：产出 `ConfigState`（源码 SourceText + 全量 Values/Enabled 缓存 + Version），由调用方经 `IWorkspaceStore` 存取——**Core 不自己写路径**
- 无状态入口：`FormSession.Execute(plugin, state, command) → OperationResult{State?, Errors}`（服务器路径，同一引擎）

**关键设计点**：

- **路径规则**：静态字段 `http.servers`；数组项需稳定路径（建议 `http.servers[0]` 序号形式）。现 VM 的数组项 Path 由自增 Id 生成（`servers_item_1`）**不稳定**，必须定义新规则并在 FormSession 层维护 路径 ↔ VM 映射
- **不暴露事件树**：UI 变更后重新 `GetSnapshot()` 拿新状态；快照要足够渲染（值、校验错误、可选性、N/M 计数、依赖显隐）
- 复用现有组件：FormBuilder / FormFieldViewModel / ConfigValidator / ConfigValueCollector / ConfigImporter / RendererFactory 原样复用，FormSession 是编排层
- **端口化**：同期引入 `IPluginSource`（`DirectoryPluginSource` 实现现有扫描，`PluginManager` 改依赖端口）；`IWorkspaceStore` 契约按 §5.3 扩展 Scope 维度
- 线程模型：同步纯函数即可；单机走实例式、服务器走静态 Execute，方法签名与 DTO 完全一致

**实现步骤**：

1. 引入 `IPluginSource` 端口 + `DirectoryPluginSource`（现有扫描逻辑原样搬迁），`PluginManager` 改依赖端口
2. 新增 Session 目录：协议 DTO（Commands / ConfigState / OperationResult）、快照、路径解析器
3. 实现 FormSession 引擎：实例式方法 + 静态 `Execute(state, command)` 共享同一执行内核
4. 把 MainViewModel 的 `BuildEntry` / `CollectEnabledStates` / `ApplyPresetModules` / 事件驱动逻辑抽成 FormSession（Core 内），MainViewModel 改为调用
5. 测试：77 项保留 + 新增 FormSessionTests——**同一组命令断言同时覆盖实例式与静态 Execute**（创建/改值/切换启用/增删数组项/预设/校验/渲染/持久化往返/版本冲突），断言不依赖 WPF
6. MainViewModel 重构为“会话客户端”：WPF 行为保持对等（勾选语义 v3 不回归）
7. 验收：`dotnet build` 0 警告；`dotnet test` 全过；手动跑 WPF 验证 Nginx 插件原有功能不变

### M1.5：工作空间与配置管理（WPF）——用户已确认优先级（依赖 M1.6，源码为权威）

**产品模型（三层）**：

```text
工作空间 Workspace（如“项目A”）
└── 配置 Config（绑定一个插件：nginx.conf / redis.conf / docker-compose.yml…）
    ├── 源码文本 SourceText（权威：配置产物，随配置存档）
    └── 历史版本 Version（源码快照，可查看 / 回滚）
```

**表单是打开配置时由源码解析出的工作视图，不入存储主体。**

**产品行为**：

- 工作空间 = 项目级顶层容器：新建 / 重命名 / 删除（仅显式）/ 切换；**一个工作空间内可含多个插件的配置（跨插件，按用户倾向设计）**
- 配置：属于某工作空间、绑定某插件；名字默认 = 插件默认文件名（nginx.conf），可自定义；新建时可选用模板起步；复制 / 删除（仅显式）
- 切换工作空间或配置后，表单、预览、校验、自动保存全部作用于当前配置，互不影响
- 打开配置 = 源码 → 解析 → 表单（json/yaml 走 ConfigImporter；layout/ini 走 M1.6 解析器）；编辑后 = 表单 → 渲染 → 新源码保存
- 查看 / 导出 / 存档直接读源码，不依赖渲染与插件；导出默认文件名 = 配置名，可自定义；未来推送以“工作空间 + 配置名”标记
- 留档：手动“留档”按钮保存当前配置的版本快照（源码 + 时间 + 备注）；版本列表可查看、可回滚；默认全保留，可手动删除
- 刷新 / 重启：所有工作空间 / 配置 / 版本完整恢复；“清空配置”只作用于当前配置
- 新增“默认工作空间”自动承载旧数据，旧配置按插件归类迁移

**UI 影响**：左侧两级导航（工作空间列表 → 配置列表，按插件分组）；插件选择退居配置属性，不再是顶层概念。

**技术要点**：

- 存储键演进：`IWorkspaceStore` 从 `(pluginKey)` → `(WorkspaceId, ConfigId)`；版本历史独立存储或同文件（小规模同文件，规模扩大换 SQLite）
- **源码为权威**：配置存档 = 源码文本（主数据）+ 表单状态缓存（Values / Enabled，打开时由源码解析派生）；渲染仍是编辑后的必经步骤，但查看 / 导出 / 存档不再依赖渲染
- 打开配置 = 源码 → 解析 → 表单（json/yaml 走 ConfigImporter；layout/ini 走 M1.6）；解析的未识别内容保留机制是硬性要求（见 M1.6）
- `ConfigState` 增加 WorkspaceId / ConfigId / SourceText；命令集新增 `Snapshot` / `ListVersions` / `RestoreVersion`
- 版本快照以源码为主（可 diff；回滚 = 换回旧源码再解析）
- 容量：默认全保留 + 手动删除；保留上限策略后置
- 与 M1 / M1.6 的关系：存储契约在 M1 按新模型设计；**执行顺序在 M1.6 之后**（无解析能力则无法从源码打开表单）

**插件缺失与版本变化（恢复健壮性，跨插件后必须）**：

- 配置绑定 `PluginKey` + 插件版本（plugin.yaml version），恢复时优先精确匹配
- 插件缺失（目录删除 / 改名 / 未安装 / **外部拷贝来的存档从未装过该插件**）：配置列表仍显示（置灰 + “插件缺失”标记）；**查看 / 导出直接读源码，无需插件**；仅表单编辑被禁用；提供“重新关联插件”与“从可移植包恢复插件”（M1.7）操作；数据不丢（非显式删除不删）
- 插件版本变化：按字段 id 回填（现有 FormBuilder 天然兼容——新增字段用默认值、已删除字段的值保留在存档中不丢），UI 提示“该配置创建于插件 vX，当前为 vY，字段可能有增减”
- 可选增强：配置存档记录创建时的字段 id 清单，用于差异对比提示

**验收**：在“项目A”下创建 Nginx“反向代理”与 Redis“生产”两个配置，分别勾选模块、填值并留档；重启后工作空间、配置、版本全部恢复；导出文件名默认为配置名；回滚版本后表单与预览回到该版本状态。

**待用户确认**：跨插件范围（当前按支持设计）；留档触发（手动为主，导出/推送前自动留档可选）；版本保留策略。

### M1.6：自定义格式导入（反向解析）——核心依赖，执行顺序先于 M1.5

**目标**：把任意格式的已有配置文件（nginx.conf、redis.conf 等 layout / ini 自定义格式）导入 Ferry 并回填表单，让新人通过表单的字段说明（label / description / 枚举选项）快速理解前人留下的配置。

**现状**：json / yaml 插件已支持导入回填；layout / ini 因逆向解析成本高，MVP 明确不做。本步将其提升为核心功能。

**地位**：M1.5 采用“源码为权威”存储后，打开任何 layout / ini 配置都要先经过源码 → 表单解析，本功能从“辅助导入”升级为**核心依赖**；解析质量直接决定表单回填的完整度。

**产品行为**：

- 导入入口对 layout / ini 插件开放：选择文件 → 解析 → 回填表单 + 解析报告
- 解析报告：识别出的字段数、未识别内容数（自定义指令 / 注释 / 宏等）；**未识别内容不丢弃、单独保留是硬性要求**——解析是打开配置的主路径，若丢弃未知内容，编辑保存后会静默丢失前人的配置
- 导入后表单逐字段展示含义，新人据此阅读理解并修改
- 导入目标：可导入到新配置（新建时导入覆盖），或覆盖当前配置（先确认）

**技术设计（分阶段）**：

- 阶段1 宽松解析：Ferry 根据 schema 的字段 id 与字段级 line / render 前缀生成扫描规则，按指令名提取键值；块形结构按缩进 / 括号识别；未知行保留为“未识别内容”原始文本
- 阶段2 声明式 parse（可选）：插件声明 parse 规则（render 的逆操作），提高复杂块（嵌套数组 / 条件 / 多级缩进）的还原精度
- 导入结果 = 值树 + 未识别文本 + 报告；未识别文本随配置存档，导出时可选择追加（不主动丢弃）

**验收**：手写一份含未知指令的 nginx.conf 导入，已知字段正确回填表单、未知指令保留且报告提示；导出时不丢数据。

**待用户确认**：解析精度策略（宽松解析先行 vs 同时要求插件作者提供 parse 规则）；未知内容默认处理（存档保留 + 导出可选追加，当前按此设计）。

### M1.7：可移植存档包（工作空间 / 配置导出与导入）——用户确认为边界场景

**目标**：存档从其他机器 / 他人处拷贝过来时，即使本机没有对应插件，也能查看与导出，不变成死数据；装上插件后完整可编辑。

**产品行为**：

- 导出：配置或整个工作空间 → 单一文件（zip 容器），内含配置数据（源码 SourceText 为权威 + Values / Enabled 缓存 + 版本历史）+ 所用插件定义（plugin.yaml / schema.yaml / templates.yaml）与插件版本
- 导入：检测本机插件——存在同 key 插件 → 正常加载；不存在 → 从包内提取插件定义（随包只读加载）或提示安装；即使不安装，配置源码可查看 / 导出
- 场景：换机器、团队分发、拷贝他人存档；与 M1.5 插件缺失策略、M1.6 导入回填衔接
- 安全提示：包内插件定义按“不可信外部代码”处理，只读加载，不自动写入本机插件目录（由用户显式安装）

**技术要点**：

- 包格式 = zip，清单文件描述插件 key / 版本 / 内容结构；插件定义与配置数据分离存放，便于按需加载
- 随包插件仅用于渲染与表单重建，不注册到本机 `Plugins/`；“安装”= 用户确认后复制到插件目录
- 依赖 M1.5 的“源码为权威”存储与插件缺失策略；不依赖 M1.6（宽松解析不涉及）

**验收**：导出含 Nginx 配置的工作空间包，在无 Nginx 插件的环境导入，仍可查看 / 导出配置源码；显式安装包内插件后可完整编辑；导入时本机已有同名插件则优先用本机插件。

### M2：Photino spike

**目标**：验证“原生 HTML/CSS/JS 渲染插件表单 + WebView2 同进程 IPC”可行性，量化延迟。

**交付物**：

- 最小 Photino.NET 宿主（新项目，与 Ferry.Ui 并列），加载本地 HTML
- HTML/JS 侧：根据 `GetSnapshot()` 渲染模块树/表单；交互经 WebView2 消息桥调用 Core FormSession；变更后重新拉快照更新
- 只做 Nginx 插件 + 核心交互（勾选/填值/预览/增删数组项），不做完整 UI
- 测量：每次操作端到端延迟（目标 <50ms 可接受，实测记录）

**通过标准**：WebView2 可用、IPC 调用正确、表单操作流畅；FormSession API 覆盖 spike 全部需求（缺口回 M1 补）；结论记录到 design.md。

### M3：正式迁移 UI（Photino + HTML/CSS/JS）

前置：M1、M2 通过，且用户确认迁移。

**功能对等清单（逐项验收）**：

- 插件列表 + 刷新 + 工作区恢复
- 工作空间与配置管理（含版本历史：留档/查看/回滚，M1.5 功能对等）
- 模块树：有子选项的父选项都能折叠；**不勾选也能打开**；父级两态 + N/M 计数；选子自动选父（级联向上）；勾父不级联子
- 表单：6 种字段类型控件、依赖显隐、枚举自定义值、数组增删、校验错误内联显示
- 预设（模板下拉）、源码预览/编辑/应用修改（json/yaml 可回填）
- 导入/导出、清空工作区、日志入口

UI 视觉参考 `Ferry_UI_设计规范.md`（模块树/面包屑/卡片表单/预览不折行/模板弹窗等），但以 M1 数据契约为准。WPF 兜底保留到对等验收通过后再退役。

### 后置（接口预留、暂不实现）

- 显示模式（全部/已选/未选）与搜索过滤
- Git/SSH 推送（`Ferry.Push` 实现 `IPushService`；PushRequest 已含 Branch/CommitMessage/CredentialId）
- 模板引擎回归（Scriban 作为可选高级模块）
- `validations` 字典执行、字段级 required/长度等更细校验
- 应用版本过滤（target.version 字段已预留）

---

## 8. 用户偏好与工作约定（重要）

- **全程中文交流**；面向用户/插件作者的文案用中文
- **宁可保留状态，不要级联删配置**：取消父模块保留子状态；非显式声明删除（“清空配置”按钮）不得删除数据
- **尽量留在 MVP 已验证路线**：dev 上不引入未验证的大改动；新框架先 spike 验证再决定
- **提交/推送前需用户确认**；push 联网可能失败（曾有网络拦截），失败时可用代理端口 7890：`git -c http.proxy=http://127.0.0.1:7890 push`
- 提交信息用中文、符合现有风格（feat:/fix:/docs:/test:）
- 核心逻辑放 Core 并以静态方法/文档式接口暴露；UI 只依赖公开 API
- 错误处理：用户能直接理解的错误在界面状态栏/弹窗提示；详细错误统一走 `FerryLog` 写 `ferry.log`，不散落硬编码路径
- C# 12 / .NET 10，Nullable + ImplicitUsings；命名空间按项目分层；**构建 0 警告 0 错误为底线**

---

## 9. 新线程开工清单

1. `git fetch` + `git pull origin master`（快进本地 master，消除 behind 1）
2. 确认在 `dev`、工作区干净
3. 基线：`dotnet build Ferry.slnx`（0 警告）+ `dotnet test`（77 通过）
4. 通读：`docs/design.md`（尤其 §6/§7）、`docs/developer-guide.md`、`docs/plugin-development.md`、本文件
5. 查看 UI 参考：`C:\Users\Wu\Downloads\Ferry_UI_设计规范.md` / `Ferry_UI_Prototype.html`
6. 与用户确认 M1 路径规则等设计细节后开工
7. 每完成一个里程碑提交一次并请用户确认；完成后把决策追加到 `docs/design.md`

---

## 10. 建议给新线程的第一条消息（可稍作裁剪后直接粘贴）

```
你是 Ferry 正式开发的新线程。请先完整阅读 D:\Program\C#program\Ferry\DEV_HANDOFF.md
（交接文档，不要提交它）、docs/design.md 的 §6 和 §7、docs/developer-guide.md、
docs/plugin-development.md，再开始工作。

项目状态：MVP 已冻结（tag mvp-1.0），dev 是正式开发主线，工作区干净，
dotnet build 0 警告、dotnet test 77 项全过。开工前先 git fetch 并快进本地 master，
然后重跑 build/test 确认基线。

第一个任务：实现 M1 Headless FormSession API（Ferry.Core 的纯命令式会话层），
设计草案与路径规则见交接文档 §7；敲定细节后先向用户确认再实现。
第二个任务：M1.6 自定义格式导入（反向解析，layout/ini 回填表单；这是"源码为权威"存储
的核心依赖，必须先做，见交接文档 §7 M1.6）。
第三个任务：M1.5 工作空间与配置管理（工作空间→配置→历史版本三层模型，源码为权威，
打开配置 = 源码→解析→表单，见交接文档 §7 M1.5）。
第四个任务：M1.7 可移植存档包（zip 容器含插件定义与源码，无插件环境可查看/导出，见 §7 M1.7）。
硬性约束：Core 不依赖 WPF；勾选语义 v3 不回归；WPF 行为保持对等；
输入输出均为纯数据 DTO，不暴露事件树；提交前请用户确认。
```
