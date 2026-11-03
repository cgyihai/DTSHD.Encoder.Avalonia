using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DTSHD.Encoder.Avalonia.Models;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>扬声器"角色"：与具体声道布局无关的物理位置。文件名里的各种写法（FL / Left / 左…）先归一到角色，
/// 再按当前（或自动选出的）声道布局映射到具体输入框标签（L / Lss / Lsr …）。</summary>
public enum SpeakerRole
{
    L, R, C, LFE,
    Lt, Rt,
    SideL, SideR,        // 侧环绕：Ls/Rs、SL/SR、Lss/Rss
    BackL, BackR,        // 后环绕：Lsr/Rsr、BL/BR、Lb/Rb、Lrs/Rrs
    BackC,               // 后中置：Cs、BC、S
    WideL, WideR,        // 前宽：Lw/Rw
    HeightL, HeightR,    // 前上：Lh/Rh、TFL/TFR
    HeightSideL, HeightSideR, // 侧上：Lhs/Rhs
    Top,                 // 顶置：Oh、TC
    HeightC,             // 中置上：Ch、TFC
}

/// <summary>文件名中识别到的声道信息。</summary>
/// <param name="Role">归一化后的扬声器角色。</param>
/// <param name="Token">文件名中实际出现的写法（原样，如 "FL"、"Lsr"、"左"）。</param>
/// <param name="Stem">去掉声道标记后的"组键"（小写），同一组分轨文件的 Stem 相同。</param>
public sealed record ChannelMatch(SpeakerRole Role, string Token, string Stem);

/// <summary>一次导入的识别结果：所选布局 + 每个输入框标签对应的文件。</summary>
public sealed record ChannelAssignment(
    ChannelLayout Layout,
    IReadOnlyDictionary<string, string> FilesByLabel,
    IReadOnlyList<string> Unassigned,
    bool PerfectFit);

/// <summary>
/// 分轨声道自动识别：
///  1. 文件名分词（_ - . 空格 括号等），从后往前匹配声道别名（FL/Left/Ls/SL/BL/LFE/Sub/左/右/中置/重低音…），
///     也支持无分隔符的驼峰结尾（MovieFL、MovieLsr）与中文结尾（电影左）；
///  2. 去掉声道标记得到组键（Stem），扫描同目录同扩展名文件，Stem 相同者视为同一组分轨；
///  3. 依据识别到的声道集合自动选择最匹配的声道布局（如 L/R/C/LFE/SL/SR/BL/BR → 7.1），
///     再把每个文件放入对应输入框。
/// </summary>
public static class ChannelRecognizer
{
    // ---------------- 别名表 ----------------
    // 写法均为"规范大小写"：分词匹配时不区分大小写；无分隔符的结尾匹配要求首字母大写（避免 Final → L 误判）。
    private static readonly (SpeakerRole Role, string[] Aliases)[] AliasTable =
    {
        (SpeakerRole.L,      new[] { "L", "FL", "Left", "FrontLeft", "LeftFront", "Lo", "左", "左声道", "左前", "前左", "左主" }),
        (SpeakerRole.R,      new[] { "R", "FR", "Right", "FrontRight", "RightFront", "Ro", "右", "右声道", "右前", "前右", "右主" }),
        (SpeakerRole.C,      new[] { "C", "FC", "Center", "Centre", "Ctr", "Cntr", "Cen", "FrontCenter", "Mono", "M", "中", "中置", "中央", "中间" }),
        (SpeakerRole.LFE,    new[] { "LFE", "LFE1", "SW", "Sub", "Subwoofer", "Boom", "低音", "重低音", "超低音", "低频" }),
        (SpeakerRole.Lt,     new[] { "Lt" }),
        (SpeakerRole.Rt,     new[] { "Rt" }),
        (SpeakerRole.SideL,  new[] { "Ls", "SL", "Lss", "SideLeft", "LeftSide", "LeftSurround", "SurroundLeft", "LSurround", "LSur", "Lsd", "左环", "左环绕", "左侧", "左侧环绕", "左环绕声" }),
        (SpeakerRole.SideR,  new[] { "Rs", "SR", "Rss", "SideRight", "RightSide", "RightSurround", "SurroundRight", "RSurround", "RSur", "Rsd", "右环", "右环绕", "右侧", "右侧环绕", "右环绕声" }),
        (SpeakerRole.BackL,  new[] { "Lsr", "Lrs", "Lb", "BL", "RL", "SBL", "BackLeft", "RearLeft", "LeftBack", "LeftRear", "LeftRearSurround", "LeftBackSurround", "左后", "后左", "左后环绕", "左后环" }),
        (SpeakerRole.BackR,  new[] { "Rsr", "Rrs", "Rb", "BR", "RR", "SBR", "BackRight", "RearRight", "RightBack", "RightRear", "RightRearSurround", "RightBackSurround", "右后", "后右", "右后环绕", "右后环" }),
        (SpeakerRole.BackC,  new[] { "Cs", "BC", "S", "Cb", "Bc", "BackCenter", "RearCenter", "CenterSurround", "SurroundCenter", "SurroundBack", "后中", "后中置", "中后" }),
        (SpeakerRole.WideL,  new[] { "Lw", "FLW", "WL", "FLC", "WideLeft", "LeftWide", "左宽" }),
        (SpeakerRole.WideR,  new[] { "Rw", "FRW", "WR", "FRC", "WideRight", "RightWide", "右宽" }),
        (SpeakerRole.HeightL, new[] { "Lh", "TFL", "Lvh", "FLH", "TopFrontLeft", "HeightLeft", "LeftHeight", "左上", "左天空" }),
        (SpeakerRole.HeightR, new[] { "Rh", "TFR", "Rvh", "FRH", "TopFrontRight", "HeightRight", "RightHeight", "右上", "右天空" }),
        (SpeakerRole.HeightSideL, new[] { "Lhs", "TSL", "TopSideLeft", "左侧上" }),
        (SpeakerRole.HeightSideR, new[] { "Rhs", "TSR", "TopSideRight", "右侧上" }),
        (SpeakerRole.Top,    new[] { "Oh", "TC", "Ts", "Top", "TopCenter", "Overhead", "VoG", "顶置", "头顶" }),
        (SpeakerRole.HeightC, new[] { "Ch", "TFC", "Cvh", "Chf", "TopFrontCenter", "CenterHeight", "中上" }),
    };

