using Ferry.Core.Models;

namespace Ferry.Core.Services.Form;

/// <summary>
/// 表单树节点（v2 领域模型）：纯数据 + 行为，不依赖任何 UI 事件。
/// UI 通过快照 DTO 读取状态；本节点只维护值、启用状态与父子关系，
/// 可见性/可选性/N-M 计数均为派生计算，保证一致性。
/// </summary>
public sealed class FormNode
{
    private long _nextItemSequence;

    public FormNode(FieldDefinition definition)
    {
        Definition = definition;
        Value = definition.DefaultValue;
    }

    public FieldDefinition Definition { get; }
    public FormNode? Parent { get; private set; }
    public List<FormNode> Children { get; } = new();
    public object? Value { get; set; }
    public string? ValidationError { get; set; }

    /// <summary>字段启用状态：false 时该项（含子内容）从输出中移除；required 字段不可关闭。</summary>
    public bool IsEnabled { get; private set; } = true;

    public bool IsArrayItem => Parent?.Definition.Type == FieldType.Array;
    public bool IsModule => Definition.Module;

    /// <summary>
    /// 字段路径（PathResolver 规则）：静态字段 http.servers，数组项 http.servers[0]，
    /// 数组项子字段 http.servers[0].server_name。
    /// </summary>
    public string Path
    {
        get
        {
            if (Parent is null)
            {
                return Definition.Id;
            }
            if (Parent.Definition.Type == FieldType.Array)
            {
                return $"{Parent.Path}[{Parent.Children.IndexOf(this)}]";
            }
            return $"{Parent.Path}.{Definition.Id}";
        }
    }

    /// <summary>
    /// 可见性（依赖显隐）：向上查找最近的同名祖先字段，值匹配 ExpectedValue 才可见。
    /// 未找到依赖字段时按 MVP 语义隐藏。
    /// </summary>
    public bool IsVisible
    {
        get
        {
            var rule = Definition.VisibilityDependency;
            if (rule is null)
            {
                return true;
            }
            var ancestor = Parent;
            while (ancestor is not null)
            {
                if (ancestor.Definition.Id == rule.DependsOnField)
                {
                    return ancestor.Value?.ToString() == rule.ExpectedValue?.ToString();
                }
                ancestor = ancestor.Parent;
            }
            return false;
        }
    }

    /// <summary>勾选框是否可操作（所有祖先已启用）。</summary>
    public bool IsSelectable => IsAncestorsEnabled();

    /// <summary>勾选框最终可用性：祖先已启用且非必填。</summary>
    public bool CanToggleEnabled => IsSelectable && !Definition.Required;

    public int TotalChildModulesCount => Children.Count(c => c.Definition.Module);
    public int EnabledChildModulesCount => Children.Count(c => c.Definition.Module && c.IsEnabled);
    public string EnabledChildModulesText =>
        TotalChildModulesCount == 0 ? string.Empty : $"{EnabledChildModulesCount}/{TotalChildModulesCount}";

    /// <summary>
    /// 设置启用状态（勾选语义 v3）：
    /// cascadeUp=true（用户/预设触发）：启用时自动级联启用所有祖先；
    /// cascadeUp=false（工作区精确恢复）：不做级联，避免"父停用但子保留"的保存状态被恢复破坏。
    /// 取消父模块不重置子模块状态与值。
    /// </summary>
    public void SetEnabled(bool value, bool cascadeUp = true)
    {
        var target = Definition.Required || value;
        if (IsEnabled == target)
        {
            return;
        }
        IsEnabled = target;
        if (target && cascadeUp)
        {
            EnableAncestors();
        }
    }

    /// <summary>向数组字段添加一个元素项，返回新项节点。</summary>
    public FormNode AddItem()
    {
        if (Definition.Type != FieldType.Array || Definition.Children is null)
        {
            throw new InvalidOperationException($"字段「{Definition.Id}」不是可增删的数组字段");
        }
        _nextItemSequence++;
        var itemDef = CreateArrayItemDefinition(Definition, _nextItemSequence);
        var item = CreateFromDefinition(itemDef, this, null);
        Children.Add(item);
        return item;
    }

    /// <summary>移除当前节点（仅数组项有效）。</summary>
    public void RemoveItem()
    {
        if (Parent?.Definition.Type == FieldType.Array)
        {
            Parent.Children.Remove(this);
        }
    }

    private void EnableAncestors()
    {
        var ancestor = Parent;
        while (ancestor is not null)
        {
            ancestor.SetEnabled(true);
            ancestor = ancestor.Parent;
        }
    }

    private bool IsAncestorsEnabled()
    {
        var ancestor = Parent;
        while (ancestor is not null)
        {
            if (!ancestor.IsEnabled)
            {
                return false;
            }
            ancestor = ancestor.Parent;
        }
        return true;
    }

    private static FieldDefinition CreateArrayItemDefinition(FieldDefinition array, long sequence)
        => new()
        {
            Id = $"{array.Id}_item_{sequence}",
            Label = $"项目 {sequence}",
            Type = FieldType.Object,
            Children = array.Children
        };

    /// <summary>
    /// 从 FieldDefinition 递归创建完整表单树。可传入值字典（导入/回填/恢复时使用），
    /// 标量按字段类型强制转换，数组按列表重建项。
    /// </summary>
    public static FormNode CreateFromDefinition(
        FieldDefinition definition,
        FormNode? parent = null,
        Dictionary<string, object?>? values = null)
    {
        var node = new FormNode(definition) { Parent = parent };

        switch (definition.Type)
        {
            case FieldType.Object:
                if (definition.Children is null)
                {
                    break;
                }
                // 数组项（父为 Array）：其值就是传入的 item 字典本身；
                // 普通 Object：子字段的值字典 = values[本字段 Id]。
                var objectScope = parent?.Definition.Type == FieldType.Array
                    ? values
                    : values is not null && values.TryGetValue(definition.Id, out var own)
                        ? own as Dictionary<string, object?>
                        : null;
                foreach (var childDef in definition.Children)
                {
                    node.Children.Add(CreateFromDefinition(childDef, node, objectScope));
                }
                break;

            case FieldType.Array:
                if (values is not null
                    && values.TryGetValue(definition.Id, out var listValue)
                    && listValue is IEnumerable<object?> items)
                {
                    long seq = 0;
                    foreach (var item in items)
                    {
                        seq++;
                        var itemDef = CreateArrayItemDefinition(definition, seq);
                        var itemScope = item as Dictionary<string, object?>;
                        node.Children.Add(CreateFromDefinition(itemDef, node, itemScope));
                    }
                    node._nextItemSequence = seq;
                }
                break;

            default:
                if (values is not null && values.TryGetValue(definition.Id, out var raw))
                {
                    node.Value = ConfigValueConverter.Coerce(definition.Type, raw);
                }
                break;
        }

        return node;
    }
}
