using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 与原生引擎（DtsJobQueue.exe / DTSToolFramewrk.exe）交换文本时使用的编码与路径转换。
///
/// 【背景】原生引擎是老式 Win32 程序，按"系统 ANSI 代码页"（简体中文系统为 GBK/936）解析 socket 命令与 .cfg；
/// 官方 Java GUI 也是用系统默认字符集发送。此前本程序一律用 UTF-8 发送，路径中只要含中文
/// （包括位于 C:\Users\中文用户名\AppData\Local\Temp 下的 .cfg），引擎就打不开文件——
/// 编码失败、输出 0 字节，界面却一直显示"编码中"。
///
/// 【策略】对每个交给引擎的路径：
///   1. 纯 ASCII → 原样使用；
///   2. 否则尝试 Windows 8.3 短路径（纯 ASCII，任何代码页都能正确解析）；
///   3. 否则若能用系统 ANSI 代码页无损表示 → 原样使用（按 ANSI 发送）；
///   4. 都不行 → 返回 null，由调用方给出明确错误提示（而不是让引擎静默失败）。
/// </summary>
public static class EngineText
{
    private static readonly Lazy<Encoding> _encoding = new(CreateEncoding);

    /// <summary>发送给引擎 / 写 .cfg 使用的编码（Windows=系统 ANSI 代码页；其它平台=UTF-8）。</summary>
    public static Encoding Encoding => _encoding.Value;

    private static Encoding CreateEncoding()
    {
        if (!OperatingSystem.IsWindows()) return new UTF8Encoding(false);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            int acp = (int)GetACP();
            if (acp == 65001) return new UTF8Encoding(false);
            // 不可表示的字符抛异常（用于 CanEncode 判断），而不是静默替换成 '?'
            return Encoding.GetEncoding(acp, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        }
        catch { return new UTF8Encoding(false); }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint bufferSize);

    public static bool IsAscii(string s)
    {
        foreach (char c in s) if (c > 0x7F) return false;
        return true;
    }

    /// <summary>能否用引擎编码无损表示。</summary>
    public static bool CanEncode(string s)
    {
        if (IsAscii(s)) return true;
        try { Encoding.GetBytes(s); return true; }
        catch { return false; }
    }

    /// <summary>编码为字节；不可表示的字符替换为 '?'（仅用于发送已校验过的文本）。</summary>
    public static byte[] GetBytes(string s)
    {
        try { return Encoding.GetBytes(s); }
        catch { return Encoding.GetEncoding(Encoding.CodePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback).GetBytes(s); }
    }

    public static string GetString(byte[] bytes, int index, int count) => Encoding.GetString(bytes, index, count);

    /// <summary>Windows 8.3 短路径（仅对已存在的文件/目录有效；卷禁用 8.3 时返回原路径）。</summary>
    public static string? ToShortPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint n = GetShortPathNameW(path, sb, (uint)sb.Capacity);
            if (n == 0) return null;
            if (n > sb.Capacity)
            {
                sb = new StringBuilder((int)n + 1);
                n = GetShortPathNameW(path, sb, (uint)sb.Capacity);
                if (n == 0) return null;
            }
            return sb.ToString();
        }
        catch { return null; }
    }

    /// <summary>已存在的文件/目录 → 引擎可识别的路径；无法表示返回 null。</summary>
    public static string? SafeExistingPath(string path)
    {
        if (string.IsNullOrEmpty(path) || IsAscii(path)) return path;
        var shortPath = ToShortPath(path);
        if (shortPath != null && IsAscii(shortPath)) return shortPath;
        return CanEncode(path) ? path : null;
    }

    /// <summary>
    /// 输出"前缀"（目录 + 文件名，可能尚不存在）→ 引擎可识别的形式。
    /// 目录部分（会被创建）可转为短路径；文件名部分必须能被引擎编码表示。
    /// </summary>
    public static string? SafeOutputPrefix(string directoryWithSeparator, string fileName)
    {
        string dir = directoryWithSeparator;
        if (!string.IsNullOrEmpty(dir) && !IsAscii(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
            var safeDir = SafeExistingPath(dir.TrimEnd('\\', '/'));
            if (safeDir == null) return null;
            dir = safeDir + Path.DirectorySeparatorChar;
        }
        if (!CanEncode(fileName)) return null;
        return dir + fileName;
    }

    /// <summary>
    /// 把命令里带引号的路径参数逐个转换为引擎可识别的形式（StreamTools 的 "T ..." 命令）。
    /// 已存在的文件/目录用 <see cref="SafeExistingPath"/>，尚不存在的输出文件用 <see cref="SafeOutputPrefix"/>；
    /// 非路径参数（时间码、帧率等纯 ASCII）原样保留。无法转换的路径放入 <paramref name="bad"/>。
    /// </summary>
    public static string MakeCommandPathsSafe(string command, out System.Collections.Generic.List<string> bad)
    {
        var badList = new System.Collections.Generic.List<string>();
        string result = System.Text.RegularExpressions.Regex.Replace(command, "\"([^\"]*)\"", m =>
        {
            string arg = m.Groups[1].Value;
            if (IsAscii(arg)) return m.Value;
            string? safe;
            if (File.Exists(arg) || Directory.Exists(arg)) safe = SafeExistingPath(arg);
            else
            {
                string dir = Path.GetDirectoryName(arg) ?? "";
                safe = SafeOutputPrefix(dir.Length > 0 ? dir + Path.DirectorySeparatorChar : "", Path.GetFileName(arg));
            }
            if (safe == null) { badList.Add(arg); return m.Value; }
            return "\"" + safe + "\"";
        });
        bad = badList;
        return result;
    }
}
