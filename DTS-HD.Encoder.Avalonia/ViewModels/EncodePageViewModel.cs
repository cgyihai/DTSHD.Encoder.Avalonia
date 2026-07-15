using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DTSHD.Encoder.Avalonia.Models;
using DTSHD.Encoder.Avalonia.Services;

namespace DTSHD.Encoder.Avalonia.ViewModels;

/// <summary>
/// 编码进度状态（语义枚举；View 据此用 Style/DataTrigger 映射到 ProgressBar.Foreground 颜色：
/// Working=DodgerBlue, Completed=SeaGreen, Error=OrangeRed, Idle=默认）。
/// </summary>
public enum EncodeProgressState { Idle, Working, Completed, Error }

/// <summary>引擎连接状态语义：View 据此映射 EngineStatusText 颜色与 ProgressRing 旋转。</summary>
public enum EngineStatusState { NotReady, Connecting, Connected }

/// <summary>
/// 编码页 ViewModel：从 WinUI3 EncodePage.xaml.cs 完整移植。
///
/// 【文件选择器注入模式】
/// WinUI3 用 Windows.Storage.Pickers.*Picker + InitializeWithWindow(hwnd) 弹原生对话框。
/// 在 Avalonia 中等价物是 IStorageProvider（通过 TopLevel.GetTopLevel(control).StorageProvider 取得），
/// 但 VM 不应引用控件。故本 VM 暴露若干 Func 委托属性，由 View 的 code-behind 在 Loaded 时注入：
///     vm.PickChannelFileDelegate = label => ... 返回所选文件路径或 null
///     vm.PickAudioFileDelegate   = () => ...
///     vm.PickAafFileDelegate      = () => ...
///     vm.PickCsvFileDelegate      = () => ...
///     vm.PickOutputFileDelegate   = () => ...   （保存对话框）
///     vm.PickSettingsOpenDelegate = () => ...
///     vm.PickSettingsSaveDelegate = () => ...   （保存对话框，默认文件名）
///     vm.ShowInfoDelegate    = (title, msg) => Task
///     vm.ShowConfirmDelegate = (title, msg) => Task&lt;bool&gt;
/// 委托未注入时（如单元测试）相关命令静默 no-op；这样 VM 可脱离 Avalonia UI 独立测试。
///
/// 【Dispatcher】后台线程回调（引擎 socket 读取、轮询）通过 Avalonia.Threading.Dispatcher.UIThread.Post
/// 编组回 UI 线程，对应 WinUI3 的 DispatcherQueue.GetForCurrentThread().TryEnqueue(...)。
/// </summary>
public sealed partial class EncodePageViewModel : ViewModelBase
{
    public override string Title => "音频编码";

    // ============ 注入的文件选择 / 对话框委托（View code-behind 设置）============
    /// <summary>声道文件选择器；参数=声道标签（仅用于对话框标题提示），返回路径或 null。</summary>
    public Func<string, Task<string?>>? PickChannelFileDelegate { get; set; }
    public Func<Task<string?>>? PickAudioFileDelegate { get; set; }
    public Func<Task<string?>>? PickAafFileDelegate { get; set; }
    public Func<Task<string?>>? PickCsvFileDelegate { get; set; }
    public Func<Task<string?>>? PickOutputFileDelegate { get; set; }
    public Func<Task<string?>>? PickSettingsOpenDelegate { get; set; }
    /// <summary>设置档"另存为"对话框；返回路径或 null。</summary>
    public Func<Task<string?>>? PickSettingsSaveDelegate { get; set; }
    public Func<string, string, Task>? ShowInfoDelegate { get; set; }
    public Func<string, string, Task<bool>>? ShowConfirmDelegate { get; set; }

    // ============ 静态选项目录（绑定到 ComboBox.ItemsSource）============
    public IReadOnlyList<string> DestFormats { get; } = new[]
    { "Blu-ray Disc (.dtshd)", "BD Secondary Audio (.dtshd)", "DVD (.cpt)", "DTS Music Disc (.wav)", "Digital Delivery (.dtshd)" };
    public IReadOnlyList<string> StreamTypes { get; } = new[]
    { "DTS-HD Master Audio", "DTS-HD High Res", "DTS Digital Surround", "DTS Digital Surround ES", "DTS 96/24", "DTS-HD LBR (Express)" };
    public IReadOnlyList<string> ChannelLayoutDisplays { get; } = ChannelLayouts.All.Select(x => x.Display).ToList();
    public IReadOnlyList<string> EncodeTypeOptions { get; } = OptionCatalog.EncodeTypes.ToList();
    public IReadOnlyList<int> SampleRateOptions { get; } = OptionCatalog.SampleRates.ToList();
    public IReadOnlyList<int> BitWidthOptions { get; } = OptionCatalog.BitWidths.ToList();
    public IReadOnlyList<string> CoreBitRateOptions { get; } = OptionCatalog.CoreBitRates.Select(x => x.ToString()).ToList();
    public IReadOnlyList<string> FrameRateOptions { get; } = OptionCatalog.FrameRates.ToList();
    public IReadOnlyList<string> DialogNormOptions { get; } = OptionCatalog.DialogNormalization
        .Select(x => x == -31 ? "-31 dBFS (No Atten.)" : $"{x} dBFS").ToList();
    public IReadOnlyList<string> PaFadeOptions { get; } = new[] { "0.0", "0.5", "1.0", "1.5", "2.0", "2.5", "3.0", "4.0", "5.0" };

    // ============ 可绑定属性 ============
    // —— 输入/输出 ——
    [ObservableProperty] private bool _isSingleFileMode;
    [ObservableProperty] private bool _isSingleFilePanelVisible;
    [ObservableProperty] private bool _isChannelRowsVisible = true;
    [ObservableProperty] private string _selectedChannelLayoutDisplay =
        ChannelLayouts.Get(ChannelLayout.PA_71_L_R_C_LFE_Lss_Rss_Lsr_Rsr).Display;
    [ObservableProperty] private string _singleFilePath = "";
    [ObservableProperty] private string _outputPath = "";

    // —— 单文件信息（仿分轨行：声道/位宽/采样率/时长，从 WAV 头读取）——
    [ObservableProperty] private string _singleChannelInfo = "-";
    [ObservableProperty] private string _singleBitWidthInfo = "-";
    [ObservableProperty] private string _singleSampleRateInfo = "-";
    [ObservableProperty] private string _singleDurationInfo = "-";
    [ObservableProperty] private int _singleBits;
    [ObservableProperty] private int _singleSampleRate;
    [ObservableProperty] private int _singleChannelCount;

    // —— 编码设置 ——
    [ObservableProperty] private int _selectedDestFormatIndex;
    [ObservableProperty] private int _selectedStreamTypeIndex;
    [ObservableProperty] private string _selectedEncodeType = "Lossless and Core";
    [ObservableProperty] private int _selectedSampleRate = 48000;
    [ObservableProperty] private int _selectedBitWidth = 24;
    [ObservableProperty] private string _coreBitRateText = "1509";
    [ObservableProperty] private int _selectedDialogNormIndex;
    [ObservableProperty] private string _selectedPaFadeDown = "2.0";
    [ObservableProperty] private string _selectedPaFadeUp = "2.0";

    // —— 高级（音频）Toggle ——
    [ObservableProperty] private bool _use9624;
    [ObservableProperty] private bool _attenuateRearCh;
    [ObservableProperty] private bool _esPhaseShift;
    [ObservableProperty] private bool _isPremixed;

    // —— 时间码 ——
    [ObservableProperty] private string _selectedFrameRate = "23.976";
    [ObservableProperty] private string _startTime = "00:00:00:00";
    [ObservableProperty] private string _endTime = "00:00:00:00";
    [ObservableProperty] private bool _encodeEntireFile = true;
    [ObservableProperty] private string _encodeFromTime = "00:00:00:00";
    [ObservableProperty] private string _encodeToTime = "00:00:00:00";
    [ObservableProperty] private bool _useReferenceTime;
    [ObservableProperty] private string _referenceTime = "00:00:00:00";

    // —— 比特流分区 ——
    [ObservableProperty] private bool _isBitstreamEnabled = true;
    [ObservableProperty] private double _bitstreamContentOpacity = 1.0;
    [ObservableProperty] private bool _usePrimaryAudioAttenuation;
    // 主音频衰减 / 单声道映射 dB
    [ObservableProperty] private string _primaryDb = "0.0";
    [ObservableProperty] private string _monoLDb = "INF";
    [ObservableProperty] private string _monoRDb = "INF";
    [ObservableProperty] private string _monoCDb = "0.0";
    [ObservableProperty] private string _monoLsDb = "INF";
    [ObservableProperty] private string _monoRsDb = "INF";
    // AAF
    [ObservableProperty] private bool _embedAaf;
    [ObservableProperty] private string _aafFilePath = "";
    [ObservableProperty] private bool _isAafIndependent;
    [ObservableProperty] private string _aafUnisonValue = "0";
    [ObservableProperty] private string _aafLF = "0";
    [ObservableProperty] private string _aafRF = "0";
    [ObservableProperty] private string _aafC = "0";
    [ObservableProperty] private string _aafLS = "0";
    [ObservableProperty] private string _aafRS = "0";
    [ObservableProperty] private string _aafLFE = "0";
    [ObservableProperty] private bool _aafPanningEnabled;
    [ObservableProperty] private string _panLF = "0";
    [ObservableProperty] private string _panRF = "0";
    [ObservableProperty] private string _panC = "0";
    [ObservableProperty] private string _panLS = "0";
    [ObservableProperty] private string _panRS = "0";
    // Seamless / 其它
    [ObservableProperty] private bool _seamlessSingleClip;
    [ObservableProperty] private bool _seamlessCsvBranchPoints;
    [ObservableProperty] private string _csvFilePath = "";
    [ObservableProperty] private bool _useExpressDialogMode;
    [ObservableProperty] private bool _useWideRemapping = true;
    [ObservableProperty] private string _programInfo = "";

