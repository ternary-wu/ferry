namespace Ferry.Core.Services.Rendering;

/// <summary>配置渲染器：把与格式无关的值树渲染为文本。</summary>
public interface IConfigRenderer
{
    string Render(Dictionary<string, object?> config);
}
