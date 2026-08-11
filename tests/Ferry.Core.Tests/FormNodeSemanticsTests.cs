using Ferry.Core.Models;
using Ferry.Core.Services;
using Ferry.Core.Services.Form;

namespace Ferry.Core.Tests;

public class FormNodeSemanticsTests
{
    private static ConfigSchema CreateModuleSchema() => new()
    {
        Fields =
        [
            new FieldDefinition
            {
                Id = "http",
                Type = FieldType.Object,
                Module = true,
                Children =
                [
                    new FieldDefinition { Id = "sendfile", Type = FieldType.String },
                    new FieldDefinition
                    {
                        Id = "servers",
                        Type = FieldType.Object,
                        Module = true,
                        Children =
                        [
                            new FieldDefinition { Id = "listen", Type = FieldType.Number }
                        ]
                    }
                ]
            }
        ]
    };

    private static (FormNode Http, FormNode Servers) BuildModules()
    {
        var roots = FormBuilder.Build(CreateModuleSchema());
        var http = roots.Single(r => r.Definition.Id == "http");
        var servers = http.Children.Single(c => c.Definition.Id == "servers");
        return (http, servers);
    }

    [Fact]
    public void EnableChild_CascadesToAncestors()
    {
        var (http, servers) = BuildModules();

        http.SetEnabled(false);
        servers.SetEnabled(false);
        Assert.False(http.IsEnabled);
        Assert.False(servers.IsEnabled);
        Assert.False(servers.IsSelectable);

        servers.SetEnabled(true);
        Assert.True(http.IsEnabled);
        Assert.True(servers.IsEnabled);
    }

    [Fact]
    public void DisableParent_KeepsChildStateAndValues()
    {
        var (http, servers) = BuildModules();
        servers.Children.Single(c => c.Definition.Id == "listen").Value = 8080;

        http.SetEnabled(false);

        Assert.True(servers.IsEnabled);
        Assert.Equal(8080, servers.Children.Single(c => c.Definition.Id == "listen").Value);
    }

    [Fact]
    public void Restore_WithExactStates_DoesNotCascade()
    {
        var roots = FormBuilder.Build(
            CreateModuleSchema(),
            enabledStates: new Dictionary<string, bool>
            {
                ["http"] = false,
                ["http.servers"] = true
            });

        var http = roots.Single(r => r.Definition.Id == "http");
        var servers = http.Children.Single(c => c.Definition.Id == "servers");

        Assert.False(http.IsEnabled);
        Assert.True(servers.IsEnabled);
    }

    [Fact]
    public void RequiredField_CannotBeDisabled()
    {
        var schema = new ConfigSchema
        {
            Fields = [new FieldDefinition { Id = "app_name", Type = FieldType.String, Required = true }]
        };
        var roots = FormBuilder.Build(schema);
        var field = roots.Single();

        field.SetEnabled(false);

        Assert.True(field.IsEnabled);
        Assert.False(field.CanToggleEnabled);
    }

    [Fact]
    public void ArrayAddRemove_PathsArePositionalAndStable()
    {
        var schema = new ConfigSchema
        {
            Fields =
            [
                new FieldDefinition
                {
                    Id = "servers",
                    Type = FieldType.Array,
                    Children = [new FieldDefinition { Id = "host", Type = FieldType.String }]
                }
            ]
        };
        var roots = FormBuilder.Build(schema);
        var array = roots.Single();

        var first = array.AddItem();
        var second = array.AddItem();
        Assert.Equal("servers[0]", first.Path);
        Assert.Equal("servers[1]", second.Path);
        Assert.Equal("servers[0].host", first.Children.Single().Path);

        first.RemoveItem();
        Assert.Equal("servers[0]", second.Path);
    }

    [Fact]
    public void ModuleCounts_ReflectChildModuleStates()
    {
        var (http, servers) = BuildModules();

        Assert.Equal(1, http.TotalChildModulesCount);
        Assert.Equal(1, http.EnabledChildModulesCount);
        Assert.Equal("1/1", http.EnabledChildModulesText);

        servers.SetEnabled(false);
        Assert.Equal(0, http.EnabledChildModulesCount);
        Assert.Equal("0/1", http.EnabledChildModulesText);
    }
}
