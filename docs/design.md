# Ferry MVP 评估与设计定稿

> 日期：2026-08-10 · 基于当前 `dev` 分支代码（36aa9e5）评估

## 1. 现状与差距

### 已完成

- WPF (.NET 10) 应用骨架，`Ferry.Core` / `Ferry.Ui` 双项目，可构建（0 错误，1 个可空性警告）
- 插件扫描：`Plugins/<name>/plugin.yaml` + `schema.yaml`，YamlDotNet 解析
- 动态表单：6 种字段类型（String/Number/Boolean/Enum/Array/Object）通过 DataTemplate 动态渲染
- 数组增删、字段间依赖显隐（VisibilityDependency）
- 示例插件 Nginx-test（较完整的 nginx schema）

### 缺口（相对最终目标）

| 最终目标 | 现状 | 缺口 |
| --- | --- | --- |
| 1. 多格式生成（json/yaml/ini/自定义） | 只有 schema，无任何输出能力 | 缺渲染引擎、缺"语法规则/模板"概念 |
| 2. 动态渲染插件 | 表单渲染已实现 | 数组增删有 3 个已知 BUG 未修 |
| 3. 导入/导出/预览 | 无 | 全部缺失 |
| 4. 高级编辑（软件内直接编辑） | 无 | 缺源码编辑面板 |

### 已知 BUG（提交信息记录，代码定位）

1. **字段删除后无法构建回来**：数组项 ID 用 `Children.Count + 1` 生成，删除中间项后再添加会产生重复 ID；且删除是永久性的，没有从定义重建的路径。
2. **部分有内容的字段无法展开 / upstream 下无法增加 IP 列表**：嵌套数组（Array 内嵌 Array）场景的增删与渲染链路存在缺陷。
3. **CS8618 警告**：非数组字段的 `AddItemCommand` 可空性声明问题。

## 2. 关键架构决策

### 2.1 渲染模型：统一的"值树 + 渲染器"

表单值统一收集为**嵌套字典树**（字段 id → 值），与格式无关：

```text
Dictionary<string, object?>
  ├─ 标量（string / int / bool）
  ├─ Object → 嵌套 Dictionary
  └─ Array  → List<Dictionary>
```

插件通过 `renderer` 声明输出方式，MVP 支持四种内置渲染器：

| 类型 | 实现 | 适用 |
| --- | --- | --- |
| `json` | System.Text.Json | JSON |
| `yaml` | YamlDotNet | YAML |
| `ini` | 内置 INI 写入器（对象→`[path]` 节，数组→`[path.N]`） | INI |
| `layout` | 声明式布局引擎（纯 YAML：字段 render 段 + 全局样式，`{{ .字段 }}` 插值） | 自定义/自研格式（如 nginx.conf） |

layout 即"语法规则"：插件作者只用 YAML 声明"标量行 / 块 / 数组项怎么写"，循环、条件、缩进全部由引擎处理，
不暴露任何控制流语法；唯一的插值技能是 `{{ . }}` / `{{ .key }}` / `{{ .子字段id }}`（可迁移至 Helm/envsubst 思路）。

> 决策记录（2026-08-10）：Scriban 模板渲染器已移除。未来正式开发中，可将其作为**可选高级模块**（类似推送模块）
> 重新引入，供资深工程师在 layout 表达不了的极端格式下使用；常规插件永远不需要。

### 2.2 插件格式定稿（向后兼容）

`plugin.yaml` 的 `renderer` 段：

```yaml
name: Nginx
version: 1.27.0
target: nginx
author: Ferry Community
description: 生成 Nginx 主配置文件
renderer:
  type: layout              # json | yaml | ini | layout
  layout:                   # layout 全局默认样式（字段 render 段可覆盖）
    line: "{{ .key }} {{ . }};"
    blockOpen: "{{ .key }} {"
    blockClose: "}"
    indent: "    "
  defaultFileName: nginx.conf
  outputExtension: .conf
```

未声明 renderer 的插件默认按 `layout` 处理（使用全局默认样式）。

`schema.yaml` 字段属性（MVP 已实现）：

