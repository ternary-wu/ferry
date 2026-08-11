using Ferry.Core.Infrastructure;
using Ferry.Core.Models;
using Ferry.Core.Ports;
using Ferry.Core.Services.Rendering;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ferry.Core.Services;

/// <summary>
/// 本地目录插件源：扫描 plugin.yaml + schema.yaml + templates.yaml 三文件结构。
/// 优先 templates.yaml，兼容回退 schema.yaml 旧 presets。
/// 单个插件解析失败返回带 LoadErrors 的描述符，不中断整体加载。
/// </summary>
public sealed class DirectoryPluginSource : IPluginSource
{
    private readonly string _pluginRootPath;

    public DirectoryPluginSource(string pluginRootPath)
    {
        _pluginRootPath = pluginRootPath;
    }

    public IReadOnlyList<PluginDescriptor> LoadAllPlugins()
    {
        var plugins = new List<PluginDescriptor>();
        if (!Directory.Exists(_pluginRootPath))
        {
            return plugins;
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        foreach (var pluginDir in Directory.GetDirectories(_pluginRootPath))
        {
            plugins.Add(LoadPlugin(pluginDir, deserializer));
        }

        return plugins;
    }

    private static PluginDescriptor LoadPlugin(string pluginDir, IDeserializer deserializer)
    {
        var dirName = new DirectoryInfo(pluginDir).Name;
        var descriptor = new PluginDescriptor { PluginFolderPath = pluginDir };

        try
        {
            var yamlFile = Directory.GetFiles(pluginDir, "*.yaml")
                .FirstOrDefault(f =>
                    Path.GetFileName(f).Equals("plugin.yaml", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(f).Equals($"{dirName}.yaml", StringComparison.OrdinalIgnoreCase));

            if (yamlFile is null)
            {
                descriptor.LoadErrors.Add("缺少 plugin.yaml 元数据文件");
                return descriptor;
            }

            var meta = deserializer.Deserialize<PluginMetadata>(File.ReadAllText(yamlFile));

            var schemaFile = Path.Combine(pluginDir, "schema.yaml");
            ConfigSchema? schema = null;
            if (File.Exists(schemaFile))
            {
                schema = deserializer.Deserialize<ConfigSchema>(File.ReadAllText(schemaFile));
            }

            var templates = LoadTemplates(pluginDir, schema, deserializer);

            descriptor.Name = meta.Name ?? dirName;
            descriptor.Version = meta.Version ?? "1.0";
            descriptor.Author = meta.Author ?? "unknown";
            descriptor.Description = meta.Description ?? string.Empty;
            descriptor.Schema = schema;
            descriptor.RendererConfig = meta.Renderer ?? new PluginRendererConfig();
            descriptor.TargetInfo = meta.Target;
            descriptor.Templates = templates;

            NormalizePresetValues(templates);
            ValidateRendererConfig(descriptor);
        }
        catch (Exception ex)
        {
            FerryLog.Error($"加载插件失败：{dirName}（{pluginDir}）", ex);
            descriptor.LoadErrors.Add(ex.Message);
        }

        return descriptor;
    }

    private static List<ConfigPreset> LoadTemplates(
        string pluginDir,
        ConfigSchema? schema,
        IDeserializer deserializer)
    {
        var templatesFile = Path.Combine(pluginDir, "templates.yaml");
        if (File.Exists(templatesFile))
        {
            var root = deserializer.Deserialize<TemplatesFile>(File.ReadAllText(templatesFile));
            return root.Templates ?? new List<ConfigPreset>();
        }
        return schema?.Presets ?? new List<ConfigPreset>();
    }

    /// <summary>
    /// 加载时提前校验渲染器配置（layout 占位符、不支持的渲染器类型等），
    /// 让错误在界面直接可见，而不是等到生成时才暴露。
    /// </summary>
    private static void ValidateRendererConfig(PluginDescriptor descriptor)
    {
        try
        {
            _ = RendererFactory.Create(descriptor);
        }
        catch (Exception ex)
        {
            FerryLog.Error($"插件 {descriptor.Name} 渲染器配置无效", ex);
            descriptor.LoadErrors.Add(ex.Message);
        }
    }

    /// <summary>
    /// 模板/预设的 values 经 YamlDotNet 反序列化后可能是混合类型字典，
    /// 统一归一化为值树，保证后续直接可用。
    /// </summary>
    private static void NormalizePresetValues(List<ConfigPreset> templates)
    {
        foreach (var preset in templates)
        {
            if (preset.Values is null)
            {
                continue;
            }
            var normalized = ConfigImporter.NormalizeTree(preset.Values) as Dictionary<string, object?>;
            preset.Values = normalized ?? new Dictionary<string, object?>();
        }
    }

    private sealed class PluginMetadata
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }
        public PluginRendererConfig? Renderer { get; set; }
        public PluginTargetInfo? Target { get; set; }
    }

    private sealed class TemplatesFile
    {
        public List<ConfigPreset>? Templates { get; set; }
    }
}
