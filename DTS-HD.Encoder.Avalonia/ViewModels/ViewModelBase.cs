using CommunityToolkit.Mvvm.ComponentModel;

namespace DTSHD.Encoder.Avalonia.ViewModels;

/// <summary>
/// 所有页面/窗口 ViewModel 的基类。CommunityToolkit.Mvvm 源生成器驱动（partial ObservableObject）。
/// 与 Avalonia-Fluent-UI Gallery.ViewModelBase 保持一致的最小约定。
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>页面标题（可由导航栏或页面头部显示）。</summary>
    public virtual string Title => string.Empty;
}
