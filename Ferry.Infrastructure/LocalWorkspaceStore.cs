using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ferry.Core.Ports;
using Ferry.Core.Services;

namespace Ferry.Infrastructure;

/// <summary>
/// 工作空间本地 JSON 存储（v2 契约实现）：工作空间 → 配置 → 版本三层，
/// 单文件存储，接口不变的前提下后续可换 SQLite/服务端。
/// 只有显式 Delete 才删除数据；写操作线程安全。
/// </summary>
public sealed class LocalWorkspaceStore : IWorkspaceStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly object _sync = new();

    public string FilePath { get; }

    public LocalWorkspaceStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ferry",
            "v2",
            "workspace.json");
    }

    public IReadOnlyList<ProjectInfo> ListProjects()
    {
        lock (_sync)
        {
            var array = LoadRoot()["projects"] as JsonArray ?? new JsonArray();
            return array.Select(n => ParseProject(n!)).ToList();
        }
    }

    public ProjectInfo? GetProject(string projectId)
    {
        lock (_sync)
        {
            var array = LoadRoot()["projects"] as JsonArray ?? new JsonArray();
            return array
                .Select(n => ParseProject(n!))
                .FirstOrDefault(p => p.Id == projectId);
        }
    }

    public void SaveProject(ProjectInfo project)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var array = root["projects"] as JsonArray ?? new JsonArray();
            var existing = array.FirstOrDefault(n => n?["id"]?.GetValue<string>() == project.Id);
            if (existing is not null)
            {
                array.Remove(existing);
            }
            array.Add(ToProjectNode(project));
            root["projects"] = array;
            Save(root);
        }
    }

    public void DeleteProject(string projectId)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var projects = root["projects"] as JsonArray ?? new JsonArray();
            var existing = projects.FirstOrDefault(n => n?["id"]?.GetValue<string>() == projectId);
            if (existing is not null)
            {
                projects.Remove(existing);
            }
            root["projects"] = projects;

            // 级联删除该项目下的工作空间、配置与版本
            var workspaces = root["workspaces"] as JsonArray;
            var configs = root["configs"] as JsonObject;
            var versions = root["versions"] as JsonObject;
            if (workspaces is not null)
            {
                var toRemove = workspaces
                    .Where(n => n?["projectId"]?.GetValue<string>() == projectId)
                    .ToList();
                foreach (var ws in toRemove)
                {
                    var wsId = ws!["id"]!.GetValue<string>();
                    workspaces.Remove(ws);
                    (root["configOrder"] as JsonObject)?.Remove(wsId);
                    if (configs is not null && configs[wsId] is JsonArray configArray)
                    {
                        foreach (var configNode in configArray)
                        {
                            var configId = configNode?["id"]?.GetValue<string>();
                            if (configId is not null && versions is not null)
                            {
                                versions.Remove(configId);
                            }
                        }
                        configs.Remove(wsId);
                    }
                }
            }
            (root["workspaceOrder"] as JsonObject)?.Remove(projectId);
            Save(root);
        }
    }

    public IReadOnlyList<WorkspaceInfo> ListWorkspaces()
    {
        lock (_sync)
        {
            var array = LoadRoot()["workspaces"] as JsonArray ?? new JsonArray();
            return array.Select(n => ParseWorkspace(n!)).ToList();
        }
    }

    public WorkspaceInfo? GetWorkspace(string workspaceId)
    {
        lock (_sync)
        {
            var array = LoadRoot()["workspaces"] as JsonArray ?? new JsonArray();
            return array
                .Select(n => ParseWorkspace(n!))
                .FirstOrDefault(w => w.Id == workspaceId);
        }
    }

    public void SaveWorkspace(WorkspaceInfo workspace)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var array = root["workspaces"] as JsonArray ?? new JsonArray();
            var existing = array.FirstOrDefault(n => n?["id"]?.GetValue<string>() == workspace.Id);
            var isNew = existing is null;
            if (existing is not null)
            {
                array.Remove(existing);
            }
            array.Add(ToWorkspaceNode(workspace));
            root["workspaces"] = array;
            if (isNew)
            {
                EnsureWorkspaceOrderEntry(root, workspace.ProjectId, workspace.Id);
            }
            Save(root);
        }
    }

    public void DeleteWorkspace(string workspaceId)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var array = root["workspaces"] as JsonArray ?? new JsonArray();
            var existing = array.FirstOrDefault(n => n?["id"]?.GetValue<string>() == workspaceId);
            var projectId = existing?["projectId"]?.GetValue<string>();
            if (existing is not null)
            {
                array.Remove(existing);
            }
            root["workspaces"] = array;
            if (projectId is not null)
            {
                RemoveWorkspaceOrderEntry(root, projectId, workspaceId);
            }

            // 级联删除该工作空间下的配置及其版本。
            var configs = root["configs"] as JsonObject;
            if (configs is not null && configs[workspaceId] is JsonArray configArray)
            {
                var versions = root["versions"] as JsonObject;
                foreach (var configNode in configArray)
                {
                    var configId = configNode?["id"]?.GetValue<string>();
                    if (configId is not null && versions is not null)
                    {
                        versions.Remove(configId);
                    }
                }
                configs.Remove(workspaceId);
            }
            (root["configOrder"] as JsonObject)?.Remove(workspaceId);
            Save(root);
        }
    }

    public IReadOnlyList<ConfigInfo> ListConfigs(string workspaceId)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var configs = root["configs"] as JsonObject;
            var array = configs?[workspaceId] as JsonArray ?? new JsonArray();
            return array.Select(n => ParseConfigInfo(n!)).ToList();
        }
    }

    public ConfigData? LoadConfig(string workspaceId, string configId)
    {
        lock (_sync)
        {
            return FindConfigNode(LoadRoot(), workspaceId, configId) is { } node
                ? ParseConfigData(node)
                : null;
        }
    }

    public void SaveConfig(ConfigData config)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var configs = root["configs"] as JsonObject ?? new JsonObject();
            var array = configs[config.WorkspaceId] as JsonArray ?? new JsonArray();
            var existing = array.FirstOrDefault(n => n?["id"]?.GetValue<string>() == config.Id);
            if (existing is not null)
            {
                array.Remove(existing);
            }
            array.Add(ToConfigNode(config));
            configs[config.WorkspaceId] = array;
            root["configs"] = configs;
            EnsureConfigOrderEntry(root, config.WorkspaceId, config.Id);
            Save(root);
        }
    }

    public void DeleteConfig(string workspaceId, string configId)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var configs = root["configs"] as JsonObject;
            var array = configs?[workspaceId] as JsonArray;
            var existing = array?.FirstOrDefault(n => n?["id"]?.GetValue<string>() == configId);
            if (existing is not null)
            {
                array!.Remove(existing);
            }
            (root["versions"] as JsonObject)?.Remove(configId);
            RemoveConfigOrderEntry(root, workspaceId, configId);
            Save(root);
        }
    }

    public void RemoveConfig(string workspaceId, string configId)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var configs = root["configs"] as JsonObject;
            var array = configs?[workspaceId] as JsonArray;
            var existing = array?.FirstOrDefault(n => n?["id"]?.GetValue<string>() == configId);
            if (existing is not null)
            {
                array!.Remove(existing);
            }
            RemoveConfigOrderEntry(root, workspaceId, configId);
            Save(root);
        }
    }

    public IReadOnlyList<VersionSnapshot> ListVersions(string workspaceId, string configId)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var versions = root["versions"] as JsonObject;
            var array = versions?[configId] as JsonArray ?? new JsonArray();
            return array.Select(n => ParseVersion(n!)).ToList();
        }
    }

    public VersionSnapshot? GetVersion(string workspaceId, string configId, string versionId)
    {
        lock (_sync)
        {
            var versions = LoadRoot()["versions"] as JsonObject;
            var array = versions?[configId] as JsonArray ?? new JsonArray();
            return array
                .Select(n => ParseVersion(n!))
                .FirstOrDefault(v => v.Id == versionId);
        }
    }

    public void SaveVersion(VersionSnapshot version)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var versions = root["versions"] as JsonObject ?? new JsonObject();
            var array = versions[version.ConfigId] as JsonArray ?? new JsonArray();
            var existing = array.FirstOrDefault(n => n?["id"]?.GetValue<string>() == version.Id);
            if (existing is not null)
            {
                array.Remove(existing);
            }
            array.Add(ToVersionNode(version));
            versions[version.ConfigId] = array;
            root["versions"] = versions;

            // 留档即成为当前版本
            if (FindConfigNode(root, version.ConfigId, out _) is { } configNode)
            {
                configNode["currentVersionId"] = version.Id;
                configNode["updatedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            }
            Save(root);
        }
    }

    public void DeleteVersion(string workspaceId, string configId, string versionId)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var versions = root["versions"] as JsonObject;
            var array = versions?[configId] as JsonArray;
            var existing = array?.FirstOrDefault(n => n?["id"]?.GetValue<string>() == versionId);
            if (existing is not null)
            {
                array!.Remove(existing);
            }
            Save(root);
        }
    }

    public IReadOnlyList<string> GetConfigOrder(string workspaceId)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var map = root["configOrder"] as JsonObject;
            var array = map?[workspaceId] as JsonArray ?? new JsonArray();
            return array
                .Select(n => n?.GetValue<string>() ?? string.Empty)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }
    }

    public void SaveConfigOrder(string workspaceId, IReadOnlyList<string> configIds)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var map = root["configOrder"] as JsonObject ?? new JsonObject();
            map[workspaceId] = new JsonArray(
                configIds.Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => (JsonNode)s)
                    .ToArray());
            root["configOrder"] = map;
            Save(root);
        }
    }

    public IReadOnlyList<string> GetWorkspaceOrder(string projectId)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var map = root["workspaceOrder"] as JsonObject;
            var array = map?[projectId] as JsonArray ?? new JsonArray();
            return array
                .Select(n => n?.GetValue<string>() ?? string.Empty)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }
    }

    public void SaveWorkspaceOrder(string projectId, IReadOnlyList<string> workspaceIds)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var map = root["workspaceOrder"] as JsonObject ?? new JsonObject();
            map[projectId] = new JsonArray(
                workspaceIds.Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => (JsonNode)s)
                    .ToArray());
            root["workspaceOrder"] = map;
            Save(root);
        }
    }

    public Dictionary<string, object?> LoadSettings()
    {
        lock (_sync)
        {
            var root = LoadRoot();
            return root["settings"] is JsonObject settings
                ? ConfigImporter.FromJsonObject(settings)
                : new Dictionary<string, object?>();
        }
    }

    public void SaveSettings(Dictionary<string, object?> settings)
    {
        lock (_sync)
        {
            var root = LoadRoot();
            var node = root["settings"] as JsonObject ?? new JsonObject();
            foreach (var kv in settings)
            {
                if (kv.Value is null)
                {
                    node.Remove(kv.Key);
                }
                else
                {
                    node[kv.Key] = JsonSerializer.SerializeToNode(kv.Value);
                }
            }
            root["settings"] = node;
            Save(root);
        }
    }

    // ---------- 内部：JSON 模型 ----------

    private JsonObject LoadRoot()
    {
        if (!File.Exists(FilePath))
        {
            return new JsonObject();
        }
        try
        {
            return JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private void Save(JsonObject root)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(FilePath, root.ToJsonString(WriteOptions));
    }

    private static JsonObject ToWorkspaceNode(WorkspaceInfo workspace) => new()
    {
        ["id"] = workspace.Id,
        ["projectId"] = workspace.ProjectId,
        ["name"] = workspace.Name,
        ["createdAt"] = workspace.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        ["updatedAt"] = workspace.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)
    };

    private static JsonObject ToProjectNode(ProjectInfo project) => new()
    {
        ["id"] = project.Id,
        ["name"] = project.Name,
        ["createdAt"] = project.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        ["updatedAt"] = project.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)
    };

    private static WorkspaceInfo ParseWorkspace(JsonNode node)
    {
        var o = node.AsObject();
        return new WorkspaceInfo(
            o["id"]!.GetValue<string>(),
            o["projectId"]?.GetValue<string>() ?? string.Empty,
            o["name"]!.GetValue<string>(),
            ParseDate(o["createdAt"]),
            ParseDate(o["updatedAt"]));
    }

    private static ProjectInfo ParseProject(JsonNode node)
    {
        var o = node.AsObject();
        return new ProjectInfo(
            o["id"]!.GetValue<string>(),
            o["name"]!.GetValue<string>(),
            ParseDate(o["createdAt"]),
            ParseDate(o["updatedAt"]));
    }

    private static JsonObject ToConfigNode(ConfigData config) => new()
    {
        ["id"] = config.Id,
        ["projectId"] = config.ProjectId,
        ["workspaceId"] = config.WorkspaceId,
        ["name"] = config.Name,
        ["pluginKey"] = config.PluginKey,
        ["pluginVersion"] = config.PluginVersion,
        ["updatedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
        ["currentVersionId"] = config.VersionId,
        ["sourceText"] = config.SourceText,
        ["values"] = JsonSerializer.SerializeToNode(config.Values),
        ["enabled"] = JsonSerializer.SerializeToNode(config.Enabled),
        ["unrecognized"] = JsonSerializer.SerializeToNode(config.Unrecognized)
    };

    private static ConfigInfo ParseConfigInfo(JsonNode node)
    {
        var o = node.AsObject();
        return new ConfigInfo(
            o["id"]!.GetValue<string>(),
            o["workspaceId"]!.GetValue<string>(),
            o["name"]!.GetValue<string>(),
            o["pluginKey"]!.GetValue<string>(),
            o["pluginVersion"]!.GetValue<string>(),
            ParseDate(o["updatedAt"]),
            o["currentVersionId"]?.GetValue<string>());
    }

    private static ConfigData ParseConfigData(JsonNode node)
    {
        var o = node.AsObject();
        return new ConfigData
        {
            Id = o["id"]!.GetValue<string>(),
            ProjectId = o["projectId"]?.GetValue<string>() ?? string.Empty,
            WorkspaceId = o["workspaceId"]!.GetValue<string>(),
            Name = o["name"]!.GetValue<string>(),
            PluginKey = o["pluginKey"]!.GetValue<string>(),
            PluginVersion = o["pluginVersion"]!.GetValue<string>(),
            SourceText = o["sourceText"]?.GetValue<string>() ?? string.Empty,
            Values = o["values"] is JsonObject values
                ? ConfigImporter.FromJsonObject(values)
                : new Dictionary<string, object?>(),
            Enabled = ReadBoolMap(o["enabled"]),
            Unrecognized = ReadStringList(o["unrecognized"]),
            VersionId = o["currentVersionId"]?.GetValue<string>()
        };
    }

    private static JsonObject ToVersionNode(VersionSnapshot version) => new()
    {
        ["id"] = version.Id,
        ["configId"] = version.ConfigId,
        ["sourceText"] = version.SourceText,
        ["timestamp"] = version.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        ["note"] = version.Note
    };

    private static VersionSnapshot ParseVersion(JsonNode node)
    {
        var o = node.AsObject();
        return new VersionSnapshot(
            o["id"]!.GetValue<string>(),
            o["configId"]!.GetValue<string>(),
            o["sourceText"]?.GetValue<string>() ?? string.Empty,
            ParseDate(o["timestamp"]),
            o["note"]?.GetValue<string>());
    }

    private static DateTimeOffset ParseDate(JsonNode? node)
        => DateTimeOffset.Parse(
            node?.GetValue<string>() ?? DateTimeOffset.MinValue.ToString("O"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static Dictionary<string, bool> ReadBoolMap(JsonNode? node)
    {
        var map = new Dictionary<string, bool>();
        if (node is JsonObject obj)
        {
            foreach (var kv in obj)
            {
                if (kv.Value is JsonValue v && v.TryGetValue<bool>(out var value))
                {
                    map[kv.Key] = value;
                }
            }
        }
        return map;
    }

    private static List<string> ReadStringList(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            return array
                .Select(n => n?.GetValue<string>() ?? string.Empty)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }
        return new List<string>();
    }

    private static JsonNode? FindConfigNode(JsonObject root, string workspaceId, string configId)
    {
        var configs = root["configs"] as JsonObject;
        var array = configs?[workspaceId] as JsonArray;
        return array?.FirstOrDefault(n => n?["id"]?.GetValue<string>() == configId);
    }

    private static JsonNode? FindConfigNode(JsonObject root, string configId, out string workspaceId)
    {
        var configs = root["configs"] as JsonObject;
        if (configs is not null)
        {
            foreach (var kv in configs)
            {
                var array = kv.Value as JsonArray;
                var match = array?.FirstOrDefault(n => n?["id"]?.GetValue<string>() == configId);
                if (match is not null)
                {
                    workspaceId = kv.Key;
                    return match;
                }
            }
        }
        workspaceId = string.Empty;
        return null;
    }

    private static void EnsureConfigOrderEntry(JsonObject root, string workspaceId, string configId)
    {
        var map = root["configOrder"] as JsonObject ?? new JsonObject();
        var array = map[workspaceId] as JsonArray ?? new JsonArray();
        if (array.Any(n => n?.GetValue<string>() == configId))
        {
            return;
        }
        array.Add((JsonNode)configId);
        map[workspaceId] = array;
        root["configOrder"] = map;
    }

    private static void RemoveConfigOrderEntry(JsonObject root, string workspaceId, string configId)
    {
        if (root["configOrder"] is not JsonObject map)
        {
            return;
        }
        if (map[workspaceId] is not JsonArray array)
        {
            return;
        }
        var existing = array.FirstOrDefault(n => n?.GetValue<string>() == configId);
        if (existing is not null)
        {
            array.Remove(existing);
        }
        map[workspaceId] = array;
    }

    private static void EnsureWorkspaceOrderEntry(JsonObject root, string projectId, string workspaceId)
    {
        var map = root["workspaceOrder"] as JsonObject ?? new JsonObject();
        var array = map[projectId] as JsonArray ?? new JsonArray();
        if (array.Any(n => n?.GetValue<string>() == workspaceId))
        {
            return;
        }
        array.Add((JsonNode)workspaceId);
        map[projectId] = array;
        root["workspaceOrder"] = map;
    }

    private static void RemoveWorkspaceOrderEntry(JsonObject root, string projectId, string workspaceId)
    {
        if (root["workspaceOrder"] is not JsonObject map)
        {
            return;
        }
        if (map[projectId] is not JsonArray array)
        {
            return;
        }
        var existing = array.FirstOrDefault(n => n?.GetValue<string>() == workspaceId);
        if (existing is not null)
        {
            array.Remove(existing);
        }
        map[projectId] = array;
    }
}
