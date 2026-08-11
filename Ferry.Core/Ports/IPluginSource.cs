using Ferry.Core.Models;

namespace Ferry.Core.Ports;

/// <summary>
/// 插件来源端口：实现可以是本地目录扫描（DirectoryPluginSource）、
/// 服务端共享注册表（未来）或存档包内只读加载（M1.7）。
/// 解析失败的目录返回带 LoadErrors 的描述符，不中断整体加载。
/// </summary>
public interface IPluginSource
{
    IReadOnlyList<PluginDescriptor> LoadAllPlugins();
}