- 基础：`id / label / description / type / defaultValue / enumOptions / visibilityDependency / children`
- 校验约束：`min / max / integerOnly`（Number 字段，或允许自定义数字值的 Enum 字段）
- 枚举扩展：`allowCustomValue: true`（可编辑下拉，"选择 auto 或填写数字"这种模式）
- 可选模块：`module: true`（Object/Array 显示勾选框；未勾选时整块不输出，父模块未启用时子模块锁定）
- 输出格式（layout）：字段级 `render` 段（`line / open / close / item / itemOpen / itemClose / inline / keepEmpty / hidden`），
  空数组/空对象默认省略（OmitEmpty），首个子字段可作为项名称（`hidden: true` 不输出行）
- 预设搭配：schema 顶层 `presets`（`name / description / modules / values`，`modules` 用字段路径列出启用的模块，其余禁用）

校验在表单变更、导出前自动执行，错误实时显示在字段下方并阻止生成/导出。

### 2.7 模块显示模型（v2 调整）

- **所有字段/块始终列出并可见**，未勾选的模块内容置灰但可展开检视子模块与选项——勾选状态只决定"是否写入配置文件"，不决定"是否显示"。
- 字段启用泛化：**所有字段（含标量）默认可取消**，取消勾选即不输出；`required: true` 锁定必填字段（适配无 include 机制、必须完整的配置文件）。
- 子字段勾选框在父级未启用时锁定（不可单独启用），父级启用后解锁；之前勾选的子字段状态保留。
- 预设（快速模板）只决定**初始**勾选集合与初始值，之后用户可自由调整。
- 保存与恢复：工作区全量保存（含未勾选模块的值），刷新/重启后完整恢复——修复了此前 Object 子字段作用域解析错误导致的"http 块恢复后丢失"问题。

> 分歧点已选定方案 A：未勾选也能点开查看子模块/选项。若偏好方案 B（未勾选不可展开），仅需把模块模板的内容可见性改为跟随勾选状态。

### 2.3 导入范围（敲定边界）

- **json / yaml 插件**：支持导入——解析文件 → 归一化为值树 → 重建表单并回填。
- **ini / template（自定义格式）**：MVP **不做反向解析**（逆向解析任意模板格式成本高、易错），支持导出与预览；UI 上禁用"应用修改"并提示。
- 反向解析插件化（插件自带 parser）列入后续路线。

### 2.4 高级编辑（源码面板）

- 右侧下方新增 AvalonEdit 源码面板（已引用的 AvalonEdit 终于用上）。
- 三种状态：**预览**（只读）→ **编辑**（可写）→ **应用修改**（仅 json/yaml，解析回表单）。
- 表单 → 源码：实时同步（表单任何值变化自动重新生成）。
- 源码 → 表单：仅 json/yaml；应用后保留用户文本，不强制重新格式化。
- 导出即把源码面板文本写入文件。

### 2.5 数组增删修复方案

- 每个数组 VM 维护自增序号 `_nextItemSequence`，项 ID 唯一稳定，删除后重建不再冲突。
- 数组项始终从 `Definition.Children` **新建** VM 树（不共享实例），嵌套数组递归创建。
- `AddItemCommand` 恒非空，修复 CS8618。

### 2.6 工作区持久化

- 表单值（含停用模块的全量值）与模块勾选状态保存到 `%AppData%/Ferry/workspace.json`，按插件目录名分键。
- 变更后 500ms 防抖落盘；刷新插件列表/重启应用自动恢复；切换插件时立即保存旧插件。
- **只有「清空配置」按钮显式删除**；刷新、重扫均不会丢数据。
- 数组项内的模块状态（如每个 server 项里的 locations）暂不持久化，恢复时保持默认启用。

### 2.8 模块解耦与接口预留

- `Ferry.Core`：纯逻辑库（模型、插件加载、渲染、导入、校验、工作区），不依赖 WPF/UI 框架。
- `Ferry.Ui`：WPF 前端，只通过 Core 的公开 API 工作；未来若做 Web 前端，可复用 Core。
- `Ferry.Push`：推送层独立程序集，定义 `IPushService` 契约（本地目录 / Git / SSH），当前提供本地目录参考实现；UI 不直接依赖具体实现。
- `IWorkspaceStore`：工作区存储契约，UI 只依赖接口，未来扩展多配置列表时保持契约。

## 3. MVP 范围与验收标准

### 范围

1. 修复 3 个数组 BUG，嵌套数组（upstream → servers）可正常增删
2. 渲染引擎：json / yaml / ini / layout 四种渲染器
3. 插件格式：renderer 声明 + 字段 render 段（纯 YAML，无模板文件）
4. UI：源码预览/编辑面板、生成、应用修改、导入、导出
5. 示例插件：Nginx（layout，输出 nginx.conf）+ App-config（yaml，验证多格式与导入）
6. 单元测试：数组增删、值收集、渲染、导入回填

