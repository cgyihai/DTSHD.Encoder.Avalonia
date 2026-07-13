using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DTSHD.Encoder.Avalonia.Services;

namespace DTSHD.Encoder.Avalonia.ViewModels;

/// <summary>
/// DTS-HD StreamTools 页 ViewModel：从 WinUI3 StreamToolsPage.xaml.cs 完整移植。
///
/// 【职责】11 个标签页：
///   Info/Verify/Split/Join/Trim/AddSilence/Restripe/Metadata/Append/PBR/RawCmd
///   对已编码的 .dtshd/.cpt 码流做信息/校验/拆分/合并/裁剪/加静音/重打时间码/元数据等处理
///   （原生 DTSToolFramewrk 引擎，模块 T，端口 4442）。
///
/// 【UI 框架解耦】
///   WinUI3 用 Windows.Storage.Pickers.*Picker + InitializeWithWindow(hwnd) 弹原生对话框。
///   在 Avalonia 中等价物是 IStorageProvider，但 VM 不应引用控件。故本 VM 暴露若干委托，
///   由 View 的 code-behind 在 Loaded 时注入：
///     PickOpenFileDelegate = () => Task&lt;string?&gt;   （打开文件对话框，返回路径或 null）
///     PickSaveFileDelegate = () => Task&lt;string?&gt;   （保存文件对话框，返回路径或 null）
///     ShowInfoDelegate     = (title, msg) => Task    （信息对话框）
///   委托未注入时（如单元测试）相关命令静默 no-op；这样 VM 可脱离 Avalonia UI 独立测试。
///
/// 【Dispatcher】后台线程回调（引擎 socket 读取）通过 Avalonia.Threading.Dispatcher.UIThread.Post
/// 编组回 UI 线程，对应 WinUI3 的 DispatcherQueue.GetForCurrentThread().TryEnqueue(...)。
///
/// 【PBR 图表】VM 仅暴露数据点集合 PbrPoints，View 的 Canvas 负责实际像素绘制
/// （对应 WinUI3 DrawPbrGraph 的坐标轴/折线/刻度绘制逻辑迁移至 View，VM 不含像素计算）。
/// </summary>
public sealed partial class StreamToolsPageViewModel : ViewModelBase
{
    public override string Title => "DTS-HD StreamTools";

    // ============ 注入的文件选择 / 对话框委托（View code-behind 设置）============
    /// <summary>打开文件选择器；返回所选文件路径或 null。委托未注入时浏览命令静默 no-op。</summary>
    public Func<Task<string?>>? PickOpenFileDelegate { get; set; }

    /// <summary>保存文件选择器；返回所选路径或 null。</summary>
    public Func<Task<string?>>? PickSaveFileDelegate { get; set; }

    /// <summary>信息对话框；参数：(标题, 正文)。</summary>
    public Func<string, string, Task>? ShowInfoDelegate { get; set; }

    // ============ 静态选项目录（绑定到 ComboBox.ItemsSource）============
    /// <summary>帧率选项；ReFps / MetaFps 共用（对应 WinUI3 ReFps/MetaFps ComboBoxItem 列表）。</summary>
    public IReadOnlyList<string> FrameRateOptions { get; } = new[] { "23.976", "24", "25", "29.97", "29.97 Drop", "30" };

    // ============ 可绑定属性 ============
    // —— 引擎状态 ——
    /// <summary>引擎状态文本（对应 WinUI3 EngineStatus.Text）。</summary>
    [ObservableProperty] private string _engineStatusText = "";
    /// <summary>引擎状态是否就绪；View 据此映射 EngineStatusText 前景色（SeaGreen/OrangeRed）。</summary>
    [ObservableProperty] private bool _engineStatusOk;
    /// <summary>引擎状态文字前景色：已连接=SeaGreen，正在连接=#FFC107（黄），失败/未加载=OrangeRed。</summary>
    [ObservableProperty] private IBrush _engineStatusForeground = Brushes.OrangeRed;
    /// <summary>ProgressRing 是否旋转（正在连接时 true）。</summary>
    [ObservableProperty] private bool _engineRingActive;
    /// <summary>引擎已连接（View 据此显示 ✓ 绿色勾图标）。</summary>
    [ObservableProperty] private bool _isEngineConnected;
    /// <summary>引擎正在连接中（View 据此显示 ProgressRing 旋转 + 黄色文字）。</summary>
    [ObservableProperty] private bool _isEngineConnecting;
    /// <summary>引擎失败/未加载（View 据此显示 ✗ 红色叉图标）。</summary>
    [ObservableProperty] private bool _isEngineFailed = true;

