namespace Ferry.Core.Tests;

internal static class TestPaths
{
    /// <summary>向上查找包含 Ferry.slnx 的仓库根目录。</summary>
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ferry.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("未找到 Ferry.slnx（仓库根目录）");
    }

    public static string PluginsRoot => Path.Combine(FindRepoRoot(), "Plugins");
}
