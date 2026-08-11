using System.Text.Json;

namespace Ferry.Core.Services.Rendering;

/// <summary>内置 JSON 渲染器（缩进输出）。</summary>
public sealed class JsonConfigRenderer : IConfigRenderer
{
    public string Render(Dictionary<string, object?> config)
        => JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
}
