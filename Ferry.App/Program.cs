using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Ferry.Core.Services;
using Ferry.Core.Services.Session;
using Ferry.Core.Services.Session.Protocol;
using Photino.NET;

namespace Ferry.App;

/// <summary>
/// Photino spike（M6）：最小宿主 + 原生 HTML/CSS/JS 表单。
/// JS 通过 WebView2 消息桥调用 FormSession 命令，变更后重新拉快照。
/// 设置环境变量 FERRY_SPIKE_SELFCHECK=1 时自动执行自检并写结果文件后退出。
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "ferry-spike-log.txt");

    [STAThread]
    public static void Main()
    {
        Log("start");
        var pluginsRoot = Path.Combine(AppContext.BaseDirectory, "Plugins");
        var manager = new PluginManager(new DirectoryPluginSource(pluginsRoot));
        var plugins = manager.LoadAllPlugins();
        var nginx = plugins.FirstOrDefault(p => p.PluginKey == "Nginx")
            ?? throw new InvalidOperationException("缺少 Nginx 插件，无法运行 spike");

        var session = FormSession.Create(nginx);
        var selfCheck = Environment.GetEnvironmentVariable("FERRY_SPIKE_SELFCHECK") == "1";
        var window = new PhotinoWindow()
            .SetTitle("Ferry Photino Spike")
            .SetSize(1150, 760)
            .RegisterWebMessageReceivedHandler((sender, message) =>
                HandleMessage(sender, session, message, selfCheck));
        Log("window-created");

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        window.Load(htmlPath);
        Log("loaded");

        if (selfCheck)
        {
            _ = RunSelfCheckAsync(window);
        }

        window.WaitForClose();
    }

    private static void HandleMessage(
        object? sender,
        FormSession session,
        string message,
        bool selfCheck)
    {
        try
        {
            Log("message:" + (JsonNode.Parse(message) as JsonObject)?["action"]?.GetValue<string>() ?? "?");
            if (sender is not PhotinoWindow window)
            {
                return;
            }
            var request = JsonNode.Parse(message) as JsonObject;
            var action = request?["action"]?.GetValue<string>() ?? string.Empty;
            var sw = Stopwatch.StartNew();

            OperationResult? result = null;
            switch (action)
            {
                case "snapshot":
                    result = session.Apply(new SnapshotCommand());
                    break;
                case "validate":
                    result = session.Apply(new ValidateCommand());
                    break;
                case "render":
                    result = session.Apply(new RenderCommand());
                    break;
                case "setValue":
                    result = session.Apply(new SetValueCommand(
                        request!["path"]!.GetValue<string>(),
                        ConvertValue(request!["value"])));
                    break;
                case "toggle":
                    result = session.Apply(new ToggleEnabledCommand(
                        request!["path"]!.GetValue<string>(),
                        request["enabled"] is null ? null : request["enabled"]!.GetValue<bool>()));
                    break;
                case "addItem":
                    result = session.Apply(new AddItemCommand(request!["path"]!.GetValue<string>()));
                    break;
                case "removeItem":
                    result = session.Apply(new RemoveItemCommand(request!["path"]!.GetValue<string>()));
                    break;
                case "applyPreset":
                    result = session.Apply(new ApplyPresetCommand(request!["preset"]!.GetValue<string>()));
                    break;
                case "spike:run":
                    result = new OperationResult { Ok = true };
                    break;
                case "spike:result":
                    OnSpikeResult(window, message);
                    return;
                case "log":
                    Log("JS: " + (request?["text"]?.GetValue<string>() ?? string.Empty));
                    return;
                default:
                    result = new OperationResult
                    {
                        Ok = false,
                        Errors = new List<string> { $"未知操作：{action}" }
                    };
                    break;
            }

            sw.Stop();
            var response = new JsonObject
            {
                ["ok"] = result.Ok,
                ["latencyMs"] = sw.Elapsed.TotalMilliseconds,
                ["errors"] = JsonSerializer.SerializeToNode(result.Errors),
                ["stateVersion"] = session.Version,
                ["snapshot"] = JsonSerializer.SerializeToNode(result.Snapshot),
                ["text"] = result.RenderedText,
                ["newItemPath"] = result.NewItemPath
            };
            window.SendWebMessage(response.ToJsonString(JsonOptions));
        }
        catch (Exception ex)
        {
            if (sender is PhotinoWindow w)
            {
                w.SendWebMessage(JsonSerializer.Serialize(new
                {
                    ok = false,
                    errors = new[] { ex.Message }
                }));
            }
        }
    }

    private static object? ConvertValue(JsonNode? node) => node switch
    {
        null => null,
        JsonObject o => ConfigImporter.FromJsonObject(o),
        JsonArray a => a.Select(ConvertValue).ToList(),
        JsonValue v => v.TryGetValue<long>(out var l) ? l
            : v.TryGetValue<double>(out var d) ? d
            : v.TryGetValue<bool>(out var b) ? b
            : v.TryGetValue<string>(out var s) ? s
            : v.ToJsonString(),
        _ => null
    };

    /// <summary>
    /// 自检模式：等页面加载后触发 JS 跑一轮命令序列，
    /// JS 上报每步端到端延迟（含 IPC 往返），写结果文件后关闭窗口。
    /// </summary>
    private static async Task RunSelfCheckAsync(PhotinoWindow window)
    {
        try
        {
            await Task.Delay(1800);
            Log("selfcheck-sending");
            window.SendWebMessage("""{"action":"spike:run"}""");
        }
        catch (Exception ex)
        {
            WriteSpikeResult(new
            {
                ok = false,
                error = ex.Message
            });
        }
    }

    internal static void OnSpikeResult(PhotinoWindow window, string message)
    {
        try
        {
            Log("spike-result");
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "ferry-spike-result.json"),
                message);
        }
        finally
        {
            window.Close();
        }
    }

    private static void WriteSpikeResult(object data)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "ferry-spike-result.json"),
                JsonSerializer.Serialize(data, JsonOptions));
        }
        catch
        {
            // 结果写入失败不阻塞
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                LogPath,
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不阻塞
        }
    }
}
