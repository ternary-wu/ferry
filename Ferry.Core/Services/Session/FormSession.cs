using Ferry.Core.Models;
using Ferry.Core.Services.Parsing;
using Ferry.Core.Services.Form;
using Ferry.Core.Services.Rendering;
using Ferry.Core.Services.Session.Protocol;

namespace Ferry.Core.Services.Session;

/// <summary>
/// 表单会话引擎：命令式协议的一等实现。
/// 实例式入口（单机/Photino）：Create + 直接方法调用；
/// 无状态入口（服务器）：Execute(plugin, state, command) 返回新状态，底层共享同一执行内核。
/// 不暴露事件树；UI 每次变更后重新 GetSnapshot()。
/// </summary>
public sealed class FormSession
{
    private readonly PluginDescriptor _plugin;
    private List<FormNode> _roots;
    private long _version;
    private string _sourceText = string.Empty;
    private List<string> _unrecognized = new();

    private FormSession(
        PluginDescriptor plugin,
        List<FormNode> roots,
        long version,
        string sourceText,
        List<string>? unrecognized = null)
    {
        _plugin = plugin;
        _roots = roots;
        _version = version;
        _sourceText = sourceText;
        _unrecognized = unrecognized ?? new List<string>();
    }

    public PluginDescriptor Plugin => _plugin;
    public long Version => _version;

    /// <summary>创建会话：可从 ConfigState 恢复（values/enabled/version/sourceText）。</summary>
    public static FormSession Create(PluginDescriptor plugin, ConfigState? state = null)
    {
        var values = state?.Values ?? new Dictionary<string, object?>();
        var enabled = state?.Enabled ?? new Dictionary<string, bool>();
        var roots = FormBuilder.Build(plugin.Schema ?? new ConfigSchema(), values, enabled);
        return new FormSession(
            plugin,
            roots,
            state?.Version ?? 0,
            state?.SourceText ?? string.Empty,
            state?.Unrecognized);
    }

    /// <summary>当前表单快照树（渲染所需全部状态）。</summary>
    public List<FormFieldSnapshot> GetSnapshot() => SnapshotBuilder.BuildAll(_roots);

    /// <summary>导入时未能识别的原始内容（layout/ini 宽松解析产生）。</summary>
    public IReadOnlyList<string> Unrecognized => _unrecognized;

    /// <summary>校验整棵树并返回错误列表（同时写回各节点 ValidationError）。</summary>
    public List<string> Validate() => ConfigValidator.Validate(_roots);

    /// <summary>渲染当前值为配置文本（不做校验），并缓存为 SourceText。</summary>
    public string Render()
    {
        if (_plugin.Schema is null)
        {
            throw new InvalidOperationException("插件 schema 缺失，无法渲染");
        }
        var config = ConfigValueCollector.Collect(_roots);
        var renderer = RendererFactory.Create(_plugin);
        _sourceText = renderer.Render(config);
        return _sourceText;
    }

    /// <summary>产出可序列化文档状态（全量值 + 启用状态 + 版本）。</summary>
    public ConfigState GetState() => new()
    {
        PluginKey = _plugin.PluginKey,
        PluginVersion = _plugin.Version,
        Values = ConfigValueCollector.Collect(_roots, includeDisabled: true),
        Enabled = CollectEnabledStates(),
        SourceText = _sourceText,
        Unrecognized = _unrecognized.ToList(),
        Version = _version
    };

    /// <summary>应用一条命令（实例式入口）。</summary>
    public OperationResult Apply(FormCommand command) => command switch
    {
        SetValueCommand c => SetValue(c.Path, c.Value),
        ToggleEnabledCommand c => ToggleEnabled(c.Path, c.Enabled),
        AddItemCommand c => AddItem(c.ArrayPath),
        RemoveItemCommand c => RemoveItem(c.Path),
        ApplyPresetCommand c => ApplyPreset(c.PresetId),
        ImportCommand c => Import(c.Text),
        ValidateCommand => ValidateResult(),
        RenderCommand => RenderResult(),
        SnapshotCommand => SnapshotResult(),
        _ => Fail("unsupported", "不支持的命令")
    };

    /// <summary>
    /// 无状态入口（服务器）：请求携带 ConfigState + 命令 → 返回新 ConfigState；
    /// 可选 ExpectedVersion 做乐观锁，冲突返回 ErrorCode=conflict。
    /// </summary>
    public static OperationResult Execute(
        PluginDescriptor plugin,
        ConfigState state,
        FormCommand command,
        long? expectedVersion = null)
    {
        if (expectedVersion is long expected && state.Version != expected)
        {
            return new OperationResult
            {
                Ok = false,
                ErrorCode = "conflict",
                Errors = new List<string> { $"版本冲突：期望 {expected}，当前 {state.Version}" },
                Version = state.Version
            };
        }
        return Create(plugin, state).Apply(command);
    }