### 验收标准

- `dotnet build` 0 错误
- `dotnet test` 全部通过
- Nginx 插件：添加多个 upstream/server/location 后能生成结构正确的 nginx.conf
- App-config 插件：能导出 YAML、能导入 YAML 回填表单
- 源码面板可编辑，json/yaml 可应用回表单

## 4. 已知限制（MVP 之后）

- layout / ini 插件的反向解析（导入）
- validations 校验执行、required 标记
- 插件渲染器扩展（自定义 C# 渲染器/解析器）
- 配置多方案（profile）管理
- 文件编码与格式美化选项（缩进、引号风格等）
- 数组项内模块状态的持久化（当前只持久化静态路径上的模块）
- 字段级 required / 长度等更细的校验规则

### 4.1 未来方向（已记录，暂不实现）

- **多配置列表**：每个插件可保存多个命名配置，配置名作为文件名/标记，工作区以列表形式管理（接口已预留）。
- **显示模式**：全部 / 已选 / 未选 三种视图；对条目量巨大的配置做搜索过滤。
- **推送**：通过 `Ferry.Push` 的 `IPushService` 实现 Git（分支/提交信息）与 SSH（凭据管理）目标。
- **可选高级模块：模板引擎回归**：Scriban 已移除；如 layout 遇到表达不了的极端格式，以可选模块（类似推送）
  重新引入，供资深工程师使用，常规插件不依赖。
- **Web 前端**：Core 已与 UI 解耦，未来可新增 Web 后端复用同一套 Core 逻辑。

## 5. 实现状态（2026-08-10）

已按本设计完成 MVP：

- [x] 数组增删/嵌套数组 BUG 修复（稳定自增 Id、从定义重建、AddItemCommand 恒非空）
- [x] 渲染引擎：json / yaml / ini / layout（声明式布局，无模板语言）
- [x] 插件 renderer 声明 + 字段 render 段 + 全局布局样式
- [x] UI：源码预览/编辑面板（AvalonEdit）、生成、应用修改、导入、导出
- [x] 示例插件：Nginx（layout，纯 YAML）+ App Config（yaml）
- [x] 校验约束：min / max / integerOnly / 枚举自定义输入（worker_processes = auto 或数字）
- [x] 可选模块：模块勾选、层级解锁、输出门控；插件内预设搭配（快速模板）
- [x] 工作区持久化：刷新/重启恢复、显式清空、全量保存
- [x] 模块显示模型 v2：全部列出、未勾选置灰可检视、勾选才写入
- [x] 保存修复：Object 子字段作用域解析错误（http 块恢复丢失）已修复并加回归测试
- [x] 解耦与预留：`IWorkspaceStore` 接口、`Ferry.Push`（`IPushService` + 本地目录实现）
- [x] 日志与错误处理：集中日志（应用根目录 `ferry.log`、轮转）、插件加载/渲染/导入导出错误界面直显、全局异常兜底弹窗
- [x] 字段启用：所有字段默认可取消、`required` 必填锁定、工作区/预设适配
- [x] 单元/集成测试 58 项全通过，`dotnet build` 0 代码警告 0 错误

未包含（见第 4 节）：layout/ini 反向导入、validations 校验执行、插件自定义渲染器扩展等。

## 6. 决策记录（2026-08-11，UI 与插件格式演进）

### 6.1 勾选语义 v3（已确认）

- 勾选子模块 → **级联勾选所有祖先**（父作为上下文随子启用）。
- 勾选父模块 → **不级联勾选子模块**；子模块的勾选集合由模板文件（templates.yaml）或用户手动定义。
- 取消父模块 → **保留子模块状态与全部值**：子项在 UI 中保留（置灰、可展开检视），但**不写入源码/输出**；重新勾选父后原样恢复，不重置。
- 块内标量字段不受"子模块"规则影响：父块启用后，标量按其自身默认启用状态输出。
- 新建配置初始状态：**保持现状**（所有模块默认启用），模板负责重塑场景。
- 父勾选框显示：**两态（自身启用）+ "N/M 子模块"小字**，不用三态半选。
- 实现要点：级联向上只作用于**用户/预设触发的启用**；工作区恢复为"精确恢复"，不做级联（避免父停用但子保留的状态被恢复破坏）。

