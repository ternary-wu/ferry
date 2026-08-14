using Ferry.Core.Ports;

namespace Ferry.Infrastructure;

/// <summary>
/// SSH 推送：把配置源码通过 scp 推送到远程主机（使用系统 ssh-agent/默认密钥，BatchMode 无交互）。
/// </summary>
public sealed class SshPushService : IPushService
{
    public string Name => "SSH";

    public bool Supports(PushTargetType target) => target == PushTargetType.SshServer;

    public async Task<PushResult> PushAsync(
        PushRequest request,
        CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ferry-push-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (request.Target != PushTargetType.SshServer)
            {
                return new PushResult(false, "不支持的目标类型");
            }
            if (string.IsNullOrWhiteSpace(request.SshHost))
            {
                return new PushResult(false, "未指定 SSH 主机");
            }
            if (string.IsNullOrWhiteSpace(request.RemotePath))
            {
                return new PushResult(false, "未指定远端目录");
            }

            var fileName = PushProcess.SafeFileName(request.ConfigName);
            Directory.CreateDirectory(tempDir);
            var tempFile = Path.Combine(tempDir, fileName);
            await File.WriteAllTextAsync(
                tempFile,
                request.Content ?? string.Empty,
                cancellationToken).ConfigureAwait(false);

            var scp = FindScp();
            var args = BuildScpArguments(request, tempFile);
            var result = await PushProcess
                .RunAsync(
                    scp,
                    null,
                    cancellationToken,
                    args)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return new PushResult(false, "SSH 推送失败：" + result.Error.Trim());
            }
            return new PushResult(true, $"已推送到 {request.SshUser ?? "root"}@{request.SshHost}:{request.RemotePath}/{fileName}");
        }
        catch (OperationCanceledException)
        {
            return new PushResult(false, "推送已取消");
        }
        catch (Exception ex)
        {
            return new PushResult(false, "推送失败：" + ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响结果
            }
        }
    }

    internal static string[] BuildScpArguments(PushRequest request, string sourceFile)
    {
        var args = new List<string> { "-o", "BatchMode=yes", "-q" };
        var port = request.SshPort ?? 22;
        if (port != 22)
        {
            args.Add("-P");
            args.Add(port.ToString());
        }
        if (!string.IsNullOrWhiteSpace(request.KeyFile))
        {
            args.Add("-i");
            args.Add(request.KeyFile);
        }
        args.Add(sourceFile);
        var user = string.IsNullOrWhiteSpace(request.SshUser) ? "root" : request.SshUser;
        args.Add($"{user}@{request.SshHost}:{request.RemotePath}");
        return args.ToArray();
    }

    private static string FindScp()
    {
        var systemOpenSsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "OpenSSH",
            "scp.exe");
        return File.Exists(systemOpenSsh) ? systemOpenSsh : "scp";
    }
}
