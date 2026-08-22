# Ferry

> 插件化的运维配置生成工具：通过插件定义"表单 + 语法规则"，动态渲染配置界面，生成 **json / yaml / ini / 任意自定义格式**（layout 声明式）的配置文件，并支持预览、导入导出、工作空间与版本管理。

Ferry 的目标是让"生成配置文件"这件事变得可配置、可复用、可编辑：运维或开发安装一个插件，在表单里勾选需要的模块、填写字段，即可得到一份完整且经过校验的配置文件；同一份配置还可以在软件内直接编辑、导入导出、反复修改，并按工作空间/配置/版本留档管理。

## 当前状态

 **Ferry**，绿地重构，尚在开发中。

## 仓库结构（规划）

```text
Ferry.Core/           纯逻辑库：领域模型、插件加载、表单引擎、校验、渲染、导入、端口接口
....
```

## 环境要求

Windows + .NET 10 SDK。

```bash
dotnet build Ferry.slnx
dotnet test
```

## 文档

- [插件开发文档](docs/plugin-development.md)
- [二次开发文档](docs/developer-guide.md)

## 许可

[Apache License 2.0](LICENSE.txt)
