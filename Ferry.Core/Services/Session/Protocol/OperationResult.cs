namespace Ferry.Core.Services.Session.Protocol;

/// <summary>
/// 统一操作结果（业务错误不靠异常传递）。
/// ErrorCode 供服务器适配器映射 HTTP：conflict→409 / not_found→404 / validation→400 / unsupported→400。
/// </summary>
public sealed class OperationResult
{
    public bool Ok { get; init; }
    public List<string> Errors { get; init; } = new();
    public ConfigState? State { get; init; }
    public List<FormFieldSnapshot>? Snapshot { get; init; }
    public string? RenderedText { get; init; }
    public long? Version { get; init; }
    public string? NewItemPath { get; init; }
    public string? ErrorCode { get; init; }
}
