# Ferry 插件开发文档

本指南面向插件作者：如何为 Ferry 编写一个插件，让 Ferry 能渲染你的配置表单并生成目标格式的配置文件。

设计原则：**插件开发只依赖 YAML 这一种可迁移技能**。你不需要学任何模板语言——循环、条件、缩进全部由 Ferry
引擎处理，你唯一要写的是"字段怎么输出"的声明，以及最基础的 `{{ .字段 }}` 插值（与 Helm / envsubst 思路一致）。

## 1. 插件是什么

一个插件 = 一个目录 + 三个文件：

```text
Plugins/
└── my-plugin/
    ├── plugin.yaml      # ① 元数据：名称、插件版本、适用应用与应用版本范围、渲染器（必需）
    ├── schema.yaml      # ② 字段定义：字段、校验、模块、输出格式（必需）
    └── templates.yaml   # ③ 场景模板：反向代理/静态站点等，定义涉及的模块与数值（可选）
```

插件目录放在应用运行目录的 `Plugins/` 下即可被自动扫描加载。开发时也可以放在源码的 `Ferry.Ui/Plugins/` 下（构建会自动拷贝到输出目录）。

## 2. plugin.yaml

```yaml
name: Nginx                # 显示名称（必填，缺省时用目录名）
version: 1.27.0            # 版本号
target:                    # 适用应用与应用版本范围
  name: nginx              # 应用名称
  version: ">=1.25, <2.0"  # 应用版本范围（语义化，MVP 仅展示与记录）
  note: 指令字段按 1.25+ 常用生产集整理   # 可选说明
author: Ferry Community    # 作者
description: 生成 Nginx 主配置文件  # 界面上的插件说明
renderer:
  type: layout             # json | yaml | ini | layout
  layout:                  # layout 的全局默认样式（字段级 render 可覆盖）
    line: "{{ .key }} {{ . }};"     # 标量行默认格式
    blockOpen: "{{ .key }} {"       # 对象/数组块默认开头
    blockClose: "}"                 # 对象/数组块默认闭合
    indent: "    "                  # 缩进单元
  defaultFileName: nginx.conf       # 导出对话框的默认文件名
  outputExtension: .conf            # 默认文件扩展名（文件名已有扩展名时忽略）
```

`renderer.type` 决定了输出格式：

| type | 说明 | 需要额外文件 |
| --- | --- | --- |
| `json` | 内置 JSON 渲染器（System.Text.Json，缩进输出） | 否 |
| `yaml` | 内置 YAML 渲染器（YamlDotNet） | 否 |
| `ini` | 内置 INI 渲染器（见 4.2） | 否 |
| `layout` | 声明式布局引擎，可输出任意自定义/自研格式 | 否（一切在 schema 的 render 段声明） |

未声明 `renderer` 的插件默认按 `layout` 处理（使用全局默认样式）。

## 3. schema.yaml：配置表单定义

```yaml
rootId: nginx_config        # 根标识（当前仅作记录）
fields:                     # 顶级字段列表
  - id: worker_processes
    label: Worker 进程数
    type: Enum
    defaultValue: auto
    ...
```

### 3.1 字段通用属性

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `id` | string | 字段唯一标识（同一作用域内），也是输出配置中的键名 |
| `label` | string | 界面显示名称 |
| `description` | string | 悬停提示（❓） |
| `type` | enum | 字段类型，见 3.2 |
| `defaultValue` | any | 默认值 |
| `visibilityDependency` | object | 依赖显隐，见 3.4 |
| `module` | bool | 是否为可选模块，见 3.5 |
| `required` | bool | 插件声明必填：该字段不可取消（勾选框锁定），始终输出；默认所有字段都可取消 |
| `render` | object | 输出格式声明（layout），见 4.3 |
| `children` | list | Object/Array 的子字段定义 |

> **字段启用**：默认情况下**每个字段都可以取消**（包括 worker_processes、app_name 这类标量），
> 取消勾选后该项不写入输出。只有 `required: true` 的字段会被锁定保留——适合"没有 include 机制、
> 某些内容必须存在才能使用"的配置文件。块级字段用 `module: true` 获得块样式勾选。

### 3.2 字段类型

