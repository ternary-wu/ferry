using Ferry.Core.Ports;

namespace Ferry.Infrastructure;

/// <summary>
/// Git 推送：把配置源码写入本地 Git 仓库并提交（本地仓库即版本源，不推远端）。
/// </summary>
public sealed class GitPushService : IPushService
{
    public string Name => "Git 仓库";

    public bool Supports(PushTargetType target) => target == PushTargetType.GitRepository;

    public async Task<PushResult> PushAsync(
        PushRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.Target != PushTargetType.GitRepository)
            {
                return new PushResult(false, "不支持的目标类型");
            }
            if (string.IsNullOrWhiteSpace(request.RemotePath))
            {
                return new PushResult(false, "未指定 Git 仓库路径");
            }

            var repoPath = Path.GetFullPath(request.RemotePath);
            var fileName = PushProcess.SafeFileName(request.ConfigName);
            var branch = string.IsNullOrWhiteSpace(request.Branch) ? "main" : request.Branch;
            var git = PushProcess.FindGit();

            Directory.CreateDirectory(repoPath);
            var gitDir = Path.Combine(repoPath, ".git");
            if (!Directory.Exists(gitDir) && !File.Exists(gitDir))
            {
                var init = await PushProcess
                    .RunAsync(git, repoPath, cancellationToken, "init", "-b", branch)
                    .ConfigureAwait(false);
                if (init.ExitCode != 0)
                {
                    return new PushResult(false, "Git 初始化失败：" + init.Error.Trim());
                }
            }

            await File.WriteAllTextAsync(
                Path.Combine(repoPath, fileName),
                request.Content ?? string.Empty,
                cancellationToken).ConfigureAwait(false);

            var add = await PushProcess
                .RunAsync(git, repoPath, cancellationToken, "add", "--", fileName)
                .ConfigureAwait(false);
            if (add.ExitCode != 0)
            {
                return new PushResult(false, "Git add 失败：" + add.Error.Trim());
            }

            var message = string.IsNullOrWhiteSpace(request.CommitMessage)
                ? $"Ferry: 更新 {fileName}"
                : request.CommitMessage;
            var commitArgs = new List<string>();
            commitArgs.AddRange(BuildIdentityArgs(request));
            commitArgs.Add("commit");
            commitArgs.Add("-m");
            commitArgs.Add(message);
            var commit = await PushProcess
                .RunAsync(git, repoPath, cancellationToken, commitArgs.ToArray())
                .ConfigureAwait(false);
            if (commit.ExitCode != 0)
            {
                var combined = commit.Output + commit.Error;
                if (combined.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
                {
                    return new PushResult(true, "内容无变化，未生成新提交");
                }
                if (combined.Contains("user.name", StringComparison.OrdinalIgnoreCase)
                    || combined.Contains("Please tell me who you are", StringComparison.OrdinalIgnoreCase))
                {
                    return new PushResult(
                        false,
                        "未配置 Git 用户：请在 Git 全局配置 user.name/user.email，或在推送目标中单独设置用户与邮箱");
                }
                return new PushResult(false, "Git commit 失败：" + commit.Error.Trim());
            }

            var hash = await PushProcess
                .RunAsync(git, repoPath, cancellationToken, "rev-parse", "--short", "HEAD")
                .ConfigureAwait(false);
            var shortHash = hash.ExitCode == 0 ? hash.Output.Trim() : "?";
            return new PushResult(true, $"已提交 {shortHash}：{fileName}");
        }
        catch (OperationCanceledException)
        {
            return new PushResult(false, "推送已取消");
        }
        catch (Exception ex)
        {
            return new PushResult(false, "推送失败：" + ex.Message);
        }
    }

    /// <summary>目标设置了用户/邮箱时用 -c 覆盖，否则交给 git 全局配置。</summary>
    internal static string[] BuildIdentityArgs(PushRequest request)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.GitUserName))
        {
            args.Add("-c");
            args.Add($"user.name={request.GitUserName}");
        }
        if (!string.IsNullOrWhiteSpace(request.GitUserEmail))
        {
            args.Add("-c");
            args.Add($"user.email={request.GitUserEmail}");
        }
        return args.ToArray();
    }
}
