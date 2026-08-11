using System.Globalization;
using Ferry.Core.Models;

namespace Ferry.Core.Services;

/// <summary>按字段类型强转表单值（数字/布尔），供收集与回填使用。</summary>
public static class ConfigValueConverter
{
    public static object? Coerce(FieldType type, object? raw) => type switch
    {
        FieldType.Number => ToNumber(raw),
        FieldType.Boolean => ToBool(raw),
        _ => raw
    };

    private static object? ToNumber(object? raw)
    {
        if (raw is null)
        {
            return null;
        }
        if (raw is int or long or double or decimal)
        {
            return raw;
        }
        var text = raw.ToString();
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lng))
        {
            return lng;
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl))
        {
            return dbl;
        }
        return raw;
    }

    private static object? ToBool(object? raw)
    {
        if (raw is bool)
        {
            return raw;
        }
        if (bool.TryParse(raw?.ToString(), out var result))
        {
            return result;
        }
        return raw;
    }
}
