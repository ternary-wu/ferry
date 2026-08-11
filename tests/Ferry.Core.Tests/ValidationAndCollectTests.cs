using Ferry.Core.Models;
using Ferry.Core.Services;
using Ferry.Core.Services.Form;

namespace Ferry.Core.Tests;

public class ValidationAndCollectTests
{
    private static ConfigSchema CreateSchema() => new()
    {
        Fields =
        [
            new FieldDefinition { Id = "app_name", Label = "应用名称", Type = FieldType.String, Required = true },
            new FieldDefinition
            {
                Id = "worker_connections",
                Label = "连接数",
                Type = FieldType.Number,
                Min = 1,
                Max = 1048576,
                IntegerOnly = true
            },
            new FieldDefinition
            {
                Id = "mode",
                Label = "模式",
                Type = FieldType.Enum,
                EnumOptions = [new EnumOption { Value = "proxy" }, new EnumOption { Value = "static" }]
            },
            new FieldDefinition
            {
                Id = "proxy_pass",
                Label = "代理地址",
                Type = FieldType.String,
                VisibilityDependency = new DependencyRule { DependsOnField = "mode", ExpectedValue = "proxy" }
            },
            new FieldDefinition
            {
                Id = "host_pattern",
                Label = "主机名",
                Type = FieldType.String,
                Validations = new Dictionary<string, object> { ["pattern"] = "^[a-z0-9.-]+$" }
            }
        ]
    };

    [Fact]
    public void Validate_ReportsRequiredNumberEnumAndPatternErrors()
    {
        var schema = CreateSchema();
        var roots = FormBuilder.Build(schema, new Dictionary<string, object?>
        {
            ["app_name"] = "",
            ["worker_connections"] = "1.5",
            ["mode"] = "other",
            ["host_pattern"] = "BAD_HOST!"
        });

        var errors = ConfigValidator.Validate(roots);

        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, e => e.Contains("app_name"));
        Assert.Contains(errors, e => e.Contains("worker_connections"));
        Assert.Contains(errors, e => e.Contains("mode"));
        Assert.Contains(errors, e => e.Contains("host_pattern"));
    }

    [Fact]
    public void Validate_PassesOnValidValues()
    {
        var schema = CreateSchema();
        var roots = FormBuilder.Build(schema, new Dictionary<string, object?>
        {
            ["app_name"] = "demo",
            ["worker_connections"] = 1024,
            ["mode"] = "proxy",
            ["proxy_pass"] = "http://127.0.0.1",
            ["host_pattern"] = "web-01"
        });

        var errors = ConfigValidator.Validate(roots);
        Assert.Empty(errors);
    }

    [Fact]
    public void Collect_ExcludesDisabledAndHidden_IncludeDisabledKeepsAll()
    {
        var schema = CreateSchema();
        var roots = FormBuilder.Build(schema, new Dictionary<string, object?>
        {
            ["app_name"] = "demo",
            ["worker_connections"] = 1024,
            ["mode"] = "static",
            ["proxy_pass"] = "http://127.0.0.1",
            ["host_pattern"] = "web-01"
        });

        // mode=static → proxy_pass 隐藏；host_pattern 停用。
        roots.Single(r => r.Definition.Id == "host_pattern").SetEnabled(false);

        var collected = ConfigValueCollector.Collect(roots);
        Assert.False(collected.ContainsKey("host_pattern"));
        Assert.False(collected.ContainsKey("proxy_pass"));
        Assert.True(collected.ContainsKey("worker_connections"));

        var full = ConfigValueCollector.Collect(roots, includeDisabled: true);
        Assert.Equal("web-01", full["host_pattern"]);
        Assert.Equal("http://127.0.0.1", full["proxy_pass"]);
    }

    [Fact]
    public void Collect_CoercesNumbersAndDropsEmptyStrings()
    {
        var schema = CreateSchema();
        var roots = FormBuilder.Build(schema, new Dictionary<string, object?>
        {
            ["app_name"] = "demo",
            ["worker_connections"] = "1024",
            ["mode"] = "static",
            ["host_pattern"] = ""
        });

        var collected = ConfigValueCollector.Collect(roots);
        Assert.Equal(1024L, collected["worker_connections"]);
        Assert.False(collected.ContainsKey("host_pattern"));
    }
}
