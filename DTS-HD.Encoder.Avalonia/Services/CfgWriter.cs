using System;
using System.Globalization;
using System.IO;
using System.Text;
using DTSHD.Encoder.Avalonia.Models;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 精确移植自 com.dts.encoder.config.ConfigTextFileWriter + 22 个 *_Configurator。
/// 按声道布局的 ConfiguratorKey 分派，生成与原版逐行一致的 .cfg。
/// </summary>
public sealed class CfgWriter
{
    private readonly StringBuilder _sb = new();
    private readonly EncodeSettings _s;

    public CfgWriter(EncodeSettings settings) => _s = settings;

    public string Build()
    {
        _sb.Clear();
        switch (_s.ConfiguratorKey)
        {
            case "C": BuildMono(); break;
            case "L_R": BuildLR(); break;
            case "Lt_Rt": BuildLtRt(); break;
            case "L_R_S": BuildLRS(); break;
            case "L_C_R": BuildLCR(); break;
            case "L_C_R_S": BuildLCRS(); break;
            case "L_R_Ls_Rs": BuildLRLsRs(); break;
            case "L_C_R_Ls_Rs": BuildLCRLsRs(); break;
            case "L_C_R_Ls_Rs_Cs_ES_Matrix":
                if (_s.IsPremixed) BuildEsMatrixPremixed(); else BuildEsMatrix(); break;
            case "L_C_R_Ls_Rs_Cs_ES_Discrete": BuildEsDiscrete(); break;
            case "L_C_R_Ls_Rs_Cs_Ch": Build71("es DTS_CHCFG_SRRD_CENTER", "esb DTS_CHCFG_HIGH_CENTER"); break;
            case "L_C_R_Ls_Rs_Cs_Oh": Build71("es DTS_CHCFG_SRRD_CENTER", "esb DTS_CHCFG_TOP_CENTER_SRRD"); break;
            case "L_C_R_Ls_Rs_Lh_Rh": Build71("es DTS_CHCFG_HIGH_LEFT", "esb DTS_CHCFG_HIGH_RIGHT"); break;
            case "L_C_R_Ls_Rs_Lhs_Rhs": Build71("es DTS_CHCFG_HIGH_SIDE_LEFT", "esb DTS_CHCFG_HIGH_SIDE_RIGHT"); break;
            case "L_C_R_Ls_Rs_Lsr_Rsr": Build71("es DTS_CHCFG_REAR_SRRD_LEFT", "esb DTS_CHCFG_REAR_SRRD_RIGHT"); break;
            case "L_C_R_Ls_Rs_Lw_Rw": Build71("es DTS_CHCFG_LEFT_WIDE", "esb DTS_CHCFG_RIGHT_WIDE"); break;
            case "L_C_R_Lss_Rss_Lsr_Rsr": Build71("es DTS_CHCFG_REAR_SRRD_LEFT", "esb DTS_CHCFG_REAR_SRRD_RIGHT", sideSurround: true); break;
            default: BuildLCRLsRs(); break;
        }
        return _sb.ToString();
    }