    // —— 下混分区 ——
    [ObservableProperty] private bool _isDownmixEnabled = true;
    [ObservableProperty] private double _downmixContentOpacity = 1.0;
    [ObservableProperty] private bool _use51Downmix;
    [ObservableProperty] private bool _useCurrentChannels;
    [ObservableProperty] private bool _useLegacyMatrix;
    // 5.1 下混系数（primary + es(xchA) + esb(xchB)）
    [ObservableProperty] private string _dmx51LPrimary = "3.0";
    [ObservableProperty] private string _dmx51LXchA = "INF";
    [ObservableProperty] private string _dmx51LXchB = "INF";
    [ObservableProperty] private string _dmx51RPrimary = "3.0";
    [ObservableProperty] private string _dmx51RXchA = "INF";
    [ObservableProperty] private string _dmx51RXchB = "INF";
    [ObservableProperty] private string _dmx51CPrimary = "3.0";
    [ObservableProperty] private string _dmx51CXchA = "INF";
    [ObservableProperty] private string _dmx51CXchB = "INF";
    [ObservableProperty] private string _dmx51LfePrimary = "3.0";
    [ObservableProperty] private string _dmx51LfeXchA = "INF";
    [ObservableProperty] private string _dmx51LfeXchB = "INF";
    [ObservableProperty] private string _dmx51LsPrimary = "3.0";
    [ObservableProperty] private string _dmx51LsXchA = "3.0";
    [ObservableProperty] private string _dmx51LsXchB = "INF";
    [ObservableProperty] private string _dmx51RsPrimary = "3.0";
    [ObservableProperty] private string _dmx51RsXchA = "INF";
    [ObservableProperty] private string _dmx51RsXchB = "3.0";
    // 2.0 下混
    [ObservableProperty] private bool _use20Downmix;
    [ObservableProperty] private bool _is2ChDmixLtRt;
    [ObservableProperty] private bool _downmixSaturationCheck;
    [ObservableProperty] private bool _isEmbeddedDownmix;
    [ObservableProperty] private string _dmx20LeftA = "3.0";
    [ObservableProperty] private string _dmx20LeftB = "INF";
    [ObservableProperty] private string _dmx20RightA = "INF";
    [ObservableProperty] private string _dmx20RightB = "3.0";
    [ObservableProperty] private string _dmx20CenterA = "6.0";
    [ObservableProperty] private string _dmx20CenterB = "6.0";
    [ObservableProperty] private string _dmx20LfeA = "INF";
    [ObservableProperty] private string _dmx20LfeB = "INF";
    [ObservableProperty] private string _dmx20LsA = "6.0";
    [ObservableProperty] private string _dmx20LsB = "INF";
    [ObservableProperty] private string _dmx20RsA = "INF";
    [ObservableProperty] private string _dmx20RsB = "6.0";

    // —— 进度 / 引擎状态 / 日志 ——（预览改为弹窗形式，由 View 调用 BuildCfgPreview/BuildCommandPreview）
    [ObservableProperty] private string _progressText = "就绪";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private EncodeProgressState _progressState = EncodeProgressState.Idle;
    [ObservableProperty] private string _engineStatusText = "正在连接引擎…";
    [ObservableProperty] private EngineStatusState _engineStatusState = EngineStatusState.Connecting;
    [ObservableProperty] private bool _engineRingActive = true;
    /// <summary>引擎状态文字前景色：已连接=SeaGreen，正在连接=#FFC107（黄），失败/未加载=OrangeRed。</summary>
    [ObservableProperty] private IBrush _engineStatusForeground = Brushes.Goldenrod;
    /// <summary>引擎已连接（View 据此显示 ✓ 绿色勾图标）。</summary>
    [ObservableProperty] private bool _isEngineConnected;
    /// <summary>引擎正在连接中（View 据此显示 ProgressRing 旋转 + 黄色文字）。</summary>
    [ObservableProperty] private bool _isEngineConnecting = true;
    /// <summary>引擎失败/未加载（View 据此显示 ✗ 红色叉图标）。</summary>
    [ObservableProperty] private bool _isEngineFailed;
    [ObservableProperty] private string _logText = "";

    // —— 队列 ——
    public ObservableCollection<EncodeJob> Jobs { get; } = new();
    [ObservableProperty] private EncodeJob? _selectedJob;

    // ============ 内部状态 ============
    private readonly List<ChannelRowViewModel> _channelInputs = new();
    // 终止编码标志：Abort() 置 true，MonitorJobAsync 每轮检查后立即退出循环，
    // 避免终止后仍显示"编码中 X%"并继续算耗时
    private volatile bool _aborted;
    // 单文件多声道 WAV 重排临时文件路径（编码完成/出错/终止后清理）
    private string? _remapTmpPath;
    public ObservableCollection<ChannelRowViewModel> ChannelRows { get; } = new();
    private double _singleSeconds;
    private bool _hooked;

    // 引擎上报的编码百分比（'D' 帧，最准确的进度源）；-1 表示尚未收到
    private volatile int _enginePercent = -1;
    // 单调递增锁：防止进度回退（文件大小估算波动时）
    private int _maxPct;

    // 当前布局下的声道标签（供 View 刷新 SpeakerLayout 图示）
    public IReadOnlyList<string> CurrentChannels => _channelInputs.Select(c => c.Label).ToList();

    /// <summary>当前所选声道布局枚举。</summary>
    private ChannelLayout CurrentLayout =>
        ChannelLayouts.All.First(x => x.Display == SelectedChannelLayoutDisplay).Layout;

    // 声道名分隔符（用于从文件名反推前缀，识别同目录同名其它声道）
    private static readonly string[] Separators = { "_", "-", ".", " ", "" };

    public EncodePageViewModel()
    {
        // 启动性能优化：构造函数不再同步读取 3 个 properties 文件（避免 UI 线程冻结 5~20ms）。
        // 下混默认值改用 LoadDownmixDefaultsAsync() 在 View Loaded 后异步加载，
        // 由 Task.Run 在线程池读取文件 → Dispatcher.UIThread.Post 回 UI 线程赋值。
        // 字段初始化器已提供内置默认值（与 properties 文件数值一致），即使异步加载失败也不影响显示。
        BuildChannelRows();
        UpdateEngineStatus();
        HookEngine();
        // 不再自动加载上次保存的设置档：默认值按 Java GUI（WinUI3 复现版）设定。
        // 用户仍可通过工具栏"加载设置"按钮手动加载 .dtsprof 文件。
        // TryAutoLoadProfileAsync 方法保留供 LoadSettingsCommand 调用。
    }

    /// <summary>异步加载官方 conf/downmix*.properties 默认值。
    /// 由 View.OnLoaded 调用，避免构造函数同步 IO 阻塞 UI 线程。
    /// 文件读取在线程池执行，结果通过 Dispatcher.UIThread.Post 编组回 UI 线程赋值。</summary>
    public async Task LoadDownmixDefaultsAsync()
    {
        // 主音频衰减 / 单声道映射：纯赋值，直接在 UI 线程设置
        PrimaryDb = "0.0";
        MonoLDb = "INF"; MonoRDb = "INF"; MonoCDb = "0.0"; MonoLsDb = "INF"; MonoRsDb = "INF";

        // properties 文件读取放线程池，避免 UI 线程同步 IO
        var confDir = Path.Combine(AppServices.Settings.ToolDir, "conf");
        DownmixStereoCoeffs d;
        try
        {
            await Task.Run(() => DownmixDefaults.EnsureLoaded(confDir));
            d = DownmixDefaults.For20() ?? new DownmixStereoCoeffs();
        }
        catch { d = new DownmixStereoCoeffs(); }
        // For20() 仅读取已加载的内存字典，无 IO，可在 UI 线程执行
        Dmx20LeftA = d.LeftA; Dmx20LeftB = d.LeftB;
        Dmx20RightA = d.RightA; Dmx20RightB = d.RightB;
        Dmx20CenterA = d.CenterA; Dmx20CenterB = d.CenterB;
        Dmx20LfeA = d.LfeA; Dmx20LfeB = d.LfeB;
        Dmx20LsA = d.LsA; Dmx20LsB = d.LsB;
        Dmx20RsA = d.RsA; Dmx20RsB = d.RsB;

        // 5.1 下混（每列 3 个推子：primary + es(Lsr) + esb(Rsr)）
        var c = new Downmix51Coeffs();
        Dmx51LPrimary = c.LeftPrimary; Dmx51LXchA = c.LeftXchA; Dmx51LXchB = c.LeftXchB;
        Dmx51RPrimary = c.RightPrimary; Dmx51RXchA = c.RightXchA; Dmx51RXchB = c.RightXchB;
        Dmx51CPrimary = c.CenterPrimary; Dmx51CXchA = c.CenterXchA; Dmx51CXchB = c.CenterXchB;
        Dmx51LfePrimary = c.LfePrimary; Dmx51LfeXchA = c.LfeXchA; Dmx51LfeXchB = c.LfeXchB;
        Dmx51LsPrimary = c.LsPrimary; Dmx51LsXchA = c.LsXchA; Dmx51LsXchB = c.LsXchB;
        Dmx51RsPrimary = c.RsPrimary; Dmx51RsXchA = c.RsXchA; Dmx51RsXchB = c.RsXchB;
    }

    // =========================================================
    //  属性变更回调
    // =========================================================
    partial void OnIsSingleFileModeChanged(bool value)
    {
        IsSingleFilePanelVisible = value;
        IsChannelRowsVisible = !value;
    }

    partial void OnSelectedChannelLayoutDisplayChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        BuildChannelRows();
    }

    partial void OnSelectedFrameRateChanged(string value) => AutoFillTimecode();

    partial void OnIsBitstreamEnabledChanged(bool value) => BitstreamContentOpacity = value ? 1.0 : 0.45;
    partial void OnIsDownmixEnabledChanged(bool value) => DownmixContentOpacity = value ? 1.0 : 0.45;

    partial void OnSingleFilePathChanged(string value) => _ = UpdateSingleInfoAsync();

    // =========================================================
    //  默认值
    // =========================================================
    private void LoadDownmixDefaults()
    {
        // 主音频衰减 / 单声道映射
        PrimaryDb = "0.0";
        MonoLDb = "INF"; MonoRDb = "INF"; MonoCDb = "0.0"; MonoLsDb = "INF"; MonoRsDb = "INF";

        // 2.0 下混（优先读官方 conf/downmix20.properties，回退内置默认——二者数值一致）
        DownmixStereoCoeffs d;
        try
        {
            DownmixDefaults.EnsureLoaded(Path.Combine(AppServices.Settings.ToolDir, "conf"));
            d = DownmixDefaults.For20() ?? new DownmixStereoCoeffs();
        }
        catch { d = new DownmixStereoCoeffs(); }
        Dmx20LeftA = d.LeftA; Dmx20LeftB = d.LeftB;
        Dmx20RightA = d.RightA; Dmx20RightB = d.RightB;
        Dmx20CenterA = d.CenterA; Dmx20CenterB = d.CenterB;
        Dmx20LfeA = d.LfeA; Dmx20LfeB = d.LfeB;
        Dmx20LsA = d.LsA; Dmx20LsB = d.LsB;
        Dmx20RsA = d.RsA; Dmx20RsB = d.RsB;

        // 5.1 下混（每列 3 个推子：primary + es(Lsr) + esb(Rsr)）
        var c = new Downmix51Coeffs();
        Dmx51LPrimary = c.LeftPrimary; Dmx51LXchA = c.LeftXchA; Dmx51LXchB = c.LeftXchB;
        Dmx51RPrimary = c.RightPrimary; Dmx51RXchA = c.RightXchA; Dmx51RXchB = c.RightXchB;
        Dmx51CPrimary = c.CenterPrimary; Dmx51CXchA = c.CenterXchA; Dmx51CXchB = c.CenterXchB;
        Dmx51LfePrimary = c.LfePrimary; Dmx51LfeXchA = c.LfeXchA; Dmx51LfeXchB = c.LfeXchB;
        Dmx51LsPrimary = c.LsPrimary; Dmx51LsXchA = c.LsXchA; Dmx51LsXchB = c.LsXchB;
        Dmx51RsPrimary = c.RsPrimary; Dmx51RsXchA = c.RsXchA; Dmx51RsXchB = c.RsXchB;
    }

    // =========================================================
    //  动态声道输入行
    // =========================================================
    private static IReadOnlyList<string> InputChannels(ChannelLayout layout)
    {
        var tokens = layout.ToString().Split('_').ToList();
        if (tokens.Count >= 2) tokens.RemoveRange(0, 2);
        while (tokens.Count > 0 && tokens[0] is "ES" or "Matrix" or "Discrete" or "PreMixed")
            tokens.RemoveAt(0);
        return tokens;
    }

    // 将声道（UI/枚举序）重排为 cfg 声道声明顺序：L,R,核心环绕对,C,LFE,扩展声道(xch)
    private static List<string> CfgChannelOrder(IReadOnlyList<string> tokens)
    {
        var surrounds = tokens.Where(t => t is not ("L" or "R" or "C" or "LFE")).ToList();
        var core = surrounds.Take(2).ToList();
        var rest = surrounds.Skip(2).ToList();
        var o = new List<string>();
        if (tokens.Contains("L")) o.Add("L");
        if (tokens.Contains("R")) o.Add("R");
        o.AddRange(core);
        if (tokens.Contains("C")) o.Add("C");
        if (tokens.Contains("LFE")) o.Add("LFE");
        o.AddRange(rest);
        return o;
    }

    private void BuildChannelRows()
    {
        var channels = InputChannels(CurrentLayout);
        _channelInputs.Clear();
        ChannelRows.Clear();
        foreach (var label in channels)
        {
            var row = new ChannelRowViewModel(label, this);
            _channelInputs.Add(row);
            ChannelRows.Add(row);
        }
        ApplyLayoutDownmixDefaults();
        OnPropertyChanged(nameof(CurrentChannels));   // 通知 View 刷新 SpeakerLayout 图示
    }

    // 下混参数随声道布局实时变化（仿官方）：6.x/7.x 自动启用 5.1 下混矩阵+保留当前声道，5.1 及以下关闭
    private void ApplyLayoutDownmixDefaults()
    {
        var info = ChannelLayouts.Get(CurrentLayout);
        bool hasXch = info.MainChannels >= 6;
        Use51Downmix = hasXch;
        UseCurrentChannels = hasXch;
        if (!hasXch) return;

        // 应用官方 conf/downmix51.properties 里该布局的默认 5.1 下混系数
        try
        {
            DownmixDefaults.EnsureLoaded(Path.Combine(AppServices.Settings.ToolDir, "conf"));
            var c = DownmixDefaults.For51(info.PropertiesFile);
            if (c != null)
            {
                Dmx51LPrimary = c.LeftPrimary; Dmx51LXchA = c.LeftXchA; Dmx51LXchB = c.LeftXchB;
                Dmx51RPrimary = c.RightPrimary; Dmx51RXchA = c.RightXchA; Dmx51RXchB = c.RightXchB;
                Dmx51CPrimary = c.CenterPrimary; Dmx51CXchA = c.CenterXchA; Dmx51CXchB = c.CenterXchB;
                Dmx51LfePrimary = c.LfePrimary; Dmx51LfeXchA = c.LfeXchA; Dmx51LfeXchB = c.LfeXchB;
                Dmx51LsPrimary = c.LsPrimary; Dmx51LsXchA = c.LsXchA; Dmx51LsXchB = c.LsXchB;
                Dmx51RsPrimary = c.RsPrimary; Dmx51RsXchA = c.RsXchA; Dmx51RsXchB = c.RsXchB;
            }
        }
        catch { }
    }

    /// <summary>声道行 FilePath 变更时异步读取 WAV 头并刷新该行 Ch/Bw/Fs/Duration。</summary>
    internal async Task OnChannelPathChangedAsync(ChannelRowViewModel row)
    {
        var path = row.FilePath?.Trim() ?? "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            row.Channel = row.Bw = row.Fs = row.Duration = "-";
            row.Seconds = 0;
            Post(AutoFillTimecode);
            return;
        }
        var info = await Task.Run(() => WavInfo.TryRead(path));
        Post(() =>
        {
            if (info == null) { row.Channel = row.Bw = row.Fs = row.Duration = "?"; row.Seconds = 0; row.Bits = row.SampleRate = 0; }
            else
            {
                row.Channel = info.Ch; row.Bw = info.Bw; row.Fs = info.Fs; row.Duration = info.Duration; row.Seconds = info.Seconds;
                row.Bits = info.Bits; row.SampleRate = info.SampleRate;
                // 位宽随输入自动切换（16/24）
                if (info.Bits is 16 or 24 && OptionCatalog.BitWidths.Contains(info.Bits))
                    SelectedBitWidth = info.Bits;
                // 采样率若匹配也自动切换
                if (info.SampleRate > 0 && OptionCatalog.SampleRates.Contains(info.SampleRate))
                    SelectedSampleRate = info.SampleRate;
            }
            AutoFillTimecode();
        });
    }

    /// <summary>依据已导入文件的最大时长，自动填入 结束时间 / 编码至（时间码）。</summary>
    private void AutoFillTimecode()
    {
        double sec = _singleSeconds;
        foreach (var r in _channelInputs) if (r.Seconds > sec) sec = r.Seconds;
        if (sec <= 0) return;
        var fr = SelectedFrameRate ?? "23.976";
        var tc = SecondsToTimecode(sec, fr);
        EndTime = tc;
        EncodeToTime = tc;
    }

    private static (double actual, int nominal) Fps(string fr) => fr switch
    {
        "23.976" => (24000.0 / 1001.0, 24),
        "24" => (24.0, 24),
        "25" => (25.0, 25),
        "29.97" or "29.97 Drop" => (30000.0 / 1001.0, 30),
        "30" or "30 Drop" => (30.0, 30),
        _ => (24000.0 / 1001.0, 24),
    };

    private static string SecondsToTimecode(double seconds, string frameRate)
    {
        var (actual, nominal) = Fps(frameRate);
        long tf = (long)Math.Round(seconds * actual);
        int ff = (int)(tf % nominal);
        long totalSec = tf / nominal;
        int ss = (int)(totalSec % 60);
        int mm = (int)((totalSec / 60) % 60);
        int hh = (int)(totalSec / 3600);
        return $"{hh:00}:{mm:00}:{ss:00}:{ff:00}";
    }

    private async Task UpdateSingleInfoAsync()
    {
        var path = SingleFilePath?.Trim() ?? "";
        _singleSeconds = 0;
        SingleChannelInfo = "-";
        SingleBitWidthInfo = "-";
        SingleSampleRateInfo = "-";
        SingleDurationInfo = "-";
        SingleBits = 0;
        SingleSampleRate = 0;
        SingleChannelCount = 0;

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var info = await Task.Run(() => WavInfo.TryRead(path));
            Post(() =>
            {
                if (info == null)
                {
                    SingleChannelInfo = SingleBitWidthInfo = SingleSampleRateInfo = SingleDurationInfo = "?";
                    _singleSeconds = 0;
                }
                else
                {
                    SingleChannelInfo = info.Ch;
                    SingleBitWidthInfo = info.Bw;
                    SingleSampleRateInfo = info.Fs;
                    SingleDurationInfo = info.Duration;
                    _singleSeconds = info.Seconds;
                    SingleBits = info.Bits;
                    SingleSampleRate = info.SampleRate;
                    SingleChannelCount = info.Channels;

                    // 位宽/采样率自动同步（仿分轨行：合法值时才覆盖默认值）
                    if (info.Bits is 16 or 24 && OptionCatalog.BitWidths.Contains(info.Bits))
                        SelectedBitWidth = info.Bits;
                    if (info.SampleRate > 0 && OptionCatalog.SampleRates.Contains(info.SampleRate))
                        SelectedSampleRate = info.SampleRate;
                }
                AutoFillTimecode();
            });
            return;
        }
        Post(AutoFillTimecode);
    }

    // =========================================================
    //  文件选择命令
    // =========================================================
    [RelayCommand]
    private async Task BrowseSingleAsync()
    {
        var f = await (PickAudioFileDelegate?.Invoke() ?? Task.FromResult<string?>(null));
        if (f != null) SingleFilePath = f;
    }

    [RelayCommand]
    private async Task BrowseAafAsync()
    {
        var f = await (PickAafFileDelegate?.Invoke() ?? Task.FromResult<string?>(null));
        if (f != null) AafFilePath = f;
    }

    [RelayCommand]
    private async Task BrowseCsvAsync()
    {
        var f = await (PickCsvFileDelegate?.Invoke() ?? Task.FromResult<string?>(null));
        if (f != null) CsvFilePath = f;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var f = await (PickOutputFileDelegate?.Invoke() ?? Task.FromResult<string?>(null));
        if (f != null) OutputPath = f;
    }

    /// <summary>任一声道"浏览…"按钮：选文件 → 填入该声道 → 自动补齐同名其它声道。</summary>
    internal async Task PickAndSetChannelFileAsync(string label)
    {
        if (PickChannelFileDelegate == null) return;
        var path = await PickChannelFileDelegate(label);
        if (path == null) return;
        var row = _channelInputs.FirstOrDefault(c => c.Label == label);
        if (row != null) row.FilePath = path;
        AutoFillFrom(path); // 选任一声道即自动补齐同名其它声道
    }

    // 由任一声道文件(名+分隔符+声道)反推前缀/分隔符，识别并填充同目录其它声道
    public void AutoFillFrom(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            var ext = Path.GetExtension(path);
            var name = Path.GetFileNameWithoutExtension(path);
            foreach (var lab in InputChannels(CurrentLayout).OrderByDescending(l => l.Length))
                foreach (var sep in Separators)
                {
                    var suffix = sep + lab;
                    if (suffix.Length > 0 && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        var prefix = name.Substring(0, name.Length - suffix.Length);
                        FillSiblings(dir, prefix, ext);
                        return;
                    }
                }
        }
        catch { }
    }

    private void FillSiblings(string dir, string prefix, string ext)
    {
        foreach (var r in _channelInputs)
        {
            if (!string.IsNullOrEmpty(r.FilePath)) continue;
            foreach (var s in Separators)
            {
                var cand = Path.Combine(dir, prefix + s + r.Label + ext);
                if (File.Exists(cand)) { r.FilePath = cand; break; }
            }
        }
    }

    // 判断文件属于当前布局的哪个声道（最长标签优先）
    private string? DetectChannel(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var lab in InputChannels(CurrentLayout).OrderByDescending(l => l.Length))
            foreach (var sep in Separators)
            {
                var suffix = sep + lab;
                if (suffix.Length > 0 && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return lab;
            }
        return null;
    }

    // ---------- 拖放导入 ----------
    /// <summary>页面级拖入文件批量导入（由 View 的 DragDrop 处理器调用）。</summary>
    public void ImportFiles(List<string> files)
    {
        if (files == null || files.Count == 0) return;
        if (IsSingleFileMode) { SingleFilePath = files[0]; return; }

        var unmatched = new List<string>();
        foreach (var f in files)
        {
            var ch = DetectChannel(f);
            var pair = ch == null ? null : _channelInputs.FirstOrDefault(c => c.Label == ch);
            if (pair != null) pair.FilePath = f;
            else unmatched.Add(f);
        }
        if (files.Count == 1 && DetectChannel(files[0]) != null)
            AutoFillFrom(files[0]); // 单个已识别 → 自动补齐同名其它声道
        int idx = 0;
        foreach (var f in unmatched)
        {
            while (idx < _channelInputs.Count && !string.IsNullOrEmpty(_channelInputs[idx].FilePath)) idx++;
            if (idx >= _channelInputs.Count) break;
            _channelInputs[idx].FilePath = f; idx++;
        }
        Log($"拖入 {files.Count} 个文件。");
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var r in _channelInputs) r.FilePath = "";
        SingleFilePath = "";
        Log("已清除全部导入轨道。");
    }

    // =========================================================
    //  设置组装
    // =========================================================
    private EncodeSettings BuildSettings()
    {
        var layout = CurrentLayout;
        var encType = SelectedEncodeType ?? "Lossless and Core";
        int dialNorm = -31 + SelectedDialogNormIndex;
        var s = new EncodeSettings
        {
            ChannelLayout = layout,
            SampleRate = SelectedSampleRate,
            CoreSampleRate = SelectedSampleRate,
            BitWidth = SelectedBitWidth,
            CoreBitWidth = SelectedBitWidth,
            FrameRate = SelectedFrameRate ?? "23.976",
            UseLFE = ChannelLayouts.Get(layout).HasLFE,
            Use9624 = Use9624,
            AttenuateRearCh = AttenuateRearCh,
            EsPhaseShift = EsPhaseShift,
            IsPremixed = IsPremixed,
            UseDialogNormalization = true,   // 官方始终写 DIALOGNORM（-31=不衰减亦写）
            DialogNormalization = dialNorm,
            EncodeEntireFile = EncodeEntireFile,
        };
        s.TimecodeStart = StartTime;
        s.TimecodeEnd = EndTime;
        s.TimecodeEncodeFrom = EncodeFromTime;
        s.TimecodeEncodeTo = EncodeToTime;
        s.UseTimecodeReferenceTime = UseReferenceTime;
        s.TimecodeReferenceTime = ReferenceTime;

        s.IsLossless = encType is "Lossless and Core" or "Lossless Only";
        s.IsLbr = encType == "LBR";
        if (int.TryParse(CoreBitRateText, out int br)) s.BitRate = br;
        s.ResidualData = s.IsLossless ? "-r1" : "";   // 官方无损用 -r1

        switch (SelectedDestFormatIndex)
        {
            case 0: s.DestFormat = DestFormat.BdPrimary; s.IsMediaTypeBd = true; s.FileType = ".dtshd"; break;
            case 1: s.DestFormat = DestFormat.BdSecondary; s.IsMediaTypeBdSecondaryAudio = true; s.IsMediaTypeBd = false; s.FileType = ".dtshd"; break;
            case 2: s.DestFormat = DestFormat.Dvd; s.IsMediaTypeDvd = true; s.IsMediaTypeBd = false; s.FileType = ".cpt"; break;
            case 3: s.DestFormat = DestFormat.DtsCd; s.IsMediaTypeCd = true; s.IsMediaTypeBd = false; s.FileType = ".wav"; break;
            case 4: s.DestFormat = DestFormat.Dece; s.IsDece = true; s.IsMediaTypeBd = false; s.FileType = ".dtshd"; break;
        }

        // 比特流 / 元数据
        s.UsePrimaryAudioAttenuation = UsePrimaryAudioAttenuation;
        s.PaPrimary = PrimaryDb; s.PaFadeDown = SelectedPaFadeDown ?? "2.0"; s.PaFadeUp = SelectedPaFadeUp ?? "2.0";
        s.MonoMetadataL = MonoLDb; s.MonoMetadataR = MonoRDb; s.MonoMetadataC = MonoCDb;
        s.MonoMetadataLs = MonoLsDb; s.MonoMetadataRs = MonoRsDb;
        s.UseAaf = EmbedAaf;
        s.UsingAaf = s.UseAaf;
        s.AafFilename = AafFilePath;
        s.AafAttenuationKind = IsAafIndependent ? AafAttenuation.Independent : AafAttenuation.Unison;
        s.AafUnison = AafUnisonValue; s.AafLF = AafLF; s.AafRF = AafRF; s.AafC = AafC;
        s.AafLS = AafLS; s.AafRS = AafRS; s.AafLFE = AafLFE;
        s.AafPanningEnabled = AafPanningEnabled; s.AafPanningActive = s.AafPanningEnabled;
        s.PanLF = PanLF; s.PanRF = PanRF; s.PanC = PanC; s.PanLS = PanLS; s.PanRS = PanRS;
        s.SeamlessSingleClip = SeamlessSingleClip;
        s.SeamlessCsvBranchPoints = SeamlessCsvBranchPoints;
        s.SeamlessCsvFilename = CsvFilePath;
        s.UseExpressDialogMode = UseExpressDialogMode;
        s.UseWideRemapping = UseWideRemapping;
        s.ProgramInfo = ProgramInfo ?? "";

        // 下混
        bool hasXch = ChannelLayouts.Get(layout).MainChannels >= 6; // 6.x/7.x 必须带 5.1 下混矩阵
        s.Use20Downmix = Use20Downmix;
        s.Is2ChDmixLtRt = Is2ChDmixLtRt;
        s.DownmixSaturationCheck = DownmixSaturationCheck;
        s.IsEmbeddedDownmix = IsEmbeddedDownmix;
        s.Use51Downmix = hasXch || Use51Downmix;       // xch 布局强制生成矩阵
        s.UseCurrent = hasXch || UseCurrentChannels;    // 官方默认保留当前声道
        s.UseLegacyMatrix = UseLegacyMatrix;
        s.Downmix20 = new DownmixStereoCoeffs
        {
            LeftA = Dmx20LeftA, LeftB = Dmx20LeftB, RightA = Dmx20RightA, RightB = Dmx20RightB,
            CenterA = Dmx20CenterA, CenterB = Dmx20CenterB, LfeA = Dmx20LfeA, LfeB = Dmx20LfeB,
            LsA = Dmx20LsA, LsB = Dmx20LsB, RsA = Dmx20RsA, RsB = Dmx20RsB,
        };
        s.Downmix51 = new Downmix51Coeffs
        {
            LeftPrimary = Dmx51LPrimary, LeftXchA = Dmx51LXchA, LeftXchB = Dmx51LXchB,
            RightPrimary = Dmx51RPrimary, RightXchA = Dmx51RXchA, RightXchB = Dmx51RXchB,
            CenterPrimary = Dmx51CPrimary, CenterXchA = Dmx51CXchA, CenterXchB = Dmx51CXchB,
            LsPrimary = Dmx51LsPrimary, LsXchA = Dmx51LsXchA, LsXchB = Dmx51LsXchB,
            RsPrimary = Dmx51RsPrimary, RsXchA = Dmx51RsXchA, RsXchB = Dmx51RsXchB,
            LfePrimary = Dmx51LfePrimary, LfeXchA = Dmx51LfeXchA, LfeXchB = Dmx51LfeXchB,
        };

        // 输入（分轨：按 cfg 声道声明顺序排列，否则引擎会把声道对错）
        s.InputFiles.Clear();
        if (IsSingleFileMode)
        {
            s.InputFileOrder = string.IsNullOrWhiteSpace(SingleFilePath) ? "" : $"\"{SingleFilePath.Trim()}\"";
            if (!string.IsNullOrWhiteSpace(SingleFilePath)) s.InputFiles["*"] = SingleFilePath.Trim();
        }
        else
        {
            var paths = new List<string>();
            foreach (var lab in CfgChannelOrder(InputChannels(layout)))
            {
                var row = _channelInputs.FirstOrDefault(c => c.Label == lab);
                if (row != null && !string.IsNullOrWhiteSpace(row.FilePath))
                    paths.Add($"\"{row.FilePath.Trim()}\"");
            }
            s.InputFileOrder = string.Join(" ", paths);
            // 按声道标签存原始路径（用于官方格式日志）
            foreach (var row in _channelInputs)
                if (!string.IsNullOrWhiteSpace(row.FilePath)) s.InputFiles[row.Label] = row.FilePath.Trim();
        }

        // 输出
        var outPath = (OutputPath ?? "").Trim();
        if (!string.IsNullOrEmpty(outPath))
        {
            var dir = Path.GetDirectoryName(outPath);
            s.SaveToDirectory = !string.IsNullOrEmpty(dir) ? dir + Path.DirectorySeparatorChar : "";
            s.SaveToFilename = Path.GetFileNameWithoutExtension(outPath);
            var ext = Path.GetExtension(outPath);
            if (!string.IsNullOrEmpty(ext)) s.FileType = ext;
        }
        return s;
    }

    // ========================================================
    //  设置档：保存/加载用户自定义编码设置（JSON）
    // ========================================================
    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var path = EncodeProfileService.DefaultFilePath();
            EncodeProfileService.Save(BuildSettings(), path);
            Log($"设置已保存: {path}");
        }
        catch (Exception ex) { Log("保存设置失败: " + ex.Message); }
    }

    [RelayCommand]
    private async Task SaveSettingsAsAsync()
    {
        try
        {
            string path = await (PickSettingsSaveDelegate?.Invoke() ?? Task.FromResult<string?>(null))
                ?? EncodeProfileService.DefaultFilePath();
            EncodeProfileService.Save(BuildSettings(), path);
            Log($"设置已另存为: {path}");
            // 另存为后第一时间打开所在文件夹并选中该文件
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex) { Log("另存为失败: " + ex.Message); }
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        try
        {
            var f = await (PickSettingsOpenDelegate?.Invoke() ?? Task.FromResult<string?>(null));
            if (f == null) return;
            var s = EncodeProfileService.Load(f);
            if (s == null) { Log("无法解析设置文件: " + f); return; }
            ApplySettings(s);
            Log($"已加载设置: {f}");
        }
        catch (Exception ex) { Log("加载设置失败: " + ex.Message); }
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        // 恢复 Java GUI 默认编码参数（保留当前声道布局与已选输入/输出文件）。
        // 注意：不能直接用 `new EncodeSettings { ChannelLayout = CurrentLayout }`，
        // 因为 EncodeSettings 字段默认值与 Java GUI 默认值不一致（FrameRate、PaFade 等），
        // 必须显式按 Java GUI 默认值重置每个属性。
        var s = new EncodeSettings { ChannelLayout = CurrentLayout };
        // 修正 EncodeSettings 默认值与 Java GUI 不一致的字段
        s.FrameRate = "23.976";          // Java GUI 默认 23.976（非 EncodeSettings 的 "30"）
        s.PaFadeDown = "2.0";            // Java GUI 默认 2.0（非 EncodeSettings 的 "0"）
        s.PaFadeUp = "2.0";              // Java GUI 默认 2.0（非 EncodeSettings 的 "0"）
        s.PaPrimary = "0.0";             // Java GUI 默认 0.0（非 EncodeSettings 的 "INF"）
        s.MonoMetadataL = "INF";         // Java GUI 默认 INF（非 EncodeSettings 的 "0"）
        s.MonoMetadataR = "INF";
        s.MonoMetadataLs = "INF";
        s.MonoMetadataRs = "INF";
        ApplySettings(s);
        LoadDownmixDefaults();
        ApplyLayoutDownmixDefaults();
        Log("已恢复默认编码设置。");
    }

    /// <summary>启动时自动加载最近一次保存的设置档（若存在）。
    /// 异步版本：目录枚举 + JSON 反序列化在后台线程执行；ApplySettings 编组回 UI 线程。
    /// 避免阻塞 UI 线程拖慢启动。</summary>
    private async Task TryAutoLoadProfileAsync()
    {
        try
        {
            // 后台线程：目录扫描 + JSON 反序列化（这是主要耗时部分）
            var path = await EncodeProfileService.NewestProfilePathAsync();
            if (path == null) return;
            var s = await EncodeProfileService.LoadAsync(path);
            if (s == null) return;
            // 编组回 UI 线程：ApplySettings 触发大量 PropertyChanged 通知 + 声道行重建
            // （ObservableProperty 非线程安全，必须在 UI 线程执行）
            Post(() =>
            {
                try { ApplySettings(s); Log($"已自动加载上次设置: {path}"); }
                catch { }
            });
        }
        catch { }
    }

    /// <summary>将设置档回填到界面属性（BuildSettings 的逆操作；不改动输入/输出文件）。</summary>
    private void ApplySettings(EncodeSettings s)
    {
        // 声道布局（会触发声道行重建与该布局默认下混）
        var disp = ChannelLayouts.Get(s.ChannelLayout).Display;
        if (SelectedChannelLayoutDisplay != disp)
            SelectedChannelLayoutDisplay = disp;

        // 基本参数
        SelectedSampleRate = s.SampleRate;
        SelectedBitWidth = s.BitWidth;
        SelectedFrameRate = s.FrameRate;
        SelectedEncodeType = s.IsLbr ? "LBR" : s.IsLossless ? "Lossless and Core" : "Core Encode";
        if (s.BitRate > 0) CoreBitRateText = s.BitRate.ToString();
        SelectedDialogNormIndex = Math.Clamp(s.DialogNormalization + 31, 0, DialogNormOptions.Count - 1);
        SelectedDestFormatIndex = Math.Clamp((int)s.DestFormat - 1, 0, DestFormats.Count - 1);

        // 高级（音频）
        Use9624 = s.Use9624; AttenuateRearCh = s.AttenuateRearCh;
        EsPhaseShift = s.EsPhaseShift; IsPremixed = s.IsPremixed;

        // 时间码
        EncodeEntireFile = s.EncodeEntireFile;
        StartTime = s.TimecodeStart; EndTime = s.TimecodeEnd;
        EncodeFromTime = s.TimecodeEncodeFrom; EncodeToTime = s.TimecodeEncodeTo;
        UseReferenceTime = s.UseTimecodeReferenceTime; ReferenceTime = s.TimecodeReferenceTime;

        // 比特流
        UsePrimaryAudioAttenuation = s.UsePrimaryAudioAttenuation;
        PrimaryDb = s.PaPrimary; SelectedPaFadeDown = s.PaFadeDown; SelectedPaFadeUp = s.PaFadeUp;
        MonoLDb = s.MonoMetadataL; MonoRDb = s.MonoMetadataR; MonoCDb = s.MonoMetadataC;
        MonoLsDb = s.MonoMetadataLs; MonoRsDb = s.MonoMetadataRs;
        EmbedAaf = s.UseAaf; AafFilePath = s.AafFilename;
        IsAafIndependent = s.AafAttenuationKind == AafAttenuation.Independent;
        AafUnisonValue = s.AafUnison; AafLF = s.AafLF; AafRF = s.AafRF; AafC = s.AafC;
        AafLS = s.AafLS; AafRS = s.AafRS; AafLFE = s.AafLFE;
        AafPanningEnabled = s.AafPanningEnabled;
        PanLF = s.PanLF; PanRF = s.PanRF; PanC = s.PanC; PanLS = s.PanLS; PanRS = s.PanRS;
        SeamlessSingleClip = s.SeamlessSingleClip;
        SeamlessCsvBranchPoints = s.SeamlessCsvBranchPoints; CsvFilePath = s.SeamlessCsvFilename;
        UseExpressDialogMode = s.UseExpressDialogMode; UseWideRemapping = s.UseWideRemapping;
        ProgramInfo = s.ProgramInfo;

        // 下混
        Use20Downmix = s.Use20Downmix;
        Is2ChDmixLtRt = s.Is2ChDmixLtRt;
        DownmixSaturationCheck = s.DownmixSaturationCheck; IsEmbeddedDownmix = s.IsEmbeddedDownmix;
        Use51Downmix = s.Use51Downmix; UseCurrentChannels = s.UseCurrent; UseLegacyMatrix = s.UseLegacyMatrix;

        var d = s.Downmix20;
        Dmx20LeftA = d.LeftA; Dmx20LeftB = d.LeftB; Dmx20RightA = d.RightA; Dmx20RightB = d.RightB;
        Dmx20CenterA = d.CenterA; Dmx20CenterB = d.CenterB; Dmx20LfeA = d.LfeA; Dmx20LfeB = d.LfeB;
        Dmx20LsA = d.LsA; Dmx20LsB = d.LsB; Dmx20RsA = d.RsA; Dmx20RsB = d.RsB;

        var c = s.Downmix51;
        Dmx51LPrimary = c.LeftPrimary; Dmx51LXchA = c.LeftXchA; Dmx51LXchB = c.LeftXchB;
        Dmx51RPrimary = c.RightPrimary; Dmx51RXchA = c.RightXchA; Dmx51RXchB = c.RightXchB;
        Dmx51CPrimary = c.CenterPrimary; Dmx51CXchA = c.CenterXchA; Dmx51CXchB = c.CenterXchB;
        Dmx51LfePrimary = c.LfePrimary; Dmx51LfeXchA = c.LfeXchA; Dmx51LfeXchB = c.LfeXchB;
        Dmx51LsPrimary = c.LsPrimary; Dmx51LsXchA = c.LsXchA; Dmx51LsXchB = c.LsXchB;
        Dmx51RsPrimary = c.RsPrimary; Dmx51RsXchA = c.RsXchA; Dmx51RsXchB = c.RsXchB;
    }

    // =========================================================
    //  操作
    // =========================================================
    [RelayCommand]
    private async Task EncodeAsync()
    {
        try
        {
            if (AppServices.Host == null || AppServices.Queue == null || !AppServices.ToolsetReady)
            { Log("工具集未就绪，请到「设置」配置 DTS-HD_Tool。"); return; }
            var s = BuildSettings();
            if (string.IsNullOrWhiteSpace(s.SaveToFilename)) { Log("请先指定输出文件。"); return; }
            if (string.IsNullOrWhiteSpace(s.InputFileOrder)) { Log("请先选择输入文件。"); return; }

            // ====== 单文件多声道 WAV 声道重排 ======
            // DtsJobQueue.exe 不解析 WAVE dwChannelMask，按 .cfg 中 CHANNEL 声明顺序
            // 读取 WAV 帧交错。标准 WAVE 7.1 顺序 (L,R,C,LFE,BL,BR,SL,SR) 与 DTS cfg
            // 期望顺序 (L,R,Ls,Rs,C,LFE,Lsr,Rsr) 不匹配，位置 3-7 全部错位。
            // Java 原版不支持多声道单文件，这是 C# 移植版新增功能引入的回归 bug。
            // 修复：检测到多声道 WAV 时，按 cfg 顺序重排声道到临时文件再提交编码。
            //
            // 【性能要点】重排是 CPU+IO 密集任务，必须完全在后台线程执行：
            // - GetDiagnostic / NeedsRemap / RemapToTempWav 全部在 Task.Run 内
            // - 进度回调仅更新 OverallProgress 数值（一次 setter），不向 TextBox 拼接日志
            // - 完成后只 Log 一次总结，避免高频 Post 淹没 UI 线程
            _remapTmpPath = null;
            if (IsSingleFileMode && !string.IsNullOrWhiteSpace(SingleFilePath) && File.Exists(SingleFilePath))
            {
                SetProgress(EncodeProgressState.Working, "① 正在分析 WAV 声道顺序...");
                // 把所有重排相关 IO+CPU 工作整体放到后台线程，UI 线程 0 阻塞
                var remapResult = await Task.Run(() =>
                {
                    // 1. 诊断 + 是否需要重排（按当前所选 ChannelLayout 决定 cfg 期望顺序）
                    var layout = CurrentLayout;
                    string diag = WavChannelRemapper.GetDiagnostic(SingleFilePath, layout);
                    bool needsRemap = false;
                    long srcSize = new FileInfo(SingleFilePath).Length;
                    try { needsRemap = WavChannelRemapper.NeedsRemap(SingleFilePath, layout); }
                    catch { /* 解析失败，按原文件提交，让引擎报错 */ }

                    if (!needsRemap)
                        return (diag, needsRemap, srcSize, tmp: (string?)null, tmpSize: 0L,
                            tmpCh: 0, tmpBits: 0, tmpRate: 0, tmpSec: 0.0, err: (string?)null);

                    // 2. 执行重排，进度回调走 Dispatcher.UIThread.Post 更新 OverallProgress
                    //    仅整 2% 边界触发，最多 50 次 Post（不向 TextBox 拼接日志）
                    try
                    {
                        string tmp = WavChannelRemapper.RemapToTempWav(SingleFilePath, layout, pct =>
                        {
                            // 节流：WavChannelRemapper 内部已限制为 2% 步进
                            Dispatcher.UIThread.Post(() =>
                            {
                                OverallProgress = pct;
                                ProgressText = $"① 声道重排中… {pct}%";
                            });
                        });
                        long tmpSize = new FileInfo(tmp).Length;
                        var tmpInfo = WavInfo.TryRead(tmp);
                        return (diag, needsRemap, srcSize, tmp: (string?)tmp, tmpSize: tmpSize,
                            tmpCh: tmpInfo?.Channels ?? 0, tmpBits: tmpInfo?.Bits ?? 0,
                            tmpRate: tmpInfo?.SampleRate ?? 0, tmpSec: tmpInfo?.Seconds ?? 0,
                            err: (string?)null);
                    }
                    catch (Exception ex)
                    {
                        return (diag, needsRemap, srcSize, tmp: (string?)null, tmpSize: 0L,
                            tmpCh: 0, tmpBits: 0, tmpRate: 0, tmpSec: 0.0, err: ex.Message);
                    }
                });

                // 3. 回到 UI 线程：只 Log 一次总结（不再每帧追加进度日志）
                if (remapResult.err != null)
                {
                    Log("声道重排失败: " + remapResult.err + "（将使用原文件，可能出现声道错位）");
                }
                else if (remapResult.needsRemap && remapResult.tmp != null)
                {
                    _remapTmpPath = remapResult.tmp;
                    s.InputFileOrder = $"\"{remapResult.tmp}\"";
                    s.InputFiles["*"] = remapResult.tmp;
                    // 单条日志包含所有信息（避免多条 Log 串联触发多次 TextBox 重绘）
                    Log($"声道重排完成: {remapResult.srcSize} → {remapResult.tmpSize} bytes, " +
                        $"ch={remapResult.tmpCh}, bits={remapResult.tmpBits}, " +
                        $"sr={remapResult.tmpRate}, dur={TimeSpan.FromSeconds(remapResult.tmpSec):hh\\:mm\\:ss\\.fff}, " +
                        $"fmt=WAVE_FORMAT_PCM（已去掉 dwChannelMask）");
                    // 复用重排时读的 WavInfo 结果，避免后续同步读 WAV 头
                    if (remapResult.tmpSec > 0) _singleSeconds = remapResult.tmpSec;
                }
                else
                {
                    Log("无需重排（声道顺序已匹配 cfg 或非多声道 WAV）");
                }
                // 重排结束，进度归零（后续编码阶段会接管）
                OverallProgress = 0;
                ProgressText = "就绪";
            }

            // ③ 输入一致性校验（位宽/采样率一致、与所选采样率相符、AAF/96-24 等）
            var vErr = ValidateInputs(s);
            if (vErr != null) { await ShowInfoAsync("无法编码", vErr); Log("校验失败: " + vErr); return; }

            // ① 磁盘空间预检（官方：需求≈输入总大小×0.8）
            if (!await CheckDriveSpaceAsync(s)) { Log("已取消（存储空间不足）。"); return; }

            // 按需连接引擎（不阻塞 UI），含自动启动 Authorizer + DtsJobQueue + 端口就绪探测
            Log("正在连接引擎…");
            bool connected = await AppServices.Host.EnsureConnectedAsync();
            if (!connected)
            {
                Log("无法连接 DtsJobQueue 引擎。请检查：");
                Log("  1. DTS-HD_Tool 目录下是否有 DtsJobQueue.exe 和 DTSEncConfig.dll");
                Log("  2. 手动双击 DtsJobQueue.exe 看是否能运行（可能缺少 VC++ 运行库）");
                Log("  3. 端口 4444 是否被占用（任务管理器关闭残留 DtsJobQueue 进程）");
                Log("  4. 查看上方 [EngineHost] 日志了解启动详情");
                return;
            }
            Log("引擎已连接，正在提交编码任务…");

            var outBase = s.SaveToDirectory + s.SaveToFilename;
            var outputPath = outBase + s.FileType;
            var logPath = outputPath + "_log.txt";

            // 预估最终输出大小（用于进度百分比）
            // 单文件模式：若 _singleSeconds 还没读出来（用户选完文件立即点编码），临时同步读一次 WAV 头
            double totalSec = _channelInputs.Count > 0 ? _channelInputs.Max(c => c.Seconds) : _singleSeconds;
            if (IsSingleFileMode && totalSec <= 0 && !string.IsNullOrWhiteSpace(SingleFilePath) && File.Exists(SingleFilePath))
            {
                var syncInfo = WavInfo.TryRead(SingleFilePath);
                if (syncInfo != null) { _singleSeconds = syncInfo.Seconds; totalSec = syncInfo.Seconds; }
            }
            int ch = InputChannels(CurrentLayout).Count;
            double pcmBps = ch * s.SampleRate * (s.BitWidth / 8.0);
            double bps = !s.IsLossless ? s.BitRate * 1000.0 / 8.0 : pcmBps * 0.62; // CBR(核心/LBR)可准确，无损按比例估算
            double estFinalBytes = totalSec > 0 ? totalSec * bps : 0;

            EncodeJob job;
            try
            {
                job = AppServices.Queue.Submit(s, folderBased: false);
            }
            catch (InvalidOperationException)
            {
                // 连接已断开，尝试重连一次再提交
                Log("连接已断开，正在重连引擎…");
                if (!await AppServices.Host.EnsureConnectedAsync())
                { Log("重连失败，请重试编码。"); return; }
                Log("重连成功，重新提交编码任务…");
                try { job = AppServices.Queue.Submit(s, folderBased: false); }
                catch (Exception ex2) { Log("提交失败: " + ex2.Message); return; }
            }
            catch (Exception ex) { Log("提交失败: " + ex.Message); return; }
            job.OutputPath = outputPath; job.LogPath = logPath;   // 供"信息"按钮打开日志
            RefreshJobs();                       // 立即显示队列（不依赖异步订阅）
            Log($">> 开始编码 #{job.Handle}: {job.Name}（提交至引擎）");
            _aborted = false;  // 重置终止标志
            _enginePercent = -1;  // 重置引擎上报百分比（等待新的 'D' 帧）
            _maxPct = 0;          // 重置单调递增锁
            SetProgress(EncodeProgressState.Working, "② 编码中…");
            OverallProgress = 0;
            _ = MonitorJobAsync(job, outputPath, logPath, estFinalBytes, totalSec, s);
        }
        catch (Exception ex) { Log("提交失败: " + ex.Message); }
    }

    private static string Hms(double sec)
    {
        if (sec < 0 || double.IsNaN(sec) || double.IsInfinity(sec)) return "--:--";
        var t = TimeSpan.FromSeconds(sec);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    // 引擎编码中不回 socket 帧 → 轮询输出大小(按预估最终大小算百分比)与日志(完成/出错)
    private async Task MonitorJobAsync(EncodeJob job, string outputPath, string logPath, double estFinalBytes, double totalSec, EncodeSettings settings)
    {
        try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }
        var startTime = DateTime.Now;
        long lastSize = -1; int stableCount = 0;
        while (true)
        {
            await Task.Delay(1000);
            // 终止检查：Abort() 已置标志 → 立即退出循环，不再更新进度/耗时
            if (_aborted)
            {
                job.Percent = 0; job.Status = "已停止";
                OverallProgress = 0;
                SetProgress(EncodeProgressState.Idle, "✗ 已停止编码");
                break;
            }
            long size = 0;
            try { if (File.Exists(outputPath)) size = new FileInfo(outputPath).Length; } catch { }

            string? err = null; bool logReadable = false, logEmpty = false;
            if (File.Exists(logPath))
            {
                try
                {
                    using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var sr = new StreamReader(fs);
                    var txt = sr.ReadToEnd().Trim();
                    logReadable = true; logEmpty = txt.Length == 0; err = txt;
                }
                catch { logReadable = false; }
            }

            if (size == lastSize) stableCount++; else { stableCount = 0; lastSize = size; }
            // 按日志内容判定：含 "completed successfully"=成功；含 "Error"=失败；
            // 其它非空内容(如 Saturation 警告/MD5)不算失败；空日志+输出停止增长=成功
            string logTxt = err ?? "";
            bool completedOk = logReadable && logTxt.IndexOf("completed successfully", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasError = logReadable && !completedOk && logTxt.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0;
            bool finishedOk = completedOk || (logReadable && logEmpty && size > 0 && stableCount >= 2);
            bool finishedErr = hasError;

            double elapsed = (DateTime.Now - startTime).TotalSeconds;
            int pct;
            double eta;

            // 优先用引擎上报的百分比（'D' 帧，最准确）
            if (_enginePercent >= 0)
            {
                pct = _enginePercent;
                eta = elapsed > 0 && pct > 0 && pct < 100 ? (elapsed / pct) * (100 - pct) : -1;
            }
            else if (estFinalBytes > 0 && size > 0)
            {
                // 用输出文件大小 / 预估输出大小计算进度（fallback）
                double frac = Math.Min(0.99, size / estFinalBytes);
                pct = (int)Math.Round(frac * 100);
                eta = frac > 0.02 ? elapsed * (1 - frac) / frac : -1;
            }
            else if (totalSec > 0 && elapsed > 1)
            {
                // 兜底：用已用时间 / 总时长估算（基于线性处理速率假设）
                double timeFrac = Math.Min(0.99, elapsed / totalSec);
                pct = (int)Math.Round(timeFrac * 100);
                eta = totalSec - elapsed;
            }
            else
            {
                pct = 0;
                eta = -1;
            }

            // 单调递增：防止波动导致进度回退
            if (pct < _maxPct) pct = _maxPct;
            else _maxPct = pct;

            string mb = (size / 1024.0 / 1024.0).ToString("0.0");
            int warnCount = logTxt.Split('\n').Count(l => l.Contains("Saturation", StringComparison.OrdinalIgnoreCase));
            string md5 = logTxt.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("MD5", StringComparison.OrdinalIgnoreCase))?.Trim() ?? "";

            Post(() =>
            {
                if (finishedErr)
                {
                    job.Percent = 0; job.Status = "错误";
                    OverallProgress = 0;
                    SetProgress(EncodeProgressState.Error, "✗ 出错: " + err);
                    Log("<< 编码出错: " + err);
                }
                else if (finishedOk)
                {
                    job.Percent = 100; job.Status = warnCount > 0 ? $"完成 ({warnCount} 警告)" : "完成";
                    OverallProgress = 100;
                    SetProgress(EncodeProgressState.Completed,
                        $"✓ 编码完成 · 耗时 {Hms(elapsed)} · {mb} MB" + (warnCount > 0 ? $" · {warnCount} 个下混饱和警告" : ""));
                    Log($"<< 编码完成: {outputPath}（耗时 {Hms(elapsed)}, {mb} MB）");
                    if (!string.IsNullOrEmpty(md5)) Log("   " + md5);
                    if (warnCount > 0) Log($"   {warnCount} 个下混饱和警告（不影响结果；如介意可调下混系数/启用饱和保护）");
                }
                else
                {
                    job.Percent = pct;
                    job.Status = $"编码中 {pct}%";
                    OverallProgress = pct;
                    SetProgress(EncodeProgressState.Working,
                        $"② 编码中… {pct}% · 已用 {Hms(elapsed)} · 剩余 ~{Hms(eta)} · {mb} MB");
                }
            });

            if (finishedOk || finishedErr)
            {
                // 生成官方格式日志：在引擎输出前补上设置头（.dtshd_log.txt = 头 + 引擎输出）
                try
                {
                    string engineLog = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
                    if (!engineLog.StartsWith("****"))
                    {
                        string header = EncodeLogWriter.BuildHeader(settings, AppServices.Settings.ToolDir);
                        File.WriteAllText(logPath, header + engineLog);
                    }
                }
                catch { }
                break;
            }
            if (elapsed > 12 * 3600) break;
        }

        // 清理声道重排临时文件（编码完成/出错/终止后都执行）
        if (_remapTmpPath != null && File.Exists(_remapTmpPath))
        {
            try { File.Delete(_remapTmpPath); Log("已清理声道重排临时文件: " + _remapTmpPath); }
            catch { }
            _remapTmpPath = null;
        }
    }

    // 终止任务：终止所选；未选则终止队列中正在处理的任务
    [RelayCommand]
    private void Abort()
    {
        if (AppServices.Queue == null) return;
        int handle = -1;
        if (SelectedJob is EncodeJob sel) handle = sel.Handle;
        else
        {
            var cur = AppServices.Queue.Jobs.Values.FirstOrDefault(j => j.Status.Contains("编码") || j.Status.Contains("PBR") || j.Status.Contains("MD5"));
            if (cur != null) handle = cur.Handle;
        }
        if (handle < 0) { Log("没有可终止的任务（请在队列中选择，或等任务开始）。"); return; }
        _aborted = true;  // 通知 MonitorJobAsync 立即退出循环，停止进度/耗时更新
        AppServices.Queue.Cancel(handle);
        Log($"终止任务 #{handle}");
        // 立即更新 UI 状态（不等 MonitorJobAsync 下一轮轮询）
        if (SelectedJob is EncodeJob aj) aj.Status = "已停止";
        SetProgress(EncodeProgressState.Idle, "✗ 已停止编码");
        OverallProgress = 0;
    }

    // ---------- 队列按钮（仿官方）----------
    [RelayCommand]
    private void JobMoveUp()
    {
        if (AppServices.Queue != null && SelectedJob is EncodeJob j) AppServices.Queue.MoveUp(j.Handle);
    }

    [RelayCommand]
    private void JobMoveDown()
    {
        if (AppServices.Queue != null && SelectedJob is EncodeJob j) AppServices.Queue.MoveDown(j.Handle);
    }

    [RelayCommand]
    private void ClearComplete()
    {
        AppServices.Queue?.ClearCompleted();
        Log("已清除全部已完成任务。");
    }

    [RelayCommand]
    private void ClearJob()
    {
        if (AppServices.Queue == null) return;
        if (SelectedJob is EncodeJob j)
        {
            if (j.InProgress) AppServices.Queue.Cancel(j.Handle);
            AppServices.Queue.RemoveLocal(j.Handle);
            Log($"已从队列移除 #{j.Handle} {j.Name}");
        }
        else Log("请先在队列中选择要清除的任务。");
    }

    [RelayCommand]
    private void JobInfo(EncodeJob? job)
    {
        var j = job ?? SelectedJob;
        if (j == null) return;
        if (!string.IsNullOrEmpty(j.LogPath) && File.Exists(j.LogPath))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(j.LogPath) { UseShellExecute = true }); }
            catch (Exception ex) { Log("打开日志失败: " + ex.Message); }
        }
        else Log($"任务 #{j.Handle} {j.Name} · {j.Status}" + (string.IsNullOrEmpty(j.OutputPath) ? "" : $" · {j.OutputPath}"));
    }

    // ③ 输入一致性校验（逆向自官方 InputFilesValidator/AudioSettingsValidator）。返回错误文本或 null。
    private string? ValidateInputs(EncodeSettings s)
    {
        if (IsSingleFileMode) return null; // 单文件不做多轨一致性校验
        if (_channelInputs.Any(r => string.IsNullOrWhiteSpace(r.FilePath)))
            return "请填入所有声道的输入文件。";
        var info = _channelInputs.Where(r => r.Bits > 0 && r.SampleRate > 0).ToList();
        if (info.Count > 0)
        {
            int b0 = info[0].Bits;
            if (info.Any(r => r.Bits != b0)) return "各输入文件的位宽不一致。";
            int sr0 = info[0].SampleRate;
            if (info.Any(r => r.SampleRate != sr0)) return "各输入文件的采样率不一致。";
            if (sr0 != s.SampleRate) return $"输入文件采样率 {sr0} Hz 与所选采样率 {s.SampleRate} Hz 不符。";
        }
        if (s.UseAaf && string.IsNullOrWhiteSpace(s.AafFilename)) return "已启用 AAF，但未加载 AAF 文件。";
        if (s.Use9624 && s.Use20Downmix) return "96/24 Core 不能与 2.0 下混同时使用。";
        return null;
    }

    private async Task ShowInfoAsync(string title, string msg)
    {
        if (ShowInfoDelegate != null) await ShowInfoDelegate(title, msg);
    }

    // ① 磁盘空间预检（与官方 DriveSpaceValidator 一致：需求 = 输入总字节 × 8 / 10）
    private async Task<bool> CheckDriveSpaceAsync(EncodeSettings s)
    {
        try
        {
            long inputBytes = 0;
            if (IsSingleFileMode)
            {
                if (File.Exists(SingleFilePath)) inputBytes = new FileInfo(SingleFilePath).Length;
            }
            else
            {
                foreach (var r in _channelInputs)
                    if (File.Exists(r.FilePath)) inputBytes += new FileInfo(r.FilePath).Length;
            }
            long required = inputBytes / 10 * 8;
            long free = -1;
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(string.IsNullOrEmpty(s.SaveToDirectory) ? "." : s.SaveToDirectory));
                if (!string.IsNullOrEmpty(root)) free = new DriveInfo(root).AvailableFreeSpace;
            }
            catch { free = -1; }

            static string GB(long b) => (b / 1024.0 / 1024 / 1024).ToString("0.00") + " GB";
            if (free < 0)
                return await ConfirmAsync("无法确定可用存储空间", "无法读取目标卷的可用空间，是否仍要继续？");
            if (free <= required)
                return await ConfirmAsync("存储空间可能不足",
                    $"目标卷可用 {GB(free)}，预计约需 {GB(required)}。空间可能不足，是否继续编码？");
            return true;
        }
        catch { return true; }
    }

    private async Task<bool> ConfirmAsync(string title, string msg)
    {
        return ShowConfirmDelegate != null ? await ShowConfirmDelegate(title, msg) : true;
    }

    // ---------- 预览（弹窗：由 View 的 OnPreviewCfgClick/OnPreviewCommandClick 调用）----------
    /// <summary>构建 .cfg 文本内容（供预览弹窗显示）。</summary>
    public string BuildCfgPreview() => new CfgWriter(BuildSettings()).Build();

    /// <summary>构建编码命令行文本（供预览弹窗显示）。ConfigFilePath 用 &lt;job&gt; 占位以体现提交后的实际路径。</summary>
    public string BuildCommandPreview()
    {
        var s = BuildSettings();
        s.ConfigFilePath = Path.Combine(AppServices.QueueDir, "<job>", s.SaveToFilename + ".cfg");
        return DtsCommandBuilder.BuildEncodeCommand(s, false);
    }

    // =========================================================
    //  引擎状态 / 队列 / 日志
    // =========================================================
    private void HookEngine()
    {
        if (_hooked) return; _hooked = true;
        AppServices.EngineStateChanged += UpdateEngineStatus;
        // 语言切换时重新本地化引擎状态文本
        LocalizationManager.LanguageChanged += UpdateEngineStatus;
        if (AppServices.Host != null)
        {
            AppServices.Host.ResponseParsed += OnEngineResponse;
            AppServices.Host.DiagnosticLog += OnDiagnosticLog;
            AppServices.Host.ConnectionStateChanged += OnConnChanged;
        }
        if (AppServices.Queue != null) AppServices.Queue.Changed += RefreshJobs;
    }

    private void OnConnChanged(bool connected) => UpdateEngineStatus();

    private void OnDiagnosticLog(string msg) => Post(() =>
    {
        LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}";
        // 连接阶段：将引擎宿主诊断实时反映到状态栏——用户可见分步进度，
        // 而非干等"正在连接引擎…"数秒（参考 WinUI3 连接过程的逐步反馈体验）。
        if (EngineStatusState == EngineStatusState.Connecting)
        {
            // 提取 "[EngineHost] " 之后的正文作为简短状态文本
            var text = msg;
            const string tag = "[EngineHost] ";
            if (text.StartsWith(tag)) text = text[tag.Length..];
            // "!!" 开头的错误诊断保持原样展示
            EngineStatusText = text.StartsWith("!!")
                ? $"正在连接引擎·{text[3..].Trim()}"
                : $"正在连接引擎·{text}";
        }
    });

    private void UpdateEngineStatus() => Post(() =>
    {
        if (!AppServices.ToolsetReady)
        {
            EngineRingActive = false;
            EngineStatusText = LocalizationManager.Get("Lang.Eng.NotReady", "工具集未就绪 · 请到「设置」配置 DTS-HD_Tool 目录");
            EngineStatusState = EngineStatusState.NotReady;
            // 失败/未加载：红色 + ✗
            EngineStatusForeground = Brushes.OrangeRed;
            IsEngineConnected = false; IsEngineConnecting = false; IsEngineFailed = true;
        }
        else if (AppServices.Connected)
        {
            // 连接成功：绿字 + ✓，圆形加载图消失
            EngineRingActive = false;
            string lvl = AppServices.Host?.IsAuthorized == true
                ? (AppServices.Host.IsMaster ? "Master Audio Suite" : "Surround Audio Suite")
                : "DTS-HD";
            EngineStatusText = string.Format(LocalizationManager.Get("Lang.Eng.Connected", "引擎已连接 · {0}"), lvl);
            EngineStatusState = EngineStatusState.Connected;
            // 已连接：绿色
            EngineStatusForeground = Brushes.SeaGreen;
            IsEngineConnected = true; IsEngineConnecting = false; IsEngineFailed = false;
        }
        else
        {
            // 连接中：圆形加载图旋转 + 黄色文字
            EngineRingActive = true;
            EngineStatusText = LocalizationManager.Get("Lang.Eng.Connecting", "正在连接引擎…");
            EngineStatusState = EngineStatusState.Connecting;
            // 正在连接：黄色
            EngineStatusForeground = Brushes.Goldenrod;
            IsEngineConnected = false; IsEngineConnecting = true; IsEngineFailed = false;
        }
        // 防御性重订阅（Host/Queue 可能在 VM 构造后才可用）
        if (AppServices.Host != null)
        {
            AppServices.Host.ResponseParsed -= OnEngineResponse; AppServices.Host.ResponseParsed += OnEngineResponse;
            AppServices.Host.DiagnosticLog -= OnDiagnosticLog; AppServices.Host.DiagnosticLog += OnDiagnosticLog;
            AppServices.Host.ConnectionStateChanged -= OnConnChanged; AppServices.Host.ConnectionStateChanged += OnConnChanged;
        }
        if (AppServices.Queue != null) { AppServices.Queue.Changed -= RefreshJobs; AppServices.Queue.Changed += RefreshJobs; }
    });

    private void OnEngineResponse(DtsResponse r) => Post(() =>
    {
        switch (r.Kind)
        {
            case DtsResponseKind.License:
                Log($"<< 引擎授权码: {r.LicenseCode ?? "(空)"}（{(AppServices.Host?.IsAuthorized == true ? "已授权" : "未授权")}）");
                UpdateEngineStatus();
                break;
            case DtsResponseKind.JobStarted:
                Log($"<< [{r.Raw}] job={r.JobHandle} 编码开始");
                if (_aborted) break;  // 已终止：忽略引擎延迟消息
                OverallProgress = 0; SetProgress(EncodeProgressState.Working, "② 开始编码"); break;
            case DtsResponseKind.Pbr:
                Log($"<< [{r.Raw}] job={r.JobHandle} PBR 分析");
                if (_aborted) break;
                SetProgress(EncodeProgressState.Working, "② PBR 分析"); break;
            case DtsResponseKind.Progress:
                if (_aborted) break;  // 已终止：忽略进度消息，避免覆盖"已停止编码"
                if (r.Percent >= 0)
                {
                    _enginePercent = r.Percent;
                    if (r.Percent > _maxPct) _maxPct = r.Percent;
                }
                SetProgress(EncodeProgressState.Working, r.Percent == 100 ? "② 校验 MD5" : $"② 编码中… {Math.Max(r.Percent, 0)}%");
                break;
            case DtsResponseKind.Finished:
                if (r.ResultCode == 0) { OverallProgress = 100; SetProgress(EncodeProgressState.Completed, "✓ 编码完成"); Log($"<< job={r.JobHandle} 编码完成"); }
                else if (r.ResultCode == 1) { SetProgress(EncodeProgressState.Idle, "✗ 已停止编码"); Log($"<< job={r.JobHandle} 已终止"); }
                else { SetProgress(EncodeProgressState.Error, "✗ 出错: " + r.Text); Log($"<< job={r.JobHandle} 出错: {r.Text}"); }
                break;
            default:
                Log($"<< [{r.Raw}] job={r.JobHandle} pct={r.Percent} rc={r.ResultCode} {r.Text}");
                break;
        }
    });

    /// <summary>
    /// 将操作编组到 Avalonia UI 线程执行（对应 WinUI3 的 DispatcherQueue.GetForCurrentThread().TryEnqueue(...)）。
    /// 后台线程（引擎 socket 读取、轮询、WAV 头读取）回调均通过此入口刷新可绑定属性。
    /// </summary>
    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    private void SetProgress(EncodeProgressState state, string text)
    {
        ProgressState = state;
        ProgressText = text;
    }

    private void RefreshJobs() => Post(() =>
    {
        Jobs.Clear();
        if (AppServices.Queue == null) return;
        foreach (var j in AppServices.Queue.Jobs.Values.OrderBy(x => x.Handle)) Jobs.Add(j);
    });

    private void Log(string msg) => Post(() =>
    {
        LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}";
    });
}

/// <summary>
/// 单个声道输入行 VM：绑定到行模板（标签 / 文件 TextBox / Ch·Bw·Fs·Duration 显示 / 浏览按钮）。
/// FilePath 变化时回调父 VM 异步读取 WAV 头刷新显示。
/// </summary>
public sealed partial class ChannelRowViewModel : ObservableObject
{
    private readonly EncodePageViewModel _parent;
    public string Label { get; }

    public ChannelRowViewModel(string label, EncodePageViewModel parent)
    {
        Label = label;
        _parent = parent;
    }

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _channel = "-";
    [ObservableProperty] private string _bw = "-";
    [ObservableProperty] private string _fs = "-";
    [ObservableProperty] private string _duration = "-";
    [ObservableProperty] private double _seconds;
    [ObservableProperty] private int _bits;
    [ObservableProperty] private int _sampleRate;

    partial void OnFilePathChanged(string value) => _ = _parent.OnChannelPathChangedAsync(this);

    [RelayCommand]
    private async Task BrowseAsync() => await _parent.PickAndSetChannelFileAsync(Label);
}