### 6.2 插件三文件结构（已确认方向）

```text
Plugins/<name>/
├── plugin.yaml       # 元数据：名称、插件版本、适用应用与应用版本范围、渲染器
├── schema.yaml       # 字段定义（presets 移出）
└── templates.yaml    # 场景模板（反向代理/静态站点等）：modules + values（可含数组项）
```

- 元数据新增 `target.name` / `target.version`（语义化版本范围，MVP 仅展示与记录；字段级版本过滤留待后续）。
- `templates.yaml` 即现有 presets 搬家并结构化；`PluginManager` 优先读它，回退兼容 schema 内旧 `presets`。
- 应用版本过滤字段（`minAppVersion`/`maxAppVersion` + 目标版本选择器）**不在 MVP 范围**。

### 6.3 headless API 与前端通信（已确认）

- Core 提供"会话 + 命令/状态"式 API（SetValue / ToggleEnabled / AddItem / Validate / Render 等），前端只依赖契约，不依赖 WPF 对象。
- Photino 桌面版：WebView2 与 .NET 宿主**同进程**消息通信，不经过 HTTP、不监听端口、不涉及防火墙。
- 纯 Web 版（未来）：ASP.NET Core HTTP 端点，届时再考虑端口选择与防火墙放行；Core 复用不变。

### 6.4 headless API 的多人化评估（2026-08-11）

核心迁到服务器、为多人提供服务时，**域逻辑与 API 契约无需大改**，变化集中在外部基建：

- 传输层：Photino 同进程消息 → HTTP/WebSocket 端点（Web API 直接映射同一套命令）。
- 会话/用户：新增认证、权限、按用户隔离配置；会话可换为"无状态文档"（前端随请求携带值树，天然可水平扩展）。
- 持久化：`IWorkspaceStore` 接口不变，实现从本地 JSON 换为数据库/服务端存储。
- 并发：文档模型上增加版本号做乐观锁即可。
- 插件：插件由"本地目录扫描"改为服务端共享注册表/缓存（schema 与模板只读共享）；`PluginManager` 的插件根目录需可配置。

因此当前实现约束：API 保持"纯函数/文档式"（Validate/Render/Import 输入输出皆数据），不暴露事件树；Core 不硬编码本地路径。

## 7. MVP 冻结与后续规划（2026-08-11）

### 7.1 版本管理策略（建议：Tag，不建单独分支/仓库）

- 当前 `dev` 提交全部保留在 git 历史中；MVP 完成态打 **Tag `mvp-1.0`** 作为里程碑即可。
- 不单独建分支：仅当"正式开发与 MVP 热修并行"时才需要从 tag 拉分支（届时 `git branch mvp <tag>` 一步可建）。
- 不单独建仓库：除非 MVP 需要独立分发/开源授权。
- 正式开发开始时再建 `dev-v2`/`next` 分支隔离重写（届时 UI 可能换栈、插件格式可能演进）。

### 7.2 第一批插件规划（Dockerfile / Docker Compose / Redis）

均可用现有 layout 渲染器表达，无需改引擎：

**Dockerfile**（无分号、无块）
- 全局样式：`line: "{{ .key }} {{ . }}"`（去掉默认分号）。
- 指令 id 用大写（FROM/WORKDIR/RUN…）保证输出美观；重复指令用行形数组（COPY/RUN/EXPOSE 每项一行）。

**Redis**（扁平 conf，指令名含连字符）
- 全局样式同上；字段 id 用下划线（`protected_mode`），通过字段级 `line: "protected-mode {{ . }}"` 输出连字符指令名。
- 注意：占位符正则 `[\w.]*` 不支持连字符，**字段 id 不要用连字符**（否则无法在 itemOpen/子字段占位符中引用）。
- 空密码（requirepass）默认空串 → 自动省略，符合 redis 默认无密码语义。

**Docker Compose**（YAML，services 是"键为服务名的映射"而非列表）
- 内置 yaml 渲染器会把数组输出为列表（`- name: web`），**不满足 compose 结构**；改用 layout 渲染器手写 YAML 风格。
- 用"Object 包 Array"解决"services: 头 + 动态服务项"结构：`services` Object（`open: "services:"`、`close: ""`），其子字段 `items` Array（`itemOpen: "{{ .name }}:"`、`itemClose: ""`），服务项子字段（image/ports/environment…）缩进两级。
- ports/environment/volumes 用行形数组输出 `- "80:80"` / `- KEY=VALUE`。
- 备选：给 layout 引擎的块形数组增加字段级 `open`/`close` 包装（正式开发再评估）。

