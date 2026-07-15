using System;
using System.IO;
using DTSHD.Encoder.Avalonia.Models;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 多声道 WAV 声道重排：把 WAVE 标准声道顺序（按 dwChannelMask）重排为 DTS 引擎期望的
/// cfg CHANNEL 声明顺序。
///
/// 【根因】DtsJobQueue.exe 解析多声道 WAV 时，会读 fmt chunk 的 dwChannelMask 字段
/// 来确定声道顺序。标准 WAVE 7.1 的 dwChannelMask=0x63F 顺序为 L,R,C,LFE,BL,BR,SL,SR，
/// 而 DTS cfg 期望顺序为 L,R,Ls,Rs,C,LFE,Lsr,Rsr。如果不重排 data 并同时改写 fmt chunk，
/// 引擎会按 dwChannelMask 解释声道 → 位置 3-7 全部错位。
///
/// 【修复策略】
/// 1. 按 dwChannelMask 解析 WAVE 实际声道顺序
/// 2. 按 cfg 顺序重排 data 帧的声道交错
/// 3. **重写 fmt chunk 为 WAVE_FORMAT_PCM（0x0001）**，去掉 dwChannelMask
///    —— 这是关键！否则引擎仍按原 channel mask 解释，重排无效
///
/// 【支持范围】全部 31 种 DTS-HD 声道布局：1.0/2.0/3.0/3.1/4.0/4.1/5.0/5.1/
/// 6.0 ES/6.1 ES/7.0/7.1（含 Lw/Rw、Cs+Oh、Lhs/Rhs、Lh/Rh 等变体）。
/// 各布局的 cfg CHANNEL 声明顺序与 CfgWriter 完全一致。
///
/// 【Java 原版】不支持多声道单文件 WAV（仅 8 轨分立），从未触发此问题。
/// </summary>
public static class WavChannelRemapper
{
    // dwChannelMask bit 位定义（Microsoft WAVE 标准）
    // bit 0: SPEAKER_FRONT_LEFT           (L)
    // bit 1: SPEAKER_FRONT_RIGHT          (R)
    // bit 2: SPEAKER_FRONT_CENTER         (C)
    // bit 3: SPEAKER_LOW_FREQUENCY        (LFE)
    // bit 4: SPEAKER_BACK_LEFT            (BL/Lb/Lsr)
    // bit 5: SPEAKER_BACK_RIGHT           (BR/Rb/Rsr)
    // bit 6: SPEAKER_FRONT_LEFT_OF_CENTER (FLC)
    // bit 7: SPEAKER_FRONT_RIGHT_OF_CENTER(FRC)
    // bit 8: SPEAKER_BACK_CENTER          (BC/Cs)
    // bit 9: SPEAKER_SIDE_LEFT            (SL/Ls)
    // bit 10: SPEAKER_SIDE_RIGHT          (SR/Rs)
    // bit 11: SPEAKER_TOP_CENTER          (TC/Oh)
    // bit 12: SPEAKER_TOP_FRONT_LEFT      (TFL)
    // ...

