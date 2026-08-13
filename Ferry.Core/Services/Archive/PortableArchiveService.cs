using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ferry.Core.Models;
using Ferry.Core.Ports;

namespace Ferry.Core.Services.Archive;

/// <summary>存档包导入结果。</summary>
public sealed class ArchiveImportResult
{
    public string? WorkspaceId { get; set; }
    public int ImportedConfigs { get; set; }
    public int SkippedConfigs { get; set; }
    public List<string> LocalPlugins { get; init; } = new();
    public List<string> PackagedPlugins { get; init; } = new();
    public List<string> MissingPlugins { get; init; } = new();
}

/// <summary>
/// 可移植存档包（M5）：zip 容器，内含配置数据（源码为权威 + 缓存 + 版本历史）
/// 与所用插件定义（plugin.yaml / schema.yaml / templates.yaml）。
/// 导入时本机同 key 插件优先；无插件时从包内只读加载或仅保留源码查看/导出；
/// 显式 InstallPlugin 才写入本机插件目录（包内插件按不可信外部代码处理）。
/// </summary>
public sealed class PortableArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] PluginFiles = ["plugin.yaml", "schema.yaml", "templates.yaml"];

    private readonly WorkspaceService _service;
    private readonly IReadOnlyList<PluginDescriptor> _plugins;

    public PortableArchiveService(WorkspaceService service, IReadOnlyList<PluginDescriptor> plugins)
    {
        _service = service;
        _plugins = plugins;
    }

    /// <summary>导出整个工作空间（含全部配置、版本历史与所用插件定义）。</summary>
    public void ExportWorkspace(string workspaceId, string zipPath)
    {
        var workspace = ResolveWorkspace(workspaceId, "未归类配置");
        var project = _service.GetProject(workspace.ProjectId);
        var configs = _service.ListConfigs(workspaceId);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        WriteManifest(
            zip,
            project,
            workspace,
            configs,
            _service.ListConfigs(workspaceId).Select(c => c.Id).ToArray());
        foreach (var info in configs)
        {
            var config = _service.LoadConfig(workspaceId, info.Id);
            if (config is not null)
            {
                WriteConfig(zip, workspace, config);
                WritePlugin(zip, WorkspaceService.ResolvePlugin(_plugins, config));
            }
        }
    }

    /// <summary>
    /// 导出整个项目：所有工作空间 + 未归类配置打成一个存档，
    /// manifest 条目携带各自工作空间名，导入时按工作空间名还原（与现有 Import 兼容）。
    /// </summary>
    public void ExportProject(string projectId, string zipPath)
    {
        var project = _service.GetProject(projectId)
            ?? throw new InvalidOperationException($"项目不存在：{projectId}");
        var workspaces = _service.ListWorkspaces(projectId);
        var unassignedWs = new WorkspaceInfo(
            string.Empty,
            projectId,
            "未归类配置",
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue);
        var entries = new List<(WorkspaceInfo Workspace, ConfigInfo Info)>();
        foreach (var workspace in workspaces)
        {
            foreach (var info in _service.ListConfigs(workspace.Id))
            {
                entries.Add((workspace, info));
            }
        }
        foreach (var info in _service.ListUnassignedConfigs(projectId))
        {
            entries.Add((unassignedWs, info));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var manifestWorkspace = entries.Count > 0 ? entries[0].Workspace : unassignedWs;
        WriteManifest(
            zip,
            project,
            manifestWorkspace,
            entries.Select(e => e.Info).ToList(),
            entryWorkspaceNames: entries
                .Select(e => string.IsNullOrEmpty(e.Info.WorkspaceId) ? "未归类配置" : e.Workspace.Name)
                .ToList());
        foreach (var (workspace, info) in entries)
        {
            var config = _service.LoadConfig(info.WorkspaceId, info.Id);
            if (config is null)
            {
                continue;
            }
            WriteConfig(zip, workspace, config);
            WritePlugin(zip, WorkspaceService.ResolvePlugin(_plugins, config));
        }
    }

    /// <summary>导出单个配置（含版本历史与所用插件定义）。</summary>
    public void ExportConfig(string workspaceId, string configId, string zipPath)
    {
        var config = _service.LoadConfig(workspaceId, configId)
            ?? throw new InvalidOperationException($"配置不存在：{configId}");
        var workspace = ResolveWorkspace(workspaceId, config.WorkspaceId == string.Empty ? "未归类配置" : "工作空间");
        var project = _service.GetProject(workspace.ProjectId);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        WriteManifest(
            zip,
            project,
            workspace,
            _service.ListConfigs(workspaceId).Where(c => c.Id == configId).ToList());
        WriteConfig(zip, workspace, config);
        WritePlugin(zip, WorkspaceService.ResolvePlugin(_plugins, config));
    }

    /// <summary>
    /// 导入存档包：按清单在目标存储中创建/复用同名工作空间与配置。
    /// 本机插件优先；其次从包内只读提取；两者皆无时仍导入源码（可查看/导出）。
    /// </summary>
    public ArchiveImportResult Import(string zipPath)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ferry-archive-" + Guid.NewGuid().ToString("N"));
        var result = new ArchiveImportResult();
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var manifest = ReadManifest(zip);
            var projectName = manifest?["projectName"]?.GetValue<string>() ?? "导入的项目";
            var project = _service.ListProjects().FirstOrDefault(p => p.Name == projectName)
                ?? _service.CreateProject(projectName);

            var entries = manifest?["entries"] as JsonArray ?? new JsonArray();
            var configIdMap = new Dictionary<string, string>();
            foreach (var entryNode in entries)
            {
                var configId = entryNode?["configId"]?.GetValue<string>();
                var workspaceName = entryNode?["workspaceName"]?.GetValue<string>() ?? string.Empty;
                var workspaceId = string.Empty;
                if (!string.IsNullOrEmpty(workspaceName) && workspaceName != "未归类配置")
                {
                    workspaceId = _service.ListWorkspaces(project.Id)
                        .FirstOrDefault(w => w.Name == workspaceName)?.Id
                        ?? _service.CreateWorkspace(project.Id, workspaceName).Id;
                }
                result.WorkspaceId ??= workspaceId;
                if (string.IsNullOrEmpty(configId))
                {
                    continue;
                }
                if (_service.LoadConfig(workspaceId, configId) is not null)
                {
                    result.SkippedConfigs++;
                    continue;
                }

                var pluginKey = entryNode?["pluginKey"]?.GetValue<string>() ?? string.Empty;
                var pluginVersion = entryNode?["pluginVersion"]?.GetValue<string>() ?? string.Empty;
                var plugin = _plugins.FirstOrDefault(p => p.PluginKey == pluginKey);
                if (plugin is not null)
                {
                    AddOnce(result.LocalPlugins, pluginKey);
                }
                else
                {
                    plugin = LoadPackagedPlugin(zip, pluginKey, tempRoot);
                    if (plugin is not null)
                    {
                        AddOnce(result.PackagedPlugins, pluginKey);
                    }
                    else
                    {
                        AddOnce(result.MissingPlugins, pluginKey);
                    }
                }

                var stub = plugin ?? new PluginDescriptor
                {
                    Name = pluginKey,
                    Version = pluginVersion,
                    PluginFolderPath = Path.Combine("Plugins", pluginKey)
                };

                var configNode = ReadConfigEntry(zip, configId);
                if (configNode is null)
                {
                    result.SkippedConfigs++;
                    continue;
                }

                var config = _service.CreateConfig(
                    project.Id,
                    workspaceId,
                    stub,
                    name: configNode["name"]?.GetValue<string>() ?? stub.DefaultFileName,
                    sourceText: configNode["sourceText"]?.GetValue<string>() ?? string.Empty,
                    values: configNode["values"] is JsonObject valuesNode
                        ? ConfigImporter.FromJsonObject(valuesNode)
                        : new Dictionary<string, object?>(),
                    enabled: ReadBoolMap(configNode["enabled"]));
                config.Unrecognized = ReadStringList(configNode["unrecognized"]);
                _service.SaveConfig(config);

                VersionSnapshot? last = null;
                foreach (var version in ReadVersions(configNode["versions"], config.Id))
                {
                    _service.SaveVersionSnapshot(version);
                    last = version;
                }
                if (last is not null)
                {
                    config.VersionId = last.Id;
                    _service.SaveConfig(config);
                }
                configIdMap[configId] = config.Id;
                result.ImportedConfigs++;
            }

            var configOrder = manifest?["configOrder"] as JsonArray;
            if (configOrder is not null && result.WorkspaceId is not null)
            {
                var ordered = configOrder
                    .Select(n => n?.GetValue<string>() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Where(configIdMap.ContainsKey)
                    .Select(s => configIdMap[s])
                    .ToList();
                _service.ApplyConfigOrder(result.WorkspaceId, ordered);
            }

            return result;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响导入结果
            }
        }
    }

    /// <summary>
    /// 显式安装插件：把插件目录中的三文件复制到本机插件根目录（不自动调用）。
    /// 目标路径先校验，禁止越界。
    /// </summary>
    public static void InstallPlugin(PluginDescriptor plugin, string pluginRoot)
    {
        if (string.IsNullOrEmpty(plugin.PluginFolderPath) || !Directory.Exists(plugin.PluginFolderPath))
        {
            throw new InvalidOperationException("插件源目录不存在");
        }
        var rootFull = Path.GetFullPath(pluginRoot);
        var dest = Path.GetFullPath(Path.Combine(pluginRoot, plugin.PluginKey));
        if (!dest.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标路径越界");
        }
        Directory.CreateDirectory(dest);
        foreach (var name in PluginFiles)
        {
            var source = Path.Combine(plugin.PluginFolderPath, name);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(dest, name), overwrite: true);
            }
        }
    }

    // ---------- 写包 ----------

    private static void WriteManifest(
        ZipArchive zip,
        ProjectInfo? project,
        WorkspaceInfo workspace,
        IReadOnlyList<ConfigInfo> configs,
        IReadOnlyList<string>? configOrder = null,
        IReadOnlyList<string>? entryWorkspaceNames = null)
    {
        var entryNodes = new List<JsonNode>();
        for (var i = 0; i < configs.Count; i++)
        {
            var info = configs[i];
            var workspaceName = entryWorkspaceNames is not null
                ? entryWorkspaceNames[i]
                : string.IsNullOrEmpty(info.WorkspaceId) ? "未归类配置" : workspace.Name;
            entryNodes.Add(new JsonObject
            {
                ["configId"] = info.Id,
                ["configName"] = info.Name,
                ["workspaceName"] = workspaceName,
                ["pluginKey"] = info.PluginKey,
                ["pluginVersion"] = info.PluginVersion
            });
        }
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["exportedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            ["projectId"] = project?.Id,
            ["projectName"] = project?.Name ?? "默认项目",
            ["workspaceId"] = workspace.Id,
            ["workspaceName"] = workspace.Name,
            ["entries"] = new JsonArray(entryNodes.ToArray())
        };
        if (configOrder is not null)
        {
            manifest["configOrder"] = new JsonArray(
                configOrder.Select(id => (JsonNode)id).ToArray());
        }
        var entry = zip.CreateEntry("manifest.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(manifest.ToJsonString(JsonOptions));
    }

    private void WriteConfig(ZipArchive zip, WorkspaceInfo workspace, ConfigData config)
    {
        var versions = _service.ListVersions(workspace.Id, config.Id);
        var versionNodes = versions
            .Select(v => (JsonNode)new JsonObject
            {
                ["id"] = v.Id,
                ["sourceText"] = v.SourceText,
                ["timestamp"] = v.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                ["note"] = v.Note
            })
            .ToArray();
        var node = new JsonObject
        {
            ["configId"] = config.Id,
            ["workspaceId"] = config.WorkspaceId,
            ["workspaceName"] = workspace.Name,
            ["name"] = config.Name,
            ["pluginKey"] = config.PluginKey,
            ["pluginVersion"] = config.PluginVersion,
            ["sourceText"] = config.SourceText,
            ["values"] = JsonSerializer.SerializeToNode(config.Values),
            ["enabled"] = JsonSerializer.SerializeToNode(config.Enabled),
            ["unrecognized"] = JsonSerializer.SerializeToNode(config.Unrecognized),
            ["versions"] = new JsonArray(versionNodes)
        };
        var entry = zip.CreateEntry($"configs/{config.Id}.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(node.ToJsonString(JsonOptions));
    }

    private static void WritePlugin(ZipArchive zip, PluginDescriptor? plugin)
    {
        if (plugin is null || string.IsNullOrEmpty(plugin.PluginFolderPath) || !Directory.Exists(plugin.PluginFolderPath))
        {
            return;
        }
        foreach (var name in PluginFiles)
        {
            var file = Path.Combine(plugin.PluginFolderPath, name);
            if (!File.Exists(file))
            {
                continue;
            }
            var entry = zip.CreateEntry($"plugins/{plugin.PluginKey}/{name}");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(File.ReadAllText(file));
        }
    }

    // ---------- 读包 ----------

    private static JsonObject? ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry("manifest.json");
        if (entry is null)
        {
            return null;
        }
        using var reader = new StreamReader(entry.Open());
        return JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
    }

    private static JsonObject? ReadConfigEntry(ZipArchive zip, string configId)
    {
        var entry = zip.GetEntry($"configs/{configId}.json");
        if (entry is null)
        {
            return null;
        }
        using var reader = new StreamReader(entry.Open());
        return JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
    }

    private static PluginDescriptor? LoadPackagedPlugin(ZipArchive zip, string pluginKey, string tempRoot)
    {
        var dir = Path.Combine(tempRoot, pluginKey);
        Directory.CreateDirectory(dir);
        var wrote = false;
        foreach (var name in PluginFiles)
        {
            var entry = zip.GetEntry($"plugins/{pluginKey}/{name}");
            if (entry is null)
            {
                continue;
            }
            entry.ExtractToFile(Path.Combine(dir, name), overwrite: true);
            wrote = true;
        }
        if (!wrote)
        {
            return null;
        }
        return new DirectoryPluginSource(tempRoot)
            .LoadAllPlugins()
            .FirstOrDefault(p => p.PluginKey == pluginKey);
    }

    private static List<VersionSnapshot> ReadVersions(JsonNode? node, string configId)
    {
        var result = new List<VersionSnapshot>();
        if (node is not JsonArray array)
        {
            return result;
        }
        foreach (var item in array)
        {
            var obj = item as JsonObject;
            if (obj is null)
            {
                continue;
            }
            result.Add(new VersionSnapshot(
                obj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                configId,
                obj["sourceText"]?.GetValue<string>() ?? string.Empty,
                ParseDate(obj["timestamp"]),
                obj["note"]?.GetValue<string>()));
        }
        return result;
    }

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

    private static DateTimeOffset ParseDate(JsonNode? node)
        => DateTimeOffset.Parse(
            node?.GetValue<string>() ?? DateTimeOffset.MinValue.ToString("O"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }

    private WorkspaceInfo ResolveWorkspace(string workspaceId, string fallbackName)
        => _service.ListWorkspaces().FirstOrDefault(w => w.Id == workspaceId)
            ?? new WorkspaceInfo(workspaceId, string.Empty, fallbackName, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

}
