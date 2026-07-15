using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DTSHD.Encoder.Avalonia.Services;

namespace DTSHD.Encoder.Avalonia.ViewModels;

/// <summary>
/// 设置页 ViewModel：从 WinUI3 SettingsPage.xaml.cs 完整移植。
///
/// 【职责】
///   - 工具集目录：浏览/自动检测/打开目录/保存并重连引擎
///   - 外观：主题（跟随系统/浅色/深色）+ 云母背景开关
///   - 关于信息（静态，由 View XAML 直接呈现）
///
/// 【UI 框架解耦】
///   WinUI3 用 Windows.Storage.Pickers.FolderPicker + InitializeWithWindow(hwnd) 弹原生对话框，
///   并直接调用 (App.MainWindow as MainWindow)?.ApplyTheme()/ApplyBackdrop()。
///   在 Avalonia 中等价物是 IStorageProvider（通过 TopLevel.GetStorageProvider 取得），
///   但 VM 不应引用控件。故本 VM 暴露若干委托，由 View 的 code-behind 在 Loaded 时注入：
///     PickFolderDelegate    = () =&gt; Task&lt;string?&gt;     （返回所选文件夹路径或 null）
///     ShowInfoDelegate      = (title, msg) =&gt; Task        （信息对话框）
///     ApplyThemeDelegate    = Action                       （由 MainWindow 实现主题切换）
///     ApplyBackdropDelegate = Action                       （由 MainWindow 实现云母背景开关）
///   委托未注入时（如单元测试）相关命令静默 no-op；这样 VM 可脱离 Avalonia UI 独立测试。
/// </summary>
public sealed partial class SettingsPageViewModel : ViewModelBase
{
    public override string Title => "设置";

    // ============ 注入的委托（View code-behind 设置）============
    /// <summary>文件夹选择器；返回所选文件夹路径或 null。委托未注入时浏览命令静默 no-op。</summary>
    public Func<Task<string?>>? PickFolderDelegate { get; set; }

    /// <summary>信息对话框；参数：(标题, 正文)。用于"保存并重连引擎"后的结果反馈。</summary>
    public Func<string, string, Task>? ShowInfoDelegate { get; set; }

    /// <summary>应用主题切换；由 MainWindow 实现并注入（对应 WinUI3 (App.MainWindow as MainWindow)?.ApplyTheme()）。</summary>
    public Action? ApplyThemeDelegate { get; set; }

    /// <summary>应用云母背景切换；由 MainWindow 实现并注入（对应 WinUI3 (App.MainWindow as MainWindow)?.ApplyBackdrop()）。</summary>
    public Action? ApplyBackdropDelegate { get; set; }

    // ============ 静态选项目录（绑定到 ComboBox.ItemsSource）============
    /// <summary>主题选项（随语言本地化）；下标对应 SelectedThemeIndex：0=跟随系统, 1=浅色, 2=深色。</summary>
    [ObservableProperty] private IReadOnlyList<string> _themeOptions = BuildThemeOptions();

    private static IReadOnlyList<string> BuildThemeOptions() => new[]
    {
        LocalizationManager.Get("Lang.Theme.System", "跟随系统"),
        LocalizationManager.Get("Lang.Theme.Light", "浅色"),
        LocalizationManager.Get("Lang.Theme.Dark", "深色"),
    };

    /// <summary>语言选项（各语言以其本身名称显示，符合语言选择器惯例）。</summary>
    public IReadOnlyList<string> LanguageOptions { get; } =
        LocalizationManager.Languages.Select(x => x.Display).ToList();

