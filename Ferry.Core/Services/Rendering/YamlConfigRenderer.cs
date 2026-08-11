using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ferry.Core.Services.Rendering;

/// <summary>内置 YAML 渲染器。</summary>
public sealed class YamlConfigRenderer : IConfigRenderer
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public string Render(Dictionary<string, object?> config)
        => Serializer.Serialize(config);
}
