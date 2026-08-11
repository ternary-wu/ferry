using Ferry.Core.Models;
using Ferry.Core.Services;
using Ferry.Core.Services.Session;
using Ferry.Core.Services.Session.Protocol;

namespace Ferry.Core.Tests;

public class FormSessionTests
{
    private static PluginDescriptor CreateTestPlugin()
    {
        return new PluginDescriptor
        {
            Name = "Test",
            Version = "1.0",
            PluginFolderPath = Path.Combine(TestPaths.PluginsRoot, "Test"),
            RendererConfig = new PluginRendererConfig
            {
                Type = "layout",
                Layout = new PluginLayoutStyle
                {
                    Line = "{{ .key }} {{ . }};",
                    BlockOpen = "{{ .key }} {",
                    BlockClose = "}",
                    Indent = "    "
                }
            },
            Schema = new ConfigSchema
            {
                Fields =
                [
                    new FieldDefinition { Id = "app_name", Label = "应用名", Type = FieldType.String, Required = true },
                    new FieldDefinition
                    {
                        Id = "http",
                        Label = "HTTP",
                        Type = FieldType.Object,
                        Module = true,
                        Children = [new FieldDefinition { Id = "sendfile", Type = FieldType.String }]
                    },
                    new FieldDefinition
                    {
                        Id = "upstreams",
                        Label = "上游",
                        Type = FieldType.Array,
                        Module = true,
                        Render = new FieldRenderConfig
                        {
                            ItemOpen = "upstream {{ .upstream_name }} {",
                            ItemClose = "}"
                        },
                        Children =
                        [
                            new FieldDefinition
                            {
                                Id = "upstream_name",
                                Type = FieldType.String,
                                Render = new FieldRenderConfig { Hidden = true }
                            },
                            new FieldDefinition { Id = "address", Type = FieldType.String }
                        ]
                    }
                ]
            },
            Templates =
            [
                new ConfigPreset
                {
                    Id = "http_only",
                    Name = "仅 HTTP",
                    Modules = ["http"]
                }
            ]
        };
    }

    private static List<FormCommand> BuildCommandSequence() =>
    [
        new SetValueCommand("app_name", "demo"),
        new AddItemCommand("upstreams"),
        new SetValueCommand("upstreams[0].upstream_name", "backend"),
        new SetValueCommand("upstreams[0].address", "127.0.0.1:8080"),
        new ToggleEnabledCommand("http", false),
        new ToggleEnabledCommand("http", true)
    ];

    [Fact]
    public void Instance_And_StaticExecute_ProduceSameState()
    {
        var plugin = CreateTestPlugin();

        // 实例式
        var session = FormSession.Create(plugin);
        foreach (var command in BuildCommandSequence())
        {
            var result = session.Apply(command);
            Assert.True(result.Ok, string.Join("; ", result.Errors));
        }
        var instanceState = session.GetState();

        // 静态 Execute：同一命令序列
        var state = new ConfigState { PluginKey = plugin.PluginKey, PluginVersion = plugin.Version };
        foreach (var command in BuildCommandSequence())
        {
            var result = FormSession.Execute(plugin, state, command);
            Assert.True(result.Ok, string.Join("; ", result.Errors));
            state = result.State!;
        }

        Assert.Equal(instanceState.Version, state.Version);
        Assert.Equal(instanceState.Enabled, state.Enabled);
        Assert.Equal("demo", state.Values["app_name"]);
        var upstreams1 = (List<object?>)instanceState.Values["upstreams"]!;
        var upstreams2 = (List<object?>)state.Values["upstreams"]!;
        var item1 = (Dictionary<string, object?>)upstreams1[0]!;
        var item2 = (Dictionary<string, object?>)upstreams2[0]!;
        Assert.Equal("backend", item1["upstream_name"]);
        Assert.Equal(item1["address"], item2["address"]);
    }

    [Fact]
    public void AddItem_ReturnsStableNewItemPath_And_RemoveWorks()
    {
        var plugin = CreateTestPlugin();
        var session = FormSession.Create(plugin);

        var first = session.AddItem("upstreams");
        Assert.True(first.Ok);
        Assert.Equal("upstreams[0]", first.NewItemPath);

        var second = session.AddItem("upstreams");
        Assert.Equal("upstreams[1]", second.NewItemPath);

        var remove = session.RemoveItem("upstreams[0]");
        Assert.True(remove.Ok);

        var snapshot = session.GetSnapshot();
        var upstreams = snapshot.Single(s => s.Id == "upstreams");
        Assert.Single(upstreams.Children);
        Assert.Equal("upstreams[0]", upstreams.Children[0].Path);
    }

