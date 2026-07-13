using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using DTSHD.Encoder.Avalonia.Models;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 读取官方 conf/downmix51.properties + display.properties，按声道布局提供
/// 官方默认的 5.1 下混系数（值为 0.1dB 整数：-30=3.0dB，-600≈INF）。
/// </summary>
public static class DownmixDefaults
{
    private static Dictionary<string, string>? _dmx51;
    private static Dictionary<string, string>? _dmx20;
    private static Dictionary<string, string>? _fileToPacl;
    private static string _confDir = "";
    // 同步锁：防止后台预热与首次同步调用并发访问静态字段
    private static readonly object _lock = new();

    public static void EnsureLoaded(string confDir)
    {
        // 双重检查锁：避免每次调用都进入锁。已加载时无锁快速路径。
        if (_dmx51 != null && _confDir == confDir) return;
        lock (_lock)
        {
            if (_dmx51 != null && _confDir == confDir) return;
            _confDir = confDir;
            _dmx51 = ParseProps(Path.Combine(confDir, "downmix51.properties"));
            _dmx20 = ParseProps(Path.Combine(confDir, "downmix20.properties"));
            var disp = ParseProps(Path.Combine(confDir, "display.properties"));
            _fileToPacl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in disp)
                if (kv.Key.StartsWith("PACL", StringComparison.OrdinalIgnoreCase))
                    _fileToPacl[kv.Value] = kv.Key;   // 文件名 → PACLxxxxx
        }
    }

    private static Dictionary<string, string> ParseProps(string path)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.Length == 0 || t[0] == '#' || t[0] == '!') continue;
                int i = t.IndexOf('=');
                if (i <= 0) continue;
                d[t[..i].Trim()] = t[(i + 1)..].Trim();
            }
        }
        catch { }
        return d;
    }

    private static string ToDb(string raw)
    {
        if (int.TryParse(raw, out int tenths))
        {
            double db = -tenths / 10.0;     // -30 → 3.0
            return db >= 59.5 ? "INF" : db.ToString("0.0", CultureInfo.InvariantCulture);
        }
        return "INF";
    }

    /// <summary>官方全局 2.0 下混系数（downmix20.properties）；无则返回 null（用内置默认）。</summary>
    public static DownmixStereoCoeffs? For20()
    {
        var d = _dmx20;
        if (d == null || !d.ContainsKey("left_xcha")) return null;
        string G(string ch, string part) => d.TryGetValue($"{ch}_{part}", out var v) ? ToDb(v) : "INF";
        return new DownmixStereoCoeffs
        {
            LeftA = G("left", "xcha"), LeftB = G("left", "xchb"),
            RightA = G("right", "xcha"), RightB = G("right", "xchb"),
            CenterA = G("center", "xcha"), CenterB = G("center", "xchb"),
            LfeA = G("lfe", "xcha"), LfeB = G("lfe", "xchb"),
            LsA = G("ls", "xcha"), LsB = G("ls", "xchb"),
            RsA = G("rs", "xcha"), RsB = G("rs", "xchb"),
        };
    }

    /// <summary>按布局的 properties 文件名取官方 5.1 下混系数；无则返回 null（用内置默认）。</summary>
    public static Downmix51Coeffs? For51(string propsFile)
    {
        var dmx = _dmx51; var map = _fileToPacl;
        if (dmx == null || map == null) return null;
        if (!map.TryGetValue(propsFile, out var p)) return null;
        if (!dmx.ContainsKey($"{p}_left_primary")) return null;
        string G(string ch, string part) => dmx.TryGetValue($"{p}_{ch}_{part}", out var v) ? ToDb(v) : "INF";
        return new Downmix51Coeffs
        {
            LeftPrimary = G("left", "primary"), LeftXchA = G("left", "xcha"), LeftXchB = G("left", "xchb"),
            RightPrimary = G("right", "primary"), RightXchA = G("right", "xcha"), RightXchB = G("right", "xchb"),
            CenterPrimary = G("center", "primary"), CenterXchA = G("center", "xcha"), CenterXchB = G("center", "xchb"),
            LfePrimary = G("lfe", "primary"), LfeXchA = G("lfe", "xcha"), LfeXchB = G("lfe", "xchb"),
            LsPrimary = G("ls", "primary"), LsXchA = G("ls", "xcha"), LsXchB = G("ls", "xchb"),
            RsPrimary = G("rs", "primary"), RsXchA = G("rs", "xcha"), RsXchB = G("rs", "xchb"),
        };
    }
}
