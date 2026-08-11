using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ferry.Core.Models;

namespace Ferry.Core.Services.Rendering;

/// <summary>
/// 声明式布局渲染器（layout）：插件作者在字段 render 段 + 全局样式中
/// 用纯 YAML 声明"怎么输出"，引擎负责递归、缩进、数组遍历与空值省略。
/// 占位符：{{ . }} 当前值、{{ .key }} 字段键名、{{ .子字段id }} 当前节点子字段。
/// 不暴露任何控制流语法。
/// </summary>
public sealed class LayoutConfigRenderer : IConfigRenderer
{
    private static readonly Regex Placeholder = new(
        @"\{\{\s*\.([\w.]*)\s*\}\}",
        RegexOptions.Compiled);

    private readonly ConfigSchema? _schema;
    private readonly PluginLayoutStyle _style;

    public LayoutConfigRenderer(ConfigSchema? schema, PluginLayoutStyle? style = null)
    {
        _schema = schema;
        _style = style ?? new PluginLayoutStyle();
        Validate(schema);
    }

    public string Render(Dictionary<string, object?> config)
    {
        var sb = new StringBuilder();
        if (_schema?.Fields is not null)
        {
            foreach (var field in _schema.Fields)
            {
                if (config.TryGetValue(field.Id, out var value))
                {
                    RenderField(sb, field, value, 0);
                }
            }
        }
        var text = sb.ToString();
        return text.TrimEnd('\n') + "\n";
    }

    private void RenderField(StringBuilder sb, FieldDefinition field, object? value, int indent)
    {
        switch (value)
        {
            case null:
                return;
            case Dictionary<string, object?> dict:
                RenderObject(sb, field, dict, indent);
                break;
            case List<object?> list:
                RenderArray(sb, field, list, indent);
                break;
            default:
                RenderScalar(sb, field, value, indent);
                break;
        }
    }

    private void RenderScalar(StringBuilder sb, FieldDefinition field, object value, int indent)
    {
        if (field.Render?.Hidden == true)
        {
            return;
        }
        var format = field.Render?.Line ?? _style.Line;
        sb.Append(Indent(indent))
            .Append(Substitute(format, field.Id, value))
            .Append('\n');
    }

    private void RenderObject(
        StringBuilder sb,
        FieldDefinition field,
        Dictionary<string, object?> dict,
        int indent)
    {
        var body = new StringBuilder();
        if (field.Children is not null)
        {
            foreach (var child in field.Children)
            {
                if (dict.TryGetValue(child.Id, out var value))
                {
                    RenderField(body, child, value, indent + 1);
                }
            }
        }

        if (body.Length == 0 && field.Render?.KeepEmpty != true)
        {
            return; // OmitEmpty
        }

        sb.Append(Indent(indent))
            .Append(Substitute(field.Render?.Open ?? _style.BlockOpen, field.Id, dict))
            .Append('\n');
        sb.Append(body);
        sb.Append(Indent(indent))
            .Append(Substitute(field.Render?.Close ?? _style.BlockClose, field.Id, dict))
            .Append('\n');
    }

    private void RenderArray(
        StringBuilder sb,
        FieldDefinition field,
        List<object?> list,
        int indent)
    {
        var itemFormat = field.Render?.Item;
        if (itemFormat is not null)
        {
            // 行形 / 单行：可选块头一次 + 每项一行
            if (list.Count == 0 && field.Render?.KeepEmpty != true)
            {
                return;
            }

            var open = field.Render?.Open;
            if (open is not null)
            {
                sb.Append(Indent(indent)).Append(Substitute(open, field.Id, list)).Append('\n');
            }
            foreach (var item in list)
            {
                if (item is not Dictionary<string, object?> itemDict)
                {
                    continue;
                }
                sb.Append(Indent(indent)).Append(Substitute(itemFormat, field.Id, itemDict)).Append('\n');
            }
            var close = field.Render?.Close;
            if (close is not null)
            {
                sb.Append(Indent(indent)).Append(Substitute(close, field.Id, list)).Append('\n');
            }
            return;
        }

        // 块形：每个元素项一个块（项头 + 递归子字段 + 项闭）
        if (list.Count == 0 && field.Render?.KeepEmpty != true)
        {
            return;
        }

        var itemOpenFormat = field.Render?.ItemOpen ?? _style.BlockOpen;
        var itemCloseFormat = field.Render?.ItemClose ?? _style.BlockClose;
        foreach (var item in list)
        {
            if (item is not Dictionary<string, object?> itemDict)
            {
                continue;
            }

            sb.Append(Indent(indent)).Append(Substitute(itemOpenFormat, field.Id, itemDict)).Append('\n');
            if (field.Children is not null)
            {
                foreach (var child in field.Children)
                {
                    if (child.Render?.Hidden == true)
                    {
                        continue; // 名称字段只用于占位符，不输出行
                    }
                    if (itemDict.TryGetValue(child.Id, out var value))
                    {
                        RenderField(sb, child, value, indent + 1);
                    }
                }
            }
            sb.Append(Indent(indent)).Append(Substitute(itemCloseFormat, field.Id, itemDict)).Append('\n');
        }
    }

