using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaFluentUI.Controls;
using DTSHD.Encoder.Avalonia.Services;
using DTSHD.Encoder.Avalonia.ViewModels;

namespace DTSHD.Encoder.Avalonia.Views;

/// <summary>
/// 编码页 View：code-behind 仅做"UI 平台服务"接线（文件选择器/拖放/对话框/SpeakerLayout 同步）。
/// 业务逻辑全部在 EncodePageViewModel 中；本类不直接持有状态。
/// </summary>
public partial class EncodeView : UserControl
{
    public EncodeView()
    {
        InitializeComponent();
        // 拖放：InputCard 已在 XAML 设置 DragDrop.AllowDrop="True"，此处订阅路由事件。
        InputCard.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        InputCard.AddHandler(DragDrop.DropEvent, OnDrop);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private EncodePageViewModel? Vm => DataContext as EncodePageViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;

        // ---- 文件选择器注入（IStorageProvider 通过 TopLevel 取得，VM 不引用控件）----
        vm.PickAudioFileDelegate    = () => PickOpenFileAsync("选择音频文件", AudioFileTypes);
        vm.PickChannelFileDelegate  = label => PickOpenFileAsync($"选择声道文件 - {label}", AudioFileTypes);
        vm.PickAafFileDelegate       = () => PickOpenFileAsync("选择 AAF 元数据文件", AafFileTypes);
        vm.PickCsvFileDelegate       = () => PickOpenFileAsync("选择 CSV 分支点文件", CsvFileTypes);
        vm.PickOutputFileDelegate   = () => PickSaveFileAsync("保存编码输出", OutputFileTypes, "dtshd");
        vm.PickSettingsOpenDelegate = () => PickOpenFileAsync("加载编码设置", SettingsFileTypes);
        vm.PickSettingsSaveDelegate = () => PickSaveFileAsync("另存为编码设置", SettingsFileTypes, "json");

        // ---- 信息 / 确认对话框 ----
        vm.ShowInfoDelegate    = ShowInfoAsync;
        vm.ShowConfirmDelegate = ShowConfirmAsync;

        // ---- SpeakerLayout 同步：声道布局变化时刷新图示 ----
        SpeakerHost.SetChannels(vm.CurrentChannels);
        if (vm is INotifyPropertyChanged npc)
            npc.PropertyChanged += OnVmPropertyChanged;

        // ---- 异步加载下混默认值（启动性能优化：避免 VM 构造函数同步 IO 阻塞 UI）----
        // properties 文件读取在线程池执行，赋值回 UI 线程
        _ = vm.LoadDownmixDefaultsAsync();
    }

