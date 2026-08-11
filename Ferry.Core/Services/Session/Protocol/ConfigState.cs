namespace Ferry.Core.Services.Session.Protocol;

/// <summary>
/// 可序列化文档状态：M2 以 Values/Enabled 为权威输入；M3 起 SourceText（源码）为权威，
/// Values/Enabled 变为打开配置时由源码解析派生的缓存。Version 用于乐观锁。
/// </summary>
public sealed class ConfigState
{
    public string PluginKey { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public Dictionary<string, object?> Values { get; set; } = new();
    public Dictionary<string, bool> Enabled { get; set; } = new();
    public string SourceText { get; set; } = string.Empty;
    public long Version { get; set; }
    public string? WorkspaceId { get; set; }
    public string? ConfigId { get; set; }
}
