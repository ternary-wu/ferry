using Ferry.Core.Ports;

namespace Ferry.Infrastructure;

/// <summary>本地目录推送：把配置内容写入目标目录（参考实现，Git/SSH 预留）。</summary>
public sealed class LocalDirectoryPushService : IPushService
{
    public string Name => "本地目录";

    public bool Supports(PushTargetType target) => target == PushTargetType.LocalDirectory;

    public Task<PushResult> PushAsync(PushRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.Target != PushTargetType.LocalDirectory)
            {
                return Task.FromResult(new PushResult(false, "不支持的目标类型"));
            }
            if (string.IsNullOrEmpty(request.RemotePath))
            {
                return Task.FromResult(new PushResult(false, "未指定目标目录"));
            }
            var fileName = string.IsNullOrWhiteSpace(request.ConfigName)
                ? "config.txt"
                : request.ConfigName;
            var path = Path.Combine(request.RemotePath, fileName);
            Directory.CreateDirectory(request.RemotePath);
            File.WriteAllText(path, request.Content);
            return Task.FromResult(new PushResult(true, $"已写入 {path}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new PushResult(false, $"推送失败：{ex.Message}"));
        }
    }
}