## 8. v2 开发记录（绿地重构，2026-08-11）

v2 从零重构（新仓库，MVP 仅作参考）已完成的里程碑：

### M0 仓库与基线
- 当前仓库改名 `ferry-mvp`（历史与 tag `mvp-1.0` 完整保留）；新建 `ferry` 仓库。
- 迁移 5 个插件资产与设计文档；Nginx 插件目录统一为 `Nginx`（PluginKey 同步）。
- 许可确认为 Apache 2.0。

### M1 Core 基座
- 领域模型、`IPluginSource`（`DirectoryPluginSource`）、`PluginManager`。
- 四种渲染器（json/yaml/ini/layout，layout 语义沿用 MVP 已验证设计）。
- `FormNode` 表单树（勾选语义 v3、依赖显隐、required 锁定、数组稳定路径）。
- 校验（min/max/integerOnly/枚举/validations pattern）、值收集、类型强转、导入。
- 端口契约：`IWorkspaceStore`（v2）、`IPushService`。

### M2 FormSession 命令引擎
- 命令协议 DTO：SetValue / ToggleEnabled / AddItem / RemoveItem / ApplyPreset / Import / Validate / Render / Snapshot。
- `ConfigState`（含乐观锁 Version）+ `OperationResult`（含 ErrorCode：conflict/not_found/validation）。
- `FormFieldSnapshot` 只读快照树 + `PathResolver`（静态路径 + 数组序号 `http.servers[0]`）。
- 实例式会话与静态 `Execute(plugin, state, command, expectedVersion?)` 共享同一内核。

### M3 工作空间与版本管理
- 三层模型：工作空间 → 配置 → 版本快照；`LocalWorkspaceStore`（JSON，接口可换）。
- 留档/查看/回滚；插件缺失与版本变化健壮性；MVP workspace.json 一次性迁移。

### M4 自定义格式反向解析
- layout/ini 宽松解析：按 schema 字段 id 与 render 前缀生成扫描规则，块形按缩进/括号识别。
- 未识别内容原样保留（随配置存档、导出可选追加、计入解析报告），不主动丢弃。

### M5 可移植存档包
- zip 容器：配置数据（源码为权威 + 缓存 + 版本历史）+ 插件定义。
- 导入时本机同 key 插件优先；无插件可从包内只读加载或仅保留源码查看/导出；显式安装才写入插件目录。

### M6 Photino spike（已通过）
- 最小 Photino.NET 宿主 + 原生 HTML/CSS/JS 表单，经 WebView2 消息桥调用 FormSession。
- 自检结果（8 步命令，JS→C#→JS 端到端）：**最差单步延迟 3.3ms，全部 <50ms 目标**。
- 结论：WebView2 可用、IPC 双向正确、FormSession API 覆盖 spike 全部需求（无缺口），进入 M7 正式 UI 迁移。

### M7 正式 UI 迁移（Photino + HTML/CSS/JS）
- 三栏深色界面：左侧工作空间/配置导航 + 版本历史 + 存档包；中间表单；右侧预览/源码编辑（禁止折行，水平滚动）。
- 表单覆盖：模块勾选语义 v3（级联向上、N/M 计数、未勾选可展开、父停用保留子状态）、六类字段控件、依赖显隐、枚举自定义值、数组增删、校验内联。
- 工作空间/配置管理：新建/重命名/删除、配置按插件分组、插件缺失置灰（仅可查看/导出源码）、留档/查看/回滚、清空配置。
- 导入导出：源码编辑回填表单（json/yaml 精确、layout/ini 宽松 + 未识别内容报告）、导入文件/导出文件、存档包导出/导入。
- 日志入口：集中日志路径展示与一键打开。
- **IPC 关键经验**：Photino 的 C#→JS 消息存在延迟/重复交付，FIFO 配对会错位；
  v2 采用"请求带 requestId、响应回显 requestId、JS 按 id 配对"后全链路稳定。
- 自检 13 步全过（建工作空间/建配置/打开/勾选/数组增删/填值/渲染/留档/存档导出导入/版本列表）：
  **最差单步 34.7ms（config:open 含反向解析），全部 <50ms**，进程自动退出。
