namespace Ferry.Core.Models;

/// <summary>配置 schema（schema.yaml）：根标识 + 顶层字段 + 兼容旧 presets。</summary>
public sealed class ConfigSchema
{
    public string RootId { get; set; } = string.Empty;
    public List<FieldDefinition> Fields { get; set; } = new();
    public List<ConfigPreset> Presets { get; set; } = new();
}

/// <summary>
/// 场景模板（templates.yaml）：modules 列出启用的模块路径（其余禁用），values 为部分初始值。
/// 只决定初始勾选与初始值，应用后用户可自由调整。
/// </summary>
public sealed class ConfigPreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Modules { get; set; } = new();
    public Dictionary<string, object?>? Values { get; set; }
}
