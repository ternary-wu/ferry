using Ferry.Core.Models;
using Ferry.Core.Services.Form;

namespace Ferry.Core.Services;

/// <summary>
/// 值收集器：把表单树收集为与格式无关的嵌套字典值树。
/// includeDisabled=true 时全量收集（含停用模块），用于工作区持久化，
/// 保证停用字段的值在重新启用后不丢失。
/// </summary>
public static class ConfigValueCollector
{
    public static Dictionary<string, object?> Collect(
        IEnumerable<FormNode> roots,
        bool includeDisabled = false)
    {
        var result = new Dictionary<string, object?>();
        foreach (var node in roots)
        {
            if (!includeDisabled && !node.IsVisible)
            {
                continue;
            }
            var value = CollectField(node, includeDisabled);
            if (value is not null)
            {
                result[node.Definition.Id] = value;
            }
        }
        return result;
    }

    private static object? CollectField(FormNode node, bool includeDisabled)
    {
        // 字段未启用：整棵子树不写入输出（但 UI 中仍可见、可检视、可编辑）。
        if (!includeDisabled && !node.IsEnabled)
        {
            return null;
        }

        return node.Definition.Type switch
        {
            FieldType.Object => CollectObject(node, includeDisabled),
            FieldType.Array => CollectArray(node, includeDisabled),
            _ => NormalizeScalar(ConfigValueConverter.Coerce(node.Definition.Type, node.Value))
        };
    }

    private static Dictionary<string, object?>? CollectObject(FormNode node, bool includeDisabled)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var child in node.Children)
        {
            if (!includeDisabled && !child.IsVisible)
            {
                continue;
            }
            var value = CollectField(child, includeDisabled);
            if (value is not null)
            {
                dict[child.Definition.Id] = value;
            }
        }
        return dict;
    }

    private static List<object?>? CollectArray(FormNode node, bool includeDisabled)
    {
        var list = new List<object?>();
        foreach (var item in node.Children)
        {
            var value = CollectField(item, includeDisabled);
            if (value is not null)
            {
                list.Add(value);
            }
        }
        return list;
    }

    /// <summary>空字符串视为"未设置"，统一丢弃（模板里空串会被当作真值，容易走错分支）。</summary>
    private static object? NormalizeScalar(object? value)
        => value is string s && s.Length == 0 ? null : value;
}