| 类型 | 界面控件 | 说明 |
| --- | --- | --- |
| `String` | 文本框 | 普通字符串 |
| `Number` | 文本框 | 数字，支持 `min` / `max` / `integerOnly` 校验 |
| `Boolean` | 复选框 | 布尔值（layout 输出 `true` / `false`；需要 on/off 等文本时用 Enum） |
| `Enum` | 下拉框 | 固定选项；`allowCustomValue: true` 时变为可编辑下拉（选预设值或输入自定义值） |
| `Array` | 数组列表 | 可添加/删除项；每项是 Object（由 `children` 定义） |
| `Object` | 折叠块 | 由 `children` 定义子字段 |

### 3.3 校验约束（Number / 允许自定义值的 Enum）

```yaml
- id: worker_connections
  type: Number
  defaultValue: 1024
  integerOnly: true     # 只能填整数
  min: 1                # 下限
  max: 1048576          # 上限
```

校验规则由插件作者定义，应用在表单变更与导出前自动执行；不满足时字段下方显示错误，且阻止生成/导出。

### 3.4 依赖显隐（visibilityDependency）

```yaml
- id: proxy_pass
  type: String
  visibilityDependency:
    dependsOnField: mode      # 依赖的字段 id（向上查找最近的同名祖先）
    expectedValue: proxy      # 依赖字段的值等于它时本字段才显示
```

### 3.5 可选模块（module）

给 Object / Array 字段加 `module: true`，该块就会带一个勾选框：

- 勾选 → 该块及其内容写入输出配置；
- 未勾选 → 不写入，但内容仍列出、置灰可检视（可查看子模块与选项）；
- 父模块未勾选时，子模块勾选框锁定，勾选父模块后解锁（子模块之前的状态保留）。

### 3.6 场景模板（templates.yaml）

场景模板放在独立文件 `templates.yaml`，用户在工具栏下拉中一键应用：

```yaml
templates:
  - id: full
    name: 完整配置
    description: 启用全部模块
    modules: [events, http, http.upstreams, http.servers]
  - id: http_only
    name: 仅 HTTP 基础
    modules: [http]
    values:
      http:
        keepalive_timeout: 120
```

- `modules`：列出要启用的模块（用字段路径，如 `http.upstreams`）；未列出的模块全部禁用。
- `values`：可选的部分初始值（嵌套字典结构；数组项也支持，可直接定义 server/location 块）。
- 预设只决定**初始**勾选与初始值，应用后用户可自由调整。
- **勾选父模块不会自动勾选子模块**；子模块的勾选集合由模板的 `modules` 或用户手动定义。
- 旧格式兼容：没有 `templates.yaml` 时回退读取 `schema.yaml` 内的旧 `presets`。

## 4. 渲染器详解

### 4.1 json / yaml

无需任何额外声明。表单值按"字段 id → 值"收集为嵌套字典树，直接序列化输出。Number 输出为数字、Boolean 输出为布尔值。

### 4.2 ini

无需任何额外声明，规则如下：

```ini
worker_processes = 1

[events]
worker_connections = 1024
use = epoll

[http.upstreams.1]
upstream_name = backend
```

- 标量 → `key = value`；
- Object → `[路径]` 节；
- 数组项 → `[路径.N]` 节（N 从 1 开始）；
- Boolean → `true` / `false`。

### 4.3 layout：声明式布局（自定义/自研格式）

这是为自研软件、非通用格式准备的渲染方式。**你不需要写模板**，只需要：

1. 在 `plugin.yaml` 的 `renderer.layout` 里声明全局默认样式（标量行、块、缩进）；
2. 在 `schema.yaml` 里给"不按默认走的字段"加 `render` 段。

#### 占位符（唯一的插值语法，共三种）

| 占位符 | 含义 | 示例输出 |
| --- | --- | --- |
| `{{ . }}` | 当前字段的值 | `{{ . }}` → `on` / `8080` |
| `{{ .key }}` | 当前字段的键名（id） | `{{ .key }}` → `worker_processes` |
| `{{ .子字段id }}` | 当前节点下的子字段值 | `{{ .upstream_name }}` → `backend` |

> 没有 if / for / end，没有任何控制流。数组遍历、空值省略、缩进、模块裁剪都由引擎自动完成。

#### 字段 render 属性

| 属性 | 适用 | 说明 |
| --- | --- | --- |
| `line` | 标量 | 单行格式（缺省取全局 `layout.line`） |
| `open` / `close` | Object / Array | 块开头 / 闭合（缺省取全局 `blockOpen` / `blockClose`；`close` 可留空表示无闭合符） |
| `itemOpen` / `itemClose` | Array | 数组**块形**：每个元素项一个块的头 / 闭（如 `upstream {{ .name }} {` / `}`） |
| `item` | Array | 数组**行形**：每个元素项的格式（如 `server {{ .address }} weight={{ .weight }};`） |
| `inline` | Array | `item` 为整项单行（不递归输出子字段） |
| `keepEmpty` | Object / Array | 空块仍输出（默认 OmitEmpty：空数组/空对象整块省略） |
| `hidden` | 任意 | 仅作名称/引用使用，不输出为行（如 upstream 名称） |

