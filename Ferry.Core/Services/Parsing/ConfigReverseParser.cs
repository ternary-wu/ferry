using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ferry.Core.Models;

namespace Ferry.Core.Services.Parsing;

/// <summary>解析报告：识别字段数、未识别行数与示例片段。</summary>
public sealed record ParseReport(
    int RecognizedFields,
    int UnrecognizedLines,
    List<string> UnrecognizedSnippets);

/// <summary>反向解析结果：值树 + 未识别内容（原样保留，不主动丢弃）。</summary>
public sealed class ParseResult
{
    public Dictionary<string, object?> Values { get; init; } = new();
    public List<string> Unrecognized { get; init; } = new();
    public ParseReport Report { get; init; } = new(0, 0, new List<string>());
}

/// <summary>
/// 自定义格式反向解析（M4）：json/yaml 走 ConfigImporter；
/// layout/ini 用"宽松解析"按 schema 的字段 id 与 render 前缀生成扫描规则，
/// 块形按缩进/括号识别，未知内容原样保留并计入报告。
/// </summary>
public static class ConfigReverseParser
{
    private static readonly Regex Placeholder = new(
        @"\{\{\s*\.([\w.]*)\s*\}\}",
        RegexOptions.Compiled);

    public static ParseResult Parse(PluginDescriptor plugin, string text)
    {
        return plugin.RendererType switch
        {
            "json" or "yaml" => ParseStructured(plugin, text),
            "ini" => ParseIni(plugin, text),
            _ => ParseLayout(plugin, text)
        };
    }

