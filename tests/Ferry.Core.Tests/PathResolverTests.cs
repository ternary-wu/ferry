using Ferry.Core.Models;
using Ferry.Core.Services;
using Ferry.Core.Services.Form;
using Ferry.Core.Services.Session;

namespace Ferry.Core.Tests;

public class PathResolverTests
{
    private static List<FormNode> BuildTree()
    {
        var schema = new ConfigSchema
        {
            Fields =
            [
                new FieldDefinition
                {
                    Id = "http",
                    Type = FieldType.Object,
                    Children =
                    [
                        new FieldDefinition
                        {
                            Id = "servers",
                            Type = FieldType.Array,
                            Children = [new FieldDefinition { Id = "server_name", Type = FieldType.String }]
                        }
                    ]
                }
            ]
        };
        return FormBuilder.Build(schema);
    }

    [Fact]
    public void Resolve_StaticAndArrayPaths()
    {
        var roots = BuildTree();
        var http = PathResolver.Resolve(roots, "http");
        Assert.NotNull(http);

        var servers = http!.Children.Single();
        servers.AddItem();

        var item = PathResolver.Resolve(roots, "http.servers[0]");
        Assert.NotNull(item);
        Assert.Equal("http.servers[0]", item!.Path);

        var child = PathResolver.Resolve(roots, "http.servers[0].server_name");
        Assert.NotNull(child);
    }

    [Fact]
    public void Resolve_MissingPath_ReturnsNull()
    {
        var roots = BuildTree();

        Assert.Null(PathResolver.Resolve(roots, "http.missing"));
        Assert.Null(PathResolver.Resolve(roots, "http.servers[5]"));
        Assert.Null(PathResolver.Resolve(roots, ""));
    }

    [Fact]
    public void Resolve_NonArrayWithIndex_ReturnsNull()
    {
        var roots = BuildTree();

        Assert.Null(PathResolver.Resolve(roots, "http[0]"));
    }
}
