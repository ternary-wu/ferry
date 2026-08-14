using YamlDotNet.Serialization;

namespace Ferry.Infrastructure;

/// <summary>解析主机清单导入文件（txt / yaml）。</summary>
public static class HostFileParser
{
    /// <summary>
    /// txt 格式：每行 [ip] [hostname?]，空白分隔（可多个空格）；空行与 # 注释跳过。
    /// </summary>
    public static List<(string Ip, string? Hostname)> ParseTxt(string text)
    {
        var result = new List<(string Ip, string? Hostname)>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }
            result.Add((parts[0], parts.Length > 1 ? parts[1] : null));
        }
        return result;
    }

    /// <summary>
    /// yaml 格式：兼容“映射列表”（- IP: x / hostname: y）与“单个映射”（IP: x / hostname: y）。
    /// </summary>
    public static List<(string Ip, string? Hostname)> ParseYaml(string text)
    {
        var result = new List<(string Ip, string? Hostname)>();
        var deserializer = new DeserializerBuilder().Build();
        object? root;
        try
        {
            root = deserializer.Deserialize<object?>(text);
        }
        catch
        {
            return result;
        }

        if (root is List<object> list)
        {
            foreach (var item in list)
            {
                if (TryParseHostMap(item, out var host))
                {
                    result.Add(host);
                }
            }
        }
        else if (TryParseHostMap(root, out var single))
        {
            result.Add(single);
        }
        return result;
    }

    private static bool TryParseHostMap(object? value, out (string Ip, string? Hostname) host)
    {
        host = (string.Empty, null);
        if (value is not IDictionary<object, object> map)
        {
            return false;
        }
        var ip = GetValue(map, "IP");
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }
        host = (ip, GetValue(map, "HOSTNAME"));
        return true;
    }

    private static string? GetValue(IDictionary<object, object> map, string key)
    {
        foreach (var kv in map)
        {
            if (string.Equals(kv.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase)
                && kv.Value is not null)
            {
                return kv.Value.ToString();
            }
        }
        return null;
    }
}
