using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaFluentUI.Styling;
using AvaloniaFluentUI.Windowing;
using Avalonia.Styling;
using DTSHD.Encoder.Avalonia.Services;
using DTSHD.Encoder.Avalonia.ViewModels;

namespace DTSHD.Encoder.Avalonia.Views;

/// <summary>
/// 编码队列弹窗窗口（继承 AppWindow 以支持 Mica 背景）。
/// 关闭本窗口不影响编码——编码由 AppServices.Queue 在后台驱动，本窗口只展示队列状态。
/// DataContext 由 Show(owner, vm) 设置为 EncodePageViewModel，复用其 Jobs 集合和命令。
/// </summary>
public partial class EncodeQueueWindow : AppWindow
{
    public EncodeQueueWindow()
    {
        InitializeComponent();
        // 同步主题与 Mica 背景与主窗口一致
        ApplyTheme();
        ApplyBackdrop();
    }

    /// <summary>显示队列窗口（模态=false，避免阻塞主窗口操作）。复用主窗口的 VM。</summary>
    public void Show(Window owner, EncodePageViewModel vm)
    {
        DataContext = vm;
        Show(owner);
    }

    // 队列"信息"按钮：触发 VM 的 JobInfoCommand
    private void OnJobInfoClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is EncodeJob job && DataContext is EncodePageViewModel vm)
            vm.JobInfoCommand.Execute(job);
    }

    // OnCloseClick 已移除：AppWindow 标题栏右上角的关闭按钮已提供关闭功能

    // ---------- 主题 / 背景（与 MainWindow 一致）----------
    private void ApplyTheme()
    {
        var theme = AppServices.Settings.Theme;
        FluentAvaloniaTheme.Instance.CurrentTheme = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        // 主题切换后重新应用 Mica，使云母背景色跟随主题更新
        ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        try
        {
            // 与 MainWindow 一致：仅 Windows 11+ 才启用 Mica。
            // Win10 上 MicaEnabled 默认为 true，若不加 IsWindows11 判断会在 Win10 尝试
            // 应用云母背景（DWM 需要 Win11 22621+），导致背景表现不一致。
            EnabledMica(AppServices.Settings.MicaEnabled && Program.IsWindows11);
        }
        catch
        {
            // 非 Windows 11 平台回退到普通背景
        }
    }
}
