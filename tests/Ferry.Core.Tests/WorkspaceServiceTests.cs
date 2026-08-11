using Ferry.Core.Models;
using Ferry.Core.Ports;
using Ferry.Core.Services;
using Ferry.Infrastructure;

namespace Ferry.Core.Tests;

public class WorkspaceServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"ferry-svc-{Guid.NewGuid():N}.json");
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests()
    {
        _service = new WorkspaceService(new LocalWorkspaceStore(_file));
    }

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    private static PluginDescriptor LoadNginx()
    {
        var manager = new PluginManager(new DirectoryPluginSource(TestPaths.PluginsRoot));
        return manager.LoadAllPlugins().Single(p => p.PluginKey == "Nginx");
    }

    [Fact]
    public void CreateAndRenameWorkspace()
    {
        var ws = _service.CreateWorkspace("项目A");
        Assert.Equal("项目A", ws.Name);

        var renamed = _service.RenameWorkspace(ws.Id, "项目B");
        Assert.Equal("项目B", renamed.Name);
        Assert.Single(_service.ListWorkspaces());
    }

    [Fact]
    public void CreateConfig_UsesPluginDefaultFileName()
    {
        var nginx = LoadNginx();
        var ws = _service.CreateWorkspace("项目A");

        var config = _service.CreateConfig(ws.Id, nginx);

        Assert.Equal("nginx.conf", config.Name);
        Assert.Equal("Nginx", config.PluginKey);
        Assert.Equal(nginx.Version, config.PluginVersion);
    }

    [Fact]
    public void Snapshot_And_RestoreVersion()
    {
        var ws = _service.CreateWorkspace("项目A");
        var config = _service.CreateConfig(ws.Id, LoadNginx(), sourceText: "v1 源码");

        var snapshot = _service.SnapshotVersion(config, "初始");
        config.SourceText = "v2 源码";
        _service.SaveConfig(config);

        var restored = _service.RestoreVersion(ws.Id, config.Id, snapshot.Id);
        Assert.Equal("v1 源码", restored.SourceText);
        Assert.Empty(restored.Values);
        Assert.Single(_service.ListVersions(ws.Id, config.Id));
    }

    [Fact]
    public void ResolvePlugin_MissingAndVersionChanged()
    {
        var nginx = LoadNginx();
        var plugins = new List<PluginDescriptor> { nginx };

        var present = new ConfigData { PluginKey = "Nginx", PluginVersion = nginx.Version };
        Assert.Same(nginx, WorkspaceService.ResolvePlugin(plugins, present));
        Assert.False(WorkspaceService.IsPluginVersionChanged(nginx, present));

        var changed = new ConfigData { PluginKey = "Nginx", PluginVersion = "0.1" };
        Assert.True(WorkspaceService.IsPluginVersionChanged(nginx, changed));

        var missing = new ConfigData { PluginKey = "Unknown", PluginVersion = "1.0" };
        Assert.Null(WorkspaceService.ResolvePlugin(plugins, missing));
    }
}
