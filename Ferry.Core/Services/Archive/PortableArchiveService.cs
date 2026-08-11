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
        var workspace = _service.ListWorkspaces().FirstOrDefault(w => w.Id == workspaceId)
            ?? throw new InvalidOperationException($"工作空间不存在：{workspaceId}");
        var configs = _service.ListConfigs(workspaceId);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        WriteManifest(zip, workspace, configs);
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

    /// <summary>导出单个配置（含版本历史与所用插件定义）。</summary>
    public void ExportConfig(string workspaceId, string configId, string zipPath)
    {
        var workspace = _service.ListWorkspaces().FirstOrDefault(w => w.Id == workspaceId)
            ?? throw new InvalidOperationException($"工作空间不存在：{workspaceId}");
        var config = _service.LoadConfig(workspaceId, configId)
            ?? throw new InvalidOperationException($"配置不存在：{configId}");

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        WriteManifest(zip, workspace, _service.ListConfigs(workspaceId).Where(c => c.Id == configId).ToList());
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
            var workspaceName = manifest?["workspaceName"]?.GetValue<string>() ?? "导入的工作空间";
            var workspace = _service.ListWorkspaces().FirstOrDefault(w => w.Name == workspaceName)
                ?? _service.CreateWorkspace(workspaceName);
            result.WorkspaceId = workspace.Id;

            var entries = manifest?["entries"] as JsonArray ?? new JsonArray();
            foreach (var entryNode in entries)
            {
                var configId = entryNode?["configId"]?.GetValue<string>();
                if (string.IsNullOrEmpty(configId))
                {
                    continue;
                }
                if (_service.LoadConfig(workspace.Id, configId) is not null)
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
                    workspace.Id,
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
                result.ImportedConfigs++;
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
        WorkspaceInfo workspace,
        IReadOnlyList<ConfigInfo> configs)
    {
        var entries = configs
            .Select(info => (JsonNode)new JsonObject
            {
                ["configId"] = info.Id,
                ["configName"] = info.Name,
                ["pluginKey"] = info.PluginKey,
                ["pluginVersion"] = info.PluginVersion
            })
            .ToArray();
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["exportedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            ["workspaceId"] = workspace.Id,
            ["workspaceName"] = workspace.Name,
            ["entries"] = new JsonArray(entries)
        };
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

}
