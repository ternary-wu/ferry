namespace Ferry.Core.Ports;

/// <summary>项目（最高级容器）：项目 → 工作空间 → 配置。</summary>
public sealed record ProjectInfo(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>工作空间（项目级顶层容器）。</summary>
public sealed record WorkspaceInfo(
    string Id,
    string ProjectId,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>配置概要（绑定一个插件）。</summary>
public sealed record ConfigInfo(
    string Id,
    string WorkspaceId,
    string Name,
    string PluginKey,
    string PluginVersion,
    DateTimeOffset UpdatedAt,
    string? CurrentVersionId);

/// <summary>配置数据：源码为权威，Values/Enabled 为打开配置时由源码解析派生的缓存。</summary>
public sealed class ConfigData
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PluginKey { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public Dictionary<string, object?> Values { get; set; } = new();
    public Dictionary<string, bool> Enabled { get; set; } = new();
    /// <summary>导入/解析时未能识别的原始内容（随配置存档，导出可选追加）。</summary>
    public List<string> Unrecognized { get; set; } = new();
    public string? VersionId { get; set; }
}

/// <summary>配置留档的版本快照（源码 + 时间 + 备注）。</summary>
public sealed record VersionSnapshot(
    string Id,
    string ConfigId,
    string SourceText,
    DateTimeOffset Timestamp,
    string? Note);

/// <summary>
/// 工作区存储端口（v2 契约）：按 (WorkspaceId, ConfigId) 存取配置与版本历史。
/// 实现可换：本地 JSON / SQLite / 服务端存储。Core 不感知实现细节。
/// </summary>
public interface IWorkspaceStore
{
    IReadOnlyList<ProjectInfo> ListProjects();
    ProjectInfo? GetProject(string projectId);
    void SaveProject(ProjectInfo project);
    void DeleteProject(string projectId);

    IReadOnlyList<WorkspaceInfo> ListWorkspaces();
    WorkspaceInfo? GetWorkspace(string workspaceId);
    void SaveWorkspace(WorkspaceInfo workspace);
    void DeleteWorkspace(string workspaceId);
    IReadOnlyList<string> GetWorkspaceOrder(string projectId);
    void SaveWorkspaceOrder(string projectId, IReadOnlyList<string> workspaceIds);

    IReadOnlyList<ConfigInfo> ListConfigs(string workspaceId);
    IReadOnlyList<string> GetConfigOrder(string workspaceId);
    void SaveConfigOrder(string workspaceId, IReadOnlyList<string> configIds);
    ConfigData? LoadConfig(string workspaceId, string configId);
    void SaveConfig(ConfigData config);
    /// <summary>仅从指定工作空间桶移除配置节点（不删除版本历史；用于移动配置）。</summary>
    void RemoveConfig(string workspaceId, string configId);
    void DeleteConfig(string workspaceId, string configId);

    IReadOnlyList<VersionSnapshot> ListVersions(string workspaceId, string configId);
    VersionSnapshot? GetVersion(string workspaceId, string configId, string versionId);
    void SaveVersion(VersionSnapshot version);
    void DeleteVersion(string workspaceId, string configId, string versionId);

    Dictionary<string, object?> LoadSettings();
    void SaveSettings(Dictionary<string, object?> settings);
}
