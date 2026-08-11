using Ferry.Core.Models;
using Ferry.Core.Services.Form;

namespace Ferry.Core.Services.Session;

/// <summary>
/// 路径解析器：静态字段 http.servers；数组项 http.servers[0]（0 起索引）；
/// 数组项子字段 http.servers[0].server_name。路径与 FormNode.Path 互相可逆。
/// </summary>
public static class PathResolver
{
    public static FormNode? Resolve(IEnumerable<FormNode> roots, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        IEnumerable<FormNode> pool = roots;
        FormNode? current = null;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var (id, index) = ParseSegment(segment);
            var node = pool.FirstOrDefault(n => n.Definition.Id == id);
            if (node is null)
            {
                return null;
            }
            if (index is int itemIndex)
            {
                if (node.Definition.Type != FieldType.Array
                    || itemIndex < 0
                    || itemIndex >= node.Children.Count)
                {
                    return null;
                }
                node = node.Children[itemIndex];
            }
            current = node;
            pool = node.Children;
        }

        return current;
    }

    private static (string Id, int? Index) ParseSegment(string segment)
    {
        var open = segment.IndexOf('[');
        if (open < 0)
        {
            return (segment, null);
        }
        var close = segment.IndexOf(']', open);
        if (close < 0
            || !int.TryParse(segment.AsSpan(open + 1, close - open - 1), out var index))
        {
            return (segment, null);
        }
        return (segment[..open], index);
    }
}
