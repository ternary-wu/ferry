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
    public void ReorderWorkspaces_PersistsOrder_And_ListReturnsInOrder()
    {
        var project = NewProject();
        var a = _service.CreateWorkspace(project.Id, "A");
        var b = _service.CreateWorkspace(project.Id, "B");
        var c = _service.CreateWorkspace(project.Id, "C");

        _service.ReorderWorkspaces(project.Id, new[] { c.Id, a.Id, b.Id });

        Assert.Equal(new[] { c.Id, a.Id, b.Id }, _service.ListWorkspaces(project.Id).Select(x => x.Id));
    }

    [Fact]
    public void DeleteWorkspace_RemovesFromWorkspaceOrder()
    {
        var project = NewProject();
        var a = _service.CreateWorkspace(project.Id, "A");
        var b = _service.CreateWorkspace(project.Id, "B");
        var c = _service.CreateWorkspace(project.Id, "C");
        _service.ReorderWorkspaces(project.Id, new[] { c.Id, a.Id, b.Id });

        _service.DeleteWorkspace(a.Id);

        Assert.Equal(new[] { c.Id, b.Id }, _service.ListWorkspaces(project.Id).Select(x => x.Id));
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
    public void DuplicateConfig_CopiesSourceValuesAndUnrecognized()
    {
        var project = NewProject();
        var ws = _service.CreateWorkspace(project.Id, "生产环境");
        var nginx = LoadNginx();
        var source = _service.CreateConfig(
            project.Id,
            ws.Id,
            nginx,
            name: "nginx.conf",
            sourceText: "worker_processes auto;\n",
            values: new Dictionary<string, object?> { ["workerProcesses"] = "auto" },
            enabled: new Dictionary<string, bool> { ["http"] = true });
        source.Unrecognized.Add("# legacy line");
        _service.SaveConfig(source);

        var copy = _service.DuplicateConfig(ws.Id, source.Id);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("nginx - 副本.conf", copy.Name);
        Assert.Equal(source.SourceText, copy.SourceText);
        Assert.Equal(source.Values["workerProcesses"], copy.Values["workerProcesses"]);
        Assert.True(copy.Enabled["http"]);
        Assert.Contains("# legacy line", copy.Unrecognized);
        Assert.Equal(2, _service.ListConfigs(ws.Id).Count);
    }

    [Fact]
    public void RenameConfig_OnlyChangesName()
    {
        var project = NewProject();
        var ws = _service.CreateWorkspace(project.Id, "生产环境");
        var config = _service.CreateConfig(
            project.Id,
            ws.Id,
            LoadNginx(),
            name: "nginx.conf",
            sourceText: "worker_processes auto;\n");

        var renamed = _service.RenameConfig(ws.Id, config.Id, "prod.conf");

        Assert.Equal("prod.conf", renamed.Name);
        Assert.Equal("worker_processes auto;\n", renamed.SourceText);
        Assert.Equal("prod.conf", _service.LoadConfig(ws.Id, config.Id)!.Name);
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
    public void ReorderConfigs_PersistsOrder_And_ListReturnsInOrder()
    {
        var project = NewProject();
        var ws = _service.CreateWorkspace(project.Id, "生产环境");
        var nginx = LoadNginx();
        var a = _service.CreateConfig(project.Id, ws.Id, nginx, name: "a.conf");
        var b = _service.CreateConfig(project.Id, ws.Id, nginx, name: "b.conf");
        var c = _service.CreateConfig(project.Id, ws.Id, nginx, name: "c.conf");

        _service.ReorderConfigs(ws.Id, new[] { c.Id, a.Id, b.Id });

        Assert.Equal(new[] { c.Id, a.Id, b.Id }, _service.ListConfigs(ws.Id).Select(x => x.Id));
    }

    [Fact]
    public void ReorderConfigs_RejectsMissingExtraOrDuplicateIds()
    {
        var project = NewProject();
        var ws = _service.CreateWorkspace(project.Id, "生产环境");
        var nginx = LoadNginx();
        var a = _service.CreateConfig(project.Id, ws.Id, nginx, name: "a.conf");
        var b = _service.CreateConfig(project.Id, ws.Id, nginx, name: "b.conf");

        Assert.Throws<InvalidOperationException>(() =>
            _service.ReorderConfigs(ws.Id, new[] { a.Id }));
        Assert.Throws<InvalidOperationException>(() =>
            _service.ReorderConfigs(ws.Id, new[] { a.Id, b.Id, "unknown" }));
        Assert.Throws<InvalidOperationException>(() =>
            _service.ReorderConfigs(ws.Id, new[] { a.Id, a.Id, b.Id }));
    }

    [Fact]
    public void MoveConfig_RemovesFromSourceOrder_And_AppendsToTargetOrder()
    {
        var project = NewProject();
        var wsA = _service.CreateWorkspace(project.Id, "A");
        var wsB = _service.CreateWorkspace(project.Id, "B");
        var nginx = LoadNginx();
        var a = _service.CreateConfig(project.Id, wsA.Id, nginx, name: "a.conf");
        var b = _service.CreateConfig(project.Id, wsA.Id, nginx, name: "b.conf");

        _service.MoveConfig(a.Id, wsB.Id);

        Assert.Equal(new[] { b.Id }, _service.ListConfigs(wsA.Id).Select(x => x.Id));
        Assert.Equal(new[] { a.Id }, _service.ListConfigs(wsB.Id).Select(x => x.Id));
    }

    [Fact]
    public void Settings_RoundTrip_ThroughService()
    {
        _service.SaveSettings(new Dictionary<string, object?> { ["theme"] = "dark" });
        Assert.Equal("dark", _service.LoadSettings()["theme"]);
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
    public void EnsureDefaultProject_RemovesDefaultWorkspace_MovesConfigsToUnassigned()
    {
        var now = DateTimeOffset.Now;
        var file = Path.Combine(Path.GetTempPath(), $"ferry-clean-{Guid.NewGuid():N}.json");
        var store = new LocalWorkspaceStore(file);
        try
        {
            var project = store.ListProjects().FirstOrDefault(p => p.Name == "默认项目");
            if (project is null)
            {
                store.SaveProject(new ProjectInfo("p1", "默认项目", now, now));
                project = store.GetProject("p1")!;
            }
            store.SaveWorkspace(new WorkspaceInfo("ws1", project.Id, "默认工作空间", now, now));
            store.SaveConfig(new ConfigData
            {
                Id = "cfg1",
                ProjectId = project.Id,
                WorkspaceId = "ws1",
                Name = "nginx.conf",
                SourceText = "worker_processes auto;\n"
            });
            var service = new WorkspaceService(store);

            service.EnsureDefaultProject();

            Assert.Empty(service.ListWorkspaces());
            var unassigned = Assert.Single(service.ListUnassignedConfigs(project.Id));
            Assert.Equal("cfg1", unassigned.Id);
            Assert.Equal("worker_processes auto;\n", service.LoadConfig(string.Empty, "cfg1")!.SourceText);
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
