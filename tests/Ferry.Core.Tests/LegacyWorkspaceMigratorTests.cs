using Ferry.Core.Services;
using Ferry.Infrastructure;

namespace Ferry.Core.Tests;

public class LegacyWorkspaceMigratorTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"ferry-legacy-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    [Fact]
    public void Migrate_CreatesDefaultWorkspace_WithRenderedSource()
    {
        File.WriteAllText(_file, """
            {
              "plugins": {
                "Nginx-test": {
                  "values": { "worker_processes": "auto" },
                  "enabled": { "http": true }
                },
                "Missing-plugin": {
                  "values": { "foo": "bar" }
                }
              }
            }
            """);

        var manager = new PluginManager(new DirectoryPluginSource(TestPaths.PluginsRoot));
        var plugins = manager.LoadAllPlugins();
        var service = new WorkspaceService(new LocalWorkspaceStore(
            Path.Combine(Path.GetTempPath(), $"ferry-v2-{Guid.NewGuid():N}.json")));
        var migrator = new LegacyWorkspaceMigrator(service, plugins);

        var result = migrator.Migrate(_file);

        Assert.Equal(2, result.CreatedConfigs);
        Assert.Contains("Missing-plugin", result.MissingPlugins);

        var project = Assert.Single(service.ListProjects());
        Assert.Equal("默认项目", project.Name);
        Assert.Empty(service.ListWorkspaces());

        var configs = service.ListUnassignedConfigs(project.Id);
        Assert.Equal(2, configs.Count);
        var nginx = service.LoadConfig(string.Empty, configs.Single(c => c.PluginKey == "Nginx").Id);
        Assert.NotNull(nginx);
        Assert.Contains("worker_processes auto;", nginx!.SourceText);
        Assert.Equal("auto", nginx.Values["worker_processes"]);
        var missing = service.LoadConfig(string.Empty, configs.Single(c => c.PluginKey == "Missing-plugin").Id);
        Assert.NotNull(missing);
        Assert.Equal("Missing-plugin.conf", missing!.Name);
        Assert.Empty(missing.SourceText);

        // 源文件只读，未被修改
        Assert.Contains("\"Nginx-test\"", File.ReadAllText(_file));
    }

    [Fact]
    public void Migrate_MissingFile_ReturnsSkipped()
    {
        var service = new WorkspaceService(new LocalWorkspaceStore(
            Path.Combine(Path.GetTempPath(), $"ferry-v2-{Guid.NewGuid():N}.json")));
        var migrator = new LegacyWorkspaceMigrator(service, Array.Empty<Ferry.Core.Models.PluginDescriptor>());

        var result = migrator.Migrate(Path.Combine(Path.GetTempPath(), "不存在.json"));

        Assert.Equal(0, result.CreatedConfigs);
        Assert.NotNull(result.SkippedReason);
    }
}