    /// <summary>当前语言下拉索引；变更即实时切换界面语言。</summary>
    [ObservableProperty] private int _selectedLanguageIndex;

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_loading) return;
        if (value < 0 || value >= LocalizationManager.Languages.Count) return;
        LocalizationManager.SetLanguage(LocalizationManager.Languages[value].Code);
    }

    // ============ 关于 ============
    /// <summary>应用版本号（取自程序集版本，与 csproj &lt;Version&gt; 一致，单一来源）。</summary>
    public string AppVersion => UpdateService.CurrentVersion();
    /// <summary>项目主页（关于页链接指向，用于查看更新与新版本）。</summary>
    public string GitHubUrl => $"https://github.com/{UpdateService.Owner}/{UpdateService.Repo}";

    // ============ 在线更新（GitHub 公开仓）============
    /// <summary>更新检查状态文本（空=未检查）。</summary>
    [ObservableProperty] private string _updateStatus = "";
    /// <summary>是否检测到新版本；View 据此显示"前往下载"按钮。</summary>
    [ObservableProperty] private bool _updateAvailable;
    /// <summary>发现的新版本下载/发布页地址（默认指向 Releases 页）。</summary>
    private string _downloadUrl = UpdateService.ReleasesPageUrl;

    /// <summary>启动时自动检查更新（绑定设置页开关）。</summary>
    [ObservableProperty] private bool _autoCheckUpdate = AppServices.Settings.AutoCheckUpdate;

    partial void OnAutoCheckUpdateChanged(bool value)
    {
        if (_loading) return;
        AppServices.Settings.AutoCheckUpdate = value;
        AppServices.SaveSettings();
    }

    // ============ 可绑定属性 ============
    /// <summary>工具集目录（对应 WinUI3 ToolDirBox.Text）。</summary>
    [ObservableProperty] private string _toolDir = "";

    /// <summary>云母背景开关（对应 WinUI3 MicaToggle.IsOn）。</summary>
    [ObservableProperty] private bool _micaEnabled = true;

    /// <summary>主题下拉选中索引；0=Default, 1=Light, 2=Dark（对应 WinUI3 ThemeBox.SelectedIndex）。</summary>
    [ObservableProperty] private int _selectedThemeIndex;

    /// <summary>状态文本（对应 WinUI3 StatusText.Text）。</summary>
    [ObservableProperty] private string _statusText = "未检测";

    /// <summary>工具集是否就绪；View 据此用 DataTrigger 映射 StatusDot.Fill（SeaGreen/OrangeRed）。</summary>
    [ObservableProperty] private bool _toolsetReady;

    /// <summary>工具存在性条目集合（对应 WinUI3 ToolList 子元素）；每次刷新整体清空重建。</summary>
    [ObservableProperty] private ObservableCollection<ToolStatusItem> _toolItems = new();

    // ============ 内部状态 ============
    /// <summary>初始加载标志：构造期填充属性时压制 OnXxxChanged 持久化逻辑（对应 WinUI3 _loading）。</summary>
    private bool _loading = true;

    public SettingsPageViewModel()
    {
        Load();
        RefreshStatus();
        _loading = false;
        // 语言切换时实时刷新本页动态文案（主题选项 + 工具集状态文本）。
        LocalizationManager.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        int keep = SelectedThemeIndex;
        ThemeOptions = BuildThemeOptions();   // 重建下拉项（重新赋 ItemsSource 可能重置选中项）
        SelectedThemeIndex = keep;            // 复原选中项
        RefreshStatus();                      // 重新本地化状态文本
    }

    // =========================================================
    //  加载与状态刷新
    // =========================================================

    /// <summary>从持久化设置回填界面属性（对应 WinUI3 SettingsPage.Load）。</summary>
    private void Load()
    {
        var s = AppServices.Settings;
        ToolDir = s.ToolDir;
        MicaEnabled = s.MicaEnabled;
        SelectedThemeIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        // 当前语言下拉初值（与已应用的界面语言一致；构造期 _loading=true 不会触发切换）
        var cur = LocalizationManager.Current;
        for (int i = 0; i < LocalizationManager.Languages.Count; i++)
            if (LocalizationManager.Languages[i].Code == cur) { SelectedLanguageIndex = i; break; }
    }

    /// <summary>
    /// 刷新工具集检测：将当前 ToolDir 写回 Settings（使 *Exists 属性基于最新路径），
    /// 重建 ToolItems 列表，更新状态文本与就绪标志（对应 WinUI3 RefreshStatus）。
    /// </summary>
    private void RefreshStatus()
    {
        AppServices.Settings.ToolDir = (ToolDir ?? "").Trim();
        // 启动性能优化：ToolDir 变化后必须刷新 ToolsetReady 缓存，
        // 否则 s.ToolsetReady 仍返回 Load() 时缓存的旧值（指向旧 ToolDir 的检查结果）。
        AppServices.Settings.RefreshToolsetCache();
        var s = AppServices.Settings;

        ToolItems.Clear();
        ToolItems.Add(new ToolStatusItem("DtsJobQueue.exe", s.DtsJobQueueExists));
        ToolItems.Add(new ToolStatusItem("DTSEncConfig.dll", s.EncConfigDllExists));
        ToolItems.Add(new ToolStatusItem("MAS-SAS_Authorizer.exe", s.AuthorizerExists));
        ToolItems.Add(new ToolStatusItem("DTSHDVerify.exe", s.VerifyExists));
        ToolItems.Add(new ToolStatusItem("InfoDumper.exe", s.InfoDumperExists));

        bool ready = s.ToolsetReady;
        ToolsetReady = ready;
        StatusText = ready ? LocalizationManager.Get("Lang.Set.ToolReady", "工具集就绪 · DtsJobQueue 与 DTSEncConfig 已找到")
                           : LocalizationManager.Get("Lang.Set.ToolIncomplete", "工具集不完整 · 请确认 DTS-HD_Tool 目录");
    }

    // =========================================================
    //  属性变更回调（对应 WinUI3 的 Toggled/SelectionChanged 事件）
    // =========================================================

    /// <summary>云母背景开关变化：持久化并通知 MainWindow 重应用背景（对应 WinUI3 OnMicaToggled）。</summary>
    partial void OnMicaEnabledChanged(bool value)
    {
        if (_loading) return;
        AppServices.Settings.MicaEnabled = value;
        AppServices.SaveSettings();
        ApplyBackdropDelegate?.Invoke();
    }

    /// <summary>主题选择变化：将索引映射为 tag 字符串持久化，并通知 MainWindow 重应用主题（对应 WinUI3 OnThemeChanged）。</summary>
    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_loading) return;
        var tag = value switch { 1 => "Light", 2 => "Dark", _ => "Default" };
        AppServices.Settings.Theme = tag;
        AppServices.SaveSettings();
        ApplyThemeDelegate?.Invoke();
    }

    // =========================================================
    //  命令
    // =========================================================

    /// <summary>浏览…：弹文件夹选择器，选好后填入 ToolDir 并刷新检测（对应 WinUI3 OnBrowseToolDir）。</summary>
    [RelayCommand]
    private async Task BrowseToolDirAsync()
    {
        var path = await (PickFolderDelegate?.Invoke() ?? Task.FromResult<string?>(null));
        if (path != null)
        {
            ToolDir = path;
            RefreshStatus();
        }
    }

    /// <summary>自动检测：依据当前 ToolDir 重新检查工具集存在性（对应 WinUI3 OnDetect）。</summary>
    [RelayCommand]
    private void Detect() => RefreshStatus();

    /// <summary>打开项目主页：用系统默认浏览器跳转 GitHub（查看更新与新版本）。</summary>
    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
        }
        catch { /* 无浏览器/被拦截时静默，不影响其它功能 */ }
    }

    /// <summary>检查更新：查询 GitHub 最新 Release 并与本机版本比较。
    /// 用 AsyncRelayCommand，检查期间命令自动禁用（防重复点击）。</summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        UpdateAvailable = false;
        UpdateStatus = "正在检查更新…";
        var info = await UpdateService.CheckLatestAsync();
        if (info == null)
        {
            UpdateStatus = "检查失败：网络不可用、API 限流或暂无发布版本。";
            return;
        }
        if (info.IsNewer)
        {
            _downloadUrl = info.DownloadUrl;
            UpdateAvailable = true;
            UpdateStatus = $"发现新版本 {info.LatestVersion}（当前 {info.CurrentVersion}）。";
        }
        else
        {
            UpdateStatus = $"已是最新版本（{info.CurrentVersion}）。";
        }
    }

    /// <summary>前往下载：用系统默认浏览器打开新版本的 Release 页面。</summary>
    [RelayCommand]
    private void OpenDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
        }
        catch { /* 静默 */ }
    }

    /// <summary>打开目录：用资源管理器打开当前 ToolDir（若存在）（对应 WinUI3 OnOpenDir）。</summary>
    [RelayCommand]
    private void OpenDir()
    {
        try
        {
            var dir = (ToolDir ?? "").Trim();
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>保存并重连引擎：持久化 ToolDir，刷新检测，重新初始化引擎宿主，弹结果对话框（对应 WinUI3 OnSaveReconnect）。</summary>
    [RelayCommand]
    private async Task SaveReconnectAsync()
    {
        AppServices.Settings.ToolDir = (ToolDir ?? "").Trim();
        AppServices.SaveSettings();
        RefreshStatus();
        await AppServices.InitEngineAsync();
        if (ShowInfoDelegate != null)
        {
            await ShowInfoDelegate("引擎",
                AppServices.EngineReady ? "已重连引擎。" : "未能连接引擎，请检查目录与 DtsJobQueue.exe。");
        }
    }
}

/// <summary>
/// 工具存在性条目：绑定到 ToolList ItemsControl。
/// View 据此用 DataTrigger 映射 FontIcon Glyph（CheckMark  / Cancel ）与 Foreground（SeaGreen/OrangeRed），
/// 对应 WinUI3 SettingsPage.AddToolRow 生成的 StackPanel(FontIcon + TextBlock)。
/// </summary>
public sealed record ToolStatusItem(string Name, bool Ok);