    /// <summary>导出时可选追加未识别内容（不主动丢弃）。</summary>
    public static string AppendUnrecognized(string rendered, IEnumerable<string> unrecognized)
    {
        var lines = unrecognized.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0)
        {
            return rendered;
        }
        return rendered.TrimEnd('\n') + "\n" + string.Join('\n', lines) + "\n";
    }

    private static ParseResult ParseStructured(PluginDescriptor plugin, string text)
    {
        var values = ConfigImporter.Parse(plugin, text);
        return new ParseResult
        {
            Values = values,
            Report = new ParseReport(values.Count, 0, new List<string>())
        };
    }

    // ---------- INI ----------

    private static ParseResult ParseIni(PluginDescriptor plugin, string text)
    {
        var unrecognized = new List<string>();
        var values = new Dictionary<string, object?>();
        var path = new List<string>();
        var recognized = 0;

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith('#'))
            {
                unrecognized.Add(rawLine);
                continue;
            }
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                path = trimmed[1..^1]
                    .Split('.', StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                continue;
            }
            var eq = trimmed.IndexOf('=');
            if (eq < 0)
            {
                unrecognized.Add(rawLine);
                continue;
            }
            var key = trimmed[..eq].Trim();
            var rawValue = trimmed[(eq + 1)..].Trim();
            var field = ResolveIniField(plugin.Schema, path, key);
            if (field is null)
            {
                unrecognized.Add(rawLine);
                continue;
            }
            if (ResolveIniDict(values, plugin.Schema, path) is { } dict)
            {
                dict[key] = ParseScalar(field, rawValue);
                recognized++;
            }
            else
            {
                unrecognized.Add(rawLine);
            }
        }

        return BuildResult(values, unrecognized, recognized);
    }

    private static FieldDefinition? ResolveIniField(ConfigSchema? schema, IReadOnlyList<string> path, string key)
    {
        IEnumerable<FieldDefinition>? fields = schema?.Fields;
        FieldDefinition? current = null;
        foreach (var segment in path)
        {
            if (int.TryParse(segment, out _))
            {
                continue; // 数组索引段，跳过
            }
            var field = fields?.FirstOrDefault(f => f.Id == segment);
            if (field is null)
            {
                return null;
            }
            current = field;
            fields = field.Children;
        }
        return fields?.FirstOrDefault(f => f.Id == key);
    }

    private static Dictionary<string, object?>? ResolveIniDict(
        Dictionary<string, object?> root,
        ConfigSchema? schema,
        IReadOnlyList<string> path)
    {
        object? container = root;
        IEnumerable<FieldDefinition>? fields = schema?.Fields;
        foreach (var segment in path)
        {
            if (int.TryParse(segment, out var index))
            {
                if (container is not List<object?> list)
                {
                    return null;
                }
                index--; // ini 段编号从 1 起
                while (list.Count <= index)
                {
                    list.Add(new Dictionary<string, object?>());
                }
                container = list[index]!;
                continue;
            }
            var field = fields?.FirstOrDefault(f => f.Id == segment);
            if (field is null || container is not Dictionary<string, object?> dict)
            {
                return null;
            }
            container = field.Type switch
            {
                FieldType.Array => GetOrCreateList(dict, field.Id),
                FieldType.Object => GetOrCreateDict(dict, field.Id),
                _ => null
            };
            if (container is null)
            {
                return null;
            }
            fields = field.Children;
        }
        return container as Dictionary<string, object?>;
    }

    // ---------- layout ----------

    private static ParseResult ParseLayout(PluginDescriptor plugin, string text)
    {
        var style = plugin.RendererConfig.Layout;
        var rootMatchers = BuildMatchers(plugin.Schema, style);
        var rootDict = new Dictionary<string, object?>();
        var unrecognized = new List<string>();
        var recognized = 0;
        var indentLen = Math.Max(1, style.Indent.Length);
        var scopes = new List<Scope>
        {
            new(rootMatchers, rootDict, null, null, openedByBrace: false, isUnknown: false)
        };

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }
            var trimmed = rawLine.Trim();
            var leading = rawLine.TakeWhile(char.IsWhiteSpace).Count();
            var indent = leading == 0 ? 0 : Math.Max(1, leading / indentLen);

            if (trimmed.StartsWith('#') || trimmed.StartsWith("//"))
            {
                unrecognized.Add(rawLine);
                continue;
            }

            if (scopes[^1].IsUnknown)
            {
                unrecognized.Add(rawLine);
                if (IsClosingBrace(trimmed))
                {
                    scopes.RemoveAt(scopes.Count - 1);
                }
                continue;
            }

            // 缩进回退：仅弹出非大括号作用域
            while (scopes.Count - 1 > indent && !scopes[^1].OpenedByBrace)
            {
                scopes.RemoveAt(scopes.Count - 1);
            }

            var current = scopes[^1];
            if (IsClosingBrace(trimmed))
            {
                if (scopes.Count > 1)
                {
                    scopes.RemoveAt(scopes.Count - 1);
                }
                continue;
            }

            if (trimmed.EndsWith('{') || trimmed.EndsWith(':'))
            {
                var match = MatchBlock(current.Matchers!, trimmed);
                if (match is null)
                {
                    scopes.Add(new Scope(null, null, null, null, openedByBrace: false, isUnknown: true));
                    unrecognized.Add(rawLine);
                    continue;
                }
                recognized++;
                switch (match.Kind)
                {
                    case MatchKind.Object:
                    {
                        var objDict = new Dictionary<string, object?>();
                        current.Dict![match.Node.Field.Id] = objDict;
                        scopes.Add(new Scope(
                            match.Node.Children,
                            objDict,
                            null,
                            null,
                            openedByBrace: trimmed.EndsWith('{'),
                            isUnknown: false));
                        break;
                    }
                    case MatchKind.ArrayItem:
                    {
                        var list = GetOrCreateList(current.Dict!, match.Node.Field.Id);
                        var item = CreateItemFromTemplate(match.Node.Field, match.Match!);
                        list.Add(item);
                        scopes.Add(new Scope(
                            match.Node.Children,
                            item,
                            null,
                            null,
                            openedByBrace: trimmed.EndsWith('{'),
                            isUnknown: false));
                        break;
                    }
                    case MatchKind.ArrayLineContext:
                    {
                        var arrayList = GetOrCreateList(current.Dict!, match.Node.Field.Id);
                        var scope = new Scope(
                            match.Node.Children,
                            null,
                            match.Node.ItemRegex,
                            arrayList,
                            openedByBrace: false,
                            isUnknown: false)
                        {
                            OwnerArray = match.Node.Field
                        };
                        scopes.Add(scope);
                        break;
                    }
                }
                continue;
            }

            // 行形数组项：当前处于 open 头作用域内
            if (current.ItemRegex is not null && current.ArrayList is not null && current.OwnerArray is not null)
            {
                var itemMatch = current.ItemRegex.Match(trimmed);
                if (itemMatch.Success)
                {
                    current.ArrayList.Add(CreateItemFromTemplate(current.OwnerArray, itemMatch));
                    recognized++;
                    continue;
                }
                unrecognized.Add(rawLine);
                continue;
            }

            if (current.Dict is not null && TryMatchLine(current.Matchers!, trimmed, current.Dict))
            {
                recognized++;
                continue;
            }

            unrecognized.Add(rawLine);
        }

        return BuildResult(rootDict, unrecognized, recognized);
    }

    private static ParseResult BuildResult(
        Dictionary<string, object?> values,
        List<string> unrecognized,
        int recognized)
        => new()
        {
            Values = values,
            Unrecognized = unrecognized,
            Report = new ParseReport(
                recognized,
                unrecognized.Count,
                unrecognized.Take(5).ToList())
        };

    private static bool IsClosingBrace(string trimmed)
        => trimmed == "}" || trimmed.EndsWith('}');

    private static bool TryMatchLine(
        IReadOnlyList<MatcherNode> matchers,
        string line,
        Dictionary<string, object?> dict)
    {
        // 1) 行形数组项（如 nginx upstream 内的 server 行）
        foreach (var matcher in matchers)
        {
            if (matcher.Field.Type == FieldType.Array && matcher.ItemRegex is not null)
            {
                var match = matcher.ItemRegex.Match(line);
                if (match.Success)
                {
                    GetOrCreateList(dict, matcher.Field.Id)
                        .Add(CreateItemFromTemplate(matcher.Field, match));
                    return true;
                }
            }
        }

        // 2) 标量：字段 id 或 line 前缀匹配
        var key = FirstToken(line);
        foreach (var matcher in matchers)
        {
            var def = matcher.Field;
            if (def.Type is FieldType.Object or FieldType.Array)
            {
                continue;
            }
            var keyMatch = key == def.Id;
            var prefixMatch = matcher.LinePrefix is not null
                && line.StartsWith(matcher.LinePrefix, StringComparison.Ordinal);
            if (!keyMatch && !prefixMatch)
            {
                continue;
            }
            dict[def.Id] = ExtractScalarValue(def, matcher.LineRegex, line, key);
            return true;
        }

        return false;
    }

    private static MatchResult? MatchBlock(IReadOnlyList<MatcherNode> matchers, string line)
    {
        var key = FirstToken(line);
        foreach (var matcher in matchers)
        {
            var def = matcher.Field;
            if (def.Type == FieldType.Object)
            {
                var openPrefixMatch = matcher.OpenPrefix is not null
                    && line.StartsWith(matcher.OpenPrefix, StringComparison.Ordinal);
                if (key == def.Id || openPrefixMatch)
                {
                    return new MatchResult(matcher, MatchKind.Object, null);
                }
            }
            else if (def.Type == FieldType.Array && matcher.ItemOpenRegex is not null)
            {
                var match = matcher.ItemOpenRegex.Match(line);
                if (match.Success)
                {
                    return new MatchResult(matcher, MatchKind.ArrayItem, match);
                }
            }
            else if (def.Type == FieldType.Array
                && matcher.ItemRegex is not null
                && matcher.OpenPrefix is not null
                && line.StartsWith(matcher.OpenPrefix, StringComparison.Ordinal))
            {
                return new MatchResult(matcher, MatchKind.ArrayLineContext, null);
            }
        }
        return null;
    }

    private static List<MatcherNode> BuildMatchers(ConfigSchema? schema, PluginLayoutStyle style)
    {
        var result = new List<MatcherNode>();
        if (schema?.Fields is null)
        {
            return result;
        }
        foreach (var field in schema.Fields)
        {
            result.Add(BuildNode(field, style));
        }
        return result;
    }

    private static MatcherNode BuildNode(FieldDefinition field, PluginLayoutStyle style)
    {
        var render = field.Render;
        var lineFormat = render?.Line ?? style.Line;
        var node = new MatcherNode
        {
            Field = field,
            LinePrefix = LiteralPrefix(lineFormat),
            OpenPrefix = LiteralPrefix(render?.Open ?? style.BlockOpen),
            ItemOpenRegex = render?.ItemOpen is null ? null : BuildTemplateRegex(render.ItemOpen),
            ItemRegex = render?.Item is null ? null : BuildTemplateRegex(render.Item),
            LineRegex = BuildTemplateRegex(lineFormat)
        };
        if (field.Children is not null)
        {
            foreach (var child in field.Children)
            {
                node.Children.Add(BuildNode(child, style));
            }
        }
        return node;
    }

    private static string? LiteralPrefix(string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return null;
        }
        var index = format.IndexOf("{{", StringComparison.Ordinal);
        var prefix = (index < 0 ? format : format[..index]).Trim();
        return prefix.Length == 0 ? null : prefix;
    }

    private static Regex BuildTemplateRegex(string format)
    {
        var sb = new StringBuilder("^");
        var pos = 0;
        foreach (Match match in Placeholder.Matches(format))
        {
            sb.Append(Regex.Escape(format[pos..match.Index]));
            var path = match.Groups[1].Value;
            var name = string.IsNullOrEmpty(path) ? "value" : path.Replace('.', '_');
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                name = "p" + Math.Abs(name.GetHashCode());
            }
            sb.Append("(?<").Append(name).Append(">.*?)");
            pos = match.Index + match.Length;
        }
        sb.Append(Regex.Escape(format[pos..]));
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Singleline);
    }

    private static Dictionary<string, object?> CreateItemFromTemplate(
        FieldDefinition array,
        Match match)
    {
        var item = new Dictionary<string, object?>();
        foreach (var child in array.Children ?? new List<FieldDefinition>())
        {
            var group = match.Groups[child.Id.Replace('.', '_')];
            if (group.Success && group.Length > 0)
            {
                item[child.Id] = ParseScalar(child, group.Value);
            }
        }
        return item;
    }

    private static object? ExtractScalarValue(
        FieldDefinition def,
        Regex? lineRegex,
        string line,
        string key)
    {
        if (lineRegex is not null)
        {
            var match = lineRegex.Match(line);
            if (match.Success && match.Groups["value"].Success)
            {
                return ParseScalar(def, match.Groups["value"].Value);
            }
        }
        var rest = line[key.Length..].Trim();
        if (rest.StartsWith('='))
        {
            rest = rest[1..].Trim();
        }
        if (rest.EndsWith(';'))
        {
            rest = rest[..^1].Trim();
        }
        return ParseScalar(def, rest);
    }

    private static object? ParseScalar(FieldDefinition def, string raw)
    {
        var text = raw.Trim();
        if (text.EndsWith(';'))
        {
            text = text[..^1].Trim();
        }
        switch (def.Type)
        {
            case FieldType.Number:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lng))
                {
                    return lng;
                }
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl))
                {
                    return dbl;
                }
                return text;
            case FieldType.Boolean:
                if (bool.TryParse(text, out var b))
                {
                    return b;
                }
                return text;
            default:
                return text;
        }
    }

    private static string FirstToken(string line)
    {
        var i = 0;
        while (i < line.Length
            && !char.IsWhiteSpace(line[i])
            && line[i] != '='
            && line[i] != '{'
            && line[i] != ';')
        {
            i++;
        }
        return line[..i];
    }

    private static List<object?> GetOrCreateList(Dictionary<string, object?> dict, string id)
    {
        if (dict.TryGetValue(id, out var existing) && existing is List<object?> list)
        {
            return list;
        }
        var created = new List<object?>();
        dict[id] = created;
        return created;
    }

    private static Dictionary<string, object?> GetOrCreateDict(Dictionary<string, object?> dict, string id)
    {
        if (dict.TryGetValue(id, out var existing) && existing is Dictionary<string, object?> inner)
        {
            return inner;
        }
        var created = new Dictionary<string, object?>();
        dict[id] = created;
        return created;
    }

    private enum MatchKind
    {
        Object,
        ArrayItem,
        ArrayLineContext
    }

    private sealed record MatchResult(MatcherNode Node, MatchKind Kind, Match? Match);

    private sealed class MatcherNode
    {
        public required FieldDefinition Field { get; init; }
        public List<MatcherNode> Children { get; } = new();
        public string? LinePrefix { get; init; }
        public string? OpenPrefix { get; init; }
        public Regex? ItemOpenRegex { get; init; }
        public Regex? ItemRegex { get; init; }
        public Regex? LineRegex { get; init; }
    }

    private sealed class Scope
    {
        public Scope(
            List<MatcherNode>? matchers,
            Dictionary<string, object?>? dict,
            Regex? itemRegex,
            List<object?>? arrayList,
            bool openedByBrace,
            bool isUnknown)
        {
            Matchers = matchers;
            Dict = dict;
            ItemRegex = itemRegex;
            ArrayList = arrayList;
            OpenedByBrace = openedByBrace;
            IsUnknown = isUnknown;
            OwnerArray = null;
        }

        public List<MatcherNode>? Matchers { get; }
        public Dictionary<string, object?>? Dict { get; }
        public Regex? ItemRegex { get; }
        public List<object?>? ArrayList { get; }
        public FieldDefinition? OwnerArray { get; set; }
        public bool OpenedByBrace { get; }
        public bool IsUnknown { get; }
    }
}