    /// <summary>
    /// 根据 dwChannelMask 解析 WAVE 文件的实际声道顺序（返回声道标签列表）。
    /// 例如 mask=0x63F 返回 [L, R, C, LFE, BL, BR, SL, SR]（标准 7.1）。
    /// </summary>
    private static string[] GetWaveChannelOrder(uint channelMask, int channels)
    {
        // 按 bit 位顺序解析
        var labels = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 32 && labels.Count < channels; i++)
        {
            if ((channelMask & (1u << i)) != 0)
            {
                labels.Add(i switch
                {
                    0 => "L", 1 => "R", 2 => "C", 3 => "LFE",
                    4 => "BL", 5 => "BR", 6 => "FLC", 7 => "FRC",
                    8 => "BC", 9 => "SL", 10 => "SR",
                    11 => "TC", 12 => "TFL", 13 => "TFR", 14 => "TFC",
                    15 => "TBL", 16 => "TBR", 17 => "TBC",
                    _ => $"ch{i}",
                });
            }
        }
        // 如果 channelMask 为 0 或声道数不够（老 WAVE_FORMAT_PCM），假设是 Microsoft 标准顺序
        if (labels.Count < channels)
        {
            labels.Clear();
            var std = channels switch
            {
                1 => new[] { "L" },
                2 => new[] { "L", "R" },
                3 => new[] { "L", "R", "C" },
                4 => new[] { "L", "R", "C", "LFE" },
                5 => new[] { "L", "R", "C", "LFE", "BL" },
                6 => new[] { "L", "R", "C", "LFE", "BL", "BR" },  // 5.1 标准顺序
                7 => new[] { "L", "R", "C", "LFE", "BL", "BR", "BC" },  // 6.1 标准顺序
                8 => new[] { "L", "R", "C", "LFE", "BL", "BR", "SL", "SR" },  // 7.1 标准顺序
                _ => Array.Empty<string>(),
            };
            foreach (var s in std) labels.Add(s);
        }
        return labels.ToArray();
    }

    /// <summary>
    /// 根据 ChannelLayout 返回 DTS cfg 期望的 CHANNEL 声明顺序。
    /// 顺序与 CfgWriter 完全一致（即引擎读取 WAV 帧的声道交错顺序）。
    /// 各布局的顺序汇总（参考 CfgWriter.BuildXxx 方法）：
    ///   1.0 Mono:          L
    ///   2.0/2.1:           L, R, [LFE]
    ///   3.0 L,R,S:         L, R, [LFE], S(es)
    ///   3.0 L,C,R:         L, R, C, [LFE]
    ///   4.0 L,R,Ls,Rs:     L, R, Ls, Rs, [LFE]
    ///   4.0 L,C,R,S:       L, R, C, [LFE], S(es)
    ///   5.0/5.1:           L, R, Ls, Rs, C, [LFE]
    ///   6.0/6.1 ES:        L, R, Ls, Rs, C, [LFE], Cs(es)
    ///   7.0/7.1:           L, R, Ls, Rs, C, [LFE], + 两个 es/esb
    /// </summary>
    private static string[] GetCfgChannelOrder(ChannelLayout layout)
    {
        // es/esb 声道标签：按布局返回 cfg 中声明的第二扩展对（与 CfgWriter.Build71 一致）
        (string es, string esb) = GetEsEsbLabels(layout);

        return layout switch
        {
            // —— 1.0 ——
            ChannelLayout.SA_10_C or ChannelLayout.PA_10_C
                => new[] { "L" },
            // —— 1.1 ——
            ChannelLayout.PA_11_C_LFE
                => new[] { "L", "LFE" },
            // —— 2.0 ——
            ChannelLayout.SA_20_Lo_Ro or ChannelLayout.PA_20_Lo_Ro
                => new[] { "L", "R" },
            // —— 2.0 Lt,Rt ——
            ChannelLayout.PA_20_Lt_Rt
                => new[] { "Lt", "Rt" },
            // —— 2.1 ——
            ChannelLayout.PA_21_Lt_Rt_LFE
                => new[] { "Lt", "Rt", "LFE" },
            ChannelLayout.PA_21_L_R_LFE
                => new[] { "L", "R", "LFE" },
            // —— 3.0 L,R,S / 3.1 L,R,S,LFE ——
            ChannelLayout.PA_30_L_R_S or ChannelLayout.PA_31_L_R_S_LFE
                => HasLFE(layout) ? new[] { "L", "R", "LFE", "S" } : new[] { "L", "R", "S" },
            // —— 3.0 L,C,R / 3.1 L,C,R,LFE ——
            ChannelLayout.PA_30_L_C_R or ChannelLayout.PA_31_L_C_R_LFE
                => HasLFE(layout) ? new[] { "L", "R", "C", "LFE" } : new[] { "L", "R", "C" },
            // —— 4.0 L,R,Ls,Rs / 4.1 L,R,Ls,Rs,LFE ——
            ChannelLayout.PA_40_L_R_Ls_Rs or ChannelLayout.PA_41_L_R_Ls_Rs_LFE
                => HasLFE(layout) ? new[] { "L", "R", "Ls", "Rs", "LFE" } : new[] { "L", "R", "Ls", "Rs" },
            // —— 4.0 L,C,R,S / 4.1 L,C,R,S,LFE ——
            ChannelLayout.PA_40_L_C_R_S or ChannelLayout.PA_41_L_C_R_S_LFE
                => HasLFE(layout) ? new[] { "L", "R", "C", "LFE", "S" } : new[] { "L", "R", "C", "S" },
            // —— 5.0 / 5.1 (含 SA 5.1) ——
            ChannelLayout.PA_50_L_R_C_Ls_Rs or ChannelLayout.PA_51_L_R_C_LFE_Ls_Rs
                => HasLFE(layout) ? new[] { "L", "R", "Ls", "Rs", "C", "LFE" } : new[] { "L", "R", "Ls", "Rs", "C" },
            // —— 6.0/6.1 ES Matrix/Discrete ——
            ChannelLayout.PA_60_ES_Matrix_L_R_C_Ls_Rs_Cs or ChannelLayout.PA_61_ES_Matrix_L_R_C_LFE_Ls_Rs_Cs
                => HasLFE(layout) ? new[] { "L", "R", "Ls", "Rs", "C", "LFE", "Cs" } : new[] { "L", "R", "Ls", "Rs", "C", "Cs" },
            ChannelLayout.PA_60_ES_Discrete_L_R_C_Ls_Rs_Cs or ChannelLayout.PA_61_ES_Discrete_L_R_C_LFE_Ls_Rs_Cs
                => HasLFE(layout) ? new[] { "L", "R", "Ls", "Rs", "C", "LFE", "Cs" } : new[] { "L", "R", "Ls", "Rs", "C", "Cs" },
            // —— 7.0 / 7.1 (所有变体) ——
            _ => HasLFE(layout)
                ? new[] { "L", "R", "Ls", "Rs", "C", "LFE", es, esb }
                : new[] { "L", "R", "Ls", "Rs", "C", es, esb },
        };
    }

    /// <summary>布局是否含 LFE 声道（决定 cfg 顺序中是否包含 LFE）。</summary>
    private static bool HasLFE(ChannelLayout layout) =>
        ChannelLayouts.Get(layout).HasLFE;

    /// <summary>获取 7.1 布局的 es/esb 声道标签（与 CfgWriter.Build71 的 esDecl/esbDecl 一致）。
    /// es/esb 在 cfg 中位于 L,R,Ls,Rs,C,LFE 之后，是第 7/8 个声道。</summary>
    private static (string es, string esb) GetEsEsbLabels(ChannelLayout layout)
    {
        // 参考 CfgWriter.Build() 第 34-42 行的 case 映射
        return layout switch
        {
            // 7.1 - L, R, C, LFE, Ls, Rs, Lsr, Rsr
            ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Lsr_Rsr
                => ("Lsr", "Rsr"),
            // 7.1 - L, R, C, LFE, Ls, Rs, Lw, Rw
            ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Lw_Rw
                => ("Lw", "Rw"),
            // 7.1 - L, R, C, LFE, Ls, Rs, Cs, Oh
            ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Cs_Oh
                => ("Cs", "Oh"),
            // 7.1 - L, R, C, LFE, Ls, Rs, Cs, Ch
            ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Cs_Ch
                => ("Cs", "Ch"),
            // 7.1 - L, R, C, LFE, Ls, Rs, Lhs, Rhs
            ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Lhs_Rhs
                => ("Lhs", "Rhs"),
            // 7.1 - L, R, C, LFE, Ls, Rs, Lh, Rh
            ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Lh_Rh
                => ("Lh", "Rh"),
            // 7.1 - L, R, C, LFE, Lss, Rss, Lsr, Rsr (side surround)
            ChannelLayout.PA_71_L_R_C_LFE_Lss_Rss_Lsr_Rsr
                => ("Lsr", "Rsr"),
            // 7.0 - L, R, C, Lss, Rss, Lsr, Rsr
            ChannelLayout.PA_70_L_R_C_Lss_Rss_Lsr_Rsr
                => ("Lsr", "Rsr"),
            // 默认（未明确）：用 Lsr/Rsr
            _ => ("Lsr", "Rsr"),
        };
    }

    /// <summary>
    /// 根据 WAVE 声道顺序和 cfg 期望顺序，计算从 WAVE 取出索引到 cfg 放入索引的映射。
    /// 返回 cfg 顺序中每个位置对应的 WAVE 声道索引。
    /// </summary>
    private static int[] GetRemapOrder(string[] waveOrder, string[] cfgOrder)
    {
        var result = new int[cfgOrder.Length];
        for (int i = 0; i < cfgOrder.Length; i++)
        {
            string target = cfgOrder[i];
            int idx = FindChannel(waveOrder, target);

            // 退化匹配：cfg 标签在 WAVE 中找不到时，尝试语义等价的别名
            if (idx < 0)
            {
                idx = target switch
                {
                    // cfg Lsr/Rsr 对应 WAVE 的 BL/BR（后置左右）
                    "Lsr" => FindChannel(waveOrder, "BL"),
                    "Rsr" => FindChannel(waveOrder, "BR"),
                    // cfg Ls/Rs 对应 WAVE 的 SL/SR（侧环绕左右）
                    "Ls" => FindChannel(waveOrder, "SL"),
                    "Rs" => FindChannel(waveOrder, "SR"),
                    // cfg Cs 对应 WAVE 的 BC（后中置）
                    "Cs" => FindChannel(waveOrder, "BC"),
                    // cfg Oh 对应 WAVE 的 TC（顶中置）
                    "Oh" => FindChannel(waveOrder, "TC"),
                    _ => -1,
                };
            }
            result[i] = idx >= 0 ? idx : i;  // 找不到保持原序
        }
        return result;
    }

    /// <summary>在 waveOrder 中查找指定声道标签（大小写不敏感）。</summary>
    private static int FindChannel(string[] waveOrder, string label)
    {
        for (int i = 0; i < waveOrder.Length; i++)
        {
            if (string.Equals(waveOrder[i], label, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 检查 WAV 文件是否需要声道重排。
    /// </summary>
    /// <param name="wavPath">WAV 文件路径</param>
    /// <param name="layout">当前所选 DTS-HD 声道布局（决定 cfg 期望顺序）</param>
    public static bool NeedsRemap(string wavPath, ChannelLayout layout)
    {
        var info = WavInfo.TryRead(wavPath);
        if (info == null) return false;

        var cfgOrder = GetCfgChannelOrder(layout);
        if (cfgOrder.Length == 0) return false;
        // WAV 声道数必须与 cfg 期望声道数一致
        if (info.Channels != cfgOrder.Length) return false;

        var waveOrder = GetWaveChannelOrder(info.ChannelMask, info.Channels);
        if (waveOrder.Length != cfgOrder.Length) return false;

        var remap = GetRemapOrder(waveOrder, cfgOrder);
        for (int i = 0; i < remap.Length; i++)
            if (remap[i] != i) return true;
        return false;
    }

    /// <summary>
    /// 流式重排多声道 WAV，输出临时文件。
    /// 重排 data 并重写 fmt chunk 为 WAVE_FORMAT_PCM（去掉 dwChannelMask）。
    /// </summary>
    /// <param name="srcPath">源 WAV 文件路径</param>
    /// <param name="layout">当前所选 DTS-HD 声道布局（决定 cfg 期望顺序）</param>
    /// <param name="progress">进度回调（0-100）</param>
    public static string RemapToTempWav(string srcPath, ChannelLayout layout, Action<int>? progress = null)
    {
        var headerInfo = WavInfo.TryRead(srcPath) ?? throw new InvalidDataException("不是有效的 WAV 文件");

        int channels = headerInfo.Channels;
        var cfgOrder = GetCfgChannelOrder(layout);
        if (cfgOrder.Length == 0 || channels != cfgOrder.Length)
            return srcPath;  // WAV 声道数与 cfg 期望不匹配，原样返回让引擎报错

        var waveOrder = GetWaveChannelOrder(headerInfo.ChannelMask, channels);

        int[] order = GetRemapOrder(waveOrder, cfgOrder);
        bool needsRemap = false;
        for (int i = 0; i < order.Length; i++)
            if (order[i] != i) { needsRemap = true; break; }
        if (!needsRemap) return srcPath;

        int bitsPerSample = headerInfo.Bits > 0 ? headerInfo.Bits : 16;
        int bytesPerSample = bitsPerSample / 8;
        int blockAlign = channels * bytesPerSample;
        int sampleRate = headerInfo.SampleRate;
        string tmpPath = Path.Combine(Path.GetTempPath(), $"dts_remap_{Guid.NewGuid():N}.wav");

        // 大缓冲区：1MB / blockAlign 帧数（一次 IO 处理更多数据，减少 IO 次数）
        // 8 声道 24bit：blockAlign=24，framesPerBuffer ≈ 43690 帧 ≈ 1MB
        int framesPerBuffer = Math.Max(8192, 1_048_576 / blockAlign);
        using (var src = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            framesPerBuffer * blockAlign, FileOptions.SequentialScan))
        using (var br = new BinaryReader(src))
        using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None,
            framesPerBuffer * blockAlign, FileOptions.SequentialScan))
        using (var bw = new BinaryWriter(dst))
        {
            // ---- 1. 扫描所有 chunk 找 data 位置和大小 ----
            long dataPos = -1; long dataSize = 0;
            src.Seek(12, SeekOrigin.Begin);  // 跳过 RIFF header (12 bytes)
            while (src.Position + 8 <= src.Length)
            {
                uint chunkId = br.ReadUInt32();
                uint chunkSizeU = br.ReadUInt32();
                long chunkDataPos = src.Position;
                long chunkSize = (long)chunkSizeU;
                if (chunkId == 0x61746164)  // "data"
                {
                    dataPos = chunkDataPos;
                    // 处理大文件：chunkSize=0 或 0xFFFFFFFF 时用剩余文件长度
                    if (chunkSize == 0 || chunkSizeU == 0xFFFFFFFF)
                        dataSize = src.Length - chunkDataPos;
                    else
                        dataSize = chunkSize;
                }
                long next = chunkDataPos + chunkSize + (chunkSize & 1);
                if (next <= chunkDataPos || next > src.Length) break;
                src.Seek(next, SeekOrigin.Begin);
            }

            if (dataPos < 0) throw new InvalidDataException("找不到 data chunk");

            // ---- 2. 写新 WAV ----
            int newFmtChunkSize = 16;  // PCM fmt chunk 固定 16 字节
            long newRiffSize = 4 + (8 + newFmtChunkSize) + (8 + dataSize);
            // 写 RIFF header
            bw.Write(0x46464952u);         // "RIFF"
            bw.Write((int)newRiffSize);
            bw.Write(0x45564157u);         // "WAVE"

            // 写 fmt chunk（WAVE_FORMAT_PCM，无扩展头）
            bw.Write(0x20746D66u);         // "fmt "
            bw.Write((uint)newFmtChunkSize);
            bw.Write((ushort)1);           // wFormatTag = WAVE_FORMAT_PCM
            bw.Write((ushort)channels);
            bw.Write((uint)sampleRate);
            bw.Write((uint)(sampleRate * blockAlign));  // nAvgBytesPerSec
            bw.Write((ushort)blockAlign);
            bw.Write((ushort)bitsPerSample);

            // 写 data chunk 头
            bw.Write(0x61746164u);         // "data"
            // 大文件支持：超过 4GB 时写 0xFFFFFFFF，引擎按文件大小处理
            bw.Write(dataSize > uint.MaxValue ? uint.MaxValue : (uint)dataSize);

            // ---- 3. 流式重排 data ----
            src.Seek(dataPos, SeekOrigin.Begin);
            byte[] readBuf = new byte[framesPerBuffer * blockAlign];
            byte[] writeBuf = new byte[framesPerBuffer * blockAlign];
            long remaining = dataSize;
            long processed = 0;
            long framesProcessed = 0;
            // 进度回调节流：仅在整 2% 边界触发，避免高频 UI 调度
            // 大文件（3GB @ 1MB buffer = 3000 次循环）若每次回调会触发 3000 次 Post → UI 淹没
            int lastPct = -1;

            while (remaining > 0)
            {
                int toRead = (int)Math.Min(readBuf.Length, remaining);
                // 填满缓冲区到整帧边界：FileStream.Read 允许返回少于请求的字节，
                // 若不循环填满，最后剩下的“半帧”字节会被丢弃且流位置已推进 →
                // 后续所有帧声道整体错位、输出损坏。这里循环读满 toRead（整帧数），
                // 只有真正 EOF（文件被截断）才提前退出。
                int read = 0;
                while (read < toRead)
                {
                    int n = src.Read(readBuf, read, toRead - read);
                    if (n == 0) break;   // EOF：文件被截断，处理已读到的整帧后结束
                    read += n;
                }
                if (read == 0) break;
                // 处理非整帧数据（仅在被截断的文件末尾可能出现，丢弃不完整帧）
                int framesRead = read / blockAlign;
                int actualRead = framesRead * blockAlign;

                for (int f = 0; f < framesRead; f++)
                {
                    int srcFrameOff = f * blockAlign;
                    int dstFrameOff = f * blockAlign;
                    for (int c = 0; c < channels; c++)
                    {
                        int srcCh = order[c];
                        Buffer.BlockCopy(readBuf, srcFrameOff + srcCh * bytesPerSample,
                                         writeBuf, dstFrameOff + c * bytesPerSample,
                                         bytesPerSample);
                    }
                }
                bw.Write(writeBuf, 0, actualRead);
                remaining -= actualRead;
                processed += actualRead;
                framesProcessed += framesRead;

                // 节流：仅整 2% 边界回调一次
                int pct = (int)(processed * 100 / dataSize);
                if (pct != lastPct && (pct & 1) == 0)  // 偶数百分点（2% 步进）
                {
                    progress?.Invoke(pct);
                    lastPct = pct;
                }
            }
            // 最终确保 100% 触发一次
            progress?.Invoke(100);
        }

        return tmpPath;
    }

    /// <summary>
    /// 获取 WAV 文件声道顺序的诊断信息（用于日志）。
    /// </summary>
    /// <param name="wavPath">WAV 文件路径</param>
    /// <param name="layout">当前所选 DTS-HD 声道布局</param>
    public static string GetDiagnostic(string wavPath, ChannelLayout layout)
    {
        var info = WavInfo.TryRead(wavPath);
        if (info == null) return "解析失败";
        var waveOrder = GetWaveChannelOrder(info.ChannelMask, info.Channels);
        var cfgOrder = GetCfgChannelOrder(layout);
        var remap = GetRemapOrder(waveOrder, cfgOrder);
        return $"channels={info.Channels}, formatTag=0x{info.FormatTag:X4}, channelMask=0x{info.ChannelMask:X}, " +
               $"layout={layout}, waveOrder=[{string.Join(",", waveOrder)}], cfgOrder=[{string.Join(",", cfgOrder)}], " +
               $"remap=[{string.Join(",", remap)}]";
    }
}
