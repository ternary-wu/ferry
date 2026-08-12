using System.Text.Json.Nodes;
using Ferry.Core.Models;
using Ferry.Core.Ports;
using Ferry.Core.Services.Session;
using Ferry.Core.Services.Session.Protocol;

namespace Ferry.Core.Services;

/// <summary>旧（MVP）工作区一次性迁移结果。</summary>
public sealed record MigrationResult(
    int CreatedConfigs,
    List<string> MissingPlugins,
    string? SkippedReason);

/// <summary>
/// 旧工作区一次性迁移：读取 ferry-mvp 的 workspace.json（只读），
/// 按插件归类迁入 v2 默认工作空间。已加载插件用渲染结果生成源码（权威），
/// 未加载插件保留 values/enabled 缓存并标记缺失。
/// </summary>
public sealed class LegacyWorkspaceMigrator
{
    private static readonly Dictionary<string, string> PluginKeyAliases = new()
    {
        ["Nginx-test"] = "Nginx"
    };

    private readonly WorkspaceService _service;
    private readonly IReadOnlyList<PluginDescriptor> _plugins;

    public LegacyWorkspaceMigrator(WorkspaceService service, IReadOnlyList<PluginDescriptor> plugins)
    {
        _service = service;
        _plugins = plugins;
    }

    public MigrationResult Migrate(string legacyFilePath)
    {
        if (!File.Exists(legacyFilePath))
        {
            return new MigrationResult(0, new List<string>(), "旧工作区文件不存在，跳过迁移");
        }

        var root = JsonNode.Parse(File.ReadAllText(legacyFilePath)) as JsonObject;
        var pluginsNode = root?["plugins"] as JsonObject;
        if (pluginsNode is null || pluginsNode.Count == 0)
        {
            return new MigrationResult(0, new List<string>(), "旧工作区为空，跳过迁移");
        }

        var project = FindOrCreateDefaultProject();
        var missing = new List<string>();
        var created = 0;

        foreach (var kv in pluginsNode)
        {
            var legacyKey = kv.Key;
            var resolvedKey = PluginKeyAliases.GetValueOrDefault(legacyKey, legacyKey);
            var entry = kv.Value as JsonObject;
            var values = entry?["values"] is JsonObject valuesNode
                ? ConfigImporter.FromJsonObject(valuesNode)
                : new Dictionary<string, object?>();
            var enabled = ReadBoolMap(entry?["enabled"]) is { Count: > 0 } enabledMap
                ? enabledMap
                : ReadBoolMap(entry?["modules"]);

            var plugin = _plugins.FirstOrDefault(p => p.PluginKey == resolvedKey);
            var sourceText = string.Empty;
            if (plugin?.Schema is not null)
            {
                var session = FormSession.Create(
                    plugin,
                    new ConfigState { Values = values, Enabled = enabled });
                sourceText = session.Render();
            }
            else
            {
                missing.Add(resolvedKey);
            }

            var stub = plugin ?? new PluginDescriptor
            {
                Name = resolvedKey,
                Version = string.Empty,
                PluginFolderPath = Path.Combine("Plugins", resolvedKey),
                RendererConfig = new PluginRendererConfig()
            };

            _service.CreateConfig(
                project.Id,
                string.Empty,
                stub,
                name: plugin?.DefaultFileName ?? $"{resolvedKey}.conf",
                sourceText: sourceText,
                values: values,
                enabled: enabled);
            created++;
        }

        return new MigrationResult(created, missing, null);
    }

    private ProjectInfo FindOrCreateDefaultProject()
    {
        const string defaultName = "默认项目";
        return _service.ListProjects().FirstOrDefault(p => p.Name == defaultName)
            ?? _service.CreateProject(defaultName);
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
}