    /// <summary>View 卸载时退订 VM 的 PropertyChanged，避免内存泄漏。
    /// ViewLocator.SupportsRecycling=false 意味着每次切换页面都会创建新 View 销毁旧 View，
    /// 但 VM 是缓存的（MainWindowViewModel._cache）——若不退订，旧 View 的 OnVmPropertyChanged
    /// 会一直挂在 VM 上，访问已销毁的 SpeakerHost 控件导致异常或幽灵副作用。</summary>
    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && vm is INotifyPropertyChanged npc)
            npc.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EncodePageViewModel.CurrentChannels) && Vm is { } vm)
            SpeakerHost.SetChannels(vm.CurrentChannels);
    }

    // ============ 拖放 ============
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Vm is not { } vm) return;
        var values = e.DataTransfer.TryGetFiles();
        if (values == null) return;
        var files = new List<string>();
        foreach (var item in values)
        {
            if (item is not IStorageFile) continue;
            var path = item.TryGetLocalPath();
            if (path != null) files.Add(path);
        }
        if (files.Count > 0) vm.ImportFiles(files);
    }

    // ============ 队列"信息"按钮 ============
    private void OnJobInfoClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is EncodeJob job && Vm is { } vm)
            vm.JobInfoCommand.Execute(job);
    }

    // ============ 编码队列弹窗（关闭弹窗不影响编码）============
    // 单例窗口：重复点击"打开编码队列"时，若窗口已存在则激活而非创建新实例。
    private EncodeQueueWindow? _queueWindow;

    private void OnOpenQueueClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (_queueWindow != null)
        {
            // 窗口已存在：激活并置顶
            if (!_queueWindow.IsActive) _queueWindow.Activate();
            return;
        }
        _queueWindow = new EncodeQueueWindow();
        _queueWindow.Closed += (_, _) => _queueWindow = null;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            _queueWindow.Show(owner, vm);   // 非模态：不阻塞主窗口
        else
            _queueWindow.Show();
    }

    // ============ 文本预览弹窗（预览 .cfg / 预览命令 / 查看日志）============
    // 单例窗口：重复点击按钮时，若窗口已存在则更新内容而非创建新实例。
    // 关闭本窗口不影响编码或日志收集（日志仍持续写入 VM.LogText）。
    private TextPreviewWindow? _previewWindow;

    private void OnPreviewCfgClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        ShowPreview("预览 .cfg", vm.BuildCfgPreview(), "CfgWriter 生成的 .cfg 内容");
    }

    private void OnPreviewCommandClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        // 命令预览：可编辑模式（无滚动条 + 自动换行），用户可修改命令后复制
        ShowPreview("预览命令", vm.BuildCommandPreview(), "DtsJobQueue.exe 命令行参数",
            isEditable: true);
    }

    private void OnShowLogClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        // 日志行数作为底部状态栏提示，便于快速判断是否有新日志
        var status = string.IsNullOrEmpty(vm.LogText)
            ? "（暂无日志）"
            : $"共 {vm.LogText.Count(c => c == '\n') + 1} 行";
        ShowPreview("查看日志", vm.LogText, "引擎与编码过程的诊断日志（实时）", status);
    }

    /// <summary>显示/更新预览弹窗。窗口已存在时复用并更新内容；不存在时创建并显示。
    /// isEditable=true 时切换为可编辑模式（无滚动条），用于命令预览。</summary>
    private void ShowPreview(string title, string content, string subtitle = "", string status = "",
        bool isEditable = false)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (_previewWindow == null)
        {
            _previewWindow = new TextPreviewWindow();
            _previewWindow.Closed += (_, _) => _previewWindow = null;
            if (owner != null)
                _previewWindow.Show(owner, title, content, subtitle, status, isEditable);
            else
                _previewWindow.Show();
        }
        else
        {
            // 窗口已存在：激活并更新内容（避免被其他窗口遮挡时也保持内容最新）
            if (!_previewWindow.IsActive) _previewWindow.Activate();
            _previewWindow.UpdateContent(title, content, subtitle, status, isEditable);
        }
    }

    // ============ 文件选择对话框 ============
    private static readonly IReadOnlyList<FilePickerFileType> AudioFileTypes =
        new[]
        {
            // 仅 RIFF WAV；DtsJobQueue.exe 编码器不支持 W64（W64 由 dtshdst.exe 流工具处理）
            new FilePickerFileType("RIFF WAV") { Patterns = new[] { "*.wav" } },
            new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } },
        };

    private static readonly IReadOnlyList<FilePickerFileType> AafFileTypes =
        new[]
        {
            new FilePickerFileType("AAF 文件") { Patterns = new[] { "*.aaf" } },
            new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } },
        };

    private static readonly IReadOnlyList<FilePickerFileType> CsvFileTypes =
        new[]
        {
            new FilePickerFileType("CSV 文件") { Patterns = new[] { "*.csv" } },
            new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } },
        };

    private static readonly IReadOnlyList<FilePickerFileType> OutputFileTypes =
        new[]
        {
            new FilePickerFileType("DTS-HD Master Audio") { Patterns = new[] { "*.dtshd" } },
            new FilePickerFileType("DTS DVD") { Patterns = new[] { "*.cpt" } },
            new FilePickerFileType("DTS Music Disc") { Patterns = new[] { "*.wav" } },
            new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } },
        };

    private static readonly IReadOnlyList<FilePickerFileType> SettingsFileTypes =
        new[]
        {
            new FilePickerFileType("编码设置") { Patterns = new[] { "*.json" } },
            new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } },
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

    // ============ 信息 / 确认对话框 ============
    private async Task ShowInfoAsync(string title, string msg)
    {
        await new ContentDialog
        {
            Title = title,
            Content = msg,
            PrimaryButtonText = "确定",
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private async Task<bool> ShowConfirmAsync(string title, string msg)
    {
        var result = await new ContentDialog
        {
            Title = title,
            Content = msg,
            PrimaryButtonText = "确定",
            SecondaryButtonText = "取消",
        }.ShowAsync(TopLevel.GetTopLevel(this));
        return result == ContentDialogResult.Primary;
    }
}
