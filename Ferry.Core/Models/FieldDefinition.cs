namespace Ferry.Core.Models;

/// <summary>字段类型：UI 控件与值语义由类型决定。</summary>
public enum FieldType
{
    String,
    Number,
    Boolean,
    Enum,
    Array,
    Object
}

/// <summary>枚举选项（schema.yaml 的 enumOptions）。</summary>
public sealed class EnumOption
{
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>字段依赖显隐规则：依赖最近同名祖先字段的值。</summary>
public sealed class DependencyRule
{
    public string DependsOnField { get; set; } = string.Empty;
    public object? ExpectedValue { get; set; }
}

/// <summary>
/// 字段定义：插件 schema.yaml 中一个字段的完整声明。
/// 输出格式（layout）由 <see cref="Render"/> 声明，纯 YAML 无控制流。
/// </summary>
public sealed class FieldDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FieldType Type { get; set; } = FieldType.String;
    public List<EnumOption>? EnumOptions { get; set; }
    public object? DefaultValue { get; set; }
    public DependencyRule? VisibilityDependency { get; set; }
    public List<FieldDefinition>? Children { get; set; }

    /// <summary>
    /// 通用校验字典（v2 增量扩展）。当前支持：
    /// "required"（bool，必填）、"pattern"（string，正则，String/Enum 字段）。
    /// 数值约束继续使用 Min / Max / IntegerOnly。
    /// </summary>
    public Dictionary<string, object>? Validations { get; set; }

    public double? Min { get; set; }
    public double? Max { get; set; }
    public bool? IntegerOnly { get; set; }

    /// <summary>Enum 是否允许自定义输入（可编辑下拉，如 worker_processes 的 auto + 数字）。</summary>
    public bool? AllowCustomValue { get; set; }

    /// <summary>是否为可选模块（UI 显示勾选框，未勾选时整块从输出中移除）。</summary>
    public bool Module { get; set; }

    /// <summary>插件声明必填：该字段不可被取消，始终输出。默认所有字段都可取消。</summary>
    public bool Required { get; set; }

    /// <summary>声明式输出格式（layout 渲染器）：字段级覆盖全局默认样式。</summary>
    public FieldRenderConfig? Render { get; set; }
}

/// <summary>
/// 字段级渲染说明（schema.yaml 的 render 段）。
/// 占位符：{{ . }} 当前值、{{ .key }} 字段键名、{{ .子字段id }} 当前节点子字段。
/// </summary>
public sealed class FieldRenderConfig
{
    public string? Line { get; set; }
    public string? Open { get; set; }
    public string? Close { get; set; }
    public string? ItemOpen { get; set; }
    public string? ItemClose { get; set; }
    public string? Item { get; set; }
    public bool Inline { get; set; }
    public bool KeepEmpty { get; set; }
    public bool Hidden { get; set; }
}
