using Ferry.Core.Models;
using Ferry.Core.Ports;
using Ferry.Core.Services.Session;
using Ferry.Core.Services.Session.Protocol;

namespace Ferry.Core.Services;

/// <summary>
/// 工作空间用例层：工作空间 → 配置 → 版本 三层模型的操作入口。
/// 配置存档以 SourceText（源码）为权威；打开配置 = 源码 → 解析 → 表单（M4 前
/// layout/ini 无解析能力，Values/Enabled 缓存保留在存档中）。
/// </summary>
public sealed class WorkspaceService
{
    private readonly IWorkspaceStore _store;

    public WorkspaceService(IWorkspaceStore store)
    {
        _store = store;
    }

    public WorkspaceInfo CreateWorkspace(string name)
    {
        var now = DateTimeOffset.Now;
        var workspace = new WorkspaceInfo(NewId(), name, now, now);
        _store.SaveWorkspace(workspace);
        return workspace;
    }

    public WorkspaceInfo RenameWorkspace(string workspaceId, string name)
    {
        var workspace = _store.GetWorkspace(workspaceId)
            ?? throw new InvalidOperationException($"工作空间不存在：{workspaceId}");
        var renamed = workspace with
        {
            Name = name,
            UpdatedAt = DateTimeOffset.Now
        };
        _store.SaveWorkspace(renamed);
        return renamed;
    }

    public void DeleteWorkspace(string workspaceId) => _store.DeleteWorkspace(workspaceId);
    public IReadOnlyList<WorkspaceInfo> ListWorkspaces() => _store.ListWorkspaces();

    /// <summary>
    /// 新建配置：名字默认取插件默认文件名；可传入初始源码或 values/enabled 缓存。
    /// </summary>
    public ConfigData CreateConfig(
        string workspaceId,
        PluginDescriptor plugin,
        string? name = null,
        string? sourceText = null,
        Dictionary<string, object?>? values = null,
        Dictionary<string, bool>? enabled = null)
    {
        var config = new ConfigData
        {
            Id = NewId(),
            WorkspaceId = workspaceId,
            Name = string.IsNullOrWhiteSpace(name) ? plugin.DefaultFileName : name,
            PluginKey = plugin.PluginKey,
            PluginVersion = plugin.Version,
            SourceText = sourceText ?? string.Empty,
            Values = values ?? new Dictionary<string, object?>(),
            Enabled = enabled ?? new Dictionary<string, bool>()
        };
        _store.SaveConfig(config);
        return config;
    }

    public ConfigData? LoadConfig(string workspaceId, string configId)
        => _store.LoadConfig(workspaceId, configId);

    public void SaveConfig(ConfigData config) => _store.SaveConfig(config);

    public void DeleteConfig(string workspaceId, string configId)
        => _store.DeleteConfig(workspaceId, configId);

    public IReadOnlyList<ConfigInfo> ListConfigs(string workspaceId)
        => _store.ListConfigs(workspaceId);

    /// <summary>留档：把当前配置源码保存为版本快照，成为当前版本。</summary>
    public VersionSnapshot SnapshotVersion(ConfigData config, string? note = null)
    {
        var snapshot = new VersionSnapshot(
            NewId(),
            config.Id,
            config.SourceText,
            DateTimeOffset.Now,
            note);
        _store.SaveVersion(snapshot);
        return snapshot;
    }

    /// <summary>回滚：把版本快照的源码写回配置（表单缓存由打开配置时重新解析派生）。</summary>
    public ConfigData RestoreVersion(string workspaceId, string configId, string versionId)
    {
        var version = _store.GetVersion(workspaceId, configId, versionId)
            ?? throw new InvalidOperationException($"版本不存在：{versionId}");
        var config = _store.LoadConfig(workspaceId, configId)
            ?? throw new InvalidOperationException($"配置不存在：{configId}");

        config.SourceText = version.SourceText;
        config.Values.Clear();
        config.Enabled.Clear();
        config.VersionId = version.Id;
        _store.SaveConfig(config);
        return config;
    }

    public IReadOnlyList<VersionSnapshot> ListVersions(string workspaceId, string configId)
        => _store.ListVersions(workspaceId, configId);

    public VersionSnapshot? GetVersion(string workspaceId, string configId, string versionId)
        => _store.GetVersion(workspaceId, configId, versionId);

    /// <summary>直接保存版本快照（存档导入等场景，保留原始 Id/时间）。</summary>
    public void SaveVersionSnapshot(VersionSnapshot snapshot) => _store.SaveVersion(snapshot);

    public void DeleteVersion(string workspaceId, string configId, string versionId)
        => _store.DeleteVersion(workspaceId, configId, versionId);

    /// <summary>
    /// 按 PluginKey 在已加载插件中匹配配置绑定的插件（插件缺失返回 null，
    /// UI 据此置灰并仅允许查看/导出源码）。
    /// </summary>
    public static PluginDescriptor? ResolvePlugin(
        IReadOnlyList<PluginDescriptor> plugins,
        ConfigData config)
        => plugins.FirstOrDefault(p => p.PluginKey == config.PluginKey);

    /// <summary>插件版本是否与配置记录不一致（字段可能有增减，打开时按字段 id 回填）。</summary>
    public static bool IsPluginVersionChanged(PluginDescriptor? plugin, ConfigData config)
        => plugin is not null && !string.IsNullOrEmpty(config.PluginVersion)
            && plugin.Version != config.PluginVersion;

    private static string NewId() => Guid.NewGuid().ToString("N");
}
