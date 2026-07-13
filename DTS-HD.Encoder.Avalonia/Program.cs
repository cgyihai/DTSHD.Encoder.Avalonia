using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Win32;
using Avalonia.X11;

namespace DTSHD.Encoder.Avalonia;

/// <summary>
/// 程序入口。在 Avalonia-Fluent-UI Gallery.Desktop 经典桌面启动方式基础上，
/// 启用 GPU 硬件合成 / 高 DPI 自适应 / 高帧率（跟随显示器 vsync，不锁 60fps）。
/// </summary>
internal sealed class Program
{
    // STAThread 是 WinUI 互操作 / COM / 文件对话框的要求。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                // —— GPU 硬件渲染（显式声明，避免依赖默认值）——
                // AngleEgl = ANGLE EGL (DirectX 11 GPU 渲染)，是 WinUIComposition 的前置条件；
                // Software = 软件回退（GPU 不可用时兜底，保证不会启动失败）。
                RenderingMode = [Win32RenderingMode.AngleEgl,
                                 Win32RenderingMode.Software],

                // —— GPU 硬件合成 + Mica 模糊背景 ——
                // WinUIComposition = 把 Avalonia 渲染到 Windows.UI.Composition 树（GPU 合成 + 支持云母）；
                //   - 默认跟随显示器 vsync（144Hz 显示器即 144fps，不锁 60fps）
                //   - 是 EnabledMica(true) 生效的前提
                // DirectComposition = GPU 合成无 Mica（WinUIComposition 不可用时回退）；
                // RedirectionSurface = 软件兜底（前两者都不可用时保证窗口仍能显示）。
                CompositionMode = [Win32CompositionMode.WinUIComposition,
                                  Win32CompositionMode.DirectComposition,
                                  Win32CompositionMode.RedirectionSurface],

                // 弹出层（Flyout/ContextMenu）使用原生 Overlay，避免冗余合成
                OverlayPopups = false,
            })
            .With(new X11PlatformOptions
            {
                // Linux 下使用 GLX GPU 渲染
                RenderingMode = [X11RenderingMode.Glx],
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
