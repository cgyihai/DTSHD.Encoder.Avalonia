using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace DTSHD.Encoder.Avalonia.Controls;

/// <summary>
/// 页面宿主：替代 AvaloniaFluentUI.StackContent，解决 4K / 高刷新率下切换页面掉帧。
///
/// 【原因】StackContent 每次切换都 ① 通过 ViewLocator 重新创建整个页面（编码页约 800 个控件，
/// 构建 + 布局约 100ms），② 在 UI 线程上对整页做位移 + 透明度动画。Avalonia 不会把子树缓存成
/// GPU 纹理，动画每一帧都要用 Skia 重新光栅化整页（透明度还需离屏图层），4K 下远超 144Hz 的
/// 6.9ms 帧预算。
///
/// 【做法】仿 WinUI 的"内容只画一次、动画只做合成"：
///  1. 每个页面只创建一次并常驻（切换只改 IsVisible），切页无需重建/重新布局；
///  2. 开启动画时，先在渲染线程把新页面拍成一张快照位图（CreateCompositionVisualSnapshot），
///     动画期间真实页面不参与绘制，只对这张位图做合成层动画（Translation + Opacity，
///     运行在渲染线程，不受 UI 线程忙碌影响）——每帧只是一次贴图；
///  3. 动画结束后显示真实页面、移除快照（两者像素一致，无闪烁）。
/// 关闭动画时页面瞬间切换。
/// </summary>
public sealed class PageHost : Grid
{
    public static readonly StyledProperty<object?> PageProperty =
        AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

    /// <summary>当前页面的 ViewModel（通过 <see cref="ViewFactory"/> 映射为 View）。</summary>
    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>VM → View 工厂（默认使用项目的 ViewLocator）。</summary>
    public IDataTemplate ViewFactory { get; set; } = new ViewLocator();

    /// <summary>是否播放页面切换动画（由设置页开关控制）。</summary>
    public static bool AnimationsEnabled { get; set; }

    /// <summary>动画时长（短而轻：WinUI 页面进入动画约 167~300ms）。</summary>
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(220);
    private const float FromOffsetY = 24f;

    /// <summary>VM → 页面外框（Border 包一层：隐藏/透明设在外框上，快照拍里面的 View，
    /// 保证快照本身不带外框的透明度）。</summary>
    private readonly Dictionary<object, Border> _pages = new();
    private readonly Image _overlay = new() { IsHitTestVisible = false, IsVisible = false, Stretch = global::Avalonia.Media.Stretch.Fill };
    private Border? _current;
    private int _version;

    public PageHost()
    {
        ClipToBounds = true;
        Children.Add(_overlay);
        _overlay.ZIndex = 1000;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
            _ = ShowAsync(change.NewValue);
    }

    private Border? GetOrCreate(object vm)
    {
        if (_pages.TryGetValue(vm, out var frame)) return frame;
        var view = ViewFactory.Build(vm);
        if (view == null) return null;
        view.DataContext = vm;
        frame = new Border { Child = view, IsVisible = false };
        _pages[vm] = frame;
        Children.Insert(0, frame);
        return frame;
    }

    private async Task ShowAsync(object? vm)
    {
        int version = ++_version;
        var previous = _current;
        var next = vm == null ? null : GetOrCreate(vm);
        if (ReferenceEquals(next, previous) && next != null) return;
        _current = next;

        // 中断上一次尚未结束的动画
        ResetOverlay();
        foreach (var p in _pages.Values)
            if (!ReferenceEquals(p, next) && !ReferenceEquals(p, previous)) { p.IsVisible = false; p.Opacity = 1; }

        if (next == null) { if (previous != null) previous.IsVisible = false; return; }

        bool animate = AnimationsEnabled && previous != null && IsAttachedToVisualTree();
        if (!animate)
        {
            if (previous != null) previous.IsVisible = false;
            next.Opacity = 1;
            next.IsVisible = true;
            return;
        }

        Bitmap? snapshot = null;
        try
        {
            // ① 新页面先以透明方式参与布局（旧页面保持可见，用户看不到中间状态）
            next.Opacity = 0;
            next.IsVisible = true;
            UpdateLayout();

            var content = next.Child;
            var nextVisual = content != null ? ElementComposition.GetElementVisual(content) : null;
            var compositor = nextVisual?.Compositor;
            if (content != null && nextVisual != null && compositor != null)
            {
                // 快照是透明背景位图：子像素(ClearType)抗锯齿的文字画在透明像素上会变成乱码，
                // 拍快照期间改用灰度抗锯齿（只影响快照，拍完恢复；真实页面此时不可见）
                var oldMode = TextOptions.GetTextRenderingMode(content);
                TextOptions.SetTextRenderingMode(content, TextRenderingMode.Antialias);
                try
                {
                    await compositor.RequestCompositionBatchCommitAsync().Processed;
                    if (version != _version) return;
                    double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
                    // ② 在渲染线程把新页面拍成快照（只光栅化一次）
                    snapshot = await compositor.CreateCompositionVisualSnapshot(nextVisual, scaling);
                }
                finally { TextOptions.SetTextRenderingMode(content, oldMode); }
            }
        }
        catch { snapshot = null; }

        if (version != _version) { snapshot?.Dispose(); return; }

        if (previous != null) previous.IsVisible = false;
        if (snapshot == null)
        {
            next.Opacity = 1;
            return; // 快照失败：直接切换，不做动画
        }

        // ③ 动画期间真实页面不绘制，只对快照位图做合成层动画
        next.IsVisible = false;
        next.Opacity = 1;
        _overlay.Source = snapshot;
        _overlay.Width = next.Bounds.Width;
        _overlay.Height = next.Bounds.Height;
        _overlay.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
        _overlay.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        _overlay.IsVisible = true;
        UpdateLayout();

        var visual = ElementComposition.GetElementVisual(_overlay);
        if (visual != null)
        {
            var comp = visual.Compositor;
            var easing = new SplineEasing(0.1, 0.9, 0.2, 1.0);

            var move = comp.CreateVector3DKeyFrameAnimation();
            move.Target = "Translation";
            move.InsertKeyFrame(0f, new Vector3D(0, FromOffsetY, 0));
            move.InsertKeyFrame(1f, new Vector3D(0, 0, 0), easing);
            move.Duration = Duration;

            var fade = comp.CreateScalarKeyFrameAnimation();
            fade.Target = "Opacity";
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, easing);
            fade.Duration = Duration;

            visual.Translation = new Vector3D(0, FromOffsetY, 0);
            visual.Opacity = 0f;
            visual.StartAnimation("Translation", move);
            visual.StartAnimation("Opacity", fade);
        }

        await Task.Delay(Duration + TimeSpan.FromMilliseconds(20));
        if (version != _version) return;

        // ④ 显示真实页面并移除快照（同一批次提交，无闪烁）
        next.IsVisible = true;
        ResetOverlay();
    }

    private void ResetOverlay()
    {
        var visual = ElementComposition.GetElementVisual(_overlay);
        if (visual != null)
        {
            visual.StopAnimation("Translation");
            visual.StopAnimation("Opacity");
            visual.Translation = new Vector3D(0, 0, 0);
            visual.Opacity = 1f;
        }
        _overlay.IsVisible = false;
        var old = _overlay.Source as IDisposable;
        _overlay.Source = null;
        // 延后释放：确保渲染线程已不再引用该位图（4K 快照约 30MB，不能只等 GC）
        if (old != null) DispatcherTimer.RunOnce(old.Dispose, TimeSpan.FromSeconds(2));
    }

    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) != null;
}
