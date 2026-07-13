using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DTSHD.Encoder.Avalonia.Services;
using DTSHD.Encoder.Avalonia.ViewModels;

namespace DTSHD.Encoder.Avalonia;

public partial class App : Application
{
    /// <summary>主窗口实例（供 SettingsPage 之类的子页面调用 ApplyTheme/ApplyBackdrop）。</summary>
    public static Views.MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        // 静态资源里的 AvaloniaResource（图标/主题字典）会随 InitializeComponent 加载
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 后台初始化引擎并预连接（不阻塞 UI；编码页用圆形加载图显示连接进度）
        _ = AppServices.InitEngineAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = new Views.MainWindow();
            MainWindowInstance = main;
            main.DataContext = new MainWindowViewModel();
            desktop.MainWindow = main;
            main.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
