using Ferry.Infrastructure;

namespace Ferry.Core.Tests;

public class HostInventoryTests
{
    [Fact]
    public void ParseTxt_HandlesSpacesCommentsAndExtraFields()
    {
        var parsed = HostFileParser.ParseTxt(
            "192.168.1.1   web-01\r\n10.0.0.2\r\n# comment\r\n\r\n172.16.0.3 node-03 extra");

        Assert.Equal(3, parsed.Count);
        Assert.Equal(("192.168.1.1", "web-01"), parsed[0]);
        Assert.Equal(("10.0.0.2", null), parsed[1]);
        Assert.Equal(("172.16.0.3", "node-03"), parsed[2]);
    }

    [Fact]
    public void ParseYaml_AcceptsListAndSingleMap()
    {
        var list = HostFileParser.ParseYaml(
            "- IP: 192.168.1.1\n  hostname: web\n- IP: 10.0.0.2\n");
        Assert.Equal(2, list.Count);

        var single = HostFileParser.ParseYaml("IP: 10.0.0.9\nhostname: db\n");
        Assert.Single(single);
        Assert.Equal(("10.0.0.9", "db"), single[0]);
    }

    [Fact]
    public void MergeHosts_DedupesByIp()
    {
        var existing = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = "a", ["ip"] = "10.0.0.2", ["port"] = 22L, ["groupId"] = "default" }
        };

        var (merged, imported, skipped) = HostInventoryService.MergeHosts(
            existing,
            new[] { ("10.0.0.2", (string?)"dup"), ("10.0.0.3", (string?)"new") },
            "g1");

        Assert.Equal(2, merged.Count);
        Assert.Equal(1, imported);
        Assert.Equal(1, skipped);
        Assert.Equal("new", merged[1]["hostname"]);
        Assert.Equal("g1", merged[1]["groupId"]);
    }

    [Fact]
    public void Export_ProducesTxtAndYaml()
    {
        var hosts = new List<Dictionary<string, object?>>
        {
            new() { ["ip"] = "1.2.3.4", ["hostname"] = "h", ["port"] = 22L, ["groupId"] = "default" }
        };

        var txt = HostInventoryService.ToText(hosts);
        Assert.Equal("1.2.3.4 h" + Environment.NewLine, txt);

        var yaml = HostInventoryService.ToYaml(hosts);
        Assert.Contains("- IP: 1.2.3.4", yaml);
        Assert.Contains("hostname: h", yaml);
    }
}
