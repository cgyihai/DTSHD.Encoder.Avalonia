using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DTSHD.Encoder.Avalonia.Models;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 生成与官方一致的编码日志（.dtshd_log.txt）设置头部分，
/// 精确移植自 com.dts.encoder.ui.EncoderInfo.createSettingsSnapshotFile。
/// 引擎的输出（饱和/MD5/码率/PBR max/Encode completed）会接在头部之后，
/// 组成与官方完全相同结构的日志。
/// </summary>
public static class EncodeLogWriter
{
    private const string Sep54 = "------------------------------------------------------";
    private const string Sep52 = "----------------------------------------------------";

    // label → 官方角色名
    private static readonly Dictionary<string, string> RoleNames = new()
    {
        ["L"] = "Left", ["R"] = "Right", ["C"] = "Center", ["LFE"] = "Low Frequency Effects",
        ["Lss"] = "Left Side Surround", ["Rss"] = "Right Side Surround",
        ["Ls"] = "Left Surround", ["Rs"] = "Right Surround",
        ["Lsr"] = "Left Surround Rear", ["Rsr"] = "Right Surround Rear",
        ["S"] = "Surround", ["Cs"] = "Center Surround",
        ["Lt"] = "Left Total", ["Rt"] = "Right Total",
        ["Lh"] = "Left High", ["Rh"] = "Right High", ["Lhs"] = "Left High Side", ["Rhs"] = "Right High Side",
        ["Lw"] = "Left Wide", ["Rw"] = "Right Wide", ["Oh"] = "Overhead", ["Ch"] = "Center High",
    };
    // INPUT FILES 段落里官方的输出顺序
    private static readonly string[] RoleOrder =
        { "Lt", "Rt", "L", "R", "C", "LFE", "Lss", "Ls", "Rss", "Rs", "S", "Cs", "Lsr", "Lhs", "Lh", "Lw", "Rsr", "Rhs", "Rh", "Rw", "Oh", "Ch" };

