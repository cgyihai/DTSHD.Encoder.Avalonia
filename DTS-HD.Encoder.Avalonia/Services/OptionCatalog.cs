using System.Collections.Generic;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 选项静态目录。值来自 conf/*.properties 与标准 DTS 取值。
/// 注意：DTSEncConfig.dll 的导出是 JNI 函数（需 JVM），无法直接 P/Invoke，
/// 故合法组合在此静态维护；后续可用一次性 JNI dump 替换为精确数据。
/// </summary>
public static class OptionCatalog
{
    // conf/framerates.properties
    public static readonly IReadOnlyList<string> FrameRates = new[]
    { "23.976", "24", "25", "29.97", "29.97 Drop", "30", "30 Drop" };

    // conf/encodetypes.properties
    public static readonly IReadOnlyList<string> EncodeTypes = new[]
    { "Lossless and Core", "Lossless Only", "Core Encode", "LBR" };

    // conf/coretypes.properties
    public static readonly IReadOnlyList<string> CoreTypes = new[]
    { "5.1", "5.1 Matrixed", "6.1" };

    // conf/filetypes.properties
    public static readonly IReadOnlyList<string> FileTypes = new[] { ".dtshd", ".cpt" };

    // 常见采样率（48k 系 & 96k 系）
    public static readonly IReadOnlyList<int> SampleRates = new[] { 44100, 48000, 88200, 96000, 176400, 192000 };

    public static readonly IReadOnlyList<int> BitWidths = new[] { 16, 24 };

    // 常见核心码率 (kbps)
    public static readonly IReadOnlyList<int> CoreBitRates = new[]
    { 768, 960, 1152, 1280, 1411, 1509 };

    // LBR 码率 (kbps)
    public static readonly IReadOnlyList<int> LbrBitRates = new[] { 192, 256, 320, 384, 448, 512 };

    // conf/dialognormalization.properties: -31(无衰减)~ -1 dBFS
    public static IReadOnlyList<int> DialogNormalization
    {
        get
        {
            var list = new List<int>();
            for (int v = -31; v <= -1; v++) list.Add(v);
            return list;
        }
    }
}
