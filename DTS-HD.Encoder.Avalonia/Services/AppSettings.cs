using System;
using System.IO;
using System.Text.Json;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 应用持久化设置（%LOCALAPPDATA%\DTSHD.Encoder.Avalonia\settings.json）。
/// 完整移植自 WinUI3 DTSHD.Encoder.Services.AppSettings（仅改命名空间 + 配置目录）。
/// </summary>
public sealed class AppSettings
{
    public string ToolDir { get; set; } = DefaultToolDir();
    public string TempDir { get; set; } = "";
    public string Theme { get; set; } = "Default";   // Default / Light / Dark
    // Mica 默认开启：在 Windows 11+ 配合 WinUIComposition (GPU 硬件合成) 显示桌面云母模糊背景。
    // GPU 承担合成开销，刷新率无限制（144Hz 显示器也能开启）。
    public bool MicaEnabled { get; set; } = true;
    public string Language { get; set; } = "System";
    public bool KeepTempFiles { get; set; } = false;
    public bool AutoSelectNext { get; set; } = true;
    public int LogLines { get; set; } = 800;

    private static string ConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "DTSHD.Encoder.Avalonia", "settings.json");

    private static string DefaultToolDir()
    {
        // 默认读取主程序同目录下的 DTS-HD_Tool（开箱即用——正式版部署时把 DTS-HD_Tool
        // 文件夹拷贝到主程序 exe 同级目录即可）。
        var baseDir = AppContext.BaseDirectory;
        var primary = Path.Combine(baseDir, "DTS-HD_Tool");
        // 候选目录（按优先级）：
        //  1. 主程序 exe 同级 DTS-HD_Tool（正式部署位置，开箱即用）
        //  2. 源码树相对位置（开发调试时 bin/Debug 上溯到仓库根的上级）
        //  3. 当前工作目录下 DTS-HD_Tool（便携式启动兜底）
        // 不再硬编码任何机器专属绝对路径（如 D:\...），保证他机可移植。
        string[] candidates =
        {
            primary,
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "DTS-HD_Tool")),
            Path.Combine(Directory.GetCurrentDirectory(), "DTS-HD_Tool"),
        };
        foreach (var c in candidates)
        {
            try { if (Directory.Exists(c)) return c; } catch { /* 无效路径忽略 */ }
        }
        return primary;
    }

    public static string ExeDir => AppContext.BaseDirectory;

    // ===== ToolsetReady 缓存（启动性能优化）=====
    // 原 ToolsetReady getter 每次访问都做 2 次 File.Exists，启动期被访问 10+ 次，
    // 累积 20+ 次冗余系统调用。改为 Load() 时一次性计算并缓存到字段。
    // ToolDir 在运行时可能被设置页修改，setter 中通过 InvalidateToolsetCache() 重置缓存。
    private bool? _toolsetReadyCache;

    /// <summary>启动时一次性加载并缓存。同步执行，但已精简 IO 次数：
    /// 1 次 JSON 反序列化 + 1 次 Directory.Exists（DefaultToolDir 内）+ 1 次 File.Exists。</summary>
    public static AppSettings Load()
    {
        AppSettings s;
        try
        {
            // JSON 反序列化（仅 1 次 File.Exists + ReadAllText + 反序列化）
            s = (File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath))
                : null) ?? new AppSettings();
        }
        catch { s = new AppSettings(); }

        // 启动时始终优先主程序同目录下的 DTS-HD_Tool（开箱即用）。
        // 用户已将 DTS-HD_Tool 拷贝到编译输出目录，应优先使用该位置而非 settings.json 保存的旧路径。
        // 仅当主程序同目录无 DTS-HD_Tool 时，才回退到 settings.json 保存的路径或默认候选目录。
        var def = DefaultToolDir();  // 内部已做 3 次 Directory.Exists
        if (File.Exists(Path.Combine(def, "DtsJobQueue.exe")))
            s.ToolDir = def;
        else if (Directory.Exists(def))
            s.ToolDir = def;

        // 预计算 ToolsetReady 并缓存（避免每次 getter 都做 File.Exists）
        s.RefreshToolsetCache();
        return s;
    }

    /// <summary>重新计算并缓存 ToolsetReady。ToolDir 改变后或外部需要刷新时调用。</summary>
    public void RefreshToolsetCache()
    {
        _toolsetReadyCache = File.Exists(Path.Combine(ToolDir, "DtsJobQueue.exe"))
                          && File.Exists(Path.Combine(ToolDir, "DTSEncConfig.dll"));
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // 引擎可执行/必需文件存在性检查（每次访问做 File.Exists，仅用于设置页等低频场景）
    public bool DtsJobQueueExists => File.Exists(Path.Combine(ToolDir, "DtsJobQueue.exe"));
    public bool AuthorizerExists => File.Exists(Path.Combine(ToolDir, "MAS-SAS_Authorizer.exe"));
    public bool EncConfigDllExists => File.Exists(Path.Combine(ToolDir, "DTSEncConfig.dll"));
    public bool VerifyExists => File.Exists(Path.Combine(ToolDir, "DTSHDVerify.exe"));
    public bool InfoDumperExists => File.Exists(Path.Combine(ToolDir, "InfoDumper.exe"));

    /// <summary>工具集是否就绪（DtsJobQueue.exe + DTSEncConfig.dll 都存在）。
    /// 启动性能优化：返回 Load() 时缓存的值，避免每次访问都做 2 次 File.Exists。
    /// ToolDir 在运行时改变后，调用 RefreshToolsetCache() 刷新缓存。</summary>
    public bool ToolsetReady => _toolsetReadyCache ??= RefreshAndCheck();

    private bool RefreshAndCheck()
    {
        RefreshToolsetCache();
        return _toolsetReadyCache ?? false;
    }
}
