using System.Globalization;
using System.Text;
using Ferry.Core.Models;

namespace Ferry.Core.Services.Rendering;

/// <summary>
/// 内置 INI 渲染器：标量 key = value；Object → [路径] 节；数组项 → [路径.N]（N 从 1 起）；
/// Boolean → true/false。
/// </summary>
public sealed class IniConfigRenderer : IConfigRenderer
{
    private readonly ConfigSchema? _schema;

    public IniConfigRenderer(ConfigSchema? schema = null)
    {
        _schema = schema;
    }

    public string Render(Dictionary<string, object?> config)
    {
        var sb = new StringBuilder();
        IEnumerable<FieldDefinition> fields = _schema?.Fields
            ?? config.Keys.Select(k => new FieldDefinition { Id = k }).ToList();

        foreach (var field in fields)
        {
            if (config.TryGetValue(field.Id, out var value))
            {
                RenderField(sb, field, value, field.Id, field.Id);
            }
        }
        return sb.ToString();
    }

    private void RenderField(
        StringBuilder sb,
        FieldDefinition field,
        object? value,
        string path,
        string sectionKey)
    {
        switch (value)
        {
            case Dictionary<string, object?> dict:
                sb.Append('[').Append(path).Append(']').Append('\n');
                if (field.Children is not null)
                {
                    foreach (var child in field.Children)
                    {
                        if (dict.TryGetValue(child.Id, out var childValue))
                        {
                            RenderField(sb, child, childValue, $"{path}.{child.Id}", child.Id);
                        }
                    }
                }
                break;
            case List<object?> list:
                var index = 0;
                foreach (var item in list)
                {
                    if (item is not Dictionary<string, object?> itemDict)
                    {
                        continue;
                    }
                    index++;
                    var itemPath = $"{path}.{index}";
                    sb.Append('[').Append(itemPath).Append(']').Append('\n');
                    if (field.Children is not null)
                    {
                        foreach (var child in field.Children)
                        {
                            if (itemDict.TryGetValue(child.Id, out var childValue))
                            {
                                RenderField(sb, child, childValue, $"{itemPath}.{child.Id}", child.Id);
                            }
                        }
                    }
                }
                break;
            default:
                sb.Append(sectionKey).Append(" = ").Append(FormatValue(value)).Append('\n');
                break;
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        string s => s,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
