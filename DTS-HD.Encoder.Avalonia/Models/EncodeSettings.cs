namespace DTSHD.Encoder.Avalonia.Models;

/// <summary>
/// 编码设置模型 —— 汇总原版 Model/EncoderInfo XML 文档里被 getCommand() 与 ConfigTextFileWriter 引用的全部字段。
/// </summary>
public sealed class EncodeSettings
{
    // ---- 基本 ----
    public DestFormat DestFormat { get; set; } = DestFormat.BdPrimary;
    public StreamType StreamType { get; set; } = StreamType.MasterAudio;
    public ChannelLayout ChannelLayout { get; set; } = ChannelLayout.PA_51_L_R_C_LFE_Ls_Rs;
    public int SampleRate { get; set; } = 48000;       // SAMPLERATE
    public int BitWidth { get; set; } = 24;            // BITWIDTH (16/24)
    public int BitRate { get; set; } = 1509;           // LOSSY/LBR BITRATE (kbps)
    // 嵌入下混时 core 层（有损核心）独立的位宽/采样率（原 InputFilesPanel.getCoreBitWidth/SampleRate）
    public int CoreBitWidth { get; set; } = 24;
    public int CoreSampleRate { get; set; } = 48000;

    // ---- 输入 / 输出 ----
    /// 例如：每路输入文件按声道顺序拼成的命令片段（原版 inputfileorder）。
    public string InputFileOrder { get; set; } = "";
    public string ResidualData { get; set; } = "";
    public string SaveToDirectory { get; set; } = "";  // 末尾带分隔符
    public string SaveToFilename { get; set; } = "output";
    public string FileType { get; set; } = ".dtshd";   // .dtshd / .cpt / .wav
    public string ConfigFilePath { get; set; } = "";   // -f 指向的 .cfg

    // ---- 时码 ----
    public string FrameRate { get; set; } = "30";      // 23.976/24/25/29.97/29.97 Drop/30/30 Drop
    public string TimecodeStart { get; set; } = "00:00:00:00";
    public string TimecodeEnd { get; set; } = "00:00:00:00";
    public bool EncodeEntireFile { get; set; } = true;
    public string TimecodeEncodeFrom { get; set; } = "00:00:00:00";
    public string TimecodeEncodeTo { get; set; } = "00:00:00:00";
    public bool UseTimecodeReferenceTime { get; set; }
    public string TimecodeReferenceTime { get; set; } = "00:00:00:00";

    // ---- 编码类型 ----
    public bool IsLossless { get; set; } = true;       // isLosslessSelected
    public bool IsLbr { get; set; }                    // isLBRSelected
    public bool IsSecondaryAudio { get; set; }
    public bool Use9624 { get; set; }                  // ENABLE_CORE_X96_XLL_COMBO
    public bool EsPhaseShift { get; set; }
    public bool IsPremixed { get; set; }
    public bool AttenuateRearCh { get; set; }
    public bool UseExpressDialogMode { get; set; }
    public bool UseLFE { get; set; } = true;
    public bool UseWideRemapping { get; set; } = true;

    // ---- Dialog Normalization ----
    public bool UseDialogNormalization { get; set; }
    public int DialogNormalization { get; set; } = -31; // dialognormalizationnounits

    // ---- 媒体类型（写 MEDIA_TYPE）----
    public bool IsMediaTypeDvd { get; set; }
    public bool IsMediaTypeCd { get; set; }
    public bool IsMediaTypeBd { get; set; } = true;
    public bool IsMediaTypeBdSecondaryAudio { get; set; }
    public bool UseBdSecondaryAudioMetaData { get; set; }
    public bool IsDece { get; set; }
    public bool IsExpress { get; set; }

    // ---- 下混 ----
    public bool Use20Downmix { get; set; }
    public bool Use51Downmix { get; set; }
    public bool IsEmbeddedDownmix { get; set; }
    public bool Is2ChDmixLtRt { get; set; }
    public bool Is51Es { get; set; }
    public bool UseLegacyMatrix { get; set; }
    public bool UseCurrent { get; set; }
    public bool DownmixSaturationCheck { get; set; }
    // 2.0 下混系数（Lo/Ro，xcha/xchb），默认取 conf/downmix20.properties
    public DownmixStereoCoeffs Downmix20 { get; set; } = DownmixStereoCoeffs.Default();
    // 5.1 下混系数（primary/xcha/xchb）
    public Downmix51Coeffs Downmix51 { get; set; } = new();