    /// <summary>每个输入框标签接受的角色（第一个为首选）。</summary>
    private static readonly Dictionary<string, SpeakerRole[]> LabelRoles = new(StringComparer.Ordinal)
    {
        ["L"] = new[] { SpeakerRole.L },
        ["R"] = new[] { SpeakerRole.R },
        ["C"] = new[] { SpeakerRole.C },
        ["LFE"] = new[] { SpeakerRole.LFE },
        ["Lo"] = new[] { SpeakerRole.L, SpeakerRole.Lt },
        ["Ro"] = new[] { SpeakerRole.R, SpeakerRole.Rt },
        ["Lt"] = new[] { SpeakerRole.Lt, SpeakerRole.L },
        ["Rt"] = new[] { SpeakerRole.Rt, SpeakerRole.R },
        ["Ls"] = new[] { SpeakerRole.SideL, SpeakerRole.BackL },
        ["Rs"] = new[] { SpeakerRole.SideR, SpeakerRole.BackR },
        ["Lss"] = new[] { SpeakerRole.SideL },
        ["Rss"] = new[] { SpeakerRole.SideR },
        ["Lsr"] = new[] { SpeakerRole.BackL },
        ["Rsr"] = new[] { SpeakerRole.BackR },
        ["S"] = new[] { SpeakerRole.BackC },
        ["Cs"] = new[] { SpeakerRole.BackC },
        ["Lw"] = new[] { SpeakerRole.WideL },
        ["Rw"] = new[] { SpeakerRole.WideR },
        ["Lh"] = new[] { SpeakerRole.HeightL },
        ["Rh"] = new[] { SpeakerRole.HeightR },
        ["Lhs"] = new[] { SpeakerRole.HeightSideL },
        ["Rhs"] = new[] { SpeakerRole.HeightSideR },
        ["Oh"] = new[] { SpeakerRole.Top },
        ["Ch"] = new[] { SpeakerRole.HeightC },
    };

    /// <summary>别名（小写）→ (角色, 规范写法)。</summary>
    private static readonly Dictionary<string, (SpeakerRole Role, string Canonical)> AliasMap = BuildAliasMap();

