namespace Ferry.Core.Models;

/// <summary>插件渲染配置（plugin.yaml 的 renderer 段）。</summary>
public sealed class PluginRendererConfig
{
    public string Type { get; set; } = "layout";
    public string DefaultFileName { get; set; } = "config";
    public string OutputExtension { get; set; } = string.Empty;
    public PluginLayoutStyle Layout { get; set; } = new();
}

/// <summary>layout 全局默认样式（plugin.yaml 的 renderer.layout），字段级 render 可覆盖。</summary>
public sealed class PluginLayoutStyle
{
    public string Line { get; set; } = "{{ .key }} = {{ . }};";
    public string BlockOpen { get; set; } = "{{ .key }} {";
    public string BlockClose { get; set; } = "}";
    public string Indent { get; set; } = "    ";
}

/// <summary>插件适用的应用与应用版本范围（v2 仅展示与记录，字段级版本过滤留待后续）。</summary>
public sealed class PluginTargetInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// 已加载的插件描述（插件三文件的解析结果）。
/// PluginKey 为插件目录名，是工作区/存档中的稳定键。
/// </summary>
public sealed class PluginDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PluginFolderPath { get; set; } = string.Empty;
    public ConfigSchema? Schema { get; set; }
    public PluginRendererConfig RendererConfig { get; set; } = new();
    public List<ConfigPreset> Templates { get; set; } = new();
    public List<string> LoadErrors { get; } = new();
    public PluginTargetInfo? TargetInfo { get; set; }

    /// <summary>工作区持久化/存档使用的稳定键（插件目录名）。</summary>
    public string PluginKey
        => string.IsNullOrEmpty(PluginFolderPath)
            ? string.Empty
            : new DirectoryInfo(PluginFolderPath).Name;

    public string TargetName => TargetInfo?.Name ?? "unknown";
    public string TargetVersion => TargetInfo?.Version ?? string.Empty;
    public string RendererType => RendererConfig.Type.ToLowerInvariant();

    /// <summary>该插件输出是否支持导入回表单（M4 起四种渲染器均支持：json/yaml 精确导入，layout/ini 宽松解析）。</summary>
    public bool CanImport => true;

    /// <summary>默认导出文件名（补全扩展名）。</summary>
    public string DefaultFileName
    {
        get
        {
            var name = RendererConfig.DefaultFileName;
            var ext = RendererConfig.OutputExtension;
            if (string.IsNullOrEmpty(ext) || Path.HasExtension(name))
            {
                return name;
            }
            return name + ext;
        }
    }
}
