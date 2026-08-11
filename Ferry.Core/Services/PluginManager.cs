using Ferry.Core.Infrastructure;
using Ferry.Core.Models;
using Ferry.Core.Ports;

namespace Ferry.Core.Services;

/// <summary>
/// 插件管理器：依赖 IPluginSource 端口加载插件，聚合加载错误供 UI 展示。
/// </summary>
public sealed class PluginManager
{
    private readonly IPluginSource _source;

    /// <summary>最近一次加载中失败的插件（供 UI 直接展示，不必查看日志）。</summary>
    public List<string> LoadErrors { get; } = new();

    public PluginManager(IPluginSource source)
    {
        _source = source;
    }

    public IReadOnlyList<PluginDescriptor> LoadAllPlugins()
    {
        LoadErrors.Clear();
        var plugins = _source.LoadAllPlugins();
        foreach (var plugin in plugins)
        {
            foreach (var error in plugin.LoadErrors)
            {
                LoadErrors.Add($"插件 {plugin.Name}：{error}");
            }
        }
        if (plugins.Count == 0)
        {
            FerryLog.Info("未发现任何插件");
        }
        return plugins;
    }
}