    // —— 操作进度 / 日志 ——
    /// <summary>当前操作状态文本（对应 WinUI3 OpStatus.Text）。</summary>
    [ObservableProperty] private string _opStatus = "";
    /// <summary>操作进度条是否可见（对应 WinUI3 OpProgress.Visibility）。</summary>
    [ObservableProperty] private bool _isOpProgressVisible;
    /// <summary>操作进度条是否不确定（对应 WinUI3 OpProgress.IsIndeterminate）。</summary>
    [ObservableProperty] private bool _isOpProgressIndeterminate;
    /// <summary>操作进度条数值（对应 WinUI3 OpProgress.Value）。</summary>
    [ObservableProperty] private double _opProgressValue;
    /// <summary>工具响应日志（对应 WinUI3 ToolLog.Text）。</summary>
    [ObservableProperty] private string _logText = "";

    // —— Info ——
    [ObservableProperty] private string _infoFile = "";
    // —— Verify ——
    [ObservableProperty] private string _verifyFile = "";
    // —— Split ——
    [ObservableProperty] private string _splitIn = "";
    [ObservableProperty] private string _splitTc = "00:00:00:00";
    [ObservableProperty] private string _splitOut1 = "";
    [ObservableProperty] private string _splitOut2 = "";
    // —— Join ——
    [ObservableProperty] private string _joinF1 = "";
    [ObservableProperty] private string _joinF2 = "";
    [ObservableProperty] private string _joinOut = "";
    // —— Trim ——
    [ObservableProperty] private string _trimIn = "";
    [ObservableProperty] private string _trimStart = "00:00:00:00";
    [ObservableProperty] private string _trimEnd = "00:00:00:00";
    [ObservableProperty] private string _trimOut = "";
    // —— AddSilence ——
    [ObservableProperty] private string _silIn = "";
    [ObservableProperty] private string _silHead = "00:00:00:00";
    [ObservableProperty] private string _silTail = "00:00:00:00";
    [ObservableProperty] private string _silNewTc = "00:00:00:00";
    [ObservableProperty] private string _silOut = "";
    // —— Restripe ——
    [ObservableProperty] private string _reFile = "";
    [ObservableProperty] private string _reStart = "00:00:00:00";
    [ObservableProperty] private int _selectedReFpsIndex;
    // —— Metadata ——
    [ObservableProperty] private string _metaFile = "";
    [ObservableProperty] private string _metaStart = "00:00:00:00";
    [ObservableProperty] private string _metaDialNorm = "-31";
    [ObservableProperty] private int _selectedMetaFpsIndex;
    // —— Append ——
    [ObservableProperty] private string _appendOut = "";
    /// <summary>追加合并输入行集合（对应 WinUI3 AppendRows StackPanel 动态行）。</summary>
    public ObservableCollection<AppendRowViewModel> AppendRows { get; } = new();
    // —— PBR ——
    [ObservableProperty] private string _pbrFile = "";
    /// <summary>PBR 数据点；View 的 Canvas 绑定此集合并绘制折线图（VM 不含像素计算）。</summary>
    public ObservableCollection<PbrPoint> PbrPoints { get; } = new();
    // —— RawCmd ——
    [ObservableProperty] private string _rawCmd = "";

    // ============ 内部状态 ============
    /// <summary>StreamTools 专用宿主：原生服务端 DTSToolFramewrk.exe，端口 4442（与编码器 4444 分离）。</summary>
    private static EngineHost? Tools => AppServices.ToolsHost;
    /// <summary>上一次 PBR 分析的 .dtspbr 输出路径（用于结束时读取绘图数据）。</summary>
    private string _lastPbrOut = "";

