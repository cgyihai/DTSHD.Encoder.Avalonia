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

    /// <summary>
    /// 判断当前系统是否为 Windows 11（Build ≥ 22000）。
    /// Win11 支持 Segoe Fluent Icons 字体 + Mica 背景；
    /// Win10 回退到 Segoe MDL2 Assets 字体 + 普通背景。
    /// </summary>
    public static bool IsWindows11 { get; } = Environment.OSVersion.Platform == PlatformID.Win32NT
        && Environment.OSVersion.Version >= new Version(10, 0, 22000, 0);

    /// <summary>
    /// 判断当前系统是否为 Windows 10（Build ≥ 10240）。
    /// 用于决定是否启用 DirectComposition（Win10 1809+ 全面支持）。
    /// </summary>
    public static bool IsWindows10OrLater { get; } = Environment.OSVersion.Platform == PlatformID.Win32NT
        && Environment.OSVersion.Version >= new Version(10, 0);

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

                // —— GPU 合成层 + Mica 模糊背景 ——
                // Win11（Build≥22000）：启用 WinUIComposition 获得 Mica 模糊背景 + 高帧率 vsync 跟随。
                // Win10（Build≥10240）：WinUIComposition 在部分版本缺少 comsvc.dll，直接用 DirectComposition 更稳定；
                //   Mica 不可用，EnabledMica 调用会被 AppWindow 内部静默忽略。
                //   DirectComposition 仍提供 GPU 合成与 vsync 跟随（144Hz 显示器即 144fps，不锁 60fps）。
                // 更低版本：回退到 RedirectionSurface（软件合成兜底，保证窗口仍能显示）。
                CompositionMode = IsWindows11
                    ? [Win32CompositionMode.WinUIComposition,
                       Win32CompositionMode.DirectComposition,
                       Win32CompositionMode.RedirectionSurface]
                    : IsWindows10OrLater
                        ? [Win32CompositionMode.DirectComposition,
                           Win32CompositionMode.RedirectionSurface]
                        : [Win32CompositionMode.RedirectionSurface],

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
