using Ferry.Core.Ports;
using Ferry.Infrastructure;

namespace Ferry.Core.Tests;

public class LocalWorkspaceStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"ferry-ws-{Guid.NewGuid():N}.json");
    private readonly LocalWorkspaceStore _store;

    public LocalWorkspaceStoreTests()
    {
        _store = new LocalWorkspaceStore(_file);
    }

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    [Fact]
    public void SaveAndLoad_WorkspaceAndConfig_RoundTrip()
    {
        var now = DateTimeOffset.Now;
        _store.SaveProject(new ProjectInfo("p1", "项目A", now, now));
        _store.SaveWorkspace(new WorkspaceInfo("ws1", "p1", "项目A", now, now));

        var config = new ConfigData
        {
            Id = "cfg1",
            ProjectId = "p1",
            WorkspaceId = "ws1",
            Name = "nginx.conf",
            PluginKey = "Nginx",
            PluginVersion = "1.27.0",
            SourceText = "worker_processes auto;\n",
            Values = new Dictionary<string, object?>
            {
                ["worker_processes"] = "auto",
                ["http"] = new Dictionary<string, object?>
                {
                    ["servers"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["listen"] = 8080L }
                    }
                }
            },
            Enabled = new Dictionary<string, bool> { ["http"] = true }
        };
        _store.SaveConfig(config);

        var loaded = _store.LoadConfig("ws1", "cfg1");
        Assert.NotNull(loaded);
        Assert.Equal("nginx.conf", loaded!.Name);
        Assert.Equal("worker_processes auto;\n", loaded.SourceText);
        Assert.Equal("auto", loaded.Values["worker_processes"]);
        var http = Assert.IsType<Dictionary<string, object?>>(loaded.Values["http"]);
        var servers = Assert.IsType<List<object?>>(http["servers"]);
        var item = Assert.IsType<Dictionary<string, object?>>(servers[0]);
        Assert.Equal(8080L, item["listen"]);
        Assert.True(loaded.Enabled["http"]);

        Assert.Single(_store.ListWorkspaces());
        var info = Assert.Single(_store.ListConfigs("ws1"));
        Assert.Equal("cfg1", info.Id);
        Assert.Equal("Nginx", info.PluginKey);
    }

    [Fact]
    public void SaveVersion_SetsCurrentVersionId_And_ListWorks()
    {
        _store.SaveProject(new ProjectInfo("p1", "项目A", DateTimeOffset.Now, DateTimeOffset.Now));
        _store.SaveWorkspace(new WorkspaceInfo("ws1", "p1", "项目A", DateTimeOffset.Now, DateTimeOffset.Now));
        _store.SaveConfig(new ConfigData
        {
            Id = "cfg1",
            ProjectId = "p1",
            WorkspaceId = "ws1",
            Name = "a.conf",
            SourceText = "v1"
        });

        _store.SaveVersion(new VersionSnapshot("ver1", "cfg1", "v1", DateTimeOffset.Now, "初始"));

        var config = _store.LoadConfig("ws1", "cfg1");
        Assert.Equal("ver1", config!.VersionId);
        var version = Assert.Single(_store.ListVersions("ws1", "cfg1"));
        Assert.Equal("初始", version.Note);
        Assert.Equal("v1", _store.GetVersion("ws1", "cfg1", "ver1")!.SourceText);
    }

    [Fact]
    public void DeleteConfig_RemovesVersions()
    {
        _store.SaveProject(new ProjectInfo("p1", "项目A", DateTimeOffset.Now, DateTimeOffset.Now));
        _store.SaveWorkspace(new WorkspaceInfo("ws1", "p1", "项目A", DateTimeOffset.Now, DateTimeOffset.Now));
        _store.SaveConfig(new ConfigData { Id = "cfg1", WorkspaceId = "ws1", Name = "a.conf" });
        _store.SaveVersion(new VersionSnapshot("ver1", "cfg1", "v1", DateTimeOffset.Now, null));

        _store.DeleteConfig("ws1", "cfg1");

        Assert.Null(_store.LoadConfig("ws1", "cfg1"));
        Assert.Empty(_store.ListVersions("ws1", "cfg1"));
    }

    [Fact]
    public void DeleteWorkspace_CascadesConfigsAndVersions()
    {
        _store.SaveProject(new ProjectInfo("p1", "项目A", DateTimeOffset.Now, DateTimeOffset.Now));
        _store.SaveWorkspace(new WorkspaceInfo("ws1", "p1", "项目A", DateTimeOffset.Now, DateTimeOffset.Now));
        _store.SaveConfig(new ConfigData { Id = "cfg1", WorkspaceId = "ws1", Name = "a.conf" });
        _store.SaveVersion(new VersionSnapshot("ver1", "cfg1", "v1", DateTimeOffset.Now, null));

        _store.DeleteWorkspace("ws1");

        Assert.Empty(_store.ListWorkspaces());
        Assert.Empty(_store.ListConfigs("ws1"));
        Assert.Empty(_store.ListVersions("ws1", "cfg1"));
    }

    [Fact]
    public void ProjectCrud_And_DeleteProject_Cascades()
    {
        _store.SaveProject(new ProjectInfo("p1", "项目A", DateTimeOffset.Now, DateTimeOffset.Now));
        _store.SaveWorkspace(new WorkspaceInfo("ws1", "p1", "生产", DateTimeOffset.Now, DateTimeOffset.Now));
        _store.SaveConfig(new ConfigData
        {
            Id = "cfg1",
            ProjectId = "p1",
            WorkspaceId = "ws1",
            Name = "nginx.conf"
        });

        Assert.Single(_store.ListProjects());
        Assert.Equal("项目A", _store.GetProject("p1")!.Name);

        _store.DeleteProject("p1");

        Assert.Empty(_store.ListProjects());
        Assert.Empty(_store.ListWorkspaces());
        Assert.Empty(_store.ListConfigs("ws1"));
    }
}
