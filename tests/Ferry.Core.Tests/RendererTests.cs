using Ferry.Core.Models;
using Ferry.Core.Services;
using Ferry.Core.Services.Rendering;
using Ferry.Core.Services.Form;

namespace Ferry.Core.Tests;

public class RendererTests
{
    [Fact]
    public void JsonRenderer_SerializesIndented()
    {
        var renderer = new JsonConfigRenderer();
        var text = renderer.Render(new Dictionary<string, object?>
        {
            ["name"] = "demo",
            ["port"] = 8080L,
            ["enabled"] = true
        });

        Assert.Contains("\"port\": 8080", text);
        Assert.Contains("\"enabled\": true", text);
    }

    [Fact]
    public void YamlRenderer_SerializesKeyValue()
    {
        var renderer = new YamlConfigRenderer();
        var text = renderer.Render(new Dictionary<string, object?>
        {
            ["name"] = "demo",
            ["port"] = 8080L
        });

        Assert.Contains("port: 8080", text);
        Assert.Contains("name: demo", text);
    }

    [Fact]
    public void IniRenderer_RendersSectionsAndScalars()
    {
        var schema = new ConfigSchema
        {
            Fields =
            [
                new FieldDefinition { Id = "worker_processes", Type = FieldType.String },
                new FieldDefinition
                {
                    Id = "events",
                    Type = FieldType.Object,
                    Children =
                    [
                        new FieldDefinition { Id = "worker_connections", Type = FieldType.Number }
                    ]
                },
                new FieldDefinition
                {
                    Id = "upstreams",
                    Type = FieldType.Array,
                    Children =
                    [
                        new FieldDefinition { Id = "upstream_name", Type = FieldType.String },
                        new FieldDefinition
                        {
                            Id = "servers",
                            Type = FieldType.Array,
                            Children =
                            [
                                new FieldDefinition { Id = "server_address", Type = FieldType.String },
                                new FieldDefinition { Id = "weight", Type = FieldType.Number }
                            ]
                        }
                    ]
                }
            ]
        };

        var config = new Dictionary<string, object?>
        {
            ["worker_processes"] = "auto",
            ["events"] = new Dictionary<string, object?> { ["worker_connections"] = 1024L },
            ["upstreams"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["upstream_name"] = "backend",
                    ["servers"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["server_address"] = "127.0.0.1:8080",
                            ["weight"] = 1L
                        }
                    }
                }
            }
        };

        var text = new IniConfigRenderer(schema).Render(config);
        var expected =
            "worker_processes = auto\n" +
            "[events]\n" +
            "worker_connections = 1024\n" +
            "[upstreams.1]\n" +
            "upstream_name = backend\n" +
            "[upstreams.1.servers.1]\n" +
            "server_address = 127.0.0.1:8080\n" +
            "weight = 1\n";

        Assert.Equal(expected, text);
    }

    [Fact]
    public void LayoutRenderer_RendersBlocksAndArrays()
    {
        var style = new PluginLayoutStyle
        {
            Line = "{{ .key }} {{ . }};",
            BlockOpen = "{{ .key }} {",
            BlockClose = "}",
            Indent = "    "
        };
        var schema = new ConfigSchema
        {
            Fields =
            [
                new FieldDefinition { Id = "worker_processes", Type = FieldType.String },
                new FieldDefinition
                {
                    Id = "http",
                    Type = FieldType.Object,
                    Children =
                    [
                        new FieldDefinition
                        {
                            Id = "sendfile",
                            Type = FieldType.Enum,
                            EnumOptions = [new EnumOption { Value = "on" }, new EnumOption { Value = "off" }]
                        }
                    ]
                },
                new FieldDefinition
                {
                    Id = "upstreams",
                    Type = FieldType.Array,
                    Render = new FieldRenderConfig { ItemOpen = "upstream {{ .upstream_name }} {", ItemClose = "}" },
                    Children =
                    [
                        new FieldDefinition
                        {
                            Id = "upstream_name",
                            Type = FieldType.String,
                            Render = new FieldRenderConfig { Hidden = true }
                        },
                        new FieldDefinition
                        {
                            Id = "servers",
                            Type = FieldType.Array,
                            Render = new FieldRenderConfig { Item = "server {{ .server_address }} weight={{ .weight }};" },
                            Children =
                            [
                                new FieldDefinition { Id = "server_address", Type = FieldType.String },
                                new FieldDefinition { Id = "weight", Type = FieldType.Number }
                            ]
                        }
                    ]
                }
            ]
        };

        var config = new Dictionary<string, object?>
        {
            ["worker_processes"] = "auto",
            ["http"] = new Dictionary<string, object?> { ["sendfile"] = "on" },
            ["upstreams"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["upstream_name"] = "backend",
                    ["servers"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["server_address"] = "127.0.0.1:8080",
                            ["weight"] = 1L
                        }
                    }
                }
            }
        };

        var text = new LayoutConfigRenderer(schema, style).Render(config);
        var expected =
            "worker_processes auto;\n" +
            "http {\n" +
            "    sendfile on;\n" +
            "}\n" +
            "upstream backend {\n" +
            "    server 127.0.0.1:8080 weight=1;\n" +
            "}\n";

        Assert.Equal(expected, text);
    }

    [Fact]
    public void LayoutRenderer_InvalidPlaceholder_Throws()
    {
        var schema = new ConfigSchema
        {
            Fields =
            [
                new FieldDefinition
                {
                    Id = "upstreams",
                    Type = FieldType.Array,
                    Render = new FieldRenderConfig { ItemOpen = "upstream {{ .missing }} {" },
                    Children = [new FieldDefinition { Id = "name", Type = FieldType.String }]
                }
            ]
        };

        Assert.Throws<FormatException>(() => new LayoutConfigRenderer(schema));
    }

    [Fact]
    public void Nginx_Plugin_RendersDefaultConfiguration()
    {
        var manager = new PluginManager(new DirectoryPluginSource(TestPaths.PluginsRoot));
        var nginx = manager.LoadAllPlugins().Single(p => p.PluginKey == "Nginx");
        var roots = FormBuilder.Build(nginx.Schema!);

        var errors = ConfigValidator.Validate(roots);
        Assert.Empty(errors);

        var config = ConfigValueCollector.Collect(roots);
        var text = RendererFactory.Create(nginx).Render(config);

        Assert.Contains("worker_processes auto;", text);
        Assert.Contains("events {", text);
        Assert.Contains("http {", text);
    }
}
