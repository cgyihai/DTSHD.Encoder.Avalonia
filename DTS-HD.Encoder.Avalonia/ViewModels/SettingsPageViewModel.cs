using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    /// <summary>主题选项；下标对应 SelectedThemeIndex：0=跟随系统(Default), 1=浅色(Light), 2=深色(Dark)。</summary>
    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "跟随系统", "浅色", "深色" };

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
        StatusText = ready ? "工具集就绪 · DtsJobQueue 与 DTSEncConfig 已找到"
                           : "工具集不完整 · 请确认 DTS-HD_Tool 目录";
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
