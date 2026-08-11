using Ferry.Core.Models;

namespace Ferry.Core.Services.Form;

/// <summary>表单构建器：ConfigSchema → FormNode 树，可选值回填与启用状态恢复。</summary>
public static class FormBuilder
{
    public static List<FormNode> Build(
        ConfigSchema schema,
        Dictionary<string, object?>? values = null,
        Dictionary<string, bool>? enabledStates = null)
    {
        var result = new List<FormNode>();
        if (schema?.Fields is null)
        {
            return result;
        }

        foreach (var field in schema.Fields)
        {
            result.Add(FormNode.CreateFromDefinition(field, values: values));
        }

        if (enabledStates is not null && enabledStates.Count > 0)
        {
            ApplyEnabledStates(result, enabledStates);
        }

        return result;
    }

    /// <summary>
    /// 按字段路径应用保存的启用状态（精确恢复，不做级联，避免破坏
    /// "父停用但子保留"的保存状态）。required 字段由 SetEnabled 守卫保护。
    /// </summary>
    public static void ApplyEnabledStates(
        IEnumerable<FormNode> roots,
        Dictionary<string, bool> enabledStates)
    {
        foreach (var root in roots)
        {
            ApplyEnabledState(root, enabledStates);
        }
    }

    private static void ApplyEnabledState(FormNode node, Dictionary<string, bool> enabledStates)
    {
        if (!node.IsArrayItem && enabledStates.TryGetValue(node.Path, out var enabled))
        {
            node.SetEnabled(enabled, cascadeUp: false);
        }
        foreach (var child in node.Children)
        {
            ApplyEnabledState(child, enabledStates);
        }
    }
}
