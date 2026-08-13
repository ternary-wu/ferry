# Ferry

> 插件化的运维配置生成工具：通过插件定义"表单 + 语法规则"，动态渲染配置界面，生成 **json / yaml / ini / 任意自定义格式**（layout 声明式）的配置文件，并支持预览、导入导出、工作空间与版本管理。

Ferry 的目标是让"生成配置文件"这件事变得可配置、可复用、可编辑：运维或开发安装一个插件，在表单里勾选需要的模块、填写字段，即可得到一份完整且经过校验的配置文件；同一份配置还可以在软件内直接编辑、导入导出、反复修改，并按工作空间/配置/版本留档管理。

## 当前状态

本仓库为 **Ferry v2 正式开发仓库（绿地重构）**，前端已完成 Vue 3 + TypeScript + Vite + Tailwind CSS + Pinia 重写，旧 `wwwroot` UI 已退役。

- MVP（tag `mvp-1.0`）已冻结，代码存档于独立仓库 [ferry-mvp](https://github.com/ternary-wu/ferry-mvp.git)，仅作为需求与设计参考，不参与本仓库构建。
- 本仓库已迁移 5 个插件资产（Nginx / App Config / Dockerfile / Redis / Docker Compose）与设计文档，作为 v2 开发基线。
- 开发路线与决策见 `docs/design.md`（v2 章节随里程碑更新）。

## 仓库结构（规划）

```text
Ferry.Core/           纯逻辑库：领域模型、插件加载、表单引擎、校验、渲染、导入、端口接口
Ferry.Infrastructure/ 适配器：目录插件源、本地存储、本地目录推送、日志
Ferry.App/            Photino 桌面宿主（窗口 Shell + IPC 分发）
frontend/             Vue 3 + TypeScript 前端（Vite 构建，输出 frontend/dist）
tests/Ferry.Core.Tests/ 单元与集成测试
Plugins/              插件资产（plugin.yaml / schema.yaml / templates.yaml）
docs/                 设计文档与插件开发文档
```

## 环境要求

Windows + .NET 10 SDK；前端构建需要 Node.js（建议 LTS）与 pnpm。

```bash
dotnet build Ferry.slnx
dotnet test
```

前端构建（仓库路径含 `#`，Vite 无法直接在该路径运行，需在无 `#` 的镜像目录执行）：

```bash
# 1) 把 frontend 复制到无 # 的目录（如 C:\work\ferry-frontend-ci）
# 2) 在镜像目录安装依赖并构建
pnpm install
pnpm build
# 3) 把镜像 dist 拷回 frontend/dist，再执行 dotnet build 刷新输出目录
```

前端检查命令：`pnpm typecheck`（vue-tsc）、`pnpm test`（Vitest）、`pnpm build`（Vite）。

启动应用（默认加载新前端）：

```bash
dotnet run --project Ferry.App
```

自检模式（新前端 19 步自检，结果写入输出目录 `ferry-spike-result.json`）：

```bash
$env:FERRY_SPIKE_SELFCHECK='1'
dotnet run --project Ferry.App
```

## 文档

- [插件开发文档](docs/plugin-development.md)
- [二次开发文档](docs/developer-guide.md)
- [设计与决策](docs/design.md)

## 许可

[Apache License 2.0](LICENSE.txt)