    public StreamToolsPageViewModel()
    {
        UpdateStatus();
        AddAppendRow();          // 预置一行（对应 WinUI3 OnAddAppendRow(this, null!)）
        if (Tools != null) Tools.RawResponse += OnRawResponse;
        AppServices.EngineStateChanged += OnEngineState;
    }

    // =========================================================
    //  引擎状态
    // =========================================================
    /// <summary>工具集是否就绪：基于 AppServices.ToolsetReady 且 DTSToolFramewrk.exe 存在。</summary>
    private bool ToolsetReady => AppServices.ToolsetReady &&
        File.Exists(Path.Combine(AppServices.Settings.ToolDir, "DTSToolFramewrk.exe"));

    private void OnEngineState()
    {
        Post(() =>
        {
            UpdateStatus();
            // 引擎宿主可能在 VM 构造后才初始化，状态变化时防御性重订阅
            if (Tools != null) { Tools.RawResponse -= OnRawResponse; Tools.RawResponse += OnRawResponse; }
        });
    }

    private void UpdateStatus()
    {
        bool ok = ToolsetReady;
        bool connected = ok && Tools?.IsConnectedSafe == true;
        EngineStatusOk = ok;
        if (!ok)
        {
            // 失败/未加载：红色 + ✗
            EngineStatusText = "工具集未就绪 · 请到「设置」配置 DTS-HD_Tool（需 DTSToolFramewrk.exe）";
            EngineStatusForeground = Brushes.OrangeRed;
            EngineRingActive = false;
            IsEngineConnected = false; IsEngineConnecting = false; IsEngineFailed = true;
        }
        else if (connected)
        {
            // 已连接：绿色 + ✓
            EngineStatusText = "StreamTools 引擎已连接";
            EngineStatusForeground = Brushes.SeaGreen;
            EngineRingActive = false;
            IsEngineConnected = true; IsEngineConnecting = false; IsEngineFailed = false;
        }
        else
        {
            // 工具集就绪但未连接：黄色 + ProgressRing（操作时自动连接）
            EngineStatusText = "工具集就绪 · StreamTools（操作时自动连接）";
            EngineStatusForeground = Brushes.Goldenrod;
            EngineRingActive = true;
            IsEngineConnected = false; IsEngineConnecting = true; IsEngineFailed = false;
        }
    }

