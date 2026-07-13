using System.Text;
using DTSHD.Encoder.Avalonia.Models;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 复刻 EncodeCommandEncodeEntry.getCommand()：把设置拼成发给引擎的命令串。
/// module 固定 "E"；cmd "E"=普通编码，"F"=文件夹批量。
/// </summary>
public static class DtsCommandBuilder
{
    public static string BuildEncodeCommand(EncodeSettings s, bool folderBased)
    {
        var b = new StringBuilder();
        b.Append("E ").Append(folderBased ? "F " : "E ");
        b.Append(s.InputFileOrder).Append(' ').Append(s.ResidualData).Append(' ');
        b.Append("-s").Append(s.FrameRate.Replace(' ', '_')).Append(' ');
        b.Append("-t").Append(s.TimecodeStart).Append(' ');
        b.Append("-u").Append(s.TimecodeEnd).Append(' ');
        if (s.EncodeEntireFile)
        {
            b.Append("-v").Append(s.TimecodeStart).Append(' ');
            b.Append("-w").Append(s.TimecodeEnd).Append(' ');
        }
        else
        {
            b.Append("-v").Append(s.TimecodeEncodeFrom).Append(' ');
            b.Append("-w").Append(s.TimecodeEncodeTo).Append(' ');
        }
        if (s.IsLossless && s.AutoPbr)
            b.Append("-p\"").Append(s.SaveToDirectory).Append(s.SaveToFilename).Append(".dtspbr\" ");
        if (s.SeamlessSingleClip)
            b.Append("-y ");
        if (s.SeamlessCsvBranchPoints)
            b.Append("-y\"").Append(s.SeamlessCsvFilename).Append("\" ");
        if (s.UsePrimaryAudioAttenuation)
        {
            b.Append("-j").Append(s.PaPrimary).Append(' ');
            b.Append("-k").Append(s.PaFadeDown).Append(' ');
            b.Append("-l").Append(s.PaFadeUp).Append(' ');
        }
        b.Append("-f\"").Append(s.ConfigFilePath).Append("\" ");
        if (s.UsingAaf)
        {
            b.Append("-a\"").Append(s.AafFilename).Append("\" ");
            if (s.AafAttenuationKind == AafAttenuation.Unison)
            {
                b.Append("-AS").Append(s.AafUnison).Append(' ');
            }
            else
            {
                b.Append("-AL").Append(s.AafLF).Append(' ');
                b.Append("-AR").Append(s.AafRF).Append(' ');
                b.Append("-AC").Append(s.AafC).Append(' ');
                b.Append("-AP").Append(s.AafLS).Append(' ');
                b.Append("-AQ").Append(s.AafRS).Append(' ');
                b.Append("-AB").Append(s.AafLFE).Append(' ');
            }
            if (s.AafPanningEnabled)
            {
                b.Append("-PL").Append(s.PanLF).Append(' ');
                b.Append("-PR").Append(s.PanRF).Append(' ');
                b.Append("-PC").Append(s.PanC).Append(' ');
                b.Append("-PP").Append(s.PanLS).Append(' ');
                b.Append("-PQ").Append(s.PanRS).Append(' ');
            }
        }
        if (folderBased)
            b.Append("-L").Append((int)s.ChannelLayout).Append(' ');
        b.Append("-o\"").Append(s.SaveToDirectory).Append(s.SaveToFilename).Append('"');
        return b.ToString();
    }

    public static string CancelCommand(int hJob, bool folderBased) => (folderBased ? "E c " : "E C ") + hJob;
    public static string MoveUp(int hJob) => "U " + hJob;
    public static string MoveDown(int hJob) => "D " + hJob;
    public static string Remove(int hJob) => "R " + hJob;
    public const string Shutdown = "X";
}
