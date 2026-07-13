using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using AvaloniaFluentUI.Styling;
using AvaloniaFluentUI.Windowing;
using DTSHD.Encoder.Avalonia.Services;
using DTSHD.Encoder.Avalonia.ViewModels;

namespace DTSHD.Encoder.Avalonia.Views;

/// <summary>
/// 主窗口（继承 AvaloniaFluentUI.AppWindow 以获取 Mica/标题栏/启动画面能力）。
/// </summary>
public partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        InitializeComponent();
        // 应用启动时：恢复主题/Mica 偏好（与 WinUI3 一致）
        ApplyTheme();
        ApplyBackdrop();
        // 关闭软件时清掉后台原生引擎进程，避免残留实例下次启动造成混淆
        this.Closing += (_, _) =>
        {
            try { AppServices.ShutdownEngines(); } catch { }
        };
    }

    private void OnNavLoaded(object? sender, RoutedEventArgs e)
    {
        // 启动后默认进入编码页（保证首次未点选时也有页面）
        if (DataContext is MainWindowViewModel vm) vm.Initialize();
    }

    // ---------- 主题 / 背景 ----------
    public void ApplyTheme()
    {
        // FluentAvaloniaTheme 单例切换主题（与 Gallery 一致）
        var theme = AppServices.Settings.Theme;
        FluentAvaloniaTheme.Instance.CurrentTheme = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public void ApplyBackdrop()
    {
        // Mica 背景（仅 Windows 11+ 支持）。
        // 主动调用 EnabledMica(true/false)：
        // - true  → Background=Transparent + TransparencyLevelHint=[Mica]（GPU 合成画布显示桌面模糊）
        // - false → 内部 ResetBackground() 重置为主题色 (#202020/#F3F3F3) + 清空 TransparencyLevelHint
        // 解除刷新率限制：Mica 在高刷新率下的合成开销由 GPU 承担（WinUIComposition 已启用硬件合成），
        // 不再用 < 120Hz 防护强制跳过——让用户开关真正生效。
        try
        {
            EnabledMica(AppServices.Settings.MicaEnabled);
        }
        catch
        {
            // 非 Windows 平台或 Mica 不可用，回退到普通背景
        }
    }
}