    // =========================================================
    //  工具响应解析：T {Op} S/E/C{code}[msg]
    // =========================================================
    private void OnRawResponse(string payload) => Post(() =>
    {
        Log("<< " + payload);
        var p = payload.Trim();
        if (p.Length < 3 || p[0] != 'T') return;
        // 形如 "T J S" / "T J E" / "T J C 0" / "T K J E 1"
        var tok = p.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length < 3) return;

        // 驱动器空间检查回执：T K {Op} ...（直接放行，沿用引擎自身判断）
        if (tok[1] == "K") return;

        // PBR 分析有独立的进度/结果语义（T P M/S/C/E）
        if (tok[1] == "P") { HandlePbrResponse(tok, p); return; }

        string phase = tok[2];      // S=开始, E=结束, C=完成
        if (phase == "S")
        {
            IsOpProgressVisible = true;
            IsOpProgressIndeterminate = true;
            OpStatus = "处理中…";
        }
        else if (phase == "E")
        {
            IsOpProgressIndeterminate = false;
        }
        else if (phase == "C")
        {
            IsOpProgressIndeterminate = false;
            IsOpProgressVisible = false;
            // tok[3] = 返回码；0 表示成功，否则其后为错误信息
            if (tok.Length >= 4 && tok[3] == "0")
            {
                OpStatus = "✓ 完成";
                Log("操作完成。");
            }
            else
            {
                string msg = tok.Length >= 4 ? string.Join(' ', tok[3..]) : "操作未执行";
                OpStatus = "✗ 失败";
                Log("操作失败: " + msg);
            }
        }
    });

    // =========================================================
    //  文件选择（参数为字段名，对应 WinUI3 OnBrowse/OnBrowseSave 的 Tag）
    // =========================================================
    [RelayCommand]
    private async Task BrowseAsync(string target)
    {
        var f = await (PickOpenFileDelegate?.Invoke() ?? Task.FromResult<string?>(null));
        if (f != null) SetField(target, f);
    }

    [RelayCommand]
    private async Task BrowseSaveAsync(string target)
    {
        var f = await (PickSaveFileDelegate?.Invoke() ?? Task.FromResult<string?>(null));
        if (f != null) SetField(target, f);
    }

    private void SetField(string name, string val)
    {
        switch (name)
        {
            case nameof(InfoFile): InfoFile = val; break;
            case nameof(VerifyFile): VerifyFile = val; break;
            case nameof(SplitIn): SplitIn = val; break;
            case nameof(SplitOut1): SplitOut1 = val; break;
            case nameof(SplitOut2): SplitOut2 = val; break;
            case nameof(JoinF1): JoinF1 = val; break;
            case nameof(JoinF2): JoinF2 = val; break;
            case nameof(JoinOut): JoinOut = val; break;
            case nameof(TrimIn): TrimIn = val; break;
            case nameof(TrimOut): TrimOut = val; break;
            case nameof(SilIn): SilIn = val; break;
            case nameof(SilOut): SilOut = val; break;
            case nameof(ReFile): ReFile = val; break;
            case nameof(MetaFile): MetaFile = val; break;
            case nameof(PbrFile): PbrFile = val; break;
            case nameof(AppendOut): AppendOut = val; break;
        }
    }

    // =========================================================
    //  操作命令
    // =========================================================
    private bool EngineOk()
    {
        if (Tools != null && ToolsetReady) return true;
        Log("工具集未就绪，请到「设置」配置 DTS-HD_Tool（需 DTSToolFramewrk.exe）。"); return false;
    }

    private async Task SendToolAsync(string cmd)
    {
        try
        {
            var tools = Tools;
            if (tools == null || !await tools.EnsureConnectedAsync()) { Log("无法连接 StreamTools 引擎。"); return; }
            OpStatus = ""; OpProgressValue = 0;
            tools.Send(cmd); Log(">> " + cmd);
        }
        catch (Exception ex) { Log("发送失败: " + ex.Message); }
    }

    private bool RequireFields(params string?[] values)
    {
        foreach (var v in values)
            if (string.IsNullOrWhiteSpace(v)) { Log("请填写所有必填的文件/输出字段。"); return false; }
        return true;
    }

    private string ReFps => SelectedReFpsIndex >= 0 && SelectedReFpsIndex < FrameRateOptions.Count ? FrameRateOptions[SelectedReFpsIndex] : "";
    private string MetaFps => SelectedMetaFpsIndex >= 0 && SelectedMetaFpsIndex < FrameRateOptions.Count ? FrameRateOptions[SelectedMetaFpsIndex] : "";

    // —— Info：读取码流信息 (T I)
    [RelayCommand]
    private async Task InfoAsync()
    {
        if (string.IsNullOrEmpty(InfoFile)) { Log("请选择文件。"); return; }
        if (EngineOk()) await SendToolAsync(ToolsCommands.Info(InfoFile));
    }

    // —— Verify：校验码流 (DTSHDVerify.exe，独立进程)
    [RelayCommand]
    private async Task VerifyAsync()
    {
        var file = VerifyFile?.Trim() ?? "";
        if (string.IsNullOrEmpty(file)) { Log("请选择文件。"); return; }
        var exe = Path.Combine(AppServices.Settings.ToolDir, "DTSHDVerify.exe");
        if (!File.Exists(exe)) { Log("未找到 DTSHDVerify.exe。"); return; }
        Log(">> DTSHDVerify " + file);
        await RunExeAsync(exe, $"\"{file}\"");
    }

    // —— Split：按时间码拆分为两个文件 (T S)
    [RelayCommand]
    private async Task SplitAsync()
    {
        if (!RequireFields(SplitIn, SplitOut1, SplitOut2)) return;
        if (EngineOk()) await SendToolAsync(ToolsCommands.Split(SplitIn, SplitTc, SplitOut1, SplitOut2));
    }

    // —— Join：合并两个码流 (T J)
    [RelayCommand]
    private async Task JoinAsync()
    {
        if (!RequireFields(JoinF1, JoinF2, JoinOut)) return;
        if (EngineOk()) await SendToolAsync(ToolsCommands.Join(JoinOut, JoinF1, JoinF2));
    }

    // —— Trim：按起止时间码裁剪 (T T)
    [RelayCommand]
    private async Task TrimAsync()
    {
        if (!RequireFields(TrimIn, TrimOut)) return;
        if (EngineOk()) await SendToolAsync(ToolsCommands.Trim(TrimIn, TrimStart, TrimEnd, TrimOut));
    }

    // —— AddSilence：在头/尾添加静音并重设起始时间码 (T N)
    [RelayCommand]
    private async Task AddSilenceAsync()
    {
        if (!RequireFields(SilIn, SilOut)) return;
        if (SilHead == "00:00:00:00" && SilTail == "00:00:00:00")
        { Log("头部或尾部静音至少一个需为非零。"); return; }
        if (EngineOk()) await SendToolAsync(ToolsCommands.AddSilence(SilOut, SilIn, SilHead, SilTail, SilNewTc));
    }

    // —— Restripe：按新帧率/起始时间码重打 (T R)
    [RelayCommand]
    private async Task RestripeAsync()
    {
        if (!RequireFields(ReFile)) return;
        if (EngineOk()) await SendToolAsync(ToolsCommands.Restripe(ReFile, ReStart, ReFps));
    }

    // —— Metadata：修改对话归一化/帧率/起始时间码 (T M)
    [RelayCommand]
    private async Task MetadataAsync()
    {
        if (!RequireFields(MetaFile)) return;
        if (EngineOk()) await SendToolAsync(ToolsCommands.Metadata(MetaFile, MetaStart, MetaFps, MetaDialNorm));
    }

    // —— Append：将多个文件按各自起止时间码顺序追加合并 (T A)
    [RelayCommand]
    private async Task AppendAsync()
    {
        if (string.IsNullOrWhiteSpace(AppendOut)) { Log("请填写输出文件。"); return; }
        var rows = new List<(string File, string Start, string End)>();
        foreach (var r in AppendRows)
            if (!string.IsNullOrWhiteSpace(r.FilePath))
                rows.Add((r.FilePath.Trim(), r.Start?.Trim() ?? "", r.End?.Trim() ?? ""));
        if (rows.Count == 0) { Log("请至少添加一个输入文件。"); return; }
        if (EngineOk()) await SendToolAsync(ToolsCommands.Append(AppendOut, rows));
    }

    /// <summary>添加一行追加合并输入（对应 WinUI3 OnAddAppendRow）。</summary>
    [RelayCommand]
    private void AddAppendRow() => AppendRows.Add(new AppendRowViewModel(this));

    /// <summary>移除指定追加行（由 AppendRowViewModel.Remove 回调）。</summary>
    internal void RemoveAppendRow(AppendRowViewModel row)
    {
        if (AppendRows.Count > 1) AppendRows.Remove(row);
    }

    // —— PBR：峰值比特率分析，生成 .dtspbr 并暴露数据点供 View 绘图 (T P)
    [RelayCommand]
    private async Task PbrAsync()
    {
        var file = PbrFile?.Trim() ?? "";
        if (string.IsNullOrEmpty(file)) { Log("请选择文件。"); return; }
        _lastPbrOut = Path.ChangeExtension(file, ".dtspbr");
        PbrPoints.Clear();
        if (EngineOk()) await SendToolAsync(ToolsCommands.Pbr(file, _lastPbrOut));
    }

    private void HandlePbrResponse(string[] tok, string raw)
    {
        string phase = tok.Length >= 3 ? tok[2] : "";
        if (phase == "M")        // 进度/状态消息
        {
            var msg = raw.Length > 5 ? raw[5..].Trim() : raw;
            OpStatus = msg;
            IsOpProgressVisible = true;
            IsOpProgressIndeterminate = true;
        }
        else if (phase == "S")
        {
            OpStatus = "开始分析…";
            IsOpProgressVisible = true; IsOpProgressIndeterminate = true;
        }
        else if (phase == "C")   // T P C 1 <msg>
        {
            var idx = raw.IndexOf('1');
            if (idx >= 0 && idx + 2 <= raw.Length) Log(raw[(idx + 1)..].Trim());
        }
        else if (phase == "E")   // 结束 → 读取 .dtspbr 数据点
        {
            IsOpProgressIndeterminate = false;
            IsOpProgressVisible = false;
            OpStatus = "✓ 分析完成";
            LoadPbrData();
        }
    }

    /// <summary>读取 .dtspbr 文件并填充 PbrPoints（不含像素绘制，由 View 绑定 PbrPoints 绘制）。</summary>
    private void LoadPbrData()
    {
        try
        {
            if (!File.Exists(_lastPbrOut)) { Log("未找到 " + _lastPbrOut); return; }
            PbrPoints.Clear();
            int idx = 0;
            foreach (var line in File.ReadAllLines(_lastPbrOut))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                var parts = t.Split(new[] { ',', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                // 最后一列取作 kbps 数值；首列作时间码标签（此处以序号为 X 轴时间，View 按比例均匀分布）
                if (double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double kbps))
                    PbrPoints.Add(new PbrPoint(idx++, kbps));
            }
            Log($"PBR：读取 {PbrPoints.Count} 个数据点。");
        }
        catch (Exception ex) { Log("绘制 PBR 失败: " + ex.Message); }
    }

    // —— RawCmd：直接发送 T 命令
    [RelayCommand]
    private async Task RawSendAsync()
    {
        var cmd = RawCmd?.Trim() ?? "";
        if (!string.IsNullOrEmpty(cmd) && EngineOk()) await SendToolAsync(cmd);
    }

    // —— 取消操作 (T Z)
    [RelayCommand]
    private async Task CancelOpAsync()
    {
        if (EngineOk()) await SendToolAsync(ToolsCommands.Cancel);
    }

    // =========================================================
    //  外部进程执行（DTSHDVerify.exe）
    // =========================================================
    private async Task RunExeAsync(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = AppServices.Settings.ToolDir,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string o = await p.StandardOutput.ReadToEndAsync();
            string err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            Post(() => { Log(o); if (!string.IsNullOrWhiteSpace(err)) Log("[stderr] " + err); });
        }
        catch (Exception ex) { Post(() => Log("执行失败: " + ex.Message)); }
    }

    // =========================================================
    //  日志 / Dispatcher
    // =========================================================
    private void Log(string msg) => Post(() =>
        LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");

    /// <summary>将操作编组到 Avalonia UI 线程执行（对应 WinUI3 DispatcherQueue.GetForCurrentThread().TryEnqueue）。</summary>
    private static void Post(Action action) => Dispatcher.UIThread.Post(action);
}

/// <summary>
/// 追加合并输入行 VM：绑定到 AppendRows ItemsControl 的行模板。
/// 每行含文件路径、起止时间码、浏览/移除按钮（对应 WinUI3 OnAddAppendRow 动态构建的 Grid 行）。
/// </summary>
public sealed partial class AppendRowViewModel : ObservableObject
{
    private readonly StreamToolsPageViewModel _parent;

    public AppendRowViewModel(StreamToolsPageViewModel parent) => _parent = parent;

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _start = "00:00:00:00";
    [ObservableProperty] private string _end = "00:00:00:00";

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var f = await (_parent.PickOpenFileDelegate?.Invoke() ?? Task.FromResult<string?>(null));
        if (f != null) FilePath = f;
    }

    [RelayCommand]
    private void Remove() => _parent.RemoveAppendRow(this);
}

/// <summary>PBR 数据点：Time 为序号（X 轴按比例均匀分布），Value 为 kbps。View 的 Canvas 据此绘制折线。</summary>
public record PbrPoint(double Time, double Value);
