namespace Ferry.Core.Services.Session.Protocol;

/// <summary>
/// 命令式协议：所有操作收敛为命令/查询 DTO（纯数据、可 JSON 序列化）。
/// 单机走实例式会话，服务器走静态 Execute，两者共享同一执行内核。
/// </summary>
public abstract record FormCommand;

/// <summary>设置字段值（路径规则：http.servers[0].server_name）。</summary>
public sealed record SetValueCommand(string Path, object? Value) : FormCommand;

/// <summary>切换启用状态；Enabled 省略时取反。</summary>
public sealed record ToggleEnabledCommand(string Path, bool? Enabled = null) : FormCommand;

/// <summary>向数组字段追加一个元素项；成功后返回新项路径。</summary>
public sealed record AddItemCommand(string ArrayPath) : FormCommand;

/// <summary>移除数组项。</summary>
public sealed record RemoveItemCommand(string Path) : FormCommand;

/// <summary>应用场景模板（按 Id 或 Name 匹配）。</summary>
public sealed record ApplyPresetCommand(string PresetId) : FormCommand;

/// <summary>导入配置文本（仅 CanImport 的插件）。</summary>
public sealed record ImportCommand(string Text) : FormCommand;

/// <summary>校验整棵表单树，返回错误列表。</summary>
public sealed record ValidateCommand : FormCommand;

/// <summary>渲染当前值为配置文本（不做校验）。</summary>
public sealed record RenderCommand : FormCommand;

/// <summary>返回表单快照树（UI 渲染数据）。</summary>
public sealed record SnapshotCommand : FormCommand;
