using System.Collections.Generic;
using System.Text;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// DTS-HD StreamTools 命令（模块 "T"）。由原生 DTSToolFramewrk.exe 处理（localhost:4442）。
/// 命令格式逆向自官方 DTSTools.jar 各 *Panel.getCommandParameters()。
/// 响应：`T {Op} S`=开始，`T {Op} E`=结束，`T {Op} C {code} [msg]`=完成（code=0 表示成功）。
/// </summary>
public static class ToolsCommands
{
    // InfoPanel: T I "file"
    public static string Info(string file) => $"T I \"{file}\"";

    // SplitPanel: T S "in" "splitTC" "out1.dtshd" "out2.dtshd"
    public static string Split(string input, string splitTc, string out1, string out2)
        => $"T S \"{input}\" \"{splitTc}\" \"{out1}\" \"{out2}\"";

    // JoinPanel: T J "out.dtshd" "file1" "file2"
    public static string Join(string output, string file1, string file2)
        => $"T J \"{output}\" \"{file1}\" \"{file2}\"";

    // TrimPanel: T T "in" "startTC" "endTC" "out.dtshd"
    public static string Trim(string input, string startTc, string endTc, string output)
        => $"T T \"{input}\" \"{startTc}\" \"{endTc}\" \"{output}\"";

    // AddSilencePanel: T N "out.dtshd" "in" "headTC" "tailTC" "newTCStart"  （输出在前！）
    public static string AddSilence(string output, string input, string headSilence, string tailSilence, string newTcStart)
        => $"T N \"{output}\" \"{input}\" \"{headSilence}\" \"{tailSilence}\" \"{newTcStart}\"";

    // RestripePanel: T R "file" "newStartTC" "newFrameRate"
    public static string Restripe(string file, string newStartTc, string newFrameRate)
        => $"T R \"{file}\" \"{newStartTc}\" \"{newFrameRate}\"";

    // MetadataPanel: T M "file" "newStartTC" "newFrameRate" "newDialNorm"
    public static string Metadata(string file, string newStartTc, string newFrameRate, string newDialNorm)
        => $"T M \"{file}\" \"{newStartTc}\" \"{newFrameRate}\" \"{newDialNorm}\"";

    // AppendPanel: T A "out.dtshd" "file1" "start1" "end1" "file2" "start2" "end2" ...
    public static string Append(string output, IEnumerable<(string File, string Start, string End)> rows)
    {
        var sb = new StringBuilder($"T A \"{output}\"");
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.File)) continue;
            sb.Append($" \"{r.File}\" \"{r.Start}\" \"{r.End}\"");
        }
        return sb.ToString();
    }

    // PBRPanel: T P "file" "file.dtspbr"
    public static string Pbr(string file, string pbrOut) => $"T P \"{file}\" \"{pbrOut}\"";

    // 读取文件元数据用于回填（Restripe/Metadata 面板）：T F "file"
    public static string FileMetadata(string file) => $"T F \"{file}\"";

    // 请求文件时间码：T C "file" "fps"
    public static string RequestTimecode(string file, string fps) => $"T C \"{file}\" \"{fps}\"";

    public const string Cancel = "T Z";
}
