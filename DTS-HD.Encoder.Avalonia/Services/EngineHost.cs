using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 负责拉起原生引擎 (DtsJobQueue.exe / MAS-SAS_Authorizer.exe) 并完成 socket 握手。
/// 破解版无需加密狗，握手始终回有效 license code。
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly string _toolDir;
    private readonly string _serverExe;
    private readonly int _port;
    public DtsCommClient Comm { get; } = new();
    public string? LicenseCode { get; private set; }

    /// 授权码（与官方一致）：101 = MAS(含 PBR)，10 = SAS。其它值视为未授权。
    private int LicenseInt => int.TryParse(LicenseCode, out int v) ? v : -1;
    public bool IsMaster => LicenseInt == 101;
    public bool IsAuthorized => LicenseInt is 101 or 10;
    public bool IsConnectedSafe { get { try { return Comm.IsConnected; } catch { return false; } } }

    public event Action<DtsResponse>? ResponseParsed;
    public event Action<string>? RawResponse;   // 原始帧 payload（StreamTools "T ..." 响应）
    /// <summary>连接状态变化时触发（true=已连接，false=已断开）。</summary>
    public event Action<bool>? ConnectionStateChanged;
    /// <summary>诊断日志（供 UI 显示启动细节）。</summary>
    public event Action<string>? DiagnosticLog;

    private bool _hooked;
    private readonly System.Threading.SemaphoreSlim _connectLock = new(1, 1);

    /// <param name="serverExe">原生服务端：编码器=DtsJobQueue.exe(4444)，StreamTools=DTSToolFramewrk.exe(4442)。</param>
    public EngineHost(string toolDir, string serverExe = "DtsJobQueue.exe", int port = 4444)
    {
        _toolDir = toolDir;
        _serverExe = serverExe;
        _port = port;
    }

    private void HookOnce()
    {
        if (_hooked) return;
        _hooked = true;
        Comm.ResponseReceived += payload =>
        {
            RawResponse?.Invoke(payload);
            var r = JobResponseParser.Parse(payload);
            if (r.Kind == DtsResponseKind.License) LicenseCode = r.LicenseCode;
            ResponseParsed?.Invoke(r);
        };
        Comm.ConnectionLost += _ =>
        {
            LicenseCode = null;
            ConnectionStateChanged?.Invoke(false);
        };
    }

    /// <summary>轻量启动：只挂事件，不阻塞（连接改为按需/后台）。</summary>
    public Task<bool> StartAsync() { HookOnce(); return Task.FromResult(true); }

    /// <summary>按需确保已连接（首次/断线时静默启动服务端并异步连接）。线程安全、永不阻塞 UI。</summary>
    public async Task<bool> EnsureConnectedAsync()
    {
        if (IsConnectedSafe) return true;
        await _connectLock.WaitAsync();
        try
        {
            if (IsConnectedSafe) return true;
            HookOnce();

            // 注意：绝不主动启动 MAS-SAS_Authorizer.exe。
            // 官方流程仅在 DtsJobQueue 返回的授权码非 10/101 时才弹授权器；破解版直接回 101。
            //
            // 关键：DtsJobQueue 是【单客户端】服务器——客户端一断开它就停止监听并退出会话。
            // 因此【绝不能】用"连接后立即断开"的方式探测端口（那会把服务器探死）。
            // 唯一安全的做法：直接发起【真实连接】，连上就一直保持；连不上才清理重启。
            var procName = Path.GetFileNameWithoutExtension(_serverExe);

            // ① 若已有实例，先尝试直接连接（连上即保持，不做任何探测式断开）。
            //    用 200ms 高频重试：TCP 连不上立即返回（端口未监听 → 连接被拒），
            //    几乎零开销，连上即停。最多 5×200ms=1s，比旧的 3×400ms=1.2s 更快。
            if (Process.GetProcessesByName(procName).Length > 0)
            {
                LogDiag("尝试连接已有引擎实例…");
                if (await Comm.ConnectWithRetryAsync(port: _port, attempts: 5, delayMs: 200))
                {
                    LogDiag("已连接到现有引擎实例。");
                    ConnectionStateChanged?.Invoke(true);
                    return true;
                }
                // 连不上 → 该实例已失效（多半已停止监听），清理后重启。
                LogDiag("现有实例无法连接（可能已停止监听），清理后重启…");
                KillProcesses(procName);
                await Task.Delay(200);
            }

            // ② 启动新实例，【不再固定等 1200ms】——改用 200ms 高频重试连接作为
            //    "端口就绪"轮询：进程一旦开始监听，下一次重试立刻连上并保持。
            //    TCP 连接被拒是即时的，所以 20×200ms=4s 足以覆盖引擎冷启动，
            //    而旧方案固定等 1200ms + 6s 重试 = 7.2s（最坏情况）。
            //    典型场景：引擎 200~500ms 内开始监听 → 第 1~3 次重试就连上。
            LogDiag($"启动 {_serverExe} …");
            EnsureProcess(_serverExe);

            bool connected = await Comm.ConnectWithRetryAsync(port: _port, attempts: 20, delayMs: 200);
            if (connected)
            {
                LogDiag("引擎已连接。");
                ConnectionStateChanged?.Invoke(true);
            }
            else
            {
                LogDiag($"!! 无法连接 {_serverExe}（已启动但连接失败，请检查 DTS-HD_Tool 依赖）");
            }
            return connected;
        }
        finally { _connectLock.Release(); }
    }

    private void LogDiag(string msg) => DiagnosticLog?.Invoke($"[EngineHost] {msg}");

    /// <summary>结束指定名称的所有进程（用于清理无法连接的僵死服务端实例）。</summary>
    private void KillProcesses(string procName)
    {
        foreach (var p in Process.GetProcessesByName(procName))
        {
            try
            {
                p.Kill(true);
                if (!p.WaitForExit(2000))
                    LogDiag($"!! {procName} (PID={p.Id}) 2 秒内未退出，可能需要手动结束");
            }
            catch (Exception ex) { LogDiag($"结束 {procName} (PID={p.Id}) 失败: {ex.Message}"); }
            finally { p.Dispose(); }   // Process 对象持有 OS 进程句柄，必须 Dispose 避免句柄泄漏
        }
    }

    public void Send(string command) => Comm.SendCommand(command);

    public void Shutdown()
    {
        try { if (Comm.IsConnected) Comm.SendCommand(DtsCommandBuilder.Shutdown); } catch { }
    }

    /// <summary>若服务端未运行则静默启动；返回是否本次启动了它。</summary>
    private bool EnsureProcess(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        if (Process.GetProcessesByName(name).Length > 0)
        {
            LogDiag($"{exe} 已在运行");
            return false;
        }
        var path = Path.Combine(_toolDir, exe);
        if (!File.Exists(path))
        {
            LogDiag($"{exe} 不存在: {path}");
            return false;
        }
        try
        {
            // 分离启动 + 隐藏窗口（不重定向管道——重定向会导致该控制台服务端关闭连接）
            var proc = Process.Start(new ProcessStartInfo(path)
            {
                WorkingDirectory = _toolDir,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (proc != null)
            {
                LogDiag($"{exe} 已启动 (PID={proc.Id})");
                // 短暂等待（150ms）检查进程是否立即退出（崩溃）——
                // 不再阻塞 500ms：后续 ConnectWithRetryAsync 的 200ms 轮询本身就能感知引擎是否存活。
                System.Threading.Thread.Sleep(150);
                if (proc.HasExited)
                    LogDiag($"!! {exe} 启动后立即退出 (ExitCode={proc.ExitCode})，可能缺少依赖或配置");
            }
            return true;
        }
        catch (Exception ex)
        {
            LogDiag($"启动 {exe} 失败: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        Shutdown();        // 发送优雅退出命令（若已连接）
        Comm.Dispose();    // 关闭 socket（DtsJobQueue 会因客户端断开而自行退出）
        // 兜底：确保该服务端进程被清除，避免残留实例下次启动造成混淆。
        try { KillProcesses(Path.GetFileNameWithoutExtension(_serverExe)); } catch { }
    }
}
