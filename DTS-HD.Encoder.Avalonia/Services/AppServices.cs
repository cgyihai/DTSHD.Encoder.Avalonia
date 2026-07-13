using System;
using System.IO;
using System.Threading.Tasks;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>全局服务单例：设置、引擎宿主、任务队列。供各页面共享。</summary>
public static class AppServices
{
    public static AppSettings Settings { get; private set; } = AppSettings.Load();
    public static EngineHost? Host { get; private set; }
    /// StreamTools 专用宿主：原生服务端 DTSToolFramewrk.exe，端口 4442（与编码器 4444 分离）。
    public static EngineHost? ToolsHost { get; private set; }
    public static JobQueueService? Queue { get; private set; }

    /// 工具集是否就绪（基于文件存在性，瞬时判断，不涉及 socket）。
    public static bool ToolsetReady => Settings.ToolsetReady;
    /// 兼容旧用法：以工具集就绪表示"可用"（连接为按需）。
    public static bool EngineReady => ToolsetReady;
    public static bool Connected => Host?.IsConnectedSafe == true;

    public static event Action? EngineStateChanged;

    public static string QueueDir { get; } =
        Path.Combine(Path.GetTempPath(), "DTS-HD_Encoder_Queue");

    /// <summary>轻量初始化：仅创建宿主与队列、挂事件，不阻塞、不强制连接（连接按需）。</summary>
    public static Task InitEngineAsync()
    {
        try
        {
            Directory.CreateDirectory(QueueDir);
            Host?.Dispose();
            Host = new EngineHost(Settings.ToolDir);
            Host.ConnectionStateChanged += _ => EngineStateChanged?.Invoke();
            _ = Host.StartAsync();                 // 轻量（只挂事件）
            ToolsHost?.Dispose();
            ToolsHost = new EngineHost(Settings.ToolDir, "DTSToolFramewrk.exe", 4442);
            // 关键：ToolsHost 也必须挂 ConnectionStateChanged，否则 StreamTools 页面
            // 无法收到"连接成功"通知，ProgressRing 会一直转（看起来像加载慢）。
            ToolsHost.ConnectionStateChanged += _ => EngineStateChanged?.Invoke();
            _ = ToolsHost.StartAsync();
            Queue = new JobQueueService(Host, QueueDir);
            // 后台预连接两个引擎（编码器 4444 + StreamTools 4442），均不阻塞 UI：
            // 进入 StreamTools 页面时通常已完成连接，直接显示绿色 ✓。
            if (ToolsetReady)
            {
                _ = Host.EnsureConnectedAsync();
                _ = ToolsHost.EnsureConnectedAsync();
                // 后台预热下混配置文件（3 个 properties 文件），避免首次进入编码页时
                // LoadDownmixDefaults 在 UI 线程同步读文件。与引擎连接并发执行，无额外开销。
                var confDir = Path.Combine(Settings.ToolDir, "conf");
                _ = Task.Run(() => DownmixDefaults.EnsureLoaded(confDir));
            }
        }
        catch { }
        EngineStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>关闭程序时调用：释放并清除两个原生服务端进程，避免后台残留。</summary>
    public static void ShutdownEngines()
    {
        try { Host?.Dispose(); } catch { }
        try { ToolsHost?.Dispose(); } catch { }
        Host = null;
        ToolsHost = null;
    }

    public static void SaveSettings() => Settings.Save();
}
