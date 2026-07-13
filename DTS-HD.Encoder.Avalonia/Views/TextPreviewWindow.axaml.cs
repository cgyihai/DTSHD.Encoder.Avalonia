using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AvaloniaFluentUI.Styling;
using AvaloniaFluentUI.Windowing;
using DTSHD.Encoder.Avalonia.Services;

namespace DTSHD.Encoder.Avalonia.Views;

/// <summary>
/// 通用文本预览弹窗（继承 AppWindow 支持 Mica 背景）。
/// 用于 .cfg 预览、命令预览、日志查看等场景。
/// 单例使用：每次 Show 时复用同一窗口实例，更新 Title/Content。
///
/// 模式说明：
/// - isEditable=false（默认）：只读 + 滚动条自动，适合 .cfg / 日志等长文本
/// - isEditable=true：可编辑 + 无滚动条（自动换行），适合命令预览（用户可修改后复制）
/// 背景跟随主题（不使用固定黑色），与主界面保持一致。
/// </summary>
public partial class TextPreviewWindow : AppWindow
{
    public TextPreviewWindow()
    {
        InitializeComponent();
        ApplyTheme();
        ApplyBackdrop();
    }

    /// <summary>显示弹窗（非模态）。
    /// title=窗口标题，subtitle=副标题（可选），content=文本内容，status=底部状态栏（可选）。
    /// isEditable=true 时切换为可编辑模式（无滚动条、自动换行），适合命令预览。</summary>
    public void Show(Window owner, string title, string content, string subtitle = "", string status = "", bool isEditable = false)
    {
        ApplyMode(isEditable, content);
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        StatusText.Text = status;
        Show(owner);
    }

    /// <summary>更新已显示弹窗的内容（不重新创建窗口）。</summary>
    public void UpdateContent(string title, string content, string subtitle = "", string status = "", bool isEditable = false)
    {
        ApplyMode(isEditable, content);
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        StatusText.Text = status;
        if (!IsActive) Activate();
    }

    /// <summary>切换显示模式：只读（.cfg / 日志）或可编辑（命令预览）。
    /// 滚动条可见性由 XAML 中 ScrollViewer.*ScrollBarVisibility=Auto 决定：
    /// 命令通常较短，Auto 模式下不会出现滑块；长文本（.cfg / 日志）超出时自动显示。</summary>
    private void ApplyMode(bool isEditable, string content)
    {
        ContentBox.Text = content;
        // 滚动到顶部 / 光标归零
        ContentBox.SelectionStart = 0;
        ContentBox.SelectionEnd = 0;
        ContentBox.CaretIndex = 0;
        // 切换 IsReadOnly：命令预览可编辑，.cfg / 日志只读
        ContentBox.IsReadOnly = !isEditable;
    }

    /// <summary>一键复制：将 TextBox 全部文本复制到剪贴板，并在状态栏提示结果。</summary>
    private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        var text = ContentBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = "（无内容可复制）";
            return;
        }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            // Avalonia 12：SetTextAsync 是 IClipboard 标准方法（参考 Gallery/IconsView 用法）
            await clipboard.SetTextAsync(text);
            StatusText.Text = $"已复制 {text.Length} 字符到剪贴板";
        }
        else
        {
            StatusText.Text = "剪贴板不可用";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

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
    }

    private void ApplyBackdrop()
    {
        try { EnabledMica(AppServices.Settings.MicaEnabled); }
        catch { /* 非 Windows 11 回退 */ }
    }
}

