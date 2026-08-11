# Ferry 二次开发文档

本指南面向需要扩展 Ferry 本身的开发者：架构、核心 API、扩展点、构建与测试。

## 1. 架构总览

```text
┌─────────────────────────────────────────────────────────┐
│ Ferry.Ui（WPF 前端）                                     │
│  MainViewModel / 动态表单模板 / AvalonEdit 源码面板       │
└───────────────┬─────────────────────────────────────────┘
                │ 只依赖公开 API
┌───────────────▼─────────────────────────────────────────┐
│ Ferry.Core（纯逻辑库，不依赖 UI 框架）                   │
│  模型 → 插件加载 → 表单构建 → 校验 → 值收集 → 渲染/导入   │
│  工作区存储（IWorkspaceStore）                           │
└───────────────┬─────────────────────────────────────────┘
                │
┌───────────────▼─────────────────────────────────────────┐
│ Ferry.Push（推送层，独立程序集）                         │
│  IPushService：LocalDirectory（已实现）/ Git / SSH（预留）│
└─────────────────────────────────────────────────────────┘
```

依赖方向：`Ferry.Ui → Ferry.Core`，`Ferry.Push` 独立（未来被 UI 或 Web 后端调用）。Core 与 UI 解耦，未来做 Web 前端可直接复用 Core。

## 2. 项目结构

| 路径 | 职责 |
| --- | --- |
| `Ferry.Core/Models/` | `FieldDefinition`、`ConfigSchema`、`ConfigPreset`、`FieldType` 等数据模型 |
| `Ferry.Core/Services/` | `PluginManager`、`FormBuilder`、`FormFieldViewModel`、`ConfigValueCollector`、`ConfigValidator`、`ConfigValueConverter`、`ConfigImporter`、`WorkspaceStore` |
| `Ferry.Core/Services/Rendering/` | `IConfigRenderer` 及 json/yaml/ini/template 四种实现、`RendererFactory` |
| `Ferry.Ui/` | WPF：`MainViewModel`、`MainWindow.xaml`（数据模板与选择器）、`Converters/` |
| `Ferry.Push/` | `IPushService`、`PushRequest`/`PushResult`、`LocalDirectoryPushService` |
| `tests/Ferry.Core.Tests/` | xUnit 单元与集成测试 |
| `docs/` | 设计（design.md）、插件开发（plugin-development.md）、本指南 |

## 3. 核心数据流

```text
plugin.yaml + schema.yaml
        │ PluginManager.LoadAllPlugins()（YamlDotNet 解析）
        ▼
ConfigSchema ──► FormBuilder.Build(schema, values?)
        │             递归创建 FormFieldViewModel 树
        ▼
FormFieldViewModel 树（值/模块状态/校验错误/可见性）
        │ 用户编辑 → ConfigValidator.Validate（错误写回 ValidationError）
        │          → ConfigValueCollector.Collect（按模块/可见性裁剪，类型强转）
        ▼
Dictionary<string, object?> 值树
        │ RendererFactory.Create(plugin) → IConfigRenderer.Render
        ▼
文本（json / yaml / ini / 模板）
        │ 导入反向：ConfigImporter.Parse → FormBuilder.Build 回填
        │ 持久化：WorkspaceStore（全量值树 + 模块状态）
```

## 4. 核心 API 速览

### Ferry.Core.Models

- `FieldType`：`String / Number / Boolean / Enum / Array / Object`
- `FieldDefinition`：字段定义（`Id`、`Label`、`Type`、`DefaultValue`、`Min/Max/IntegerOnly`、`AllowCustomValue`、`Module`、`Children`、`VisibilityDependency` 等）
- `ConfigSchema`：`RootId`、`Fields`、`Presets`
- `ConfigPreset`：`Name`、`Description`、`Modules`（启用模块路径列表）、`Values`

### Ferry.Core.Services

| 类型 | 职责 | 常用成员 |
| --- | --- | --- |
| `PluginManager` | 扫描 `Plugins/` 目录，解析插件元数据与 schema | `LoadAllPlugins()` |
| `PluginContext` | 单个插件上下文 | `Name`、`RendererConfig`、`Schema`、`PluginKey`、`CanImport`、`DefaultFileName` |
| `FormBuilder` | schema → VM 树，可选值回填；应用字段启用状态 | `Build(schema, values?)`、`ApplyEnabledStates(roots, states)` |
| `FormFieldViewModel` | 字段 VM（值、启用、校验、路径） | `Value`、`IsEnabled`、`IsSelectable`、`CanToggleEnabled`、`ValidationError`、`Path`、`AddItemCommand` |
| `ConfigValueCollector` | 收集值树（按可见性/启用状态裁剪，类型强转） | `Collect(roots, includeDisabled?)` |
| `ConfigValidator` | 整树校验，错误写回 `ValidationError` | `Validate(roots)` → `List<string>` |
| `ConfigValueConverter` | 按字段类型强转值（数字/布尔） | `Coerce(type, raw)` |
| `ConfigImporter` | json/yaml 解析为值树 | `Parse(plugin, text)`、`ParseJson/Yaml`、`NormalizeTree` |
| `WorkspaceStore` | 工作区持久化（`IWorkspaceStore`） | `Load/Save/Clear(pluginKey)` |
| `FerryLog` | 集中日志（应用根目录 `ferry.log`，自动轮转） | `Configure(directory?, maxBytes?)`、`Info/Warn/Error` |

