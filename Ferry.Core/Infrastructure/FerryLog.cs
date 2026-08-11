using System.Globalization;
using System.Text;

namespace Ferry.Core.Infrastructure;

/// <summary>
/// 集中日志：默认写入应用根目录 ferry.log，超限自动轮转（ferry.log.1）。
/// 所有详细错误统一走这里，界面只展示用户可直接理解的摘要。
/// </summary>
public static class FerryLog
{
    private const long DefaultMaxBytes = 5L * 1024 * 1024;
    private static readonly object Sync = new();
    private static string _directory = AppContext.BaseDirectory;
    private static long _maxBytes = DefaultMaxBytes;

    public static void Configure(string? directory = null, long maxBytes = DefaultMaxBytes)
    {
        lock (Sync)
        {
            _directory = directory ?? AppContext.BaseDirectory;
            _maxBytes = maxBytes;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null)
        => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var file = Path.Combine(_directory, "ferry.log");
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                    .Append(" [").Append(level).Append("] ")
                    .AppendLine(message);
                if (exception is not null)
                {
                    sb.AppendLine(exception.ToString());
                }
                File.AppendAllText(file, sb.ToString());
                RotateIfNeeded(file);
            }
            catch
            {
                // 日志失败不得影响业务逻辑
            }
        }
    }

    private static void RotateIfNeeded(string file)
    {
        var info = new FileInfo(file);
        if (info.Exists && info.Length > _maxBytes)
        {
            var backup = file + ".1";
            File.Copy(file, backup, overwrite: true);
            File.WriteAllText(file, string.Empty);
        }
    }
}