    public void WriteToFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Build(), new UTF8Encoding(false));
    }

    // =========================================================
    //  布局分派实现
    // =========================================================

    // ---- Mono: C_Configurator ----
    private void BuildMono()
    {
        if (_s.UseBdSecondaryAudioMetaData || _s.UseAaf)
        {
            if (_s.UsingAaf)
            {
                if (_s.AafPanningActive) DoDeclareMixMatrixWithAafMetadataEnabled();
                else if (_s.AafAttenuationKind == AafAttenuation.Unison) DoDeclareMixMatrixWithMonoPanning();
                else DoDeclareMixMatrixWithIndepChannAttenMonoPanning();
            }
            else DoMonoMixMatrix();
        }
        WriteBeginCore();
        bool aaf = _s.UseBdSecondaryAudioMetaData || _s.UseAaf;
        WriteLn(_s.IsLbr && aaf ? "\tCHANNEL c DTS_CHCFG_UNDEFINED" : "\tCHANNEL c DTS_CHCFG_CENTER");
        PrintIfLFE();
        WriteRates();
        WriteEndCore();
        if (_s.IsLbr) BuildLbrWithData(); else BuildLossy();
        BuildLossless();
        if (aaf) DoDeclareMixMatrix();
        BuildPackageCoreOnly();
        DoProgramInfo();
    }

    // ---- L_R_Configurator ----
    private void BuildLR()
    {
        ManageLoRoDmix();
        if (_s.UseAaf || _s.UseBdSecondaryAudioMetaData)
        {
            if (_s.UseAaf && _s.AafAttenuationKind == AafAttenuation.Independent) DoDynamic20MixMatrix();
            else Do20MixMatrix();
        }
        WriteBeginCore();
        WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
        WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
        PrintIfLFE();
        WriteRates();
        WriteEndCore();
        if (_s.IsLbr || (_s.IsDece && _s.IsExpress)) BuildLbrWithData(); else BuildLossy();
        BuildLossless();
        if (_s.UseBdSecondaryAudioMetaData || _s.UseAaf) DoDeclareMixMatrix();
        BuildPackageCoreOnly();
        DoProgramInfo();
    }

    // ---- Lt_Rt_Configurator ----
    private void BuildLtRt()
    {
        if (_s.UseBdSecondaryAudioMetaData) Do20MixMatrix();
        WriteBeginCore();
        WriteLn("\tCHANNEL l DTS_CHCFG_LT");
        WriteLn("\tCHANNEL r DTS_CHCFG_RT");
        PrintIfLFE();
        PrintIfEsPhaseShift();
        WriteRates();
        WriteEndCore();
        if (!_s.IsDece || !_s.IsLossless || _s.BitRate != 0) BuildLossy();
        BuildLossless();
        if (_s.UseBdSecondaryAudioMetaData) DoDeclareMixMatrix();
        BuildPackageCoreOnly();
        DoProgramInfo();
    }

    // ---- L_R_S_Configurator (含 PA_31_L_R_S_LFE) ----
    private void BuildLRS()
    {
        ManageLoRoDmix();
        WriteBeginCore();
        WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
        WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
        PrintIfLFE();
        WriteLn("\tCHANNEL es DTS_CHCFG_SRRD_CENTER");
        WriteRates();
        WriteEndCore();
        BuildLossy();
        BuildLossless();
        BuildPackageCoreOnly();
        DoProgramInfo();
    }

    // ---- L_C_R_Configurator (含 PA_31_L_C_R_LFE) ----
    private void BuildLCR()
    {
        ManageLoRoDmix();
        WriteBeginCore();
        WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
        WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
        WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
        PrintIfLFE();
        WriteRates();
        WriteEndCore();
        BuildLossy();
        BuildLossless();
        BuildPackageCoreOnly();
        DoProgramInfo();
    }

    // ---- L_C_R_S_Configurator (含 PA_41_L_C_R_S_LFE) ----
    private void BuildLCRS()
    {
        ManageLoRoDmix();
        WriteBeginCore();
        WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
        WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
        WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
        PrintIfLFE();
        WriteLn("\tCHANNEL es DTS_CHCFG_SRRD_CENTER");
        WriteRates();
        WriteEndCore();
        BuildLossy();
        BuildLossless();
        BuildPackageCoreOnly();
        DoProgramInfo();
    }

    // ---- L_R_Ls_Rs_Configurator (含 PA_41_L_R_Ls_Rs_LFE) ----
    private void BuildLRLsRs()
    {
        ManageLoRoDmix();
        WriteBeginCore();
        WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
        WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
        WriteLn("\tCHANNEL sl DTS_CHCFG_SRRD_LEFT");
        WriteLn("\tCHANNEL sr DTS_CHCFG_SRRD_RIGHT");
        PrintIfLFE();
        WriteRates();
        WriteEndCore();
        BuildLossy();
        BuildLossless();
        BuildPackageCoreOnly();
        DoProgramInfo();
    }

    // ---- L_C_R_Ls_Rs_Configurator (5.0 / 5.1 / SA 5.1) ----
    private void BuildLCRLsRs()
    {
        if (_s.UseAaf || _s.UseBdSecondaryAudioMetaData)
        {
            if (_s.UseAaf && _s.AafAttenuationKind == AafAttenuation.Independent) Do51DynamicMixMatrix();
            else Do51MixMatrix();
        }
        HandleDownmixToStereo();

        if (_s.IsEmbeddedDownmix && _s.IsLossless)
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            WriteEndCore();
            NewLine();
            WriteBeginCore2();
            WriteLn("\tCHANNEL sl DTS_CHCFG_SRRD_LEFT");
            WriteLn("\tCHANNEL sr DTS_CHCFG_SRRD_RIGHT");
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            PrintIfLFE();
            PrintIfEsPhaseShift();
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            BuildLossy();
            BuildLossless();
            if (_s.UseBdSecondaryAudioMetaData || _s.UseAaf) DoDeclareMixMatrix();
            if (_s.IsLbr) BuildLbr();
            BuildPackageCoreAndCore2();
        }
        else
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tCHANNEL sl DTS_CHCFG_SRRD_LEFT");
            WriteLn("\tCHANNEL sr DTS_CHCFG_SRRD_RIGHT");
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            PrintIfLFE();
            PrintIfEsPhaseShift();
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            if (_s.IsLbr || (_s.IsDece && _s.IsExpress)) BuildLbrWithData(); else BuildLossy();
            BuildLossless();
            if (_s.UseBdSecondaryAudioMetaData || _s.UseAaf) DoDeclareMixMatrix();
            BuildPackageCoreOnly();
        }
        DoProgramInfo();
    }

    // ---- ES Matrix (6.0/6.1) ----
    private void BuildEsMatrix()
    {
        HandleDownmixToStereo();
        if (_s.IsEmbeddedDownmix && _s.IsLossless)
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            NewLine();
            WriteBeginCore2();
            WriteLn("\tCHANNEL sl DTS_CHCFG_SRRD_LEFT");
            WriteLn("\tCHANNEL sr DTS_CHCFG_SRRD_RIGHT");
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            if (_s.UseLFE) WriteLn("\tCHANNEL lfe DTS_CHCFG_LFE_1");
            WriteLn("\tCHANNEL es DTS_CHCFG_SRRD_CENTER");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            WriteEndCore();
            BuildLossy();
            BuildLossless();
            BuildPackageCoreAndCore2();
        }
        else
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tCHANNEL sl DTS_CHCFG_SRRD_LEFT");
            WriteLn("\tCHANNEL sr DTS_CHCFG_SRRD_RIGHT");
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            PrintIfLFE();
            WriteLn("\tCHANNEL es DTS_CHCFG_SRRD_CENTER");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            BuildLossy();
            BuildLossless();
            BuildPackageCoreOnly();
        }
        DoProgramInfo();
    }

    // ---- ES Matrix PreMixed ----
    private void BuildEsMatrixPremixed()
    {
        HandleDownmixToStereo();
        if (_s.IsEmbeddedDownmix && _s.IsLossless)
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            WriteEndCore();
            NewLine();
            WriteBeginCore2();
            WriteLn("\tCHANNEL sl DTS_CHCFG_SRRD_LEFT");
            WriteLn("\tCHANNEL sr DTS_CHCFG_SRRD_RIGHT");
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            if (_s.UseLFE) WriteLn("\tCHANNEL lfe DTS_CHCFG_LFE_1");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            BuildLossy();
            BuildLossless();
            BuildPackageCoreAndCore2();
        }
        else
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tCHANNEL sl DTS_CHCFG_SRRD_LEFT");
            WriteLn("\tCHANNEL sr DTS_CHCFG_SRRD_RIGHT");
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            if (_s.UseLFE) WriteLn("\tCHANNEL lfe DTS_CHCFG_LFE_1");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            BuildLossy();
            BuildLossless();
            BuildPackageCoreOnly();
        }
        DoProgramInfo();
    }

    // ---- ES Discrete (6.0/6.1) ----
    private void BuildEsDiscrete()
    {
        HandleDownmixToStereo();
        HandleDownmixTo51();
        bool lfe = _s.UseLFE;
        if (_s.IsEmbeddedDownmix && _s.IsLossless)
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            WriteEndCore();
            NewLine();
            WriteBeginCore2();
            WriteLn("\tCHANNEL sl DTS_CHCFG_SRRD_LEFT");
            WriteLn("\tCHANNEL sr DTS_CHCFG_SRRD_RIGHT");
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            if (lfe) WriteLn("\tCHANNEL lfe DTS_CHCFG_LFE_1");
            WriteLn("\tBITWIDTH = " + _s.CoreBitWidth);
            WriteLn("\tSAMPLERATE = " + _s.CoreSampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            WriteBeginXch();
            WriteLn("\tCHANNEL es DTS_CHCFG_SRRD_CENTER");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            WriteEsDiscreteXchDownmix(lfe);
            WriteEndCore();
            BuildLossy();
            BuildLossless();
            BuildPackageCoreAndCore2AndXch();
        }
        else
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tCHANNEL sl DTS_CHCFG_SRRD_LEFT");
            WriteLn("\tCHANNEL sr DTS_CHCFG_SRRD_RIGHT");
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            PrintIfLFE();
            WriteLn("\tBITWIDTH = " + _s.CoreBitWidth);
            WriteLn("\tSAMPLERATE = " + _s.CoreSampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            WriteBeginXch();
            WriteLn("\tCHANNEL es DTS_CHCFG_SRRD_CENTER");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            WriteEsDiscreteXchDownmix(lfe);
            WriteEndCore();
            BuildLossy();
            BuildLossless();
            BuildPackageCoreAndXch();
        }
        DoProgramInfo();
    }

    private void WriteEsDiscreteXchDownmix(bool lfe)
    {
        bool legacyByMedia = _s.IsMediaTypeDvd || _s.IsMediaTypeCd;
        if (_s.Use51Downmix)
        {
            if (legacyByMedia || (_s.UseLegacyMatrix && !legacyByMedia))
                WriteLn(lfe ? "\tDOWNMIX_MATRIX = DTSES_LEGACY_DM_MATRIX" : "\tDOWNMIX_MATRIX = DTSES_LEGACY_DM_MATRIX_NO_LFE");
            else
                WriteLn(lfe ? "\tDOWNMIX_MATRIX = six_one_to_five_one" : "\tDOWNMIX_MATRIX = six_zero_to_five_one");
        }
        else if (_s.UseLegacyMatrix || legacyByMedia)
        {
            WriteLn(lfe ? "\tDOWNMIX_MATRIX = DTSES_LEGACY_DM_MATRIX" : "\tDOWNMIX_MATRIX = DTSES_LEGACY_DM_MATRIX_NO_LFE");
        }
    }

    // ---- 7.1 通用 (core 5.1 + xch es/esb + seven_*_to_five_one) ----
    private void Build71(string esDecl, string esbDecl, bool sideSurround = false)
    {
        HandleDownmixToStereo();
        HandleDownmixTo51();
        bool lfe = _s.UseLFE;
        string sl = sideSurround ? "sl DTS_CHCFG_SIDE_SRRD_LEFT" : "sl DTS_CHCFG_SRRD_LEFT";
        string sr = sideSurround ? "sr DTS_CHCFG_SIDE_SRRD_RIGHT" : "sr DTS_CHCFG_SRRD_RIGHT";
        string dmx = lfe ? "\tDOWNMIX_MATRIX = seven_one_to_five_one" : "\tDOWNMIX_MATRIX = seven_zero_to_five_one";
        if (_s.IsEmbeddedDownmix && _s.IsLossless)
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            WriteEndCore();
            NewLine();
            WriteBeginCore2();
            WriteLn("\tCHANNEL " + sl);
            WriteLn("\tCHANNEL " + sr);
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            PrintIfLFE();
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            NewLine();
            WriteBeginXch();
            WriteLn("\tCHANNEL " + esDecl);
            WriteLn("\tCHANNEL " + esbDecl);
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            WriteLn(dmx);
            WriteEndCore();
            BuildLossy();
            BuildLossless();
            BuildPackageCoreAndCore2AndXch();
        }
        else
        {
            WriteBeginCore();
            WriteLn("\tCHANNEL l DTS_CHCFG_LEFT");
            WriteLn("\tCHANNEL r DTS_CHCFG_RIGHT");
            WriteLn("\tCHANNEL " + sl);
            WriteLn("\tCHANNEL " + sr);
            WriteLn("\tCHANNEL c DTS_CHCFG_CENTER");
            PrintIfLFE();
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            if (_s.Use20Downmix) WriteLn("\tDOWNMIX_MATRIX = dmx_to_stereo");
            WriteEndCore();
            NewLine();
            WriteBeginXch();
            WriteLn("\tCHANNEL " + esDecl);
            WriteLn("\tCHANNEL " + esbDecl);
            WriteLn("\tBITWIDTH = " + _s.BitWidth);
            WriteLn("\tSAMPLERATE = " + _s.SampleRate);
            WriteLn(dmx);
            WriteEndCore();
            BuildLossy();
            BuildLossless();
            BuildPackageCoreAndXch();
        }
        DoProgramInfo();
    }

    // =========================================================
    //  LOSSY / LOSSLESS / LBR
    // =========================================================
    // cfg 中 BITRATE 以 bps 表示；UI/目录里是 kbps（如 1509）→ ×1000
    private long BitrateBps() => _s.BitRate < 100000 ? (long)_s.BitRate * 1000 : _s.BitRate;

    private void BuildLossy()
    {
        if (_s.IsDece && _s.IsLossless && _s.BitRate == 0) return;
        WriteLn("BEGIN LOSSY");
        WriteLn("\tBITRATE = " + BitrateBps());
        if (_s.EsPhaseShift && !_s.IsLossless) WriteLn("\tES_PHASE_SHIFT");
        if (_s.IsPremixed) WriteLn("\tPRE_MIXED_ES");
        if (_s.IsSecondaryAudio || _s.IsMediaTypeBdSecondaryAudio) WriteLn("\tSECONDARY_AUDIO");
        if (_s.AttenuateRearCh) WriteLn("\tATTENUATE_REAR_CH");
        if (_s.Use9624) WriteLn("\tENABLE_CORE_X96_XLL_COMBO");
        WriteLn("END LOSSY");
        NewLine();
    }

    private void BuildLossless()
    {
        if (!_s.IsLossless) return;
        WriteLn("BEGIN LOSSLESS");
        if (!_s.UseWideRemapping && _s.IsEmbeddedDownmix && _s.IsLossless)
            WriteLn("\t EMBED_STEREO");
        WriteLn("END LOSSLESS");
        NewLine();
    }

    private void BuildLbr()
    {
        WriteLn("BEGIN LBR");
        WriteLn("END LBR");
        NewLine();
    }

    private void BuildLbrWithData()
    {
        WriteLn("BEGIN LBR");
        WriteLn("\tBITRATE = " + BitrateBps());
        if (_s.EsPhaseShift && !_s.IsLossless) WriteLn("\tES_PHASE_SHIFT");
        if (_s.IsPremixed) WriteLn("\tPRE_MIXED_ES");
        if (_s.IsSecondaryAudio || _s.IsMediaTypeBdSecondaryAudio) WriteLn("\tSECONDARY_AUDIO");
        if (_s.UseExpressDialogMode) WriteLn("\tBAND_LIMIT_RATIO = 0.5");
        if (_s.IsDece && _s.IsEmbeddedDownmix) WriteLn("\tLBR_EMBED_STEREO");
        WriteLn("END LBR");
        NewLine();
    }

    // =========================================================
    //  PACKAGE
    // =========================================================
    private void BuildPackageCoreOnly() => BuildPackage(core2: false, xch: false);
    private void BuildPackageCoreAndXch() => BuildPackage(core2: false, xch: true);
    private void BuildPackageCoreAndCore2() => BuildPackage(core2: true, xch: false);
    private void BuildPackageCoreAndCore2AndXch() => BuildPackage(core2: true, xch: true);

    private void BuildPackage(bool core2, bool xch)
    {
        WriteLn("BEGIN PACKAGE");
        if (_s.DownmixSaturationCheck) WriteLn("\tTEST_DOWNMIX_SATURATION");
        // 官方：core / core2 / xch 各占一行
        WriteLn("\tcore");
        if (core2) WriteLn("\tcore2");
        if (xch) WriteLn("\txch");
        if (_s.UseDialogNormalization) WriteLn("\tDIALOGNORM = " + _s.DialogNormalization);
        WriteMediaType();
        WriteLn("\tTIMECODE START = " + (_s.EncodeEntireFile ? _s.TimecodeStart : _s.TimecodeEncodeFrom));
        WriteLn("\tTIMECODE RATE = " + _s.FrameRate.ToUpperInvariant());
        if (_s.UseTimecodeReferenceTime) WriteLn("\tTIMECODE REF = " + _s.TimecodeReferenceTime);
        HandlePrmScale();
        WriteLn("END PACKAGE");
        NewLine();
    }

    private void WriteMediaType()
    {
        if (_s.IsMediaTypeDvd) WriteLn("\tMEDIA_TYPE = DVDVideo");
        else if (_s.IsMediaTypeCd) WriteLn("\tMEDIA_TYPE = CD");
        else if (_s.IsMediaTypeBd) WriteLn("\tMEDIA_TYPE = BD");
        else if (_s.IsMediaTypeBdSecondaryAudio) WriteLn("\tMEDIA_TYPE = BD");
        else if (_s.IsDece) WriteLn(_s.IsExpress ? "\tMEDIA_TYPE = DTSHD_INTERNAL_TEST" : "\tMEDIA_TYPE = BD");
    }

    private void HandlePrmScale()
    {
        if (!_s.UseAaf) return;
        bool multiple = (_s.MainChannelCount == 1 && _s.AafPanningActive)
                        || _s.AafAttenuationKind == AafAttenuation.Independent;
        WriteLn(multiple ? "\tMULTIPLE_PRM_SCALE" : "\tSINGLE_PRM_SCALE");
    }

    // =========================================================
    //  下混矩阵
    // =========================================================
    private void HandleDownmixToStereo()
    {
        if (!_s.Use20Downmix) return;
        var d = _s.Downmix20;
        bool lfe = _s.UseLFE;
        bool embeddedNoLoRo = _s.IsEmbeddedDownmix && _s.IsLossless;
        WriteLn("");
        WriteLn("BEGIN MATRIX dmx_to_stereo");
        if (lfe)
        {
            if (embeddedNoLoRo)
            {
                WriteLn("\tINPUT\t\t\tsr\tsl\tlfe\tc");
                WriteLn($"\tl=\t\t{d.LeftA}\t{d.RsA}\t{d.LsA}\t{d.LfeA}\t{d.CenterA}");
                WriteLn($"\tr=\t\t{d.RightB}\t{d.RsB}\t{d.LsB}\t{d.LfeB}\t{d.CenterB}");
            }
            else if (_s.Is2ChDmixLtRt)
            {
                WriteLn("\tINPUT\t\tr\tl\tsr\tsl\tlfe\tc");
                WriteLn($"\tLt=\t\t{d.RightA}\t{d.LeftA}\t{d.RsA}\t{d.LsA}\t{d.LfeA}\t{d.CenterA}");
                WriteLn($"\tRt=\t\t{d.RightB}\t{d.LeftB}\t{d.RsB}\t{d.LsB}\t{d.LfeB}\t{d.CenterB}");
            }
            else
            {
                WriteLn("\tINPUT\t\tr\tl\tsr\tsl\tlfe\tc");
                WriteLn($"\tLo=\t\t{d.RightA}\t{d.LeftA}\t{d.RsA}\t{d.LsA}\t{d.LfeA}\t{d.CenterA}");
                WriteLn($"\tRo=\t\t{d.RightB}\t{d.LeftB}\t{d.RsB}\t{d.LsB}\t{d.LfeB}\t{d.CenterB}");
            }
        }
        else
        {
            if (embeddedNoLoRo)
            {
                WriteLn("\tINPUT\t\t\tsr\tsl\tc");
                WriteLn($"\tl=\t\t{d.LeftA}\t{d.RsA}\t{d.LsA}\t{d.CenterA}");
                WriteLn($"\tr=\t\t{d.RightB}\t{d.RsB}\t{d.LsB}\t{d.CenterB}");
            }
            else if (_s.Is2ChDmixLtRt)
            {
                WriteLn("\tINPUT\t\tr\tl\tsr\tsl\tc");
                WriteLn($"\tLt=\t\t{d.RightA}\t{d.LeftA}\t{d.RsA}\t{d.LsA}\t{d.CenterA}");
                WriteLn($"\tRt=\t\t{d.RightB}\t{d.LeftB}\t{d.RsB}\t{d.LsB}\t{d.CenterB}");
            }
            else
            {
                WriteLn("\tINPUT\t\tr\tl\tsr\tsl\tc");
                WriteLn($"\tLo=\t\t{d.RightA}\t{d.LeftA}\t{d.RsA}\t{d.LsA}\t{d.CenterA}");
                WriteLn($"\tRo=\t\t{d.RightB}\t{d.LeftB}\t{d.RsB}\t{d.LsB}\t{d.CenterB}");
            }
        }
        WriteLn("END MATRIX");
        WriteLn("");
    }

    private void HandleDownmixTo51()
    {
        if (!_s.Use51Downmix || _s.Is51Es) return;
        int mc = _s.MainChannelCount;
        if (mc == 6 && _s.HasLFE && !_s.IsMatrix) Setup61To51(_s.UseCurrent);
        else if (mc == 6 && !_s.HasLFE && !_s.IsMatrix) Setup60To51(_s.UseCurrent);
        else if (mc == 7 && _s.HasLFE) Setup71To51(_s.UseCurrent);
        else if (mc == 7 && !_s.HasLFE) Setup70To51(_s.UseCurrent);
    }

    private Downmix51Coeffs C => _s.Downmix51;

    private void Setup61To51(bool cur)
    {
        WriteLn(""); WriteLn("BEGIN MATRIX six_one_to_five_one");
        if (!cur)
        {
            WriteLn("\tINPUT\t\tes");
            WriteLn("\tr=\t\t" + C.RightXchA); WriteLn("\tl=\t\t" + C.LeftXchA);
            WriteLn("\tsr=\t\t" + C.RsXchA); WriteLn("\tsl=\t\t" + C.LsXchA);
            WriteLn("\tlfe=\t\t" + C.LfeXchA); WriteLn("\tc=\t\t" + C.CenterXchA);
        }
        else
        {
            WriteLn("\tINPUT\t\t\tes");
            WriteLn($"\tr=\t\t{C.RightPrimary}\t{C.RightXchA}"); WriteLn($"\tl=\t\t{C.LeftPrimary}\t{C.LeftXchA}");
            WriteLn($"\tsr=\t\t{C.RsPrimary}\t{C.RsXchA}"); WriteLn($"\tsl=\t\t{C.LsPrimary}\t{C.LsXchA}");
            WriteLn($"\tlfe=\t\t{C.LfePrimary}\t{C.LfeXchA}"); WriteLn($"\tc=\t\t{C.CenterPrimary}\t{C.CenterXchA}");
        }
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void Setup60To51(bool cur)
    {
        WriteLn(""); WriteLn("BEGIN MATRIX six_zero_to_five_one");
        if (!cur)
        {
            WriteLn("\tINPUT\t\tes");
            WriteLn("\tr=\t\t" + C.RightXchA); WriteLn("\tl=\t\t" + C.LeftXchA);
            WriteLn("\tsr=\t\t" + C.RsXchA); WriteLn("\tsl=\t\t" + C.LsXchA);
            WriteLn("\tc=\t\t" + C.CenterXchA);
        }
        else
        {
            WriteLn("\tINPUT\t\tes");
            WriteLn($"\tr=\t\t{C.RightPrimary}\t{C.RightXchA}"); WriteLn($"\tl=\t\t{C.LeftPrimary}\t{C.LeftXchA}");
            WriteLn($"\tsr=\t\t{C.RsPrimary}\t{C.RsXchA}"); WriteLn($"\tsl=\t\t{C.LsPrimary}\t{C.LsXchA}");
            WriteLn($"\tc=\t\t{C.CenterPrimary}\t{C.CenterXchA}");
        }
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void Setup71To51(bool cur)
    {
        WriteLn(""); WriteLn("BEGIN MATRIX seven_one_to_five_one");
        if (!cur)
        {
            WriteLn("\tINPUT\t\tes\tesb");
            WriteLn($"\tr=\t\t{C.RightXchA}\t{C.RightXchB}"); WriteLn($"\tl=\t\t{C.LeftXchA}\t{C.LeftXchB}");
            WriteLn($"\tsr=\t\t{C.RsXchA}\t{C.RsXchB}"); WriteLn($"\tsl=\t\t{C.LsXchA}\t{C.LsXchB}");
            WriteLn($"\tlfe=\t\t{C.LfeXchA}\t{C.LfeXchB}"); WriteLn($"\tc=\t\t{C.CenterXchA}\t{C.CenterXchB}");
        }
        else
        {
            WriteLn("\tINPUT\t\t\tes\tesb");
            WriteLn($"\tr=\t\t{C.RightPrimary}\t{C.RightXchA}\t{C.RightXchB}"); WriteLn($"\tl=\t\t{C.LeftPrimary}\t{C.LeftXchA}\t{C.LeftXchB}");
            WriteLn($"\tsr=\t\t{C.RsPrimary}\t{C.RsXchA}\t{C.RsXchB}"); WriteLn($"\tsl=\t\t{C.LsPrimary}\t{C.LsXchA}\t{C.LsXchB}");
            WriteLn($"\tlfe=\t\t{C.LfePrimary}\t{C.LfeXchA}\t{C.LfeXchB}"); WriteLn($"\tc=\t\t{C.CenterPrimary}\t{C.CenterXchA}\t{C.CenterXchB}");
        }
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void Setup70To51(bool cur)
    {
        WriteLn(""); WriteLn("BEGIN MATRIX seven_zero_to_five_one");
        if (!cur)
        {
            WriteLn("\tINPUT\t\tes\tesb");
            WriteLn($"\tr=\t\t{C.RightXchA}\t{C.RightXchB}"); WriteLn($"\tl=\t\t{C.LeftXchA}\t{C.LeftXchB}");
            WriteLn($"\tsr=\t\t{C.RsXchA}\t{C.RsXchB}"); WriteLn($"\tsl=\t\t{C.LsXchA}\t{C.LsXchB}");
            WriteLn($"\tc=\t\t{C.CenterXchA}\t{C.CenterXchB}");
        }
        else
        {
            WriteLn("\tINPUT\t\tes\tesb");
            WriteLn($"\tr=\t\t{C.RightPrimary}\t{C.RightXchA}\t{C.RightXchB}"); WriteLn($"\tl=\t\t{C.LeftPrimary}\t{C.LeftXchA}\t{C.LeftXchB}");
            WriteLn($"\tsr=\t\t{C.RsPrimary}\t{C.RsXchA}\t{C.RsXchB}"); WriteLn($"\tsl=\t\t{C.LsPrimary}\t{C.LsXchA}\t{C.LsXchB}");
            WriteLn($"\tc=\t\t{C.CenterPrimary}\t{C.CenterXchA}\t{C.CenterXchB}");
        }
        WriteLn("END MATRIX"); WriteLn("");
    }

    // 小于 5 声道时的 LoRo 下混（manageLoRoDmix / writeSub5_xtoLoRoDmixMatrix 简化忠实版）
    private void ManageLoRoDmix()
    {
        if (!_s.Use20Downmix) return;
        int mc = _s.MainChannelCount;
        if (mc >= 5) return;
        var d = _s.Downmix20;
        bool lfe = _s.UseLFE;
        string disp = _s.ChannelLayoutDisplay;
        WriteLn("");
        WriteLn("BEGIN MATRIX StereoDmixMatrix");
        var head = new StringBuilder("\tINPUT\t\tl\tr\t");
        var lRow = new StringBuilder($"\tl=\t\t{d.LeftA}\t{d.LeftB}\t");
        var rRow = new StringBuilder($"\tr=\t\t{d.RightA}\t{d.RightB}\t");
        if (mc == 3)
        {
            bool hasC = disp.Contains("L, C, R") || disp.Contains("L, C, R, LFE");
            head.Append(hasC ? "c" : "es");
            lRow.Append(hasC ? d.CenterA : d.LsA);
            rRow.Append(hasC ? d.CenterB : d.LsB);
            head.Append('\t'); lRow.Append('\t'); rRow.Append('\t');
        }
        else if (mc == 4)
        {
            bool hasC = disp.Contains("L, C, R, S");
            head.Append(hasC ? "c\tes" : "sl\tsr");
            lRow.Append(hasC ? $"{d.CenterA}\t{d.LsA}" : $"{d.LsA}\t{d.RsA}");
            rRow.Append(hasC ? $"{d.CenterB}\t{d.LsB}" : $"{d.LsB}\t{d.RsB}");
            head.Append('\t'); lRow.Append('\t'); rRow.Append('\t');
        }
        if (lfe) { head.Append("lfe"); lRow.Append(d.LfeA); rRow.Append(d.LfeB); }
        WriteLn(head.ToString());
        WriteLn("");
        WriteLn(lRow.ToString());
        WriteLn(rRow.ToString());
        WriteLn("END MATRIX");
        WriteLn("");
    }

    // ---- MixMatrix0 系列 ----
    private void DoDeclareMixMatrix()
    {
        WriteLn(""); WriteLn("BEGIN MIXCONFIG"); WriteLn("\tMixMatrix0"); WriteLn("END MIXCONFIG"); WriteLn("");
    }

    private void DoMonoMixMatrix()
    {
        string prm = PaPrimaryValue();
        WriteLn(""); WriteLn("BEGIN MATRIX MixMatrix0");
        WriteLn("\tINPUT\t\t\t\t\tc");
        WriteLn($"\tDTS_CHCFG_LEFT=\t\t{prm}\t{_s.MonoMetadataL}");
        WriteLn($"\tDTS_CHCFG_RIGHT=\t\t{prm}\t{_s.MonoMetadataR}");
        WriteLn($"\tDTS_CHCFG_CENTER=\t\t{prm}\t{_s.MonoMetadataC}");
        WriteLn($"\tDTS_CHCFG_SRRD_LEFT=\t{prm}\t{_s.MonoMetadataLs}");
        WriteLn($"\tDTS_CHCFG_SRRD_RIGHT=\t{prm}\t{_s.MonoMetadataRs}");
        WriteLn($"\tDTS_CHCFG_LFE_1=\t\t{prm}\tINF");
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void Do20MixMatrix()
    {
        string prm = PaPrimaryValue();
        WriteLn(""); WriteLn("BEGIN MATRIX MixMatrix0");
        WriteLn("\tINPUT\t\t\t\t\tl\tr");
        WriteLn($"\tDTS_CHCFG_LEFT=\t\t{prm}\t0.0\tINF");
        WriteLn($"\tDTS_CHCFG_RIGHT=\t\t{prm}\tINF\t0.0");
        WriteLn($"\tDTS_CHCFG_CENTER=\t\t{prm}\tINF\tINF");
        WriteLn($"\tDTS_CHCFG_SRRD_LEFT=\t{prm}\tINF\tINF");
        WriteLn($"\tDTS_CHCFG_SRRD_RIGHT=\t{prm}\tINF\tINF");
        WriteLn($"\tDTS_CHCFG_LFE_1=\t\t{prm}\tINF\tINF");
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void DoDynamic20MixMatrix()
    {
        WriteLn(""); WriteLn("BEGIN MATRIX MixMatrix0");
        WriteLn("\tINPUT\t\t\t\t\tl\tr");
        WriteLn("\tDTS_CHCFG_LEFT=\t\t\t6.0\t0.0\tINF");
        WriteLn("\tDTS_CHCFG_RIGHT=\t\t6.0\tINF\t0.0");
        WriteLn("\tDTS_CHCFG_CENTER=\t\t6.0\tINF\tINF");
        WriteLn("\tDTS_CHCFG_SRRD_LEFT=\t\t6.0\tINF\tINF");
        WriteLn("\tDTS_CHCFG_SRRD_RIGHT=\t\t6.0\tINF\tINF");
        WriteLn("\tDTS_CHCFG_LFE_1=\t\t3.0\tINF\tINF");
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void Do51MixMatrix()
    {
        string prm = PaPrimaryValue();
        WriteLn(""); WriteLn("BEGIN MATRIX MixMatrix0");
        WriteLn("\tINPUT\t\t\t\t\tl\tr\tc\tsl\tsr\tlfe");
        WriteLn($"\tDTS_CHCFG_LEFT=\t\t{prm}\t0.0\tINF\tINF\tINF\tINF\tINF");
        WriteLn($"\tDTS_CHCFG_RIGHT=\t\t{prm}\tINF\t0.0\tINF\tINF\tINF\tINF");
        WriteLn($"\tDTS_CHCFG_CENTER=\t\t{prm}\tINF\tINF\t0.0\tINF\tINF\tINF");
        WriteLn($"\tDTS_CHCFG_SRRD_LEFT=\t{prm}\tINF\tINF\tINF\t0.0\tINF\tINF");
        WriteLn($"\tDTS_CHCFG_SRRD_RIGHT=\t{prm}\tINF\tINF\tINF\tINF\t0.0\tINF");
        WriteLn($"\tDTS_CHCFG_LFE_1=\t\t{prm}\tINF\tINF\tINF\tINF\tINF\t0.0");
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void Do51DynamicMixMatrix()
    {
        WriteLn(""); WriteLn("BEGIN MATRIX MixMatrix0");
        WriteLn("\tINPUT\t\t\t\t\tl\tr\tc\tsl\tsr\tlfe");
        WriteLn("\tDTS_CHCFG_LEFT=\t\t\t6.0\t0.0\tINF\tINF\tINF\tINF\tINF");
        WriteLn("\tDTS_CHCFG_RIGHT=\t\t6.0\tINF\t0.0\tINF\tINF\tINF\tINF");
        WriteLn("\tDTS_CHCFG_CENTER=\t\t6.0\tINF\tINF\t0.0\tINF\tINF\tINF");
        WriteLn("\tDTS_CHCFG_SRRD_LEFT=\t\t6.0\tINF\tINF\tINF\t0.0\tINF\tINF");
        WriteLn("\tDTS_CHCFG_SRRD_RIGHT=\t\t6.0\tINF\tINF\tINF\tINF\t0.0\tINF");
        WriteLn("\tDTS_CHCFG_LFE_1=\t\t3.0\tINF\tINF\tINF\tINF\tINF\t0.0");
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void DoDeclareMixMatrixWithMonoPanning()
    {
        WriteLn(""); WriteLn("BEGIN MATRIX MixMatrix0");
        WriteLn("\tINPUT\t\t\t\t\tc");
        WriteLn($"\tDTS_CHCFG_LEFT=\t\t6.0\t{_s.MonoMetadataL}");
        WriteLn($"\tDTS_CHCFG_RIGHT=\t\t6.0\t{_s.MonoMetadataR}");
        WriteLn($"\tDTS_CHCFG_CENTER=\t\t6.0\t{_s.MonoMetadataC}");
        WriteLn($"\tDTS_CHCFG_SRRD_LEFT=\t6.0\t{_s.MonoMetadataLs}");
        WriteLn($"\tDTS_CHCFG_SRRD_RIGHT=\t6.0\t{_s.MonoMetadataRs}");
        WriteLn($"\tDTS_CHCFG_LFE_1=\t\t6.0\t{_s.MonoMetadataLFE}");
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void DoDeclareMixMatrixWithIndepChannAttenMonoPanning()
    {
        WriteLn(""); WriteLn("BEGIN MATRIX MixMatrix0");
        WriteLn("\tINPUT\t\t\t\t\tc");
        WriteLn($"\tDTS_CHCFG_LEFT=\t\t6.0\t{_s.MonoMetadataL}");
        WriteLn($"\tDTS_CHCFG_RIGHT=\t\t6.0\t{_s.MonoMetadataR}");
        WriteLn($"\tDTS_CHCFG_CENTER=\t\t6.0\t{_s.MonoMetadataC}");
        WriteLn($"\tDTS_CHCFG_SRRD_LEFT=\t6.0\t{_s.MonoMetadataLs}");
        WriteLn($"\tDTS_CHCFG_SRRD_RIGHT=\t6.0\t{_s.MonoMetadataRs}");
        WriteLn($"\tDTS_CHCFG_LFE_1=\t\t3.0\t{_s.MonoMetadataLFE}");
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void DoDeclareMixMatrixWithAafMetadataEnabled()
    {
        WriteLn(""); WriteLn("BEGIN MATRIX MixMatrix0");
        WriteLn("\tINPUT\t\t\t\t\tc");
        WriteLn("\tDTS_CHCFG_LEFT=\t\t6.0\t0.0");
        WriteLn("\tDTS_CHCFG_RIGHT=\t\t6.0\t0.0");
        WriteLn("\tDTS_CHCFG_CENTER=\t\t6.0\t0.0");
        WriteLn("\tDTS_CHCFG_SRRD_LEFT=\t\t6.0\t0.0");
        WriteLn("\tDTS_CHCFG_SRRD_RIGHT=\t\t6.0\t0.0");
        WriteLn("\tDTS_CHCFG_LFE_1=\t\t3.0\tINF");
        WriteLn("END MATRIX"); WriteLn("");
    }

    private void DoProgramInfo()
    {
        WriteLn(""); WriteLn("BEGIN FILEINFO");
        WriteLn("\t" + _s.ProgramInfo);
        WriteLn("END FILEINFO"); WriteLn("");
    }

    // =========================================================
    //  低层写入与小工具
    // =========================================================
    private void WriteBeginCore() => WriteLn("BEGIN CHSETOBJ core");
    private void WriteBeginCore2() => WriteLn("BEGIN CHSETOBJ core2");
    private void WriteBeginXch() => WriteLn("BEGIN CHSETOBJ xch");
    private void WriteEndCore() { WriteLn("END CHSETOBJ"); NewLine(); }
    private void WriteRates() { WriteLn("\tBITWIDTH = " + _s.BitWidth); WriteLn("\tSAMPLERATE = " + _s.SampleRate); }
    private void PrintIfLFE() { if (_s.UseLFE) WriteLn("\tCHANNEL lfe DTS_CHCFG_LFE_1"); }
    private void PrintIfEsPhaseShift() { if (_s.EsPhaseShift && !_s.IsLossless) WriteLn("\tCHANNEL es DTS_CHCFG_SRRD_CENTER"); }

    private string PaPrimaryValue()
    {
        if (_s.PaPrimary.Equals("INF", StringComparison.OrdinalIgnoreCase)) return "40.0";
        if (float.TryParse(_s.PaPrimary, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            return (f > 40.0f ? 40.0f : f).ToString("0.0", CultureInfo.InvariantCulture);
        return "40.0";
    }

    private void WriteLn(string s) => _sb.Append(s).Append('\n');
    private void NewLine() => _sb.Append('\n');
}
