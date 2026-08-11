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

    private ProjectInfo NewProject() => _service.CreateProject("项目A");

    [Fact]
    public void CreateAndRenameWorkspace()
    {
        var project = NewProject();
        var ws = _service.CreateWorkspace(project.Id, "生产环境");
        Assert.Equal("生产环境", ws.Name);
        Assert.Equal(project.Id, ws.ProjectId);

        var renamed = _service.RenameWorkspace(ws.Id, "测试环境");
        Assert.Equal("测试环境", renamed.Name);
        Assert.Single(_service.ListWorkspaces());
    }

    [Fact]
    public void CreateConfig_UsesPluginDefaultFileName()
    {
        var nginx = LoadNginx();
        var project = NewProject();
        var ws = _service.CreateWorkspace(project.Id, "生产环境");

        var config = _service.CreateConfig(project.Id, ws.Id, nginx);

        Assert.Equal("nginx.conf", config.Name);
        Assert.Equal("Nginx", config.PluginKey);
        Assert.Equal(project.Id, config.ProjectId);
        Assert.Equal(nginx.Version, config.PluginVersion);
    }

    [Fact]
    public void Snapshot_And_RestoreVersion()
    {
        var project = NewProject();
        var ws = _service.CreateWorkspace(project.Id, "生产环境");
        var config = _service.CreateConfig(project.Id, ws.Id, LoadNginx(), sourceText: "v1 源码");

        var snapshot = _service.SnapshotVersion(config, "初始");
        config.SourceText = "v2 源码";
        _service.SaveConfig(config);

        var restored = _service.RestoreVersion(ws.Id, config.Id, snapshot.Id);
        Assert.Equal("v1 源码", restored.SourceText);
        Assert.Empty(restored.Values);
        Assert.Single(_service.ListVersions(ws.Id, config.Id));
    }

    [Fact]
    public void Project_UnassignedConfig_And_Move()
    {
        var project = NewProject();
        var ws = _service.CreateWorkspace(project.Id, "生产环境");
        var nginx = LoadNginx();

        var unassigned = _service.CreateConfig(project.Id, string.Empty, nginx, name: "demo-nginx");
        Assert.Single(_service.ListUnassignedConfigs(project.Id));
        Assert.Contains(_service.ListUnassignedConfigs(project.Id), c => c.Id == unassigned.Id);

        var moved = _service.MoveConfig(unassigned.Id, ws.Id);
        Assert.Equal(ws.Id, moved.WorkspaceId);
        Assert.Empty(_service.ListUnassignedConfigs(project.Id));
        Assert.Contains(_service.ListConfigs(ws.Id), c => c.Id == unassigned.Id);
    }

    [Fact]
    public void EnsureDefaultProject_MigratesLegacyWorkspaces()
    {
        var now = DateTimeOffset.Now;
        var file = Path.Combine(Path.GetTempPath(), $"ferry-ensure-{Guid.NewGuid():N}.json");
        var store = new LocalWorkspaceStore(file);
        try
        {
            // 模拟旧数据：工作空间无项目归属
            store.SaveWorkspace(new WorkspaceInfo("legacy", string.Empty, "旧工作空间", now, now));
            var service = new WorkspaceService(store);

            var project = service.EnsureDefaultProject();

            Assert.Equal("默认项目", project.Name);
            var migrated = Assert.Single(service.ListWorkspaces());
            Assert.Equal(project.Id, migrated.ProjectId);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
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