    // ---- PBR ----
    // 默认开启 AutoPBR：无损编码时引擎据 -p 生成 .dtspbr（与官方一致）
    public bool AutoPbr { get; set; } = true;

    // 各声道输入文件（label→路径），用于生成官方格式日志的 INPUT FILES 段
    public System.Collections.Generic.Dictionary<string, string> InputFiles { get; set; } = new();

    // ---- 无缝分支 ----
    public bool SeamlessSingleClip { get; set; }
    public bool SeamlessCsvBranchPoints { get; set; }
    public string SeamlessCsvFilename { get; set; } = "";

    // ---- 主音频衰减 (-j -k -l) ----
    public bool UsePrimaryAudioAttenuation { get; set; }
    public string PaPrimary { get; set; } = "INF";    // paMetadataPrimaryValue
    public string PaFadeDown { get; set; } = "0";
    public string PaFadeUp { get; set; } = "0";

    // ---- AAF 元数据 ----
    public bool UsingAaf { get; set; }
    public bool UseAaf { get; set; }
    public string AafFilename { get; set; } = "";
    public AafAttenuation AafAttenuationKind { get; set; } = AafAttenuation.Unison;
    public string AafUnison { get; set; } = "0";
    public string AafLF { get; set; } = "0"; public string AafRF { get; set; } = "0";
    public string AafC { get; set; } = "0"; public string AafLS { get; set; } = "0";
    public string AafRS { get; set; } = "0"; public string AafLFE { get; set; } = "0";
    public bool AafPanningEnabled { get; set; }
    public bool AafPanningActive { get; set; }
    public string PanLF { get; set; } = "0"; public string PanRF { get; set; } = "0";
    public string PanC { get; set; } = "0"; public string PanLS { get; set; } = "0";
    public string PanRS { get; set; } = "0";

    // 单声道下混 metadata 值
    public string MonoMetadataL { get; set; } = "0"; public string MonoMetadataR { get; set; } = "0";
    public string MonoMetadataC { get; set; } = "0"; public string MonoMetadataLs { get; set; } = "0";
    public string MonoMetadataRs { get; set; } = "0"; public string MonoMetadataLFE { get; set; } = "INF";

    // ---- 程序信息 ----
    public string ProgramInfo { get; set; } = "";

    // 便捷属性
    public int MainChannelCount => ChannelLayouts.Get(ChannelLayout).MainChannels;
    public bool HasLFE => ChannelLayouts.Get(ChannelLayout).HasLFE;
    public string ConfiguratorKey => ChannelLayouts.Get(ChannelLayout).ConfiguratorKey;
    public string ChannelLayoutDisplay => ChannelLayouts.Get(ChannelLayout).Display;
    public bool IsMatrix => ChannelLayout is ChannelLayout.PA_60_ES_Matrix_L_R_C_Ls_Rs_Cs
        or ChannelLayout.PA_61_ES_Matrix_L_R_C_LFE_Ls_Rs_Cs;
    public bool IsLbrLayout => ChannelLayout is ChannelLayout.SA_10_C or ChannelLayout.SA_20_Lo_Ro
        or ChannelLayout.SA_51_L_R_C_LFE_Ls_Rs;
}

/// 2.0 下混系数（每个声道两条增益 a/b，单位 dB；INF 表示静音）
public sealed class DownmixStereoCoeffs
{
    // 默认与官方一致（dB 衰减为正值；INF=静音）
    public string LeftA = "3.0", LeftB = "INF";
    public string RightA = "INF", RightB = "3.0";
    public string CenterA = "6.0", CenterB = "6.0";
    public string LfeA = "INF", LfeB = "INF";
    public string LsA = "6.0", LsB = "INF";
    public string RsA = "INF", RsB = "6.0";

    public static DownmixStereoCoeffs Default() => new();
}

/// 5.1 下混系数（6.1/7.1 → 5.1）：每声道 primary + xcha(es) + xchb(esb)。默认与官方一致。
public sealed class Downmix51Coeffs
{
    public string LeftPrimary = "3.0", LeftXchA = "INF", LeftXchB = "INF";
    public string RightPrimary = "3.0", RightXchA = "INF", RightXchB = "INF";
    public string CenterPrimary = "3.0", CenterXchA = "INF", CenterXchB = "INF";
    public string LsPrimary = "3.0", LsXchA = "3.0", LsXchB = "INF";   // 后环左(es)→Ls
    public string RsPrimary = "3.0", RsXchA = "INF", RsXchB = "3.0";   // 后环右(esb)→Rs
    public string LfePrimary = "3.0", LfeXchA = "INF", LfeXchB = "INF";
}
