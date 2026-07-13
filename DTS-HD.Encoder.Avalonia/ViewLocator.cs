using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DTSHD.Encoder.Avalonia.ViewModels;

namespace DTSHD.Encoder.Avalonia;

/// <summary>
/// VM→View 工厂（与 Avalonia-Fluent-UI Gallery 的 ViewLocator 一致的手工映射，
/// 不依赖反射/约定式命名）。注册顺序即导航可达的页面集合。
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public bool SupportsRecycling => false;

    public Control? Build(object? data)
    {
        if (data == null) return null;
        var name = data.GetType().FullName;
        return name switch
        {
            "DTSHD.Encoder.Avalonia.ViewModels.EncodePageViewModel"     => new Views.EncodeView(),
            "DTSHD.Encoder.Avalonia.ViewModels.SettingsPageViewModel"   => new Views.SettingsView(),
            "DTSHD.Encoder.Avalonia.ViewModels.StreamToolsPageViewModel" => new Views.StreamToolsView(),
            _ => new TextBlock { Text = $"View not registered for {name}" },
        };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