    private static string MasVersion(string toolDir)
    {
        try
        {
            foreach (var line in File.ReadAllLines(Path.Combine(toolDir, "conf", "about.properties")))
                if (line.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                    return line["version=".Length..].Trim();
        }
        catch { }
        return "2.60.22";
    }

    private static string MediaTypeDisplay(EncodeSettings s) => s.DestFormat switch
    {
        DestFormat.BdPrimary => "Blu-ray Disc (.dtshd)",
        DestFormat.BdSecondary => "BD Secondary Audio (.dtshd)",
        DestFormat.Dvd => "DVD (.cpt)",
        DestFormat.DtsCd => "DTS Music Disc (.wav)",
        DestFormat.Dece => "Digital Delivery (.dtshd)",
        _ => "Blu-ray Disc (.dtshd)",
    };

    private static string ProductType(EncodeSettings s) =>
        s.IsLbr ? "DTS Express" : s.IsLossless ? "DTS-HD Master Audio" : "DTS Digital Surround";

    /// <summary>生成官方格式的日志头（不含引擎输出部分）。</summary>
    public static string BuildHeader(EncodeSettings s, string toolDir)
    {
        var info = ChannelLayouts.Get(s.ChannelLayout);
        string disp = info.Display;
        string disp4 = (s.IsLossless) ? disp.Replace(" ES ", " ") : disp;
        bool is7x = disp.StartsWith("7");
        var w = new StringBuilder();
        void L(string line = "") => w.Append(line).Append("\r\n");
        string F22(string k, string v) => k.PadRight(22) + "= " + v;

        L("****************************************");
        L("MAS Version Number = " + MasVersion(toolDir));
        L(); L();
        L("AUDIO INPUT SETTINGS");
        L(Sep54);
        L(F22("Media Type", MediaTypeDisplay(s)));
        L(F22("Product Type", ProductType(s)));
        L(F22("Bit Rate", s.BitRate > 0 ? $"{s.BitRate} kbps" : "No Core"));
        L(F22("Channel Layout", disp4));
        L(F22("Bit Width", s.BitWidth.ToString()));
        if (s.UseDialogNormalization)
            L(F22("DialNorm", $"{s.DialogNormalization} dBFS" + (s.DialogNormalization == -31 ? " (No Attenuation)" : "")));
        else
            L("Not using Dialog Normalization");
        L(F22("Sample Rate", $"{s.SampleRate / 1000} kHz"));
        L(F22("-3db Rear Attenuation", s.AttenuateRearCh.ToString().ToLowerInvariant()));
        L(F22("ES Phase Shift", s.EsPhaseShift.ToString().ToLowerInvariant()));
        L(F22("ES Pre-Mixed", s.IsPremixed.ToString().ToLowerInvariant()));
        L(F22("Using 96/24 Core", s.Use9624.ToString().ToLowerInvariant()));
        L();
        L("INPUT FILES");
        L(Sep54);
        foreach (var lab in RoleOrder)
            if (s.InputFiles.TryGetValue(lab, out var path) && RoleNames.TryGetValue(lab, out var role))
                L(role.PadRight(26) + "= " + path);
        if (s.InputFiles.TryGetValue("*", out var single))
            L("Input".PadRight(26) + "= " + single);
        L();
        L("BITSTREAM SETTINGS");
        L(Sep54);
        L("Program Info                                     = ");
        if (!string.IsNullOrEmpty(s.ProgramInfo))
        {
            L(); L(s.ProgramInfo); L(); L("----------------------------------");
        }
        L("Enable Remapping                                 = " + s.UseWideRemapping.ToString().ToLowerInvariant());
        L();
        L("TIME CODE SETTINGS");
        L(Sep54);
        L(F22("Frame Rate", s.FrameRate));
        L(F22("Encode Entire File", s.EncodeEntireFile.ToString().ToLowerInvariant()));
        L(F22("Start Time", s.TimecodeStart));
        L(F22("End   Time", s.TimecodeEnd));
        if (!s.EncodeEntireFile)
        {
            L(F22("Encode From", s.TimecodeEncodeFrom));
            L(F22("Encode To", s.TimecodeEncodeTo));
        }
        L(F22("Use Reference", s.UseTimecodeReferenceTime.ToString().ToLowerInvariant()));
        if (s.UseTimecodeReferenceTime) L(F22("Reference Time", s.TimecodeReferenceTime));
        L();
        L("OUTPUT LOCATION");
        L(Sep54);
        L(F22("Directory", s.SaveToDirectory));
        L(F22("Filename", s.SaveToFilename + s.FileType));
        L();
        L("DOWNMIX SETTINGS");
        L(Sep54);
        WriteDownmix(L, s, info, is7x);
        L();
        L(Sep52);
        L();
        return w.ToString();
    }

    private static void WriteDownmix(Action<string> L, EncodeSettings s, ChannelLayoutInfo info, bool is7x)
    {
        var c = s.Downmix51;
        if (s.UseWideRemapping)
        {
            L("Using 7.1 Wide Remapping Downmix Settings");
        }
        else if (s.Use51Downmix && !s.UseLegacyMatrix)
        {
            L("\t5.1 Downmix Settings");
            L("\t" + Sep52);
            L("\t      \t\tScale \t\t    \t\t    ");
            L("\tInput \t\tFactor\t\tXCH1" + (is7x ? "\t\tXCH2" : ""));
            L("\t------\t\t------\t\t---" + (is7x ? "\t\t----" : ""));
            void Row(string name, string p, string a, string b) =>
                L($"\t{name}\t\t{p}\t\t{a}" + (is7x ? $"\t\t{b}" : ""));
            Row("Left", c.LeftPrimary, c.LeftXchA, c.LeftXchB);
            Row("Right", c.RightPrimary, c.RightXchA, c.RightXchB);
            Row("Center", c.CenterPrimary, c.CenterXchA, c.CenterXchB);
            if (info.HasLFE) Row("LFE", c.LfePrimary, c.LfeXchA, c.LfeXchB);
            Row("Ls", c.LsPrimary, c.LsXchA, c.LsXchB);
            Row("Rs", c.RsPrimary, c.RsXchA, c.RsXchB);
            L("");   // 官方：5.1 表末尾有一空行（"2.0 Downmix Not Enabled" 前的空行来源于此）
        }
        else if (s.UseLegacyMatrix)
        {
            L(info.HasLFE ? "Using Legacy 5.1 Downmix Matrix" : "Using Legacy 5.0 Downmix Matrix");
        }
        else
        {
            L("5.x Downmix Not Enabled");
        }

        // —— 2.0 下混段（移植自官方 createSettingsSnapshotFile 的 2.0 表）——
        // 注意：未启用时不加前导空行（官方该空行只来自上面的 5.1 表）。
        if (s.Use20Downmix)
            Write20Table(L, s, info);
        else
            L("2.0 Downmix Not Enabled");
    }

    private static void Write20Table(Action<string> L, EncodeSettings s, ChannelLayoutInfo info)
    {
        var d = s.Downmix20;
        bool lfe = info.HasLFE;
        bool embedded = s.IsEmbeddedDownmix || s.UseBdSecondaryAudioMetaData || s.UseAaf;
        L("");
        L("\t2.0 Downmix Settings");
        L("\t" + Sep52);
        if (embedded)
        {
            L("\t2.0 Downmix Embedded");
            L("\t      \t\tScale \t\t    \t\t    ");
            L("\tInput \t\tFactor" + (lfe ? "\t\tCenter \t\tLFE  " : "\t\tCenter ") + "\t\tLs  \t\tRs");
            L("\t------\t\t------\t\t----\t\t----\t\t---  " + (lfe ? "\t\t------  " : ""));
            L("\tLeft \t\t" + d.LeftA + (lfe ? "\t\t" + d.CenterA + "\t\t" + d.LfeA : "\t\t" + d.CenterA) + "\t\t" + d.LsA + "\t\t" + d.RsA);
            L("\tRight\t\t" + d.RightB + (lfe ? "\t\t" + d.CenterB + "\t\t" + d.LfeB + "  " : "\t\t" + d.CenterB) + "\t\t" + d.LsB + "\t\t" + d.RsB);
        }
        else
        {
            L(s.Is2ChDmixLtRt ? "\tLtRt 2.0 Downmix" : "\tLoRo 2.0 Downmix");
            string r5 = s.Is2ChDmixLtRt ? "\tLt \t\t" : "\tLo \t\t";
            string r6 = s.Is2ChDmixLtRt ? "\tRt \t\t" : "\tRo \t\t";
            L("\t2.0 Downmix Not Embedded");
            L("");
            L("\tInput \t\tLeft \t\tRight" + (lfe ? "\t\tCenter \t\tLFE  " : "\t\tCenter ") + "\t\tLs  \t\tRs ");
            L("\t------\t\t------\t\t----\t\t----\t\t---" + (lfe ? "\t\t-----" : "") + "\t\t------");
            L(r5 + d.LeftA + "\t\t" + d.RightA + (lfe ? "\t\t" + d.CenterA + "\t\t" + d.LfeA : "\t\t" + d.CenterA) + "\t\t" + d.LsA + "\t\t" + d.RsA);
            L(r6 + d.LeftB + "\t\t" + d.RightB + (lfe ? "\t\t" + d.CenterB + "\t\t" + d.LfeB : "\t\t" + d.CenterB) + "\t\t" + d.LsB + "\t\t" + d.RsB);
        }
    }
}
