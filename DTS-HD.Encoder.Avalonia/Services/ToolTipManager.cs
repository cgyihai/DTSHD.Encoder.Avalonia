using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 鼠标悬浮提示全局开关：在窗口根元素上设置可继承的 ToolTip.ServiceEnabled，
/// 关闭后整个窗口内的悬浮提示都不再弹出；同时统一显示延迟，避免划过时频繁闪烁。
/// </summary>
public static class ToolTipManager
{
    /// <summary>悬浮多久后显示提示（毫秒）。</summary>
    public const int ShowDelayMs = 500;

    public static void Apply(Control root)
    {
        ToolTip.SetServiceEnabled(root, AppServices.Settings.ShowToolTips);
        ToolTip.SetShowDelay(root, ShowDelayMs);
    }

    /// <summary>设置变更后刷新所有已打开窗口。</summary>
    public static void ApplyToAllWindows()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var w in desktop.Windows) Apply(w);
    }
}
