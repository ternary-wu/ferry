using Ferry.Core.Ports;
using Ferry.Infrastructure;

namespace Ferry.Core.Tests;

public class PushServicesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ferry-push-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    [Fact]
    public async Task GitPushService_CommitsAndKeepsHistory()
    {
        var repo = Path.Combine(_dir, "repo");
        var service = new GitPushService();

        var first = await service.PushAsync(new PushRequest(
            "nginx.conf",
            "worker_processes auto;\n",
            PushTargetType.GitRepository,
            "main",
            "初始版本",
            repo));
        Assert.True(first.Ok, first.Message);
        Assert.True(File.Exists(Path.Combine(repo, "nginx.conf")));

        var second = await service.PushAsync(new PushRequest(
            "nginx.conf",
            "worker_processes 2;\n",
            PushTargetType.GitRepository,
            "main",
            "第二次",
            repo));
        Assert.True(second.Ok, second.Message);

        var (exit, output, _) = await PushProcess.RunAsync(
            PushProcess.FindGit(),
            repo,
            CancellationToken.None,
            "log",
            "--format=%s",
            "--",
            "nginx.conf");
        Assert.Equal(0, exit);
        Assert.Contains("初始版本", output);
        Assert.Contains("第二次", output);

        var (revExit, revOutput, _) = await PushProcess.RunAsync(
            PushProcess.FindGit(),
            repo,
            CancellationToken.None,
            "log",
            "--format=%H",
            "--reverse",
            "--",
            "nginx.conf");
        Assert.Equal(0, revExit);
        var firstHash = revOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        var (showExit, showOutput, _) = await PushProcess.RunAsync(
            PushProcess.FindGit(),
            repo,
            CancellationToken.None,
            "show",
            $"{firstHash}:nginx.conf");
        Assert.Equal(0, showExit);
        Assert.Equal("worker_processes auto;\n", showOutput);
    }

    [Fact]
    public async Task GitPushService_SanitizesConfigFileName()
    {
        var repo = Path.Combine(_dir, "repo");
        var service = new GitPushService();
        var result = await service.PushAsync(new PushRequest(
            "../evil.conf",
            "content",
            PushTargetType.GitRepository,
            "main",
            null,
            repo));

        Assert.True(result.Ok, result.Message);
        Assert.True(File.Exists(Path.Combine(repo, "evil.conf")));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.conf")));
    }

    [Fact]
    public async Task SshPushService_RejectsInvalidTargetFormat()
    {
        var service = new SshPushService();
        var result = await service.PushAsync(new PushRequest(
            "nginx.conf",
            "content",
            PushTargetType.SshServer,
            null,
            null,
            "missing-colon-host"));

        Assert.False(result.Ok);
        Assert.Contains("格式", result.Message);
    }
}
