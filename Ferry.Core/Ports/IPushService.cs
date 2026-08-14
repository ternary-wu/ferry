namespace Ferry.Core.Ports;

/// <summary>推送目标类型：本地目录已实现，Git/SSH 预留。</summary>
public enum PushTargetType
{
    LocalDirectory,
    GitRepository,
    SshServer
}

/// <summary>推送请求：内容 + 目标 + 可选分支/提交信息/远端路径/凭据。</summary>
public sealed record PushRequest(
    string ConfigName,
    string Content,
    PushTargetType Target,
    string? Branch = null,
    string? CommitMessage = null,
    string? RemotePath = null,
    string? CredentialId = null,
    string? GitUserName = null,
    string? GitUserEmail = null,
    string? SshHost = null,
    string? SshUser = null,
    int? SshPort = null,
    string? KeyFile = null);

public sealed record PushResult(bool Ok, string Message);

/// <summary>推送端口：实现 = LocalDirectory（已实现）/ Git / SSH（预留）。</summary>
public interface IPushService
{
    string Name { get; }
    bool Supports(PushTargetType target);
    Task<PushResult> PushAsync(PushRequest request, CancellationToken cancellationToken = default);
}
