using System.Globalization;
using System.Text.Json.Nodes;
using Ferry.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ferry.Core.Services;

/// <summary>
/// 配置导入器：把 JSON / YAML 文本解析为统一的值树（嵌套字典），供表单回填。
/// v2 v1 仍仅支持 json / yaml；layout/ini 反向解析在后续里程碑实现。
/// </summary>
public static class ConfigImporter
{
    public static Dictionary<string, object?> Parse(PluginDescriptor plugin, string text)
    {
        return plugin.RendererType switch
        {
            "json" => ParseJson(text),
            "yaml" => ParseYaml(text),
            _ => throw new NotSupportedException(
                $"渲染器 {plugin.RendererConfig.Type} 暂不支持导入（仅 json/yaml 支持导入）")
        };
    }

    public static Dictionary<string, object?> ParseJson(string text)
    {
        var node = JsonNode.Parse(text) as JsonObject
            ?? throw new FormatException("JSON 根节点必须是对象");
        return FromJsonObject(node);
    }

    public static Dictionary<string, object?> ParseYaml(string text)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var root = deserializer.Deserialize<object>(text);
        var dict = NormalizeTree(root) as Dictionary<string, object?>
            ?? throw new FormatException("YAML 根节点必须是映射");
        return dict;
    }

    public static Dictionary<string, object?> FromJsonObject(JsonObject obj)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kv in obj)
        {
            result[kv.Key] = ConvertJsonNode(kv.Value);
        }
        return result;
    }

    public static object? ConvertJsonNode(JsonNode? node) => node switch
    {
        null => null,
        JsonObject o => FromJsonObject(o),
        JsonArray a => a.Select(ConvertJsonNode).ToList(),
        JsonValue v => v.TryGetValue<long>(out var l) ? l
            : v.TryGetValue<double>(out var d) ? d
            : v.TryGetValue<bool>(out var b) ? b
            : v.TryGetValue<string>(out var s) ? s
            : v.ToJsonString(),
        _ => null
    };

    /// <summary>把 YamlDotNet 反序列化产生的混合类型树归一化为值树（预设值等复用）。</summary>
    public static object? NormalizeTree(object? value) => value switch
    {
        Dictionary<object, object> dict =>
            dict.ToDictionary(kv => kv.Key?.ToString() ?? string.Empty, kv => NormalizeTree(kv.Value)),
        Dictionary<string, object?> dict =>
            dict.ToDictionary(kv => kv.Key, kv => NormalizeTree(kv.Value)),
        List<object> list => list.Select(NormalizeTree).ToList(),
        string s => CoerceScalarString(s),
        _ => value
    };

    /// <summary>YamlDotNet 反序列化为 object 时标量可能以字符串返回，按内容还原为 bool/int/long/double。</summary>
    private static object CoerceScalarString(string s)
    {
        if (bool.TryParse(s, out var b))
        {
            return b;
        }
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            return i;
        }
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return s;
    }
}
