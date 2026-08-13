using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ferry.Core.Infrastructure;
using Ferry.Core.Models;
using Ferry.Core.Ports;
using Ferry.Core.Services;
using Ferry.Core.Services.Archive;
using Ferry.Core.Services.Parsing;
using Ferry.Core.Services.Session;
using Ferry.Core.Services.Session.Protocol;
using Ferry.Infrastructure;
using Photino.NET;

namespace Ferry.App;

/// <summary>
/// Ferry v2 桌面宿主（M7 正式 UI）：Photino + 原生 HTML/CSS/JS。
/// JS 通过 WebView2 消息桥调用命令协议；Core 不感知传输。
/// 设置 FERRY_SPIKE_SELFCHECK=1 时自动跑全链路自检并写结果后退出。
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions DtoOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "ferry-spike-log.txt");

    [STAThread]
    public static void Main()
    {
        Window.WindowController.EnablePerMonitorV2Dpi();
        FerryLog.Configure();
        Log("start");

        var pluginsRoot = Path.Combine(AppContext.BaseDirectory, "Plugins");
        var pluginManager = new PluginManager(new DirectoryPluginSource(pluginsRoot));
        var selfCheck = Environment.GetEnvironmentVariable("FERRY_SPIKE_SELFCHECK") == "1";
        var workspaceFile = Environment.GetEnvironmentVariable("FERRY_WORKSPACE_FILE")
            ?? (selfCheck
                ? Path.Combine(Path.GetTempPath(), $"ferry-selfcheck-{Guid.NewGuid():N}.json")
                : null);
        var workspaceStore = workspaceFile is null
            ? new LocalWorkspaceStore()
            : new LocalWorkspaceStore(workspaceFile);
        var workspaceService = new WorkspaceService(workspaceStore);
        var context = new HostContext(
            pluginManager,
            workspaceService,
            new PortableArchiveService(workspaceService, Array.Empty<PluginDescriptor>()));

        var window = new PhotinoWindow()
            .SetTitle("Ferry")
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 800)
            .SetMinSize(1200, 720)
            .SetChromeless(true);
        var windowController = new Window.WindowController(window);
        windowController.Initialize();
        windowController.SetTheme(
            workspaceStore.LoadSettings().GetValueOrDefault("theme")?.ToString());
        window.RegisterWebMessageReceivedHandler((sender, message) =>
            HandleMessage(sender, context, windowController, message, selfCheck));
        Log("window-created");

        var useNewUi = Environment.GetEnvironmentVariable("FERRY_UI_OLD") != "1";
        var htmlPath = Path.Combine(
            AppContext.BaseDirectory,
            useNewUi ? Path.Combine("ui", "index.html") : Path.Combine("wwwroot", "index.html"));
        window.Load(htmlPath);
        Log("loaded: " + htmlPath);

        if (selfCheck)
        {
            _ = RunSelfCheckAsync(window);
        }

        window.WaitForClose();
    }

    private static void HandleMessage(
        object? sender,
        HostContext ctx,
        Window.WindowController windowController,
        string message,
        bool selfCheck)
    {
        if (sender is not PhotinoWindow window)
        {
            return;
        }
        var sw = Stopwatch.StartNew();
        try
        {
            var request = JsonNode.Parse(message) as JsonObject;
            var action = request?["action"]?.GetValue<string>() ?? string.Empty;
            var requestId = request?["requestId"]?.GetValue<string>();

            // 自检触发信号：只下发，不回包（避免与 JS 在途请求的响应错配）
            if (action == "spike:run")
            {
                return;
            }
            // 关闭窗口后 WebView 已不可用，必须提前返回，不能再 SendWebMessage 回包
            if (action == "window:close")
            {
                Log("window:close");
                WindowClose(windowController);
                return;
            }
            if (action == "spike:result")
            {
                OnSpikeResult(window, message);
                return;
            }

            JsonObject? response = action switch
            {
                "bootstrap" => Bootstrap(ctx),
                "plugins:reload" => PluginsReload(ctx),
                "projects:list" => ProjectsList(ctx),
                "project:create" => ProjectCreate(ctx, request!),
                "project:rename" => ProjectRename(ctx, request!),
                "project:delete" => ProjectDelete(ctx, request!),
                "workspaces:list" => WorkspacesList(ctx),
                "workspace:create" => WorkspaceCreate(ctx, request!),
                "workspace:rename" => WorkspaceRename(ctx, request!),
                "workspace:delete" => WorkspaceDelete(ctx, request!),
                "workspace:reorder" => WorkspaceReorder(ctx, request!),
                "nav:tree" => NavTree(ctx, request!),
                "configs:list" => ConfigsList(ctx, request!),
                "configs:unassigned" => ConfigsUnassigned(ctx, request!),
                "config:create" => ConfigCreate(ctx, request!),
                "config:duplicate" => ConfigDuplicate(ctx, request!),
                "config:rename" => ConfigRename(ctx, request!),
                "config:open" => ConfigOpen(ctx, request!),
                "config:delete" => ConfigDelete(ctx, request!),
                "config:move" => ConfigMove(ctx, request!),
                "config:reorder" => ConfigReorder(ctx, request!),
                "config:reset" => ConfigReset(ctx, request!),
                "config:saveSource" => ConfigSaveSource(ctx, request!),
                "config:exportTo" => ConfigExportTo(ctx, request!),
                "settings:get" => SettingsGet(ctx),
                "settings:save" => SettingsSave(ctx, request!, windowController),
                "form:snapshot" => FormResult(ctx, new SnapshotCommand()),
                "form:validate" => FormResult(ctx, new ValidateCommand()),
                "form:render" => FormResult(ctx, new RenderCommand()),
                "form:setValue" => FormResult(ctx, new SetValueCommand(
                    request!["path"]!.GetValue<string>(), ConvertValue(request["value"]))),
                "form:toggle" => FormResult(ctx, new ToggleEnabledCommand(
                    request!["path"]!.GetValue<string>(),
                    request["enabled"] is null ? null : request["enabled"]!.GetValue<bool>())),
                "form:addItem" => FormResult(ctx, new AddItemCommand(request!["path"]!.GetValue<string>())),
                "form:removeItem" => FormResult(ctx, new RemoveItemCommand(request!["path"]!.GetValue<string>())),
                "form:applyPreset" => FormResult(ctx, new ApplyPresetCommand(request!["preset"]!.GetValue<string>())),
                "form:importText" => FormImportText(ctx, request!),
                "versions:list" => VersionsList(ctx, request!),
                "version:snapshot" => VersionSnapshot(ctx, request!),
                "version:restore" => VersionRestore(ctx, request!),
                "version:delete" => VersionDelete(ctx, request!),
                "archive:exportWorkspace" => ArchiveExportWorkspace(ctx, request!),
                "archive:exportConfig" => ArchiveExportConfig(ctx, request!),
                "archive:import" => ArchiveImport(ctx, request!),
                "logs:path" => LogsPath(),
                "logs:open" => LogsOpen(),
                "app:dataDir" => AppDataDir(),
                "trash:list" => TrashList(),
                "trash:delete" => TrashDelete(request!),
                "window:minimize" => WindowMinimize(windowController),
                "window:maximize" => WindowMaximize(windowController),
                "window:drag" => WindowDrag(windowController),
                "log" => LogJs(request ?? new JsonObject()),
                _ => null
            };

            response ??= new JsonObject
            {
                ["ok"] = false,
                ["errors"] = new JsonArray("未知操作：" + action)
            };
            response["action"] = action;
            if (requestId is not null)
            {
                response["requestId"] = requestId;
            }
            response["latencyMs"] = sw.Elapsed.TotalMilliseconds;
            window.SendWebMessage(response.ToJsonString(JsonOptions));
        }
        catch (Exception ex)
        {
            FerryLog.Error("处理命令失败", ex);
            window.SendWebMessage(JsonSerializer.Serialize(new
            {
                ok = false,
                errors = new[] { ex.Message },
                requestId = TryGetRequestId(message),
                latencyMs = sw.Elapsed.TotalMilliseconds
            }, JsonOptions));
        }
    }

    private static string? TryGetRequestId(string message)
    {
        try
        {
            return (JsonNode.Parse(message) as JsonObject)?["requestId"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    // ---------- 命令实现 ----------

    private static JsonObject Bootstrap(HostContext ctx)
    {
        var plugins = ctx.PluginManager.LoadAllPlugins();
        ctx.RefreshArchivePlugins();
        var defaultProject = ctx.Workspaces.EnsureDefaultProject();
        return new JsonObject
        {
            ["ok"] = true,
            ["plugins"] = Node(plugins.Select(ToPluginDto)),
            ["projects"] = Node(ctx.Workspaces.ListProjects()),
            ["workspaces"] = Node(ctx.Workspaces.ListWorkspaces()),
            ["loadErrors"] = Node(ctx.PluginManager.LoadErrors)
        };
    }

    private static JsonObject PluginsReload(HostContext ctx)
    {
        var plugins = ctx.PluginManager.LoadAllPlugins();
        ctx.RefreshArchivePlugins();
        return new JsonObject
        {
            ["ok"] = true,
            ["plugins"] = Node(plugins.Select(ToPluginDto)),
            ["loadErrors"] = Node(ctx.PluginManager.LoadErrors)
        };
    }

    private static JsonObject WorkspacesList(HostContext ctx)
        => Ok(new JsonObject
        {
            ["workspaces"] = Node(ctx.Workspaces.ListWorkspaces())
        });

    /// <summary>导航树：项目下的工作空间（含配置）+ 未归类配置，一次拉取。</summary>
    private static JsonObject NavTree(HostContext ctx, JsonObject request)
    {
        var projectId = request["projectId"]!.GetValue<string>();
        var workspaceNodes = ctx.Workspaces.ListWorkspaces(projectId)
            .Select(ws => (JsonNode)new JsonObject
            {
                ["id"] = ws.Id,
                ["name"] = ws.Name,
                ["configs"] = new JsonArray(
                    ctx.Workspaces.ListConfigs(ws.Id)
                        .Select(info => ToConfigDto(ctx, ws.Id, info))
                        .ToArray())
            })
            .ToArray();
        var unassigned = ctx.Workspaces.ListUnassignedConfigs(projectId)
            .Select(info => ToConfigDto(ctx, string.Empty, info))
            .ToArray();
        return Ok(new JsonObject
        {
            ["workspaces"] = new JsonArray(workspaceNodes),
            ["unassigned"] = new JsonArray(unassigned)
        });
    }

    private static JsonObject ProjectsList(HostContext ctx)
        => Ok(new JsonObject
        {
            ["projects"] = Node(ctx.Workspaces.ListProjects())
        });

    private static JsonObject ProjectCreate(HostContext ctx, JsonObject request)
    {
        var name = request["name"]?.GetValue<string>() ?? "未命名项目";
        var project = ctx.Workspaces.CreateProject(name);
        return Ok(new JsonObject { ["project"] = Node(project) });
    }

    private static JsonObject ProjectRename(HostContext ctx, JsonObject request)
    {
        var project = ctx.Workspaces.RenameProject(
            request["id"]!.GetValue<string>(),
            request["name"]!.GetValue<string>());
        return Ok(new JsonObject { ["project"] = Node(project) });
    }

    private static JsonObject ProjectDelete(HostContext ctx, JsonObject request)
    {
        ctx.Workspaces.DeleteProject(request["id"]!.GetValue<string>());
        return Ok();
    }

    private static JsonObject WorkspaceCreate(HostContext ctx, JsonObject request)
    {
        var name = request["name"]?.GetValue<string>() ?? "未命名工作空间";
        var projectId = request["projectId"]?.GetValue<string>()
            ?? ctx.Workspaces.ListProjects().FirstOrDefault()?.Id
            ?? ctx.Workspaces.EnsureDefaultProject().Id;
        var workspace = ctx.Workspaces.CreateWorkspace(projectId, name);
        return Ok(new JsonObject { ["workspace"] = Node(workspace) });
    }

    private static JsonObject WorkspaceRename(HostContext ctx, JsonObject request)
    {
        var workspace = ctx.Workspaces.RenameWorkspace(
            request["id"]!.GetValue<string>(),
            request["name"]!.GetValue<string>());
        return Ok(new JsonObject { ["workspace"] = Node(workspace) });
    }

    private static JsonObject WorkspaceDelete(HostContext ctx, JsonObject request)
    {
        ctx.Workspaces.DeleteWorkspace(request["id"]!.GetValue<string>());
        return Ok();
    }

    private static JsonObject WorkspaceReorder(HostContext ctx, JsonObject request)
    {
        var projectId = request["projectId"]!.GetValue<string>();
        var workspaceIds = (request["workspaceIds"] as JsonArray)
            ?.Select(n => n!.GetValue<string>())
            .ToList()
            ?? throw new InvalidOperationException("未指定 workspaceIds");
        ctx.Workspaces.ReorderWorkspaces(projectId, workspaceIds);
        return Ok();
    }

    private static JsonObject ConfigsList(HostContext ctx, JsonObject request)
    {
        var workspaceId = request["workspaceId"]!.GetValue<string>();
        var infos = ctx.Workspaces.ListConfigs(workspaceId);
        var dtos = infos.Select(info => ToConfigDto(ctx, workspaceId, info)).ToArray();
        return Ok(new JsonObject { ["configs"] = new JsonArray(dtos) });
    }

    private static JsonObject ConfigsUnassigned(HostContext ctx, JsonObject request)
    {
        var projectId = request["projectId"]!.GetValue<string>();
        var dtos = ctx.Workspaces.ListUnassignedConfigs(projectId)
            .Select(info => ToConfigDto(ctx, string.Empty, info))
            .ToArray();
        return Ok(new JsonObject { ["configs"] = new JsonArray(dtos) });
    }

    private static JsonNode ToConfigDto(HostContext ctx, string workspaceId, ConfigInfo info)
    {
        var config = ctx.Workspaces.LoadConfig(workspaceId, info.Id);
        var plugin = WorkspaceService.ResolvePlugin(ctx.Plugins, config ?? new ConfigData());
        return new JsonObject
        {
            ["id"] = info.Id,
            ["name"] = info.Name,
            ["pluginKey"] = info.PluginKey,
            ["pluginVersion"] = info.PluginVersion,
            ["pluginName"] = plugin?.Name ?? info.PluginKey,
            ["pluginMissing"] = plugin is null,
            ["updatedAt"] = info.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
            ["currentVersionId"] = info.CurrentVersionId
        };
    }

    private static JsonObject ConfigCreate(HostContext ctx, JsonObject request)
    {
        var projectId = request["projectId"]?.GetValue<string>()
            ?? ctx.Workspaces.ListProjects().FirstOrDefault()?.Id
            ?? ctx.Workspaces.EnsureDefaultProject().Id;
        var workspaceId = request["workspaceId"]?.GetValue<string>() ?? string.Empty;
        var pluginKey = request["pluginKey"]!.GetValue<string>();
        var name = request["name"]?.GetValue<string>();
        var plugin = ctx.Plugins.FirstOrDefault(p => p.PluginKey == pluginKey)
            ?? throw new InvalidOperationException($"插件不存在：{pluginKey}");

        var session = FormSession.Create(plugin);
        var sourceText = string.Empty;
        try
        {
            sourceText = session.Render();
        }
        catch
        {
            // 插件 schema 缺失时仅建空配置
        }
        var config = ctx.Workspaces.CreateConfig(
            projectId,
            workspaceId,
            plugin,
            name: name,
            sourceText: sourceText,
            values: session.GetState().Values,
            enabled: session.GetState().Enabled);
        return Ok(new JsonObject { ["configId"] = config.Id });
    }

    private static JsonObject ConfigDuplicate(HostContext ctx, JsonObject request)
    {
        var workspaceId = request["workspaceId"]!.GetValue<string>();
        var configId = request["configId"]!.GetValue<string>();
        var name = request["name"]?.GetValue<string>();
        var duplicated = ctx.Workspaces.DuplicateConfig(workspaceId, configId, name);
        return Ok(new JsonObject
        {
            ["configId"] = duplicated.Id,
            ["name"] = duplicated.Name
        });
    }

    private static JsonObject ConfigRename(HostContext ctx, JsonObject request)
    {
        var workspaceId = request["workspaceId"]!.GetValue<string>();
        var configId = request["configId"]!.GetValue<string>();
        var name = request["name"]!.GetValue<string>();
        var renamed = ctx.Workspaces.RenameConfig(workspaceId, configId, name);
        return Ok(new JsonObject
        {
            ["configId"] = renamed.Id,
            ["name"] = renamed.Name
        });
    }

    private static JsonObject ConfigMove(HostContext ctx, JsonObject request)
    {
        var config = ctx.Workspaces.MoveConfig(
            request["configId"]!.GetValue<string>(),
            request["workspaceId"]?.GetValue<string>() ?? string.Empty);
        return Ok(new JsonObject { ["configId"] = config.Id });
    }

    private static JsonObject ConfigReorder(HostContext ctx, JsonObject request)
    {
        var workspaceId = request["workspaceId"]!.GetValue<string>();
        var configIds = (request["configIds"] as JsonArray)
            ?.Select(n => n!.GetValue<string>())
            .ToList()
            ?? throw new InvalidOperationException("未指定 configIds");
        ctx.Workspaces.ReorderConfigs(workspaceId, configIds);
        return Ok();
    }

    private static JsonObject SettingsGet(HostContext ctx)
        => Ok(new JsonObject { ["settings"] = Node(ctx.Workspaces.LoadSettings()) });

    private static JsonObject SettingsSave(
        HostContext ctx,
        JsonObject request,
        Window.WindowController windowController)
    {
        var settings = request["settings"] as JsonObject
            ?? throw new InvalidOperationException("未指定 settings 对象");
        ctx.Workspaces.SaveSettings(ConfigImporter.FromJsonObject(settings));
        var loaded = ctx.Workspaces.LoadSettings();
        windowController.SetTheme(loaded.GetValueOrDefault("theme")?.ToString());
        return Ok(new JsonObject { ["settings"] = Node(loaded) });
    }

    private static JsonObject ConfigOpen(HostContext ctx, JsonObject request)
    {
        var workspaceId = request["workspaceId"]!.GetValue<string>();
        var configId = request["configId"]!.GetValue<string>();
        var config = ctx.Workspaces.LoadConfig(workspaceId, configId)
            ?? throw new InvalidOperationException("配置不存在");
        var plugin = WorkspaceService.ResolvePlugin(ctx.Plugins, config);
        if (plugin is null)
        {
            ctx.SetActive(workspaceId, configId, null, null);
            return Ok(new JsonObject
            {
                ["pluginMissing"] = true,
                ["sourceText"] = config.SourceText,
                ["snapshot"] = new JsonArray(),
                ["unrecognized"] = Node(config.Unrecognized)
            });
        }

        var session = FormSession.Create(plugin, new ConfigState
        {
            PluginKey = plugin.PluginKey,
            PluginVersion = plugin.Version,
            Values = config.Values,
            Enabled = config.Enabled,
            SourceText = config.SourceText,
            Unrecognized = config.Unrecognized
        });
        // 源码为权威：打开配置 = 源码 → 解析 → 表单（M4 起 layout/ini 亦可）
        if (!string.IsNullOrWhiteSpace(config.SourceText))
        {
            var import = session.Import(config.SourceText);
            if (!import.Ok)
            {
                return Fail(import.Errors);
            }
        }
        ctx.SetActive(workspaceId, configId, plugin, session);

        var snapshot = session.GetSnapshot();
        var errors = session.Validate();
        return Ok(new JsonObject
        {
            ["config"] = Node(new
            {
                id = config.Id,
                name = config.Name,
                pluginKey = config.PluginKey,
                pluginVersion = config.PluginVersion,
                pluginName = plugin.Name
            }),
            ["snapshot"] = Node(snapshot),
            ["sourceText"] = session.GetState().SourceText,
            ["errors"] = Node(errors),
            ["unrecognized"] = Node(session.Unrecognized),
            ["versionChanged"] = WorkspaceService.IsPluginVersionChanged(plugin, config),
            ["templates"] = Node(plugin.Templates.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                description = t.Description
            }))
        });
    }

    private static JsonObject ConfigDelete(HostContext ctx, JsonObject request)
    {
        ctx.Workspaces.DeleteConfig(
            request["workspaceId"]!.GetValue<string>(),
            request["configId"]!.GetValue<string>());
        ctx.SetActive(null, null, null, null);
        return Ok();
    }

    private static JsonObject ConfigReset(HostContext ctx, JsonObject request)
    {
        if (ctx.CurrentSession is null || ctx.CurrentConfig is null || ctx.CurrentPlugin is null)
        {
            return Fail(new[] { "当前没有打开的配置" });
        }
        var session = FormSession.Create(ctx.CurrentPlugin);
        ctx.SetActive(ctx.CurrentWorkspaceId!, ctx.CurrentConfig.Id, ctx.CurrentPlugin, session);
        PersistCurrent(ctx);
        return Ok(new JsonObject
        {
            ["snapshot"] = Node(session.GetSnapshot()),
            ["sourceText"] = session.GetState().SourceText
        });
    }

    private static JsonObject ConfigSaveSource(HostContext ctx, JsonObject request)
    {
        if (ctx.CurrentConfig is null)
        {
            return Fail(new[] { "当前没有打开的配置" });
        }
        ctx.CurrentConfig.SourceText = request["text"]?.GetValue<string>() ?? string.Empty;
        ctx.Workspaces.SaveConfig(ctx.CurrentConfig);
        return Ok();
    }

    private static JsonObject ConfigExportTo(HostContext ctx, JsonObject request)
    {
        if (ctx.CurrentSession is null)
        {
            return Fail(new[] { "当前没有打开的配置" });
        }
        var errors = ctx.CurrentSession.Validate();
        if (errors.Count > 0)
        {
            return Fail(errors);
        }
        var path = request["path"]?.GetValue<string>()
            ?? throw new InvalidOperationException("未指定导出路径");
        ctx.CurrentSession.Render();
        var text = ConfigReverseParser.AppendUnrecognized(
            ctx.CurrentSession.GetState().SourceText,
            ctx.CurrentSession.Unrecognized);
        File.WriteAllText(path, text);
        return Ok(new JsonObject { ["path"] = path });
    }

    private static JsonObject FormResult(HostContext ctx, FormCommand command)
    {
        if (ctx.CurrentSession is null)
        {
            return Fail(new[] { "当前没有打开的配置" });
        }
        var result = ctx.CurrentSession.Apply(command);
        if (!result.Ok)
        {
            return Fail(result.Errors, result.ErrorCode);
        }
        var isMutation = command is not ValidateCommand and not RenderCommand and not SnapshotCommand;
        if (isMutation)
        {
            PersistCurrent(ctx);
        }
        var text = command is RenderCommand
            ? result.RenderedText
            : isMutation ? ctx.CurrentSession.GetState().SourceText : null;
        return Ok(new JsonObject
        {
            ["snapshot"] = Node(ctx.CurrentSession.GetSnapshot()),
            ["text"] = text,
            ["errors"] = Node(ctx.CurrentSession.Validate()),
            ["newItemPath"] = result.NewItemPath,
            ["unrecognized"] = Node(ctx.CurrentSession.Unrecognized)
        });
    }

    private static JsonObject FormImportText(HostContext ctx, JsonObject request)
    {
        if (ctx.CurrentSession is null || ctx.CurrentPlugin is null)
        {
            return Fail(new[] { "当前没有打开的配置" });
        }
        var text = request["text"]?.GetValue<string>() ?? string.Empty;
        var result = ctx.CurrentSession.Import(text);
        if (!result.Ok)
        {
            return Fail(result.Errors);
        }
        PersistCurrent(ctx, render: false);
        return Ok(new JsonObject
        {
            ["snapshot"] = Node(ctx.CurrentSession.GetSnapshot()),
            ["errors"] = Node(ctx.CurrentSession.Validate()),
            ["unrecognized"] = Node(ctx.CurrentSession.Unrecognized),
            ["report"] = Node(new
            {
                unrecognizedLines = ctx.CurrentSession.Unrecognized.Count,
                canImport = ctx.CurrentPlugin.CanImport
            })
        });
    }

    private static JsonObject VersionsList(HostContext ctx, JsonObject request)
    {
        var versions = ctx.Workspaces.ListVersions(
            request["workspaceId"]!.GetValue<string>(),
            request["configId"]!.GetValue<string>());
        return Ok(new JsonObject
        {
            ["versions"] = Node(versions.Select(v => new
            {
                id = v.Id,
                note = v.Note,
                timestamp = v.Timestamp.ToString("yyyy-MM-dd HH:mm"),
                length = v.SourceText.Length,
                preview = v.SourceText.Split('\n').FirstOrDefault() ?? string.Empty
            }))
        });
    }

    private static JsonObject VersionSnapshot(HostContext ctx, JsonObject request)
    {
        if (ctx.CurrentConfig is null)
        {
            return Fail(new[] { "当前没有打开的配置" });
        }
        PersistCurrent(ctx);
        var version = ctx.Workspaces.SnapshotVersion(
            ctx.CurrentConfig,
            request["note"]?.GetValue<string>());
        return Ok(new JsonObject { ["versionId"] = version.Id });
    }

    private static JsonObject VersionRestore(HostContext ctx, JsonObject request)
    {
        if (ctx.CurrentWorkspaceId is null || ctx.CurrentConfig is null)
        {
            return Fail(new[] { "当前没有打开的配置" });
        }
        var config = ctx.Workspaces.RestoreVersion(
            ctx.CurrentWorkspaceId,
            ctx.CurrentConfig.Id,
            request["versionId"]!.GetValue<string>());
        ctx.CurrentConfig = config;
        return ConfigOpen(ctx, new JsonObject
        {
            ["workspaceId"] = ctx.CurrentWorkspaceId,
            ["configId"] = config.Id
        });
    }

    private static JsonObject VersionDelete(HostContext ctx, JsonObject request)
    {
        ctx.Workspaces.DeleteVersion(
            request["workspaceId"]!.GetValue<string>(),
            request["configId"]!.GetValue<string>(),
            request["versionId"]!.GetValue<string>());
        return Ok();
    }

    private static JsonObject ArchiveExportWorkspace(HostContext ctx, JsonObject request)
    {
        var path = ResolveExportPath(request);
        ctx.Archive.ExportWorkspace(request["workspaceId"]!.GetValue<string>(), path);
        return Ok(new JsonObject { ["path"] = path });
    }

    private static JsonObject ArchiveExportConfig(HostContext ctx, JsonObject request)
    {
        var path = ResolveExportPath(request);
        ctx.Archive.ExportConfig(
            request["workspaceId"]!.GetValue<string>(),
            request["configId"]!.GetValue<string>(),
            path);
        return Ok(new JsonObject { ["path"] = path });
    }

    private static JsonObject ArchiveImport(HostContext ctx, JsonObject request)
    {
        var path = request["path"]?.GetValue<string>()
            ?? throw new InvalidOperationException("未指定存档包路径");
        if (path == "SELFCHECK")
        {
            path = Path.Combine(Path.GetTempPath(), "ferry-m7-selfcheck.zip");
        }
        var result = ctx.Archive.Import(path);
        return Ok(new JsonObject
        {
            ["imported"] = result.ImportedConfigs,
            ["skipped"] = result.SkippedConfigs,
            ["packagedPlugins"] = Node(result.PackagedPlugins),
            ["localPlugins"] = Node(result.LocalPlugins),
            ["missingPlugins"] = Node(result.MissingPlugins),
            ["workspaceId"] = result.WorkspaceId
        });
    }

    private static JsonObject LogsPath()
        => Ok(new JsonObject { ["path"] = Path.Combine(AppContext.BaseDirectory, "ferry.log") });

    private static JsonObject LogsOpen()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ferry.log");
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true
        });
        return Ok();
    }

    /// <summary>应用数据目录（%AppData%/Ferry），供回收站等 UI 功能使用。</summary>
    private static JsonObject AppDataDir()
        => Ok(new JsonObject
        {
            ["path"] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Ferry")
        });

    private static string TrashDir()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ferry",
            "trash");

    private static JsonObject TrashList()
    {
        var dir = TrashDir();
        JsonNode[] items = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.zip")
                .Select(f => new FileInfo(f))
                .Select(fi => (JsonNode)new JsonObject
                {
                    ["name"] = fi.Name,
                    ["path"] = fi.FullName,
                    ["size"] = fi.Length,
                    ["modified"] = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                })
                .ToArray()
            : Array.Empty<JsonNode>();
        return Ok(new JsonObject { ["items"] = new JsonArray(items) });
    }

    private static JsonObject TrashDelete(JsonObject request)
    {
        var path = request["path"]!.GetValue<string>();
        var full = Path.GetFullPath(path);
        var dir = Path.GetFullPath(TrashDir());
        if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(full))
        {
            return Fail(new[] { "非法路径" });
        }
        File.Delete(full);
        return Ok();
    }

    private static JsonObject WindowMinimize(Window.WindowController controller)
    {
        controller.Minimize();
        return Ok();
    }

    private static JsonObject WindowMaximize(Window.WindowController controller)
    {
        controller.ToggleMaximize();
        return Ok();
    }

    private static JsonObject WindowClose(Window.WindowController controller)
    {
        controller.Close();
        return Ok();
    }

    private static JsonObject WindowDrag(Window.WindowController controller)
    {
        controller.BeginNativeDrag();
        return Ok();
    }

    private static JsonObject LogJs(JsonObject request)
    {
        Log("JS: " + (request["text"]?.GetValue<string>() ?? string.Empty));
        return Ok();
    }

    // ---------- 工具 ----------

    private static string ResolveExportPath(JsonObject request)
    {
        var path = request["path"]?.GetValue<string>();
        if (path == "SELFCHECK")
        {
            return Path.Combine(Path.GetTempPath(), "ferry-m7-selfcheck.zip");
        }
        return path ?? throw new InvalidOperationException("未指定导出路径");
    }

    private static void PersistCurrent(HostContext ctx, bool render = true)
    {
        if (ctx.CurrentSession is null || ctx.CurrentConfig is null)
        {
            return;
        }
        if (render)
        {
            try
            {
                ctx.CurrentSession.Render();
            }
            catch
            {
                // schema 缺失时保留现有文本
            }
        }
        var state = ctx.CurrentSession.GetState();
        ctx.CurrentConfig.SourceText = state.SourceText;
        ctx.CurrentConfig.Values = state.Values;
        ctx.CurrentConfig.Enabled = state.Enabled;
        ctx.CurrentConfig.Unrecognized = state.Unrecognized.ToList();
        ctx.Workspaces.SaveConfig(ctx.CurrentConfig);
    }

    private static object ToPluginDto(PluginDescriptor plugin) => new
    {
        key = plugin.PluginKey,
        name = plugin.Name,
        version = plugin.Version,
        description = plugin.Description,
        rendererType = plugin.RendererType,
        defaultFileName = plugin.DefaultFileName,
        canImport = plugin.CanImport,
        targetName = plugin.TargetName,
        targetVersion = plugin.TargetVersion,
        loadErrors = plugin.LoadErrors,
        templates = plugin.Templates.Select(t => new
        {
            id = t.Id,
            name = t.Name,
            description = t.Description
        })
    };

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

    /// <summary>以 camelCase 序列化 DTO（与 JS 侧字段名一致）。</summary>
    private static JsonNode? Node(object? value) => JsonSerializer.SerializeToNode(value, DtoOptions);

    private static JsonObject Ok(JsonObject? data = null)
    {
        var result = data ?? new JsonObject();
        result["ok"] = true;
        return result;
    }

    private static JsonObject Fail(IReadOnlyList<string> errors, string? errorCode = null)
    {
        var result = new JsonObject { ["ok"] = false };
        if (errorCode is not null)
        {
            result["errorCode"] = errorCode;
        }
        result["errors"] = Node(errors);
        return result;
    }

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
            WriteSpikeResult(new { ok = false, error = ex.Message });
        }
    }

    private static JsonObject OnSpikeResult(PhotinoWindow window, string message)
    {
        Log("spike-result");
        try
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "ferry-spike-result.json"),
                message);
        }
        finally
        {
            window.Close();
        }
        return Ok();
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

    /// <summary>会话状态：当前打开的工作空间/配置/插件/会话。</summary>
    private sealed class HostContext
    {
        public HostContext(
            PluginManager pluginManager,
            WorkspaceService workspaces,
            PortableArchiveService archive)
        {
            PluginManager = pluginManager;
            Workspaces = workspaces;
            Archive = archive;
        }

        public PluginManager PluginManager { get; }
        public WorkspaceService Workspaces { get; }
        public PortableArchiveService Archive { get; private set; }
        public IReadOnlyList<PluginDescriptor> Plugins { get; private set; } = Array.Empty<PluginDescriptor>();
        public FormSession? CurrentSession { get; private set; }
        public ConfigData? CurrentConfig { get; set; }
        public PluginDescriptor? CurrentPlugin { get; private set; }
        public string? CurrentWorkspaceId { get; private set; }

        public void RefreshArchivePlugins()
        {
            Plugins = PluginManager.LoadAllPlugins();
            Archive = new PortableArchiveService(Workspaces, Plugins);
        }

        public void SetActive(
            string? workspaceId,
            string? configId,
            PluginDescriptor? plugin,
            FormSession? session)
        {
            CurrentWorkspaceId = workspaceId;
            CurrentPlugin = plugin;
            CurrentSession = session;
            if (configId is null)
            {
                CurrentConfig = null;
            }
            else if (workspaceId is not null)
            {
                CurrentConfig = Workspaces.LoadConfig(workspaceId, configId);
            }
        }
    }
}
