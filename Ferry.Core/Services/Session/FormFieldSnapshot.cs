using Ferry.Core.Models;
using Ferry.Core.Services.Form;

namespace Ferry.Core.Services.Session;

/// <summary>
/// 只读表单快照（供前端渲染）：值、启用、可见性、可选性、校验错误、N/M 计数与定义元数据。
/// UI 每次变更后重新 GetSnapshot()，不订阅事件树。
/// </summary>
public sealed class FormFieldSnapshot
{
    public required string Path { get; init; }
    public required string Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public FieldType Type { get; init; }
    public object? Value { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsVisible { get; init; }
    public bool IsModule { get; init; }
    public bool IsArrayItem { get; init; }
    public bool IsSelectable { get; init; }
    public bool CanToggleEnabled { get; init; }
    public string? ValidationError { get; init; }
    public int TotalChildModulesCount { get; init; }
    public int EnabledChildModulesCount { get; init; }
    public string EnabledChildModulesText { get; init; } = string.Empty;
    public bool Required { get; init; }
    public bool AllowCustomValue { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public bool IntegerOnly { get; init; }
    public List<EnumOption> EnumOptions { get; init; } = new();
    public List<FormFieldSnapshot> Children { get; init; } = new();
}

/// <summary>快照构建器：FormNode 树 → FormFieldSnapshot 树。</summary>
public static class SnapshotBuilder
{
    public static List<FormFieldSnapshot> BuildAll(IEnumerable<FormNode> roots)
        => roots.Select(Build).ToList();

    public static FormFieldSnapshot Build(FormNode node) => new()
    {
        Path = node.Path,
        Id = node.Definition.Id,
        Label = node.Definition.Label,
        Description = node.Definition.Description,
        Type = node.Definition.Type,
        Value = node.Value,
        IsEnabled = node.IsEnabled,
        IsVisible = node.IsVisible,
        IsModule = node.IsModule,
        IsArrayItem = node.IsArrayItem,
        IsSelectable = node.IsSelectable,
        CanToggleEnabled = node.CanToggleEnabled,
        ValidationError = node.ValidationError,
        TotalChildModulesCount = node.TotalChildModulesCount,
        EnabledChildModulesCount = node.EnabledChildModulesCount,
        EnabledChildModulesText = node.EnabledChildModulesText,
        Required = node.Definition.Required,
        AllowCustomValue = node.Definition.AllowCustomValue == true,
        Min = node.Definition.Min,
        Max = node.Definition.Max,
        IntegerOnly = node.Definition.IntegerOnly == true,
        EnumOptions = node.Definition.EnumOptions ?? new List<EnumOption>(),
        Children = node.Children.Select(Build).ToList()
    };
}