### Ferry.Core.Services.Rendering

- `IConfigRenderer`：`string Render(Dictionary<string, object?> config)`
- 实现：`JsonConfigRenderer` / `YamlConfigRenderer` / `IniConfigRenderer` / `LayoutConfigRenderer`（声明式布局）
- `RendererFactory.Create(PluginContext)`：按 `RendererConfig.Type` 分派
- `LayoutConfigRenderer`：递归渲染 schema 值树，处理缩进、数组遍历、OmitEmpty；占位符 `{{ . }}` / `{{ .key }}` / `{{ .子字段id }}`
- `FieldRenderConfig` / `PluginLayoutStyle`：字段级输出格式与全局默认样式（纯 YAML 声明）

### Ferry.Push

- `IPushService`：`Name`、`Supports(PushTargetType)`、`PushAsync(PushRequest, CancellationToken)`
- `PushTargetType`：`LocalDirectory / GitRepository / SshServer`
- `PushRequest`：`ConfigName`、`Content`、`Target`、`Branch`、`CommitMessage`、`RemotePath`、`CredentialId`
- `LocalDirectoryPushService`：参考实现（写文件到目录）

### Ferry.Ui

- `MainViewModel`：插件列表、表单树、预设、校验、工作区、预览文本、编辑模式
- `MainWindow.xaml`：每个 `FieldType` 一个 `DataTemplate`，`FieldTemplateSelector`（Style + DataTrigger/MultiDataTrigger）按 `Definition.Type`/`Module`/`AllowCustomValue` 选择模板
- `Converters/`：可见性、字符串、布尔反转等转换器
- `App.xaml.cs`：DI 注册（`PluginManager`、`IWorkspaceStore`、`MainViewModel`）
- `App.xaml.cs` 同时注册全局异常兜底：UI 线程未处理异常弹窗提示 + 写日志；致命异常与后台任务异常写日志

## 5. 扩展点

### 5.1 新增渲染器类型

1. 实现 `IConfigRenderer`（放 `Ferry.Core/Services/Rendering/`）；
2. 在 `RendererFactory.Create` 中注册新的 type 名；
3. 插件 `plugin.yaml` 的 `renderer.type` 即可使用。

### 5.2 为自定义格式增加导入

`ConfigImporter.Parse` 目前只支持 json/yaml；新增格式时在其 `switch` 中加分支，把文本解析为值树（`Dictionary<string, object?>`）即可复用表单回填。

### 5.3 新增字段类型

1. `FieldType` 枚举加新值；
2. `MainWindow.xaml` 新增对应 `DataTemplate` 并在 `FieldTemplateSelector` 加 DataTrigger；
3. `ConfigValueCollector.CollectField` / `ConfigValidator.ValidateField` / `ConfigValueConverter.Coerce` 补充该类型的收集/校验/转换分支。

### 5.4 推送实现（Git / SSH）

在 `Ferry.Push` 中实现 `IPushService`（或复用 `LocalDirectoryPushService` 的模式），后续接入 UI 或 Web 后端即可，Core 无需改动。

### 5.5 工作区/多配置

UI 依赖 `IWorkspaceStore` 接口；实现"每插件多命名配置"时扩展接口与存储结构，UI 增加配置列表选择即可。

## 6. 构建与测试

```bash
dotnet build Ferry.slnx          # 0 警告 0 错误
dotnet test                      # 53 项测试（数组增删、校验、模块、渲染、导入、工作区、推送、layout、日志）
dotnet run --project Ferry.Ui    # 运行
```

测试工程通过 `FindRepoRoot()`（向上查找 `Ferry.slnx`）读取真实插件文件做集成测试，新增插件字段/schema 语法时建议同步补充。

## 7. 开发约定

- C# 12 / .NET 10，`Nullable` 与 `ImplicitUsings` 开启。
- `Ferry.Core` 不得引用 WPF/UI 程序集（`FormFieldViewModel` 只使用 `ICommand`/`ObservableCollection`，来自 `System.ObjectModel`）。
- 命名空间按项目分层：`Ferry.Core.Models` / `Ferry.Core.Services` / `Ferry.Core.Services.Rendering` / `Ferry.Ui.ViewModels` / `Ferry.Push`。
- 面向用户与插件作者的字符串（错误提示、插件说明）使用中文。
- 核心逻辑尽量放 Core 并以静态方法/接口暴露，便于无 UI 复用与测试。
- 错误处理约定：用户能直接理解的错误在界面状态栏/弹窗提示；详细信息统一走 `FerryLog` 写入应用根目录 `ferry.log`，不要散落硬编码日志路径。

## 8. 已知限制与路线

- layout/ini 反向导入未实现；数组项内模块状态暂不持久化；`validations` 字典尚未执行。
- 路线：多配置列表、显示模式（全部/已选/未选）与搜索、Git/SSH 推送、Web 前端、更细校验规则；
  模板引擎（已移除）未来可作为可选高级模块回归，供极端格式使用。详见 [design.md](design.md) 第 4 节。
