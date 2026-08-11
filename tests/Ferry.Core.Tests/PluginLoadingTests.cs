using Ferry.Core.Services;

namespace Ferry.Core.Tests;

public class PluginLoadingTests
{
    private static PluginManager CreateManager()
        => new(new DirectoryPluginSource(TestPaths.PluginsRoot));

    [Fact]
    public void LoadAllPlugins_FindsAllFivePlugins()
    {
        var manager = CreateManager();
        var plugins = manager.LoadAllPlugins();

        Assert.Equal(5, plugins.Count);
        Assert.Empty(manager.LoadErrors);
        Assert.All(plugins, p => Assert.NotNull(p.Schema));

        var keys = plugins.Select(p => p.PluginKey).OrderBy(k => k).ToArray();
        Assert.Equal(
            new[] { "App-config", "Docker-compose", "Dockerfile", "Nginx", "Redis" },
            keys);
    }

    [Fact]
    public void Nginx_IsLayoutRenderer_NotImportable_DefaultNameNginxConf()
    {
        var manager = CreateManager();
        var nginx = manager.LoadAllPlugins().Single(p => p.PluginKey == "Nginx");

        Assert.Equal("layout", nginx.RendererType);
        Assert.True(nginx.CanImport);
        Assert.Equal("nginx.conf", nginx.DefaultFileName);
        Assert.NotEmpty(nginx.Templates);
        Assert.Equal(3, nginx.Templates.Count);
    }

    [Fact]
    public void AppConfig_IsYaml_Importable()
    {
        var manager = CreateManager();
        var appConfig = manager.LoadAllPlugins().Single(p => p.PluginKey == "App-config");

        Assert.Equal("yaml", appConfig.RendererType);
        Assert.True(appConfig.CanImport);
        Assert.Equal("app-config.yaml", appConfig.DefaultFileName);
    }

    [Fact]
    public void MissingPluginDirectory_ReturnsEmptyList()
    {
        var manager = new PluginManager(
            new DirectoryPluginSource(Path.Combine(TestPaths.FindRepoRoot(), "Plugins-缺")));
        var plugins = manager.LoadAllPlugins();

        Assert.Empty(plugins);
    }
}
