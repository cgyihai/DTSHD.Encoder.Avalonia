using System.Collections.Generic;
using System.Linq;

namespace DTSHD.Encoder.Avalonia.Models;

/// <summary>
/// 声道布局，symbolic id 与原版 Constants.ChannelLayout 完全一致（getCommand 的 -L 用此 id）。
/// </summary>
public enum ChannelLayout
{
    SA_10_C = 1,
    SA_20_Lo_Ro = 2,
    SA_51_L_R_C_LFE_Ls_Rs = 3,
    PA_10_C = 4,
    PA_11_C_LFE = 5,
    PA_20_Lt_Rt = 6,
    PA_20_Lo_Ro = 7,
    PA_21_Lt_Rt_LFE = 8,
    PA_21_L_R_LFE = 9,
    PA_30_L_R_S = 10,
    PA_30_L_C_R = 11,
    PA_31_L_R_S_LFE = 12,
    PA_31_L_C_R_LFE = 13,
    PA_40_L_C_R_S = 14,
    PA_40_L_R_Ls_Rs = 15,
    PA_41_L_C_R_S_LFE = 16,
    PA_41_L_R_Ls_Rs_LFE = 17,
    PA_50_L_R_C_Ls_Rs = 18,
    PA_51_L_R_C_LFE_Ls_Rs = 19,
    PA_60_ES_Matrix_L_R_C_Ls_Rs_Cs = 20,
    PA_61_ES_Matrix_L_R_C_LFE_Ls_Rs_Cs = 21,
    PA_60_ES_Discrete_L_R_C_Ls_Rs_Cs = 22,
    PA_61_ES_Discrete_L_R_C_LFE_Ls_Rs_Cs = 23,
    PA_70_L_R_C_Lss_Rss_Lsr_Rsr = 24,
    PA_71_L_R_C_LFE_Ls_Rs_Lw_Rw = 25,
    PA_71_L_R_C_LFE_Ls_Rs_Cs_Oh = 26,
    PA_71_L_R_C_LFE_Ls_Rs_Cs_Ch = 27,
    PA_71_L_R_C_LFE_Ls_Rs_Lsr_Rsr = 28,
    PA_71_L_R_C_LFE_Ls_Rs_Lhs_Rhs = 29,
    PA_71_L_R_C_LFE_Ls_Rs_Lh_Rh = 30,
    PA_71_L_R_C_LFE_Lss_Rss_Lsr_Rsr = 31,
}

public enum StreamType
{
    Express = 1,            // DTS-HD LBR
    DigitalSurround = 2,
    DigitalSurround9624 = 3,
    DigitalSurroundEs = 4,
    HiRes = 5,
    MasterAudio = 6,
}

public enum DestFormat
{
    BdPrimary = 1,          // Blu-ray Disc (.dtshd)
    BdSecondary = 2,        // BD Secondary Audio (.dtshd)
    Dvd = 3,                // DVD (.cpt)
    DtsCd = 4,              // DTS Music Disc (.wav)
    Dece = 5,               // Digital Delivery (.dtshd)
}

public enum AafAttenuation { Unison, Independent }

public sealed record ChannelLayoutInfo(
    ChannelLayout Layout, int MainChannels, bool HasLFE, string Display,
    string PropertiesFile, string ConfiguratorKey);

