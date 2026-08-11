using Ferry.Core.Models;
using Ferry.Core.Services;
using Ferry.Core.Services.Parsing;

namespace Ferry.Core.Tests;

public class ConfigReverseParserTests
{
    [Fact]
    public void Parse_NginxConf_FillsValues_And_PreservesUnknownLine()
    {
        var manager = new PluginManager(new DirectoryPluginSource(TestPaths.PluginsRoot));
        var nginx = manager.LoadAllPlugins().Single(p => p.PluginKey == "Nginx");

        var result = ConfigReverseParser.Parse(nginx, """
            worker_processes auto;
            http {
                sendfile on;
                upstream backend {
                    server 127.0.0.1:8080 weight=1;
                }
                server {
                    listen 8080;
                    server_name example.com;
                    location /api {
                        proxy_pass http://127.0.0.1:3000;
                    }
                }
            }
            totally_unknown foo bar;
            """);

        Assert.Equal("auto", result.Values["worker_processes"]);
        var http = Assert.IsType<Dictionary<string, object?>>(result.Values["http"]);
        Assert.Equal("on", http["sendfile"]);

        var upstreams = Assert.IsType<List<object?>>(http["upstreams"]);
        var upstream = Assert.IsType<Dictionary<string, object?>>(upstreams[0]);
        Assert.Equal("backend", upstream["upstream_name"]);
        var upstreamServers = Assert.IsType<List<object?>>(upstream["servers"]);
        var serverItem = Assert.IsType<Dictionary<string, object?>>(upstreamServers[0]);
        Assert.Equal("127.0.0.1:8080", serverItem["server_address"]);
        Assert.Equal(1L, serverItem["weight"]);

        var servers = Assert.IsType<List<object?>>(http["servers"]);
        var server = Assert.IsType<Dictionary<string, object?>>(servers[0]);
        Assert.Equal("example.com", server["server_name"]);
        var locations = Assert.IsType<List<object?>>(server["locations"]);
        var location = Assert.IsType<Dictionary<string, object?>>(locations[0]);
        Assert.Equal("/api", location["path"]);
        Assert.Equal("http://127.0.0.1:3000", location["proxy_pass"]);

        Assert.Contains("totally_unknown foo bar;", result.Unrecognized);
        Assert.True(result.Report.RecognizedFields > 0);
        Assert.True(result.Report.UnrecognizedLines >= 1);
    }

    [Fact]
    public void Parse_Ini_FillsSections_And_PreservesUnknown()
    {
        var plugin = new PluginDescriptor
        {
            Name = "IniApp",
            Version = "1.0",
            PluginFolderPath = Path.Combine(TestPaths.PluginsRoot, "IniApp"),
            RendererConfig = new PluginRendererConfig { Type = "ini" },
            Schema = new ConfigSchema
            {
                Fields =
                [
                    new FieldDefinition { Id = "worker_processes", Type = FieldType.String },
                    new FieldDefinition
                    {
                        Id = "events",
                        Type = FieldType.Object,
                        Children = [new FieldDefinition { Id = "worker_connections", Type = FieldType.Number }]
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
            }
        };

        var result = ConfigReverseParser.Parse(plugin, """
            worker_processes = auto
            [events]
            worker_connections = 1024
            [upstreams.1]
            upstream_name = backend
            [upstreams.1.servers.1]
            server_address = 127.0.0.1:8080
            weight = 1
            unknown_key = xyz
            """);

        Assert.Equal("auto", result.Values["worker_processes"]);
        var events = Assert.IsType<Dictionary<string, object?>>(result.Values["events"]);
        Assert.Equal(1024L, events["worker_connections"]);
        var upstreams = Assert.IsType<List<object?>>(result.Values["upstreams"]);
        var upstream = Assert.IsType<Dictionary<string, object?>>(upstreams[0]);
        Assert.Equal("backend", upstream["upstream_name"]);
        var servers = Assert.IsType<List<object?>>(upstream["servers"]);
        var server = Assert.IsType<Dictionary<string, object?>>(servers[0]);
        Assert.Equal("127.0.0.1:8080", server["server_address"]);
        Assert.Equal(1L, server["weight"]);
        Assert.Contains("unknown_key = xyz", result.Unrecognized);
    }

    [Fact]
    public void AppendUnrecognized_KeepsUnknownContentOnExport()
    {
        var rendered = "worker_processes auto;\n";
        var unknown = new List<string> { "# 注释", "totally_unknown foo bar;" };

        var text = ConfigReverseParser.AppendUnrecognized(rendered, unknown);

        Assert.Contains("totally_unknown foo bar;", text);
        Assert.Contains("# 注释", text);
    }

    [Fact]
    public void Parse_Json_Yaml_DelegatesToImporter()
    {
        var plugin = new PluginDescriptor
        {
            Name = "JsonApp",
            Version = "1.0",
            PluginFolderPath = Path.Combine(TestPaths.PluginsRoot, "JsonApp"),
            RendererConfig = new PluginRendererConfig { Type = "json" },
            Schema = new ConfigSchema
            {
                Fields =
                [
                    new FieldDefinition { Id = "name", Type = FieldType.String },
                    new FieldDefinition { Id = "port", Type = FieldType.Number }
                ]
            }
        };

        var result = ConfigReverseParser.Parse(plugin, """{"name":"demo","port":8080}""");

        Assert.Equal("demo", result.Values["name"]);
        Assert.Equal(8080L, result.Values["port"]);
        Assert.Empty(result.Unrecognized);
    }
}
