using Ferry.Core.Models;
using Ferry.Core.Services;
using Ferry.Core.Services.Archive;
using Ferry.Infrastructure;

namespace Ferry.Core.Tests;

public class PortableArchiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ferry-arch-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    private static PluginDescriptor LoadNginx()
    {
        var manager = new PluginManager(new DirectoryPluginSource(TestPaths.PluginsRoot));
        return manager.LoadAllPlugins().Single(p => p.PluginKey == "Nginx");
    }

    private (WorkspaceService Service, string WorkspaceId, string ConfigId) CreateSource(
        PluginDescriptor nginx)
    {
        var service = new WorkspaceService(new LocalWorkspaceStore(Path.Combine(_dir, "source.json")));
        var project = service.CreateProject("项目A");
        var workspace = service.CreateWorkspace(project.Id, "生产环境");
        var config = service.CreateConfig(
            project.Id,
            workspace.Id,
            nginx,
            sourceText: "worker_processes auto;\nhttp {\n    sendfile on;\n}\n",
            values: new Dictionary<string, object?> { ["worker_processes"] = "auto" });
        config.Unrecognized = ["totally_unknown foo bar;"];
        service.SaveConfig(config);
        service.SnapshotVersion(config, "初始");
        return (service, workspace.Id, config.Id);
    }

    [Fact]
    public void ExportConfig_ThenImport_WithoutLocalPlugin_RestoresEverything()
    {
        var nginx = LoadNginx();
        var (service, workspaceId, configId) = CreateSource(nginx);
        var zipPath = Path.Combine(_dir, "package.zip");
        var exporter = new PortableArchiveService(service, new List<PluginDescriptor> { nginx });
        exporter.ExportConfig(workspaceId, configId, zipPath);

        var targetService = new WorkspaceService(new LocalWorkspaceStore(Path.Combine(_dir, "target.json")));
        var importer = new PortableArchiveService(targetService, Array.Empty<PluginDescriptor>());
        var result = importer.Import(zipPath);

        Assert.Equal(1, result.ImportedConfigs);
        Assert.Contains("Nginx", result.PackagedPlugins);
        Assert.Empty(result.LocalPlugins);
        Assert.Empty(result.MissingPlugins);

        var project = Assert.Single(targetService.ListProjects());
        Assert.Equal("项目A", project.Name);
        var workspace = Assert.Single(targetService.ListWorkspaces());
        Assert.Equal("生产环境", workspace.Name);
        var info = Assert.Single(targetService.ListConfigs(workspace.Id));
        var config = targetService.LoadConfig(workspace.Id, info.Id);
        Assert.NotNull(config);
        Assert.Equal("nginx.conf", config!.Name);
        Assert.Equal("worker_processes auto;\nhttp {\n    sendfile on;\n}\n", config.SourceText);
        Assert.Equal("auto", config.Values["worker_processes"]);
        Assert.Contains("totally_unknown foo bar;", config.Unrecognized);
        Assert.Single(targetService.ListVersions(workspace.Id, info.Id));
    }

    [Fact]
    public void Import_PrefersLocalPlugin()
    {
        var nginx = LoadNginx();
        var (service, workspaceId, configId) = CreateSource(nginx);
        var zipPath = Path.Combine(_dir, "package.zip");
        new PortableArchiveService(service, new List<PluginDescriptor> { nginx })
            .ExportConfig(workspaceId, configId, zipPath);

        var targetService = new WorkspaceService(new LocalWorkspaceStore(Path.Combine(_dir, "target.json")));
        var result = new PortableArchiveService(targetService, new List<PluginDescriptor> { nginx })
            .Import(zipPath);

        Assert.Contains("Nginx", result.LocalPlugins);
        Assert.Empty(result.PackagedPlugins);
    }

    [Fact]
    public void Import_WithoutPackagedPlugin_KeepsSourceViewable()
    {
        var service = new WorkspaceService(new LocalWorkspaceStore(Path.Combine(_dir, "source.json")));
        var project = service.CreateProject("项目A");
        var workspace = service.CreateWorkspace(project.Id, "生产环境");
        var stub = new PluginDescriptor
        {
            Name = "Unknown",
            Version = "1.0",
            PluginFolderPath = Path.Combine(TestPaths.PluginsRoot, "Unknown"),
            RendererConfig = new PluginRendererConfig { Type = "layout" }
        };
        var config = service.CreateConfig(
            project.Id,
            workspace.Id,
            stub,
            sourceText: "some raw config text",
            values: new Dictionary<string, object?> { ["foo"] = "bar" });
        var zipPath = Path.Combine(_dir, "package.zip");
        new PortableArchiveService(service, Array.Empty<PluginDescriptor>())
            .ExportConfig(workspace.Id, config.Id, zipPath);

        var targetService = new WorkspaceService(new LocalWorkspaceStore(Path.Combine(_dir, "target.json")));
        var result = new PortableArchiveService(targetService, Array.Empty<PluginDescriptor>())
            .Import(zipPath);

        Assert.Equal(1, result.ImportedConfigs);
        Assert.Contains("Unknown", result.MissingPlugins);
        var info = Assert.Single(targetService.ListConfigs(result.WorkspaceId!));
        var imported = targetService.LoadConfig(result.WorkspaceId!, info.Id);
        Assert.Equal("some raw config text", imported!.SourceText);
        Assert.Equal("bar", imported.Values["foo"]);
    }

    [Fact]
    public void InstallPlugin_CopiesThreeFiles_And_IsLoadable()
    {
        var nginx = LoadNginx();
        var pluginRoot = Path.Combine(_dir, "Plugins");

        PortableArchiveService.InstallPlugin(nginx, pluginRoot);

        Assert.True(File.Exists(Path.Combine(pluginRoot, "Nginx", "plugin.yaml")));
        Assert.True(File.Exists(Path.Combine(pluginRoot, "Nginx", "schema.yaml")));
        var manager = new PluginManager(new DirectoryPluginSource(pluginRoot));
        Assert.Single(manager.LoadAllPlugins());
    }

    [Fact]
    public void ExportWorkspace_ExportsAllConfigs()
    {
        var nginx = LoadNginx();
        var service = new WorkspaceService(new LocalWorkspaceStore(Path.Combine(_dir, "source.json")));
        var project = service.CreateProject("项目A");
        var workspace = service.CreateWorkspace(project.Id, "生产环境");
        service.CreateConfig(project.Id, workspace.Id, nginx, name: "a.conf", sourceText: "A");
        service.CreateConfig(project.Id, workspace.Id, nginx, name: "b.conf", sourceText: "B");
        var zipPath = Path.Combine(_dir, "package.zip");
        new PortableArchiveService(service, new List<PluginDescriptor> { nginx })
            .ExportWorkspace(workspace.Id, zipPath);

        var targetService = new WorkspaceService(new LocalWorkspaceStore(Path.Combine(_dir, "target.json")));
        var result = new PortableArchiveService(targetService, new List<PluginDescriptor> { nginx })
            .Import(zipPath);

        Assert.Equal(2, result.ImportedConfigs);
        var configs = targetService.ListConfigs(result.WorkspaceId!);
        Assert.Equal(2, configs.Count);
    }

    [Fact]
    public void ExportWorkspace_ThenImport_PreservesConfigOrder()
    {
        var nginx = LoadNginx();
        var service = new WorkspaceService(new LocalWorkspaceStore(Path.Combine(_dir, "source.json")));
        var project = service.CreateProject("项目A");
        var workspace = service.CreateWorkspace(project.Id, "生产环境");
        var a = service.CreateConfig(project.Id, workspace.Id, nginx, name: "a.conf", sourceText: "A");
        var b = service.CreateConfig(project.Id, workspace.Id, nginx, name: "b.conf", sourceText: "B");
        var c = service.CreateConfig(project.Id, workspace.Id, nginx, name: "c.conf", sourceText: "C");
        service.ReorderConfigs(workspace.Id, new[] { c.Id, a.Id, b.Id });

        var zipPath = Path.Combine(_dir, "package.zip");
        new PortableArchiveService(service, new List<PluginDescriptor> { nginx })
            .ExportWorkspace(workspace.Id, zipPath);

        var targetService = new WorkspaceService(new LocalWorkspaceStore(Path.Combine(_dir, "target.json")));
        var result = new PortableArchiveService(targetService, new List<PluginDescriptor> { nginx })
            .Import(zipPath);

        Assert.Equal(3, result.ImportedConfigs);
        var configs = targetService.ListConfigs(result.WorkspaceId!);
        Assert.Equal(new[] { "c.conf", "a.conf", "b.conf" }, configs.Select(x => x.Name));
    }
}
