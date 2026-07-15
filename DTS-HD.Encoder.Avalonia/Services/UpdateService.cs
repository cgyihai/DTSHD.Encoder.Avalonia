using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>一次更新检查的结果。</summary>
/// <param name="LatestVersion">远端最新版本号（已去掉前缀 v）。</param>
/// <param name="CurrentVersion">本机当前版本号。</param>
/// <param name="DownloadUrl">下载/发布页地址（Release 的 html_url）。</param>
/// <param name="ReleaseName">Release 标题（可空）。</param>
/// <param name="IsNewer">远端是否比本机更新。</param>
public sealed record UpdateInfo(string LatestVersion, string CurrentVersion, string DownloadUrl, string? ReleaseName, bool IsNewer);

/// <summary>
/// GitHub 在线更新检查（公开仓，免鉴权）。
/// 查询 Releases API 的最新 Release，与本机程序集版本比较，判断是否有新版本。
/// 仅"检查 + 给出下载页地址"，不做自动下载覆盖（自包含多文件应用运行中无法安全替换自身）。
/// </summary>
public static class UpdateService
{
    public const string Owner = "cgyihai";
    public const string Repo = "DTSHD.Encoder.Avalonia";

    /// <summary>Releases 页面地址（查询失败或想手动查看时的兜底跳转目标）。</summary>
    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Repo}/releases";

    // 单例 HttpClient（避免频繁创建导致的 socket 耗尽）；GitHub API 要求带 User-Agent。
    private static readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DTSHD.Encoder.Avalonia-UpdateChecker/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>本机当前版本号（取自程序集版本，与 csproj &lt;Version&gt; 一致），形如 "1.0.1"。</summary>
    public static string CurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "1.0.1" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// 查询最新 Release。返回 null 表示查询失败（网络不可用 / API 限流 / 尚无 Release）。
    /// 全程吞异常，绝不因更新检查影响主程序稳定性。
    /// </summary>
    public static async Task<UpdateInfo?> CheckLatestAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            string html = root.TryGetProperty("html_url", out var h) ? (h.GetString() ?? ReleasesPageUrl) : ReleasesPageUrl;
            string? name = root.TryGetProperty("name", out var n) ? n.GetString() : null;

            // 版本号来源：优先 tag_name；很多仓库 tag 是固定字样（如 "Release"），
            // 真正版本写在 Release 标题 name 里（如 "v1.0.0"）→ tag 解析不出时回退用 name。
            var latestVer = NormalizeVersion(tag) ?? NormalizeVersion(name);
            var currentVer = NormalizeVersion(CurrentVersion());
            bool newer = latestVer != null && currentVer != null && latestVer > currentVer;

            // 展示用版本号：能解析出的规整版本优先，否则退回 name/tag 原文（去掉前缀 v 与空白）。
            string display = latestVer?.ToString()
                             ?? (name?.Trim().TrimStart('v', 'V') is { Length: > 0 } nm ? nm : tag.TrimStart('v', 'V'));

            return new UpdateInfo(display, CurrentVersion(), html, name, newer);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把 "v1.0.2" / "1.0.2-beta" 之类规整为可比较的 Version；无法解析返回 null。</summary>
    private static Version? NormalizeVersion(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().TrimStart('v', 'V');
        int dash = s.IndexOf('-');           // 去掉 -beta / -rc 等预发布后缀
        if (dash > 0) s = s[..dash];
        return Version.TryParse(s, out var v) ? v : null;
    }
}
