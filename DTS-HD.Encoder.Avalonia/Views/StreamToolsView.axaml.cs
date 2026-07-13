using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaFluentUI.Controls;
using DTSHD.Encoder.Avalonia.ViewModels;

namespace DTSHD.Encoder.Avalonia.Views;

/// <summary>
/// StreamTools 页 View：code-behind 仅做"UI 平台服务"接线（文件选择器/信息对话框）
/// + PBR Canvas 像素绘制（对应 WinUI3 StreamToolsPage.DrawPbrGraph 的坐标轴/折线/刻度逻辑迁移）。
/// 业务逻辑全部在 StreamToolsPageViewModel 中；本类不直接持有状态（除 Canvas 绘图）。
/// </summary>
public partial class StreamToolsView : UserControl
{
    public StreamToolsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        // 关键：ViewLocator.SupportsRecycling=false 意味着每次切换页面都创建新 View 销毁旧 View，
        // 但 VM 是缓存的（MainWindowViewModel._cache）——若不退订，旧 View 的 OnPbrPointsChanged
        // 会一直挂在 VM 上，访问已脱离可视树的 PbrCanvas，造成内存泄漏与无效 CPU 开销。
        Unloaded += OnUnloaded;
    }

    private StreamToolsPageViewModel? Vm => DataContext as StreamToolsPageViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;

        // ---- 文件选择器注入（IStorageProvider 通过 TopLevel 取得，VM 不引用控件）----
        // 对应 WinUI3 OnBrowse（FileOpenPicker）/ OnBrowseSave（FileSavePicker）。
        vm.PickOpenFileDelegate = () => PickOpenFileAsync("选择 .dtshd / .cpt", DtshdOpenFileTypes);
        vm.PickSaveFileDelegate = () => PickSaveFileAsync("保存输出", DtshdSaveFileTypes, "dtshd");

        // ---- 信息对话框（对应 WinUI3 ShowInfoDelegate 用 ContentDialog 实现）----
        vm.ShowInfoDelegate = ShowInfoAsync;

        // ---- PBR Canvas：订阅数据点变化并重绘（对应 WinUI3 _pbrPoints 变化 + SizeChanged）----
        // VM 仅暴露 PbrPoints 数据点；View 的 Canvas 负责实际像素绘制（VM 不含像素计算）。
        // 先退订再订阅，防止 View 重建时累积（虽然 Unloaded 已处理，这里防御性幂等）。
        vm.PbrPoints.CollectionChanged -= OnPbrPointsChanged;
        vm.PbrPoints.CollectionChanged += OnPbrPointsChanged;
        DrawPbrGraph();   // 若 VM 已有数据点（如重新进入页面）立即绘制
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            vm.PbrPoints.CollectionChanged -= OnPbrPointsChanged;
    }

    // ============ PBR Canvas 绘制（移植自 WinUI3 DrawPbrGraph）============
    // X 轴 = 时间（VM 以序号均匀分布），Y 轴 = 比特率 kbps；绘制坐标轴 + Y 刻度 + 折线 + 端点标签。
    private void OnPbrPointsChanged(object? sender, NotifyCollectionChangedEventArgs e) => DrawPbrGraph();

    private void OnPbrCanvasSizeChanged(object? sender, SizeChangedEventArgs e) => DrawPbrGraph();

    private void DrawPbrGraph()
    {
        if (PbrCanvas == null) return;
        PbrCanvas.Children.Clear();
        var vm = Vm;
        if (vm == null || vm.PbrPoints.Count < 2) return;

        // Avalonia 用 Bounds.Width/Height 取代 WinUI3 的 ActualWidth/ActualHeight
        double w = PbrCanvas.Bounds.Width, h = PbrCanvas.Bounds.Height;
        if (w < 20 || h < 20) return;

        const double padL = 48, padB = 22, padT = 12, padR = 12;
        double plotW = w - padL - padR, plotH = h - padT - padB;

        var pts = vm.PbrPoints;
        double maxKbps = pts.Max(p => p.Value);
        double minKbps = pts.Min(p => p.Value);
        if (maxKbps <= minKbps) maxKbps = minKbps + 1;
        int n = pts.Count;

        var axisBrush = Brushes.Gray;
        IBrush lineBrush = ResolveAccentBrush();

        // 坐标轴（左轴 + 底轴）
        PbrCanvas.Children.Add(new Line { StartPoint = new Point(padL, padT), EndPoint = new Point(padL, padT + plotH), Stroke = axisBrush, StrokeThickness = 1 });
        PbrCanvas.Children.Add(new Line { StartPoint = new Point(padL, padT + plotH), EndPoint = new Point(padL + plotW, padT + plotH), Stroke = axisBrush, StrokeThickness = 1 });

        // y 轴刻度（max / mid / min）
        void YLabel(double val, double y)
        {
            PbrCanvas.Children.Add(new Line { StartPoint = new Point(padL - 3, y), EndPoint = new Point(padL, y), Stroke = axisBrush, StrokeThickness = 1 });
            var tb = new TextBlock { Text = val.ToString("0", CultureInfo.InvariantCulture), FontSize = 10, Foreground = axisBrush };
            Canvas.SetLeft(tb, 2);
            Canvas.SetTop(tb, y - 7);
            PbrCanvas.Children.Add(tb);
        }
        YLabel(maxKbps, padT);
        YLabel((maxKbps + minKbps) / 2, padT + plotH / 2);
        YLabel(minKbps, padT + plotH);

        // 曲线（Avalonia 12 的 Polyline.Points 为 IList<Point>，用 List<Point> 装载）
        var poly = new Polyline { Stroke = lineBrush, StrokeThickness = 1.5 };
        var pc = new List<Point>();
        for (int i = 0; i < n; i++)
        {
            double x = padL + (n == 1 ? 0 : plotW * i / (n - 1));
            double y = padT + plotH * (1 - (pts[i].Value - minKbps) / (maxKbps - minKbps));
            pc.Add(new Point(x, y));
        }
        poly.Points = pc;
        PbrCanvas.Children.Add(poly);

        // 标题 + 端点序号（VM 以序号作 X 轴，按比例均匀分布）
        var title = new TextBlock { Text = "Peak Bit Rate Schedule (kbps)", FontSize = 11, Foreground = axisBrush };
        Canvas.SetLeft(title, padL + 6);
        Canvas.SetTop(title, padT - 2);
        PbrCanvas.Children.Add(title);
        var t0 = new TextBlock { Text = "#" + pts[0].Time.ToString("0", CultureInfo.InvariantCulture), FontSize = 10, Foreground = axisBrush };
        Canvas.SetLeft(t0, padL);
        Canvas.SetTop(t0, padT + plotH + 4);
        PbrCanvas.Children.Add(t0);
        var t1 = new TextBlock { Text = "#" + pts[^1].Time.ToString("0", CultureInfo.InvariantCulture), FontSize = 10, Foreground = axisBrush };
        Canvas.SetLeft(t1, padL + plotW - 60);
        Canvas.SetTop(t1, padT + plotH + 4);
        PbrCanvas.Children.Add(t1);
    }

    /// <summary>解析强调色画笔（对应 WinUI3 Application.Current.Resources["AccentFillColorDefaultBrush"]）；查找失败时回退 DodgerBlue。</summary>
    private IBrush ResolveAccentBrush()
    {
        try
        {
            if (this.TryFindResource("AccentFillColorDefaultBrush", out var obj) && obj is IBrush b)
                return b;
        }
        catch
        {
            // 资源查找异常时回退到默认蓝色
        }
        return Brushes.DodgerBlue;
    }

    // ============ 文件选择对话框 ============
    private static readonly IReadOnlyList<FilePickerFileType> DtshdOpenFileTypes =
        new[]
        {
            new FilePickerFileType("DTS-HD 码流") { Patterns = new[] { "*.dtshd", "*.cpt" } },
            new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } },
        };

    private static readonly IReadOnlyList<FilePickerFileType> DtshdSaveFileTypes =
        new[]
        {
            new FilePickerFileType("DTS-HD Master Audio") { Patterns = new[] { "*.dtshd" } },
        };

    private async Task<string?> PickOpenFileAsync(string title, IReadOnlyList<FilePickerFileType> types)
    {
        var toplevel = TopLevel.GetTopLevel(this);
        if (toplevel == null) return null;
        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types,
        };
        var values = await toplevel.StorageProvider.OpenFilePickerAsync(options);
        return values.Count > 0 ? values[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSaveFileAsync(string title, IReadOnlyList<FilePickerFileType> types, string defaultExtension)
    {
        var toplevel = TopLevel.GetTopLevel(this);
        if (toplevel == null) return null;
        var options = new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            FileTypeChoices = types,
        };
        var value = await toplevel.StorageProvider.SaveFilePickerAsync(options);
        return value?.TryGetLocalPath();
    }

    // ============ 信息对话框 ============
    private async Task ShowInfoAsync(string title, string msg)
    {
        await new ContentDialog
        {
            Title = title,
            Content = msg,
            CloseButtonText = "确定",
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }
}
