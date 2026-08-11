using Ferry.Core.Services;

namespace Ferry.Core.Tests;

public class ConfigImporterTests
{
    [Fact]
    public void ParseJson_ReturnsNormalizedValueTree()
    {
        var result = ConfigImporter.ParseJson("""
            {
              "name": "demo",
              "port": 8080,
              "enabled": true
            }
            """);

        Assert.Equal("demo", result["name"]);
        Assert.Equal(8080L, result["port"]);
        Assert.Equal(true, result["enabled"]);
    }

    [Fact]
    public void ParseYaml_ReturnsNormalizedValueTree()
    {
        var result = ConfigImporter.ParseYaml("""
            name: demo
            port: 8080
            features:
              - a
              - b
            """);

        Assert.Equal("demo", result["name"]);
        Assert.Equal(8080, result["port"]);
        var features = Assert.IsType<List<object?>>(result["features"]);
        Assert.Equal(new object?[] { "a", "b" }, features);
    }

    [Fact]
    public void ParseYaml_InvalidRoot_Throws()
    {
        Assert.Throws<FormatException>(() => ConfigImporter.ParseYaml("- 1\n- 2\n"));
    }

    [Fact]
    public void NormalizeTree_ConvertsMixedDictionaries()
    {
        object? tree = new Dictionary<object, object>
        {
            ["name"] = "demo",
            ["nested"] = new Dictionary<object, object> { ["count"] = "3" }
        };

        var normalized = ConfigImporter.NormalizeTree(tree);
        var dict = Assert.IsType<Dictionary<string, object?>>(normalized);
        Assert.Equal("demo", dict["name"]);
        var nested = Assert.IsType<Dictionary<string, object?>>(dict["nested"]);
        Assert.Equal(3, nested["count"]);
    }
}
