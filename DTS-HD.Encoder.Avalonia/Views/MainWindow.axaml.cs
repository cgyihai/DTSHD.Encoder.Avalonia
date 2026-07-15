using System;
using Avalonia;
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
        // 导航图标已改为矢量 PathIconSource（见 MainWindow.axaml），无系统字体依赖，
        // 任何机器渲染一致，无需再按 OS 版本动态设置字体/glyph。
        // 首次加载完成后根据屏幕工作区动态适配窗口尺寸（支持低分辨率）
        ResizeToIdeal();
    }

    // ---------- 窗口尺寸自适应 ----------

    /// <summary>
    /// 根据主屏幕工作区动态计算窗口尺寸，保证在低分辨率屏幕上完全可见。
    /// 策略：
    /// - 高分辨率（工作区宽 ≥ 1920 且高 ≥ 1080）：使用原始 1500×1100 设计尺寸
    /// - 中等分辨率（1366×768 笔记本常见）：窗口占工作区 88%
    /// - 低分辨率（1280×720 平板/老显示器）：窗口占工作区 95%，且不高于工作区
    /// - 始终保证窗口完全在工作区内（不超出屏幕边界）
    /// - 高 DPI 缩放不影响逻辑像素，Avalonia 自动处理 Scaling
    /// </summary>
    private void ResizeToIdeal()
    {
        try
        {
            var screen = Screens.Primary;
            if (screen == null) return;

            // DPI 自适应关键：Avalonia 的 Screen.WorkingArea 是【物理像素】，而 Window.Width/Height
            // 是【逻辑像素(DIP)】。两者不能直接比较/赋值，否则在 125%/150% 缩放的 1080p 笔记本上
            // 窗口会过大甚至超出屏幕（用户反馈的“1080p 窗口过大”正是缩放屏下的这个 bug）。
            // 这里先用 screen.Scaling 把工作区换算成逻辑像素，全程用逻辑像素计算，
            // 仅在最后设置 Position（物理像素）时再乘回 scale。
            double scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
            var workArea = screen.WorkingArea;
            double availW = workArea.Width / scale;   // 逻辑像素
            double availH = workArea.Height / scale;   // 逻辑像素

            // 设计基准：高分辨率下的最佳尺寸
            const double designW = 1500;
            const double designH = 1100;
            // 最低尺寸：保证 UI 完整可用（比 XAML 的 MinWidth/MinHeight 更宽松）
            const double minW = 960;
            const double minH = 640;

            double targetW, targetH;

            if (availW >= designW + 120 && availH >= designH + 80)
            {
                // 高分辨率屏幕：保持设计尺寸
                targetW = designW;
                targetH = designH;
            }
            else if (availW >= 1600 && availH >= 900)
            {
                // 1080p：设计尺寸高度超出（1100 > 1080-任务栏），按工作区高度适配
                targetW = Math.Min(designW, availW - 80);
                targetH = Math.Min(designH, availH - 40);
            }
            else
            {
                // 低分辨率（笔记本/平板）：窗口占工作区 95%
                targetW = Math.Min(designW, availW * 0.95);
                targetH = Math.Min(designH, availH * 0.95);
            }

            // 兜底：不低于最低尺寸（除非工作区本身就比最低小）
            targetW = Math.Max(targetW, Math.Min(minW, availW - 20));
            targetH = Math.Max(targetH, Math.Min(minH, availH - 20));

            // 确保不超出工作区
            targetW = Math.Min(targetW, availW - 20);
            targetH = Math.Min(targetH, availH - 20);

            Width = Math.Round(targetW);    // 逻辑像素
            Height = Math.Round(targetH);   // 逻辑像素

            // 居中到工作区：Position 是【物理像素】，需把逻辑尺寸乘回 scale 再计算偏移，
            // 否则在缩放屏上窗口不会真正居中。
            var x = workArea.X + (workArea.Width - Width * scale) / 2;
            var y = workArea.Y + (workArea.Height - Height * scale) / 2;
            Position = new PixelPoint((int)x, (int)y);
        }
        catch
        {
            // 屏幕 API 不可用时保持 XAML 默认尺寸
        }
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
        // 主题切换后必须重新应用 Mica：EnabledMica 内部按当前主题选择 Mica 颜色，
        // 切换主题时不会自动更新背景色，需手动重新调用 ApplyBackdrop。
        ApplyBackdrop();
    }

    public void ApplyBackdrop()
    {
        // Mica 背景（仅 Windows 11+ 支持）。
        // Win10 上 EnabledMica 调用会静默回退为普通主题色背景（AppWindow 内部处理），
        // 不会崩溃。Program.cs 已根据 OS 版本动态选择 CompositionMode：
        //   - Win11：WinUIComposition（支持 Mica + 144Hz vsync）
        //   - Win10：DirectComposition（无 Mica，仍 GPU 合成 + vsync 跟随）
        try
        {
            bool useMica = AppServices.Settings.MicaEnabled && Program.IsWindows11;
            EnabledMica(useMica);
        }
        catch
        {
            // 非 Windows 平台或 Mica 不可用，回退到普通背景
        }
    }
}
