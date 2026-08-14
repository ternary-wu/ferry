using System.Diagnostics;
using System.Text;

namespace Ferry.Infrastructure;

/// <summary>推送模块共用的进程执行与文件名安全处理。</summary>
public static class PushProcess
{
    private static string? _gitPath;

    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName,
        string? workingDirectory,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动进程：" + fileName);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 进程可能已退出
            }
            throw;
        }
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return (process.ExitCode, output, error);
    }

    /// <summary>只取文件名部分，防止配置名携带路径造成目录穿越。</summary>
    public static string SafeFileName(string? name)
    {
        var file = Path.GetFileName(name?.Trim());
        return string.IsNullOrWhiteSpace(file) || file is "." or ".."
            ? "config.txt"
            : file;
    }

    public static string FindGit()
    {
        if (_gitPath is not null)
        {
            return _gitPath;
        }
        foreach (var candidate in GitCandidates())
        {
            if (File.Exists(candidate))
            {
                _gitPath = candidate;
                return candidate;
            }
        }
        _gitPath = "git";
        return _gitPath;
    }

    private static IEnumerable<string> GitCandidates()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs")
        };
        foreach (var root in roots)
        {
            if (!string.IsNullOrEmpty(root))
            {
                yield return Path.Combine(root, "Git", "cmd", "git.exe");
            }
        }

        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "codex-runtimes");
        if (Directory.Exists(cache))
        {
            foreach (var runtime in Directory.GetDirectories(cache))
            {
                yield return Path.Combine(
                    runtime,
                    "dependencies",
                    "native",
                    "git",
                    "cmd",
                    "git.exe");
            }
        }
    }
}