    private string Indent(int level)
    {
        var sb = new StringBuilder(_style.Indent.Length * level);
        for (var i = 0; i < level; i++)
        {
            sb.Append(_style.Indent);
        }
        return sb.ToString();
    }

    private static string Substitute(string format, string key, object? current)
    {
        return Placeholder.Replace(format, match =>
        {
            var path = match.Groups[1].Value;
            if (path.Length == 0)
            {
                return FormatValue(current);
            }
            if (path == "key")
            {
                return key;
            }
            if (current is Dictionary<string, object?> dict)
            {
                object? node = dict;
                foreach (var segment in path.Split('.'))
                {
                    if (node is Dictionary<string, object?> d && d.TryGetValue(segment, out var v))
                    {
                        node = v;
                    }
                    else
                    {
                        return string.Empty; // 缺失子字段 → 空（避免控制流，渲染不报错）
                    }
                }
                return FormatValue(node);
            }
            return string.Empty;
        });
    }

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        string s => s,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    /// <summary>
    /// 校验字段 render 段中的占位符：标量行只允许 {{ . }} / {{ .key }}；
    /// 数组项格式只允许引用该字段的子字段；对象块格式不允许引用子字段。
    /// 拼写错误在渲染前直接报错，避免静默输出空值。
    /// </summary>
    private static void Validate(ConfigSchema? schema)
    {
        if (schema?.Fields is null)
        {
            return;
        }
        foreach (var field in schema.Fields)
        {
            ValidateField(field);
        }
    }

    private static void ValidateField(FieldDefinition field)
    {
        var render = field.Render;
        if (render is not null)
        {
            ValidateFormat(render.Line, field, allowCurrent: true, allowChildren: false);
            ValidateFormat(render.Open, field, allowCurrent: false, allowChildren: false);
            ValidateFormat(render.Close, field, allowCurrent: false, allowChildren: false);
            ValidateFormat(render.Item, field, allowCurrent: false, allowChildren: true);
            ValidateFormat(render.ItemOpen, field, allowCurrent: false, allowChildren: true);
            ValidateFormat(render.ItemClose, field, allowCurrent: false, allowChildren: false);
        }

        if (field.Children is not null)
        {
            foreach (var child in field.Children)
            {
                ValidateField(child);
            }
        }
    }

    private static void ValidateFormat(
        string? format,
        FieldDefinition field,
        bool allowCurrent,
        bool allowChildren)
    {
        if (string.IsNullOrEmpty(format))
        {
            return;
        }

        foreach (Match match in Placeholder.Matches(format))
        {
            var path = match.Groups[1].Value;
            if (path == "key")
            {
                continue;
            }
            if (path.Length == 0)
            {
                if (!allowCurrent)
                {
                    throw new FormatException(
                        $"字段「{field.Id}」的渲染格式不允许在此处使用 {{ . }}（当前值）");
                }
                continue;
            }
            if (!allowChildren)
            {
                throw new FormatException(
                    $"字段「{field.Id}」的渲染格式不允许引用子字段 {path}");
            }
            var first = path.Split('.')[0];
            if (field.Children?.Any(c => c.Id == first) != true)
            {
                throw new FormatException(
                    $"字段「{field.Id}」的渲染格式引用了不存在的子字段 {path}");
            }
        }
    }
}
