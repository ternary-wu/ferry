using System.Globalization;
using System.Text.RegularExpressions;
using Ferry.Core.Models;
using Ferry.Core.Services.Form;

namespace Ferry.Core.Services;

/// <summary>
/// 表单校验器：校验可见且启用的字段（将参与输出的内容），错误写回节点并返回列表。
/// 支持 required / min / max / integerOnly / 枚举选项 / validations 字典（pattern）。
/// </summary>
public static class ConfigValidator
{
    public static List<string> Validate(IEnumerable<FormNode> roots)
    {
        var errors = new List<string>();
        foreach (var root in roots)
        {
            ValidateNode(root, errors);
        }
        return errors;
    }

    private static void ValidateNode(FormNode node, List<string> errors)
    {
        node.ValidationError = null;
        if (node.IsVisible && node.IsEnabled)
        {
            var error = ValidateField(node);
            if (error is not null)
            {
                node.ValidationError = error;
                errors.Add($"{node.Path}：{error}");
            }
        }
        foreach (var child in node.Children)
        {
            ValidateNode(child, errors);
        }
    }

    private static string? ValidateField(FormNode node)
    {
        var def = node.Definition;
        var required = def.Required || IsValidationsRequired(def);
        var text = node.Value?.ToString();

        switch (def.Type)
        {
            case FieldType.String:
            {
                if (string.IsNullOrEmpty(text))
                {
                    return required ? "必填字段不能为空" : null;
                }
                return ValidatePattern(def, text);
            }

            case FieldType.Enum:
            {
                if (string.IsNullOrEmpty(text))
                {
                    return required ? "必填字段不能为空" : null;
                }

                // 命中枚举选项（如 worker_processes 的 auto）→ 直接通过，不套用数值约束。
                var knownOption = def.EnumOptions is { Count: > 0 }
                    && text is not null
                    && def.EnumOptions.Any(o => o.Value == text);
                if (knownOption)
                {
                    return ValidatePattern(def, text);
                }

                if (def.EnumOptions is { Count: > 0 } && def.AllowCustomValue != true)
                {
                    return "不在可选范围内";
                }

                if (def.AllowCustomValue == true && text is not null)
                {
                    if (def.IntegerOnly == true && !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        return "仅允许整数";
                    }
                    if (def.Min is not null || def.Max is not null)
                    {
                        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                        {
                            return "请输入数字";
                        }
                        if (def.Min is double minValue && num < minValue)
                        {
                            return $"不能小于 {minValue}";
                        }
                        if (def.Max is double maxValue && num > maxValue)
                        {
                            return $"不能大于 {maxValue}";
                        }
                    }
                }
                return ValidatePattern(def, text);
            }

            case FieldType.Number:
            {
                if (string.IsNullOrEmpty(text))
                {
                    return required ? "必填字段不能为空" : null;
                }
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return "请输入数字";
                }
                if (def.IntegerOnly == true && number != Math.Truncate(number))
                {
                    return "仅允许整数";
                }
                if (def.Min is double minValue && number < minValue)
                {
                    return $"不能小于 {minValue}";
                }
                if (def.Max is double maxValue && number > maxValue)
                {
                    return $"不能大于 {maxValue}";
                }
                return null;
            }

            default:
                return null;
        }
    }

    private static bool IsValidationsRequired(FieldDefinition def)
        => def.Validations is not null
            && def.Validations.TryGetValue("required", out var required)
            && required is bool b
            && b;

    private static string? ValidatePattern(FieldDefinition def, string? text)
    {
        if (def.Validations is not null
            && def.Validations.TryGetValue("pattern", out var pattern)
            && pattern is string p
            && !string.IsNullOrEmpty(p)
            && text is not null)
        {
            try
            {
                if (!Regex.IsMatch(text, p))
                {
                    return "不符合格式要求";
                }
            }
            catch (ArgumentException)
            {
                return "校验规则（pattern）无效";
            }
        }
        return null;
    }
}
