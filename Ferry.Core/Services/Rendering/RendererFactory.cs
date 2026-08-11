using Ferry.Core.Models;

namespace Ferry.Core.Services.Rendering;

/// <summary>按插件渲染器类型创建渲染器；不支持的类型直接报错，避免静默输出。</summary>
public static class RendererFactory
{
    public static IConfigRenderer Create(PluginDescriptor plugin)
    {
        return plugin.RendererType switch
        {
            "json" => new JsonConfigRenderer(),
            "yaml" => new YamlConfigRenderer(),
            "ini" => new IniConfigRenderer(plugin.Schema),
            "layout" => new LayoutConfigRenderer(plugin.Schema, plugin.RendererConfig.Layout),
            _ => throw new FormatException($"不支持的渲染器类型：{plugin.RendererConfig.Type}")
        };
    }
}
