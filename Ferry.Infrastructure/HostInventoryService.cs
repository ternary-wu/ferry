using System.Text;

namespace Ferry.Infrastructure;

/// <summary>主机清单的合并、序列化与导出（统一内部模型）。</summary>
public static class HostInventoryService
{
    public const string DefaultGroupId = "default";

    /// <summary>按 ip 去重合并新导入的主机，返回合并后的完整列表与统计。</summary>
    public static (
        List<Dictionary<string, object?>> Merged,
        int Imported,
        int Skipped) MergeHosts(
            List<Dictionary<string, object?>> existing,
            IEnumerable<(string Ip, string? Hostname)> parsed,
            string groupId)
    {
        var merged = new List<Dictionary<string, object?>>(existing);
        var seen = new HashSet<string>(
            existing.Select(h => h.TryGetValue("ip", out var ip) ? ip?.ToString() ?? string.Empty : string.Empty),
            StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var skipped = 0;
        foreach (var (ip, hostname) in parsed)
        {
            if (string.IsNullOrWhiteSpace(ip) || !seen.Add(ip))
            {
                skipped++;
                continue;
            }
            merged.Add(new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["ip"] = ip,
                ["hostname"] = hostname,
                ["port"] = 22L,
                ["groupId"] = groupId
            });
            imported++;
        }
        return (merged, imported, skipped);
    }

    public static string ToText(IEnumerable<Dictionary<string, object?>> hosts)
    {
        var sb = new StringBuilder();
        foreach (var host in hosts)
        {
            var ip = host.TryGetValue("ip", out var ipValue) ? ipValue?.ToString() ?? string.Empty : string.Empty;
            var hostname = host.TryGetValue("hostname", out var nameValue) ? nameValue?.ToString() : null;
            sb.Append(ip);
            if (!string.IsNullOrWhiteSpace(hostname))
            {
                sb.Append(' ').Append(hostname);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string ToYaml(IEnumerable<Dictionary<string, object?>> hosts)
    {
        var sb = new StringBuilder();
        foreach (var host in hosts)
        {
            var ip = host.TryGetValue("ip", out var ipValue) ? ipValue?.ToString() ?? string.Empty : string.Empty;
            var hostname = host.TryGetValue("hostname", out var nameValue) ? nameValue?.ToString() : null;
            sb.Append("- IP: ").Append(ip).AppendLine();
            if (!string.IsNullOrWhiteSpace(hostname))
            {
                sb.Append("  hostname: ").Append(hostname).AppendLine();
            }
        }
        return sb.ToString();
    }
}