public static class ChannelLayouts
{
    public static readonly IReadOnlyList<ChannelLayoutInfo> All = new[]
    {
        new ChannelLayoutInfo(ChannelLayout.SA_10_C, 1, false, "1.0 - Mono (LBR)", "C_LBR.properties", "C"),
        new ChannelLayoutInfo(ChannelLayout.SA_20_Lo_Ro, 2, false, "2.0 - Stereo (LBR)", "L_R_LBR.properties", "L_R"),
        new ChannelLayoutInfo(ChannelLayout.SA_51_L_R_C_LFE_Ls_Rs, 5, true, "5.1 - L, R, C, LFE, Ls, Rs (LBR)", "L_R_C_LFE_Ls_Rs_LBR.properties", "L_C_R_Ls_Rs"),
        new ChannelLayoutInfo(ChannelLayout.PA_10_C, 1, false, "1.0 - C (Mono)", "C.properties", "C"),
        new ChannelLayoutInfo(ChannelLayout.PA_11_C_LFE, 1, true, "1.1 - C, LFE", "C_LFE.properties", "C"),
        new ChannelLayoutInfo(ChannelLayout.PA_20_Lt_Rt, 2, false, "2.0 - Lt, Rt", "Lt_Rt.properties", "Lt_Rt"),
        new ChannelLayoutInfo(ChannelLayout.PA_20_Lo_Ro, 2, false, "2.0 - L, R", "L_R.properties", "L_R"),
        new ChannelLayoutInfo(ChannelLayout.PA_21_Lt_Rt_LFE, 2, true, "2.1 - Lt, Rt, LFE", "Lt_Rt.properties", "Lt_Rt"),
        new ChannelLayoutInfo(ChannelLayout.PA_21_L_R_LFE, 2, true, "2.1 - L, R, LFE", "L_R_LFE.properties", "L_R"),
        new ChannelLayoutInfo(ChannelLayout.PA_30_L_R_S, 3, false, "3.0 - L, R, S", "L_R_S.properties", "L_R_S"),
        new ChannelLayoutInfo(ChannelLayout.PA_30_L_C_R, 3, false, "3.0 - L, C, R", "L_C_R.properties", "L_C_R"),
        new ChannelLayoutInfo(ChannelLayout.PA_31_L_R_S_LFE, 3, true, "3.1 - L, R, S, LFE", "L_R_S_LFE.properties", "L_R_S"),
        new ChannelLayoutInfo(ChannelLayout.PA_31_L_C_R_LFE, 3, true, "3.1 - L, C, R, LFE", "L_C_R_LFE.properties", "L_C_R"),
        new ChannelLayoutInfo(ChannelLayout.PA_40_L_C_R_S, 4, false, "4.0 - L, C, R, S", "L_C_R_S.properties", "L_C_R_S"),
        new ChannelLayoutInfo(ChannelLayout.PA_40_L_R_Ls_Rs, 4, false, "4.0 - L, R, Ls, Rs", "L_R_Ls_Rs.properties", "L_R_Ls_Rs"),
        new ChannelLayoutInfo(ChannelLayout.PA_41_L_C_R_S_LFE, 4, true, "4.1 - L, C, R, S, LFE", "L_C_R_S_LFE.properties", "L_C_R_S"),
        new ChannelLayoutInfo(ChannelLayout.PA_41_L_R_Ls_Rs_LFE, 4, true, "4.1 - L, R, Ls, Rs, LFE", "L_R_Ls_Rs_LFE.properties", "L_R_Ls_Rs"),
        new ChannelLayoutInfo(ChannelLayout.PA_50_L_R_C_Ls_Rs, 5, false, "5.0 - L, R, C, Ls, Rs", "L_R_C_Ls_Rs.properties", "L_C_R_Ls_Rs"),
        new ChannelLayoutInfo(ChannelLayout.PA_51_L_R_C_LFE_Ls_Rs, 5, true, "5.1 - L, R, C, LFE, Ls, Rs", "L_R_C_LFE_Ls_Rs.properties", "L_C_R_Ls_Rs"),
        new ChannelLayoutInfo(ChannelLayout.PA_60_ES_Matrix_L_R_C_Ls_Rs_Cs, 6, false, "6.0 ES Matrix - L, R, C, Ls, Rs, Cs", "L_R_C_Ls_Rs_Cs_60_ES_Matrix.properties", "L_C_R_Ls_Rs_Cs_ES_Matrix"),
        new ChannelLayoutInfo(ChannelLayout.PA_61_ES_Matrix_L_R_C_LFE_Ls_Rs_Cs, 6, true, "6.1 ES Matrix - L, R, C, LFE, Ls, Rs, Cs", "L_R_C_LFE_Ls_Rs_Cs_61_ES_Matrix.properties", "L_C_R_Ls_Rs_Cs_ES_Matrix"),
        new ChannelLayoutInfo(ChannelLayout.PA_60_ES_Discrete_L_R_C_Ls_Rs_Cs, 6, false, "6.0 ES Discrete - L, R, C, Ls, Rs, Cs", "L_R_C_Ls_Rs_Cs_60_ES_Matrix.properties", "L_C_R_Ls_Rs_Cs_ES_Discrete"),
        new ChannelLayoutInfo(ChannelLayout.PA_61_ES_Discrete_L_R_C_LFE_Ls_Rs_Cs, 6, true, "6.1 ES Discrete - L, R, C, LFE, Ls, Rs, Cs", "L_R_C_LFE_Ls_Rs_Cs_61_ES_Discrete.properties", "L_C_R_Ls_Rs_Cs_ES_Discrete"),
        new ChannelLayoutInfo(ChannelLayout.PA_70_L_R_C_Lss_Rss_Lsr_Rsr, 7, false, "7.0 - L, R, C, Lss, Rss, Lsr, Rsr", "L_R_C_Lss_Rss_Lsr_Rsr.properties", "L_C_R_Lss_Rss_Lsr_Rsr"),
        new ChannelLayoutInfo(ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Lw_Rw, 7, true, "7.1 - L, R, C, LFE, Ls, Rs, Lw, Rw", "L_R_C_LFE_Ls_Rs_Lw_Rw.properties", "L_C_R_Ls_Rs_Lw_Rw"),
        new ChannelLayoutInfo(ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Cs_Oh, 7, true, "7.1 - L, R, C, LFE, Ls, Rs, Cs, Oh", "L_R_C_LFE_Ls_Rs_Cs_Oh.properties", "L_C_R_Ls_Rs_Cs_Oh"),
        new ChannelLayoutInfo(ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Cs_Ch, 7, true, "7.1 - L, R, C, LFE, Ls, Rs, Cs, Ch", "L_R_C_LFE_Ls_Rs_Cs_Ch.properties", "L_C_R_Ls_Rs_Cs_Ch"),
        new ChannelLayoutInfo(ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Lsr_Rsr, 7, true, "7.1 - L, R, C, LFE, Ls, Rs, Lsr, Rsr", "L_R_C_LFE_Ls_Rs_Lsr_Rsr.properties", "L_C_R_Ls_Rs_Lsr_Rsr"),
        new ChannelLayoutInfo(ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Lhs_Rhs, 7, true, "7.1 - L, R, C, LFE, Ls, Rs, Lhs, Rhs", "L_R_C_LFE_Ls_Rs_Lhs_Rhs.properties", "L_C_R_Ls_Rs_Lhs_Rhs"),
        new ChannelLayoutInfo(ChannelLayout.PA_71_L_R_C_LFE_Ls_Rs_Lh_Rh, 7, true, "7.1 - L, R, C, LFE, Ls, Rs, Lh, Rh", "L_R_C_LFE_Ls_Rs_Lh_Rh.properties", "L_C_R_Ls_Rs_Lh_Rh"),
        new ChannelLayoutInfo(ChannelLayout.PA_71_L_R_C_LFE_Lss_Rss_Lsr_Rsr, 7, true, "7.1 - L, R, C, LFE, Lss, Rss, Lsr, Rsr", "L_R_C_LFE_Lss_Rss_Lsr_Rsr.properties", "L_C_R_Lss_Rss_Lsr_Rsr"),
    };

    public static ChannelLayoutInfo Get(ChannelLayout l) => All.First(x => x.Layout == l);
}