#### 数组的三种形态

```yaml
# 1) 块形：每项一个块（nginx upstream / server / location）
- id: upstreams
  type: Array
  render:
    itemOpen: "upstream {{ .upstream_name }} {"
    itemClose: "}"

# 2) 行形：块头一次 + 每项一行（ini 风格）
- id: servers
  type: Array
  render:
    open: "[servers]"
    item: "{{ .host }} = {{ .port }}"

# 3) 单行：整项一行（自研软件常见的紧凑写法）
- id: servers
  type: Array
  render:
    item: "server {{ .server_address }} weight={{ .weight }};"
    inline: true
```

#### 完整示例（Nginx 的 upstream 部分）

`plugin.yaml` 全局样式：`line: "{{ .key }} {{ .}};"`、`blockOpen: "{{ .key }} {"`、`blockClose: "}"`。

`schema.yaml`：

```yaml
- id: upstreams
  type: Array
  module: true
  render:
    itemOpen: "upstream {{ .upstream_name }} {"
    itemClose: "}"
  children:
    - id: upstream_name
      type: String
      render:
        hidden: true              # 名称已用于块头，不再输出为行
    - id: servers
      type: Array
      render:
        item: "server {{ .server_address }} weight={{ .weight }};"
        inline: true
      children:
        - id: server_address
          type: String
        - id: weight
          type: Number
```

表单值为 `{ "upstreams": [{ "name": "backend", "servers": [...] }] }` 时输出：

```nginx
upstream backend {
    server 127.0.0.1:8080 weight=1;
}
```

#### 行为约定

- 空标量（未填 / 空字符串）自动跳过；空数组、空对象默认整块省略（`keepEmpty: true` 可保留空块）。
- 所有字段默认可取消（UI 勾选框）；取消勾选则不输出，`required: true` 锁定必填字段。
- 布尔值输出 `true` / `false`；需要 `on` / `off` 之类文本时，把字段定义为 `Enum` 即可（如 Nginx 的 sendfile）。
- 未勾选的模块不会出现在值树中，自然不输出。
- `render` 段中的占位符会在插件加载时校验：引用不存在的子字段、在不允许的位置使用 `{{ . }}` 都会直接报错并显示在状态栏，避免"静默输出空值"。
- 内联单行模板无法表达"可选片段"（如 `weight=` 在值为空时仍会输出 `weight=;`）：若需要严格省略，请把该字段定义为必填/默认值，或拆成块形结构。

## 5. 导入支持

- `json` / `yaml` 渲染器：支持从文件导入，解析为值树后回填表单（未知键忽略，数字按类型转换）。
- `layout` / `ini`：MVP 阶段**不支持**反向导入（自定义格式的逆向解析成本高），界面会禁用「导入/应用修改」。

## 6. 完整示例：最小插件

```yaml
# Plugins/my-app/plugin.yaml
name: My App
version: 1.0.0
target: application
author: you
description: 生成我的应用配置
renderer:
  type: yaml
  defaultFileName: app.yaml
  outputExtension: .yaml
```

```yaml
# Plugins/my-app/schema.yaml
rootId: my_app
fields:
  - id: app_name
    label: 应用名称
    type: String
    defaultValue: demo
  - id: port
    label: 端口
    type: Number
    defaultValue: 8080
    integerOnly: true
    min: 1
    max: 65535
  - id: features
    label: 功能
    type: Array
    module: true
    children:
      - id: name
        label: 功能名
        type: String
```

把目录放进运行目录 `Plugins/`（或源码 `Ferry.Ui/Plugins/` 后重新构建），启动 Ferry 即可看到该插件。

## 7. 排障

- 插件加载失败会记录到 `C:\ferry_log.txt`（含异常堆栈）。
- schema/plugin YAML 解析错误、`render` 段占位符校验错误（引用不存在的子字段等）都会在界面的状态栏提示。
- 修改插件后点左侧「重新扫描」即可重新加载；已保存的工作区配置会按插件目录名自动恢复。
- 想确认表单值树长什么样：临时把 `renderer.type` 改成 `json` 导出一次，即可看到完整的字段 id → 值结构。