    /// <summary>无分隔符结尾匹配用：按长度降序的别名列表（规范大小写）。</summary>
    private static readonly (string Alias, SpeakerRole Role)[] SuffixAliases = AliasTable
        .SelectMany(t => t.Aliases.Select(a => (a, t.Role)))
        .OrderByDescending(x => x.a.Length)
        .ToArray();

    private static readonly char[] SeparatorChars =
        { '_', '-', '.', ' ', '(', ')', '[', ']', '{', '}', '+', ',', '#', '&', '@', '=', '~', '、', '（', '）', '【', '】', '·' };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".wav", ".w64", ".rf64", ".bwf", ".aif", ".aiff", ".pcm" };

    private static Dictionary<string, (SpeakerRole, string)> BuildAliasMap()
    {
        var d = new Dictionary<string, (SpeakerRole, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, aliases) in AliasTable)
            foreach (var a in aliases)
                d.TryAdd(a, (role, a));
        return d;
    }

    /// <summary>当前布局下某个输入框可识别的写法（用于悬浮提示）。</summary>
    public static string DescribeAliases(string label)
    {
        if (!LabelRoles.TryGetValue(label, out var roles)) return label;
        var list = new List<string> { label };
        foreach (var r in roles)
            foreach (var a in AliasTable.First(t => t.Role == r).Aliases)
                if (!list.Contains(a, StringComparer.OrdinalIgnoreCase)) list.Add(a);
        return string.Join(" / ", list);
    }

    /// <summary>声道布局的输入框标签（与编码页声道行一致：去掉 PA/SA 前缀与 ES/Matrix/Discrete 修饰）。</summary>
    public static IReadOnlyList<string> LayoutLabels(ChannelLayout layout)
    {
        var tokens = layout.ToString().Split('_').ToList();
        if (tokens.Count >= 2) tokens.RemoveRange(0, 2);
        while (tokens.Count > 0 && tokens[0] is "ES" or "Matrix" or "Discrete" or "PreMixed")
            tokens.RemoveAt(0);
        return tokens;
    }

    // =========================================================
    //  1. 单个文件名识别
    // =========================================================
    private readonly record struct Tok(int Start, int Length, string Text);

    /// <summary>识别文件属于哪个扬声器声道；无法识别返回 null。</summary>
    public static ChannelMatch? Detect(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name)) return null;

        var toks = Tokenize(name);
        // 从后往前找：声道标记通常在文件名末尾（Movie_FL.wav / Movie.L.48k.wav）
        for (int i = toks.Count - 1; i >= 0; i--)
        {
            // 两个相邻词组成的写法（Front_Left / Rear-Right / Left Surround）
            if (i > 0)
            {
                var joined = toks[i - 1].Text + toks[i].Text;
                if (joined.Length > 3 && AliasMap.TryGetValue(joined, out var pair))
                {
                    int start = toks[i - 1].Start, end = toks[i].Start + toks[i].Length;
                    return new ChannelMatch(pair.Role, name.Substring(start, end - start), MakeStem(name, start, end - start));
                }
            }
            var t = toks[i];
            if (AliasMap.TryGetValue(t.Text, out var hit))
                return new ChannelMatch(hit.Role, t.Text, MakeStem(name, t.Start, t.Length));
        }

        // 无分隔符：MovieFL / MovieLsr / 电影左 —— 结尾匹配，要求驼峰边界（避免 Final → L）
        foreach (var t in Enumerable.Reverse(toks))
        {
            foreach (var (alias, role) in SuffixAliases)
            {
                if (t.Text.Length <= alias.Length) continue;
                if (!t.Text.EndsWith(alias, StringComparison.Ordinal)) continue;
                char first = alias[0];
                char prev = t.Text[t.Text.Length - alias.Length - 1];
                bool cjk = IsCjk(first);
                bool camel = char.IsUpper(first) && (char.IsLower(prev) || char.IsDigit(prev) || IsCjk(prev));
                if (!cjk && !camel) continue;
                int start = t.Start + t.Length - alias.Length;
                return new ChannelMatch(role, alias, MakeStem(name, start, alias.Length));
            }
            break; // 只看最后一个词
        }
        return null;
    }

    private static List<Tok> Tokenize(string name)
    {
        var list = new List<Tok>();
        int i = 0;
        while (i < name.Length)
        {
            while (i < name.Length && Array.IndexOf(SeparatorChars, name[i]) >= 0) i++;
            int s = i;
            while (i < name.Length && Array.IndexOf(SeparatorChars, name[i]) < 0) i++;
            if (i > s) list.Add(new Tok(s, i - s, name.Substring(s, i - s)));
        }
        return list;
    }

    private static bool IsCjk(char c) => c >= 0x3400 && c <= 0x9FFF;

    private static string MakeStem(string name, int start, int length)
    {
        var sb = new StringBuilder(name.Length);
        sb.Append(name, 0, start).Append('\u0001').Append(name, start + length, name.Length - start - length);
        return sb.ToString().ToLowerInvariant();
    }

    // =========================================================
    //  2. 同组分轨扫描
    // =========================================================
    /// <summary>根据任一声道文件，扫描同目录同扩展名、同组键的其它声道文件（含自身）。</summary>
    public static List<(ChannelMatch Match, string Path)> FindSiblings(string path)
    {
        var result = new List<(ChannelMatch, string)>();
        var self = Detect(path);
        if (self == null) return result;
        result.Add((self, path));
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return result;
            var ext = Path.GetExtension(path);
            var seen = new HashSet<SpeakerRole> { self.Role };
            foreach (var f in Directory.EnumerateFiles(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(f, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase)) continue;
                var m = Detect(f);
                if (m == null || m.Stem != self.Stem || !seen.Add(m.Role)) continue;
                result.Add((m, f));
            }
        }
        catch { /* 目录无权限等：仅返回自身 */ }
        return result;
    }

    public static bool IsAudioFile(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    // =========================================================
    //  3. 布局选择 + 分配
    // =========================================================
    /// <summary>
    /// 把识别到的文件分配到声道布局的输入框。
    /// autoLayout=true 时在全部布局里挑选最匹配者（完全匹配 &gt; 覆盖全部文件且空位最少 &gt; 保持当前）。
    /// </summary>
    public static ChannelAssignment Assign(
        IReadOnlyList<(ChannelMatch Match, string Path)> items, ChannelLayout current, bool autoLayout)
    {
        if (!autoLayout || items.Count < 2)
            return Evaluate(items, current).Assignment;

        bool currentIsLbr = current.ToString().StartsWith("SA_", StringComparison.Ordinal);
        var candidates = ChannelLayouts.All
            .Select(x => x.Layout)
            .Where(l => l == current || l.ToString().StartsWith("SA_", StringComparison.Ordinal) == currentIsLbr)
            .Select(l => Evaluate(items, l))
            .ToList();

        var perfect = candidates.Where(c => c.Assignment.PerfectFit)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => Rank(c.Assignment.Layout, current))
            .FirstOrDefault();
        if (perfect.Assignment != null) return perfect.Assignment;

        // 当前布局已能容纳全部文件（只是有空位）→ 保持用户选择
        var cur = candidates.First(c => c.Assignment.Layout == current);
        if (cur.Assignment.Unassigned.Count == 0) return cur.Assignment;

        var covering = candidates.Where(c => c.Assignment.Unassigned.Count == 0)
            .OrderBy(c => c.Empty)
            .ThenByDescending(c => c.Score)
            .ThenBy(c => Rank(c.Assignment.Layout, current))
            .FirstOrDefault();
        return covering.Assignment ?? cur.Assignment;
    }

    /// <summary>按扬声器角色集合（来自 WAV dwChannelMask）选择布局；无合适布局返回 null。</summary>
    public static ChannelLayout? SuggestLayoutForRoles(IReadOnlyList<SpeakerRole> roles, ChannelLayout current)
    {
        var items = roles.Select(r => (new ChannelMatch(r, r.ToString(), ""), r.ToString())).ToList();
        var a = Assign(items, current, autoLayout: true);
        return a.PerfectFit ? a.Layout : null;
    }

    /// <summary>WAVE dwChannelMask → 扬声器角色（mask=0 时按 Microsoft 默认顺序推断）。</summary>
    public static List<SpeakerRole> RolesFromMask(uint mask, int channels)
    {
        var roles = new List<SpeakerRole>();
        for (int i = 0; i < 18 && roles.Count < channels; i++)
        {
            if ((mask & (1u << i)) == 0) continue;
            SpeakerRole? r = i switch
            {
                0 => SpeakerRole.L, 1 => SpeakerRole.R, 2 => SpeakerRole.C, 3 => SpeakerRole.LFE,
                4 => SpeakerRole.BackL, 5 => SpeakerRole.BackR, 6 => SpeakerRole.WideL, 7 => SpeakerRole.WideR,
                8 => SpeakerRole.BackC, 9 => SpeakerRole.SideL, 10 => SpeakerRole.SideR, 11 => SpeakerRole.Top,
                12 => SpeakerRole.HeightL, 13 => SpeakerRole.HeightC, 14 => SpeakerRole.HeightR,
                _ => null,
            };
            if (r != null) roles.Add(r.Value);
        }
        if (roles.Count == channels) return roles;

        return channels switch
        {
            1 => new() { SpeakerRole.C },
            2 => new() { SpeakerRole.L, SpeakerRole.R },
            3 => new() { SpeakerRole.L, SpeakerRole.R, SpeakerRole.C },
            6 => new() { SpeakerRole.L, SpeakerRole.R, SpeakerRole.C, SpeakerRole.LFE, SpeakerRole.BackL, SpeakerRole.BackR },
            7 => new() { SpeakerRole.L, SpeakerRole.R, SpeakerRole.C, SpeakerRole.LFE, SpeakerRole.BackL, SpeakerRole.BackR, SpeakerRole.BackC },
            8 => new() { SpeakerRole.L, SpeakerRole.R, SpeakerRole.C, SpeakerRole.LFE, SpeakerRole.BackL, SpeakerRole.BackR, SpeakerRole.SideL, SpeakerRole.SideR },
            _ => new(),
        };
    }

    // 同分时的偏好：当前布局 > 常用标准布局 > 枚举顺序
    private static readonly ChannelLayout[] Preferred =
    {
        ChannelLayout.PA_71_L_R_C_LFE_Lss_Rss_Lsr_Rsr,
        ChannelLayout.PA_70_L_R_C_Lss_Rss_Lsr_Rsr,
        ChannelLayout.PA_61_ES_Discrete_L_R_C_LFE_Ls_Rs_Cs,
        ChannelLayout.PA_60_ES_Discrete_L_R_C_Ls_Rs_Cs,
        ChannelLayout.PA_51_L_R_C_LFE_Ls_Rs,
        ChannelLayout.PA_20_Lo_Ro,
    };

    private static int Rank(ChannelLayout l, ChannelLayout current)
    {
        if (l == current) return -1;
        int i = Array.IndexOf(Preferred, l);
        return i >= 0 ? i : 100 + (int)l;
    }

    private readonly record struct Eval(ChannelAssignment Assignment, int Score, int Empty);

    private static Eval Evaluate(IReadOnlyList<(ChannelMatch Match, string Path)> items, ChannelLayout layout)
    {
        var labels = LayoutLabels(layout);
        // 所有 (文件, 标签, 分数) 组合，按分数从高到低贪心分配
        var options = new List<(int Item, string Label, int Score)>();
        for (int i = 0; i < items.Count; i++)
        {
            var m = items[i].Match;
            foreach (var lab in labels)
            {
                if (!LabelRoles.TryGetValue(lab, out var roles)) continue;
                int idx = Array.IndexOf(roles, m.Role);
                if (idx < 0) continue;
                int score = string.Equals(m.Token, lab, StringComparison.OrdinalIgnoreCase) ? 3 : idx == 0 ? 2 : 1;
                options.Add((i, lab, score));
            }
        }
        var byLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedItems = new HashSet<int>();
        int total = 0;
        foreach (var o in options.OrderByDescending(o => o.Score))
        {
            if (usedItems.Contains(o.Item) || byLabel.ContainsKey(o.Label)) continue;
            byLabel[o.Label] = items[o.Item].Path;
            usedItems.Add(o.Item);
            total += o.Score;
        }
        var unassigned = Enumerable.Range(0, items.Count).Where(i => !usedItems.Contains(i)).Select(i => items[i].Path).ToList();
        int empty = labels.Count - byLabel.Count;
        return new Eval(new ChannelAssignment(layout, byLabel, unassigned, unassigned.Count == 0 && empty == 0), total, empty);
    }
}
