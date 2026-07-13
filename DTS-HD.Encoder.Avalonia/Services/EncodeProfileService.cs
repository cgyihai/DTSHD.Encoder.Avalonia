using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DTSHD.Encoder.Avalonia.Models;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 用户自定义编码设置档（JSON）的保存/加载。
/// 文件统一存放在【主程序目录\EncoderProfiles】下，默认以日期时间命名。
/// </summary>
public static class EncodeProfileService
{
    /// 设置档目录（主程序路径下）。启动即确保存在。
    public static string ProfilesDir { get; } =
        Path.Combine(AppContext.BaseDirectory, "EncoderProfiles");

    static EncodeProfileService() => EnsureDir();

    public static void EnsureDir()
    {
        try { Directory.CreateDirectory(ProfilesDir); } catch { }
    }

    /// 默认文件名（日期时间），形如 2026-06-04_161530.json。
    public static string DefaultFileName() => $"{DateTime.Now:yyyy-MM-dd_HHmmss}.json";

    public static string DefaultFilePath() => Path.Combine(ProfilesDir, DefaultFileName());

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static void Save(EncodeSettings s, string path)
    {
        EnsureDir();
        File.WriteAllText(path, JsonSerializer.Serialize(s, Opts));
    }

    public static EncodeSettings? Load(string path)
    {
        try { return JsonSerializer.Deserialize<EncodeSettings>(File.ReadAllText(path)); }
        catch { return null; }
    }

    /// <summary>异步加载（用于启动时不阻塞 UI 线程）。</summary>
    public static async Task<EncodeSettings?> LoadAsync(string path)
    {
        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<EncodeSettings>(fs);
        }
        catch { return null; }
    }

    /// 最近一次修改的设置档（用于启动时自动加载）；无则 null。
    public static string? NewestProfilePath()
    {
        try
        {
            EnsureDir();
            return new DirectoryInfo(ProfilesDir).GetFiles("*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }

    /// <summary>异步查找最近一次修改的设置档（在后台线程执行目录枚举，避免阻塞 UI）。</summary>
    public static Task<string?> NewestProfilePathAsync() =>
        Task.Run(NewestProfilePath);
}