    [Fact]
    public void ToggleEnabled_RequiredField_IsRejected()
    {
        var plugin = CreateTestPlugin();
        var session = FormSession.Create(plugin);

        var result = session.ToggleEnabled("app_name", false);

        Assert.False(result.Ok);
        Assert.Equal("validation", result.ErrorCode);
        Assert.True(session.GetSnapshot().Single(s => s.Id == "app_name").IsEnabled);
    }

    [Fact]
    public void SetValue_MissingPath_ReturnsNotFound()
    {
        var plugin = CreateTestPlugin();
        var session = FormSession.Create(plugin);

        var result = session.SetValue("missing.path", "x");

        Assert.False(result.Ok);
        Assert.Equal("not_found", result.ErrorCode);
    }

    [Fact]
    public void Execute_VersionConflict_ReturnsConflict()
    {
        var plugin = CreateTestPlugin();
        var state = new ConfigState
        {
            PluginKey = plugin.PluginKey,
            PluginVersion = plugin.Version,
            Version = 3
        };

        var result = FormSession.Execute(
            plugin,
            state,
            new SetValueCommand("app_name", "demo"),
            expectedVersion: 2);

        Assert.False(result.Ok);
        Assert.Equal("conflict", result.ErrorCode);
    }

    [Fact]
    public void ApplyPreset_SetsModuleEnabledStates()
    {
        var plugin = CreateTestPlugin();
        var session = FormSession.Create(plugin);

        var result = session.ApplyPreset("http_only");
        Assert.True(result.Ok);

        var enabled = session.GetState().Enabled;
        Assert.True(enabled["http"]);
        Assert.False(enabled["upstreams"]);
    }

    [Fact]
    public void Import_JsonIntoYamlPlugin_PopulatesValues()
    {
        var plugin = new PluginDescriptor
        {
            Name = "YamlApp",
            Version = "1.0",
            PluginFolderPath = Path.Combine(TestPaths.PluginsRoot, "YamlApp"),
            RendererConfig = new PluginRendererConfig { Type = "yaml" },
            Schema = new ConfigSchema
            {
                Fields =
                [
                    new FieldDefinition { Id = "name", Type = FieldType.String },
                    new FieldDefinition { Id = "port", Type = FieldType.Number }
                ]
            }
        };
        var session = FormSession.Create(plugin);

        var result = session.Import("""
            name: demo
            port: 8080
            """);

        Assert.True(result.Ok);
        var state = session.GetState();
        Assert.Equal("demo", state.Values["name"]);
        Assert.Equal(8080, state.Values["port"]);
    }

    [Fact]
    public void Import_LayoutPlugin_IsRejected()
    {
        var plugin = CreateTestPlugin();
        var session = FormSession.Create(plugin);

        var result = session.Import("worker_processes auto;");

        Assert.False(result.Ok);
        Assert.Equal("unsupported", result.ErrorCode);
    }

    [Fact]
    public void Render_And_Snapshot_Commands_Work()
    {
        var plugin = CreateTestPlugin();
        var session = FormSession.Create(plugin);
        session.Apply(new SetValueCommand("app_name", "demo"));
        session.Apply(new AddItemCommand("upstreams"));
        session.Apply(new SetValueCommand("upstreams[0].upstream_name", "backend"));
        session.Apply(new SetValueCommand("upstreams[0].address", "127.0.0.1:8080"));

        var validate = session.Apply(new ValidateCommand());
        Assert.True(validate.Ok);

        var render = session.Apply(new RenderCommand());
        Assert.True(render.Ok);
        Assert.Contains("upstream backend {", render.RenderedText);

        var snapshot = session.Apply(new SnapshotCommand());
        Assert.True(snapshot.Ok);
        Assert.Equal(3, snapshot.Snapshot!.Count);
    }

    [Fact]
    public void Nginx_Plugin_Session_RendersAndValidates()
    {
        var manager = new PluginManager(new DirectoryPluginSource(TestPaths.PluginsRoot));
        var nginx = manager.LoadAllPlugins().Single(p => p.PluginKey == "Nginx");
        var session = FormSession.Create(nginx);

        var errors = session.Validate();
        Assert.Empty(errors);

        var text = session.Render();
        Assert.Contains("worker_processes auto;", text);

        var snapshot = session.GetSnapshot();
        Assert.Contains(snapshot, s => s.Id == "http" && s.IsModule);
    }
}