    public OperationResult SetValue(string path, object? value)
    {
        var node = PathResolver.Resolve(_roots, path);
        if (node is null)
        {
            return Fail("not_found", $"路径不存在：{path}");
        }
        node.Value = value;
        _version++;
        return Ok();
    }

    public OperationResult ToggleEnabled(string path, bool? enabled = null)
    {
        var node = PathResolver.Resolve(_roots, path);
        if (node is null)
        {
            return Fail("not_found", $"路径不存在：{path}");
        }
        if (!node.IsSelectable)
        {
            return Fail("validation", "父级未启用，无法操作该字段");
        }
        if (node.Definition.Required)
        {
            return Fail("validation", "必填字段不可取消");
        }
        node.SetEnabled(enabled ?? !node.IsEnabled);
        _version++;
        return Ok();
    }

    public OperationResult AddItem(string arrayPath)
    {
        var node = PathResolver.Resolve(_roots, arrayPath);
        if (node is null)
        {
            return Fail("not_found", $"路径不存在：{arrayPath}");
        }
        if (node.Definition.Type != FieldType.Array)
        {
            return Fail("validation", $"字段「{node.Definition.Id}」不是数组字段");
        }
        var item = node.AddItem();
        _version++;
        return Ok(item.Path);
    }

    public OperationResult RemoveItem(string path)
    {
        var node = PathResolver.Resolve(_roots, path);
        if (node is null)
        {
            return Fail("not_found", $"路径不存在：{path}");
        }
        if (!node.IsArrayItem)
        {
            return Fail("validation", "只能移除数组项");
        }
        node.RemoveItem();
        _version++;
        return Ok();
    }

    public OperationResult ApplyPreset(string presetId)
    {
        var preset = _plugin.Templates.FirstOrDefault(t => t.Id == presetId || t.Name == presetId);
        if (preset is null)
        {
            return Fail("not_found", $"模板不存在：{presetId}");
        }
        _roots = FormBuilder.Build(_plugin.Schema ?? new ConfigSchema(), preset.Values);
        ApplyPresetModules(_roots, preset);
        _version++;
        return Ok();
    }

    public OperationResult Import(string text)
    {
        try
        {
            var parsed = _plugin.RendererType is "json" or "yaml"
                ? new ParseResult { Values = ConfigImporter.Parse(_plugin, text) }
                : ConfigReverseParser.Parse(_plugin, text);
            _roots = FormBuilder.Build(_plugin.Schema ?? new ConfigSchema(), parsed.Values);
            _sourceText = text;
            _unrecognized = parsed.Unrecognized;
            _version++;
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail("validation", $"导入失败：{ex.Message}");
        }
    }

    private OperationResult ValidateResult()
    {
        var errors = Validate();
        return new OperationResult
        {
            Ok = errors.Count == 0,
            Errors = errors,
            Version = _version
        };
    }

    private OperationResult RenderResult()
    {
        try
        {
            return new OperationResult
            {
                Ok = true,
                RenderedText = Render(),
                Version = _version
            };
        }
        catch (Exception ex)
        {
            return Fail("validation", $"渲染失败：{ex.Message}");
        }
    }

    private OperationResult SnapshotResult() => new()
    {
        Ok = true,
        Snapshot = GetSnapshot(),
        Version = _version
    };

    private OperationResult Ok(string? newItemPath = null) => new()
    {
        Ok = true,
        State = GetState(),
        Version = _version,
        NewItemPath = newItemPath
    };

    private static OperationResult Fail(string errorCode, string message) => new()
    {
        Ok = false,
        ErrorCode = errorCode,
        Errors = new List<string> { message }
    };

    private Dictionary<string, bool> CollectEnabledStates()
    {
        var map = new Dictionary<string, bool>();
        void Walk(FormNode node)
        {
            if (!node.IsArrayItem)
            {
                map[node.Path] = node.IsEnabled;
            }
            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }
        foreach (var root in _roots)
        {
            Walk(root);
        }
        return map;
    }

    /// <summary>
    /// 预设只决定初始勾选与初始值：modules 列出的模块启用（级联向上），其余禁用。
    /// 勾选父模块不自动勾选子模块。
    /// </summary>
    private static void ApplyPresetModules(List<FormNode> roots, ConfigPreset preset)
    {
        void Walk(FormNode node)
        {
            if (node.Definition.Module && !node.IsArrayItem)
            {
                var enabled = preset.Modules.Contains(node.Path)
                    || preset.Modules.Contains(node.Definition.Id);
                node.SetEnabled(enabled, cascadeUp: true);
            }
            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }
        foreach (var root in roots)
        {
            Walk(root);
        }
    }
}
