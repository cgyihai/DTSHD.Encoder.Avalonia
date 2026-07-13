using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaFluentUI.Controls;
using DTSHD.Encoder.Avalonia.ViewModels;

namespace DTSHD.Encoder.Avalonia.Views;

/// <summary>
/// 设置页 View：code-behind 仅做"UI 平台服务"接线（文件夹选择器/信息对话框/主题与背景回调）。
/// 业务逻辑全部在 SettingsPageViewModel 中；本类不直接持有状态。
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private SettingsPageViewModel? Vm => DataContext as SettingsPageViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;

        // ---- 文件夹选择器注入（IStorageProvider 通过 TopLevel 取得，VM 不引用控件）----
        vm.PickFolderDelegate = PickFolderAsync;

        // ---- 信息对话框 ----
        vm.ShowInfoDelegate = ShowInfoAsync;

        // ---- 主题 / 背景回调（由 MainWindow 实现，对应 WinUI3 (App.MainWindow as MainWindow)?.ApplyTheme()）----
        vm.ApplyThemeDelegate = () => App.MainWindowInstance?.ApplyTheme();
        vm.ApplyBackdropDelegate = () => App.MainWindowInstance?.ApplyBackdrop();
    }

    // ============ 文件夹选择对话框 ============
    private async Task<string?> PickFolderAsync()
    {
        var toplevel = TopLevel.GetTopLevel(this);
        if (toplevel == null) return null;
        var options = new FolderPickerOpenOptions
        {
            Title = "选择工具集目录",
            AllowMultiple = false,
        };
        var folders = await toplevel.StorageProvider.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
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
