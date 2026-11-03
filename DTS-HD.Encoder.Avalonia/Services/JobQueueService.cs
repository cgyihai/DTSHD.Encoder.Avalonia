using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using DTSHD.Encoder.Avalonia.Models;

namespace DTSHD.Encoder.Avalonia.Services;

public sealed class EncodeJob : INotifyPropertyChanged
{
    public int Handle { get; set; }
    public bool FolderBased { get; set; }
    /// 该任务输出/日志路径（供"信息"按钮打开日志）
    public string OutputPath { get; set; } = "";
    public string LogPath { get; set; } = "";

    // —— 引擎侧状态（由 JobQueueService 根据 socket 响应维护，供进度监视使用）——
    /// <summary>引擎分配的任务句柄（来自 J/D/X 帧）；未绑定前为 -1。
    /// 命令 "E E ..." 不携带句柄，引擎自行编号，不能假设与本地 Handle 相同。</summary>
    public int EngineHandle { get; set; } = -1;
    /// <summary>引擎 'D' 帧上报的百分比；未收到为 -1。</summary>
    public int EnginePercent { get; set; } = -1;
    /// <summary>最近一次收到该任务引擎帧的时间（UTC）；从未收到为 MinValue。</summary>
    public DateTime LastEngineFrameUtc { get; set; } = DateTime.MinValue;
    /// <summary>引擎已报告结束（'X' 帧）或连接中断。</summary>
    public bool IsFinal { get; private set; }
    /// <summary>结束码：0=成功，1=已取消，-2=引擎连接断开，其它=引擎错误。</summary>
    public int ResultCode { get; private set; } = -1;
    public string? ResultMessage { get; private set; }

    internal void MarkFinal(int code, string? message)
    {
        ResultCode = code;
        ResultMessage = message;
        IsFinal = true;
    }

    private string _name = "";
    private int _percent;
    private string _status = "排队中";
    public string Name { get => _name; set => Set(ref _name, value); }
    public int Percent { get => _percent; set { if (Set(ref _percent, value)) RaiseDerived(); } }
    public string Status { get => _status; set { if (Set(ref _status, value)) RaiseDerived(); } }

    // —— 仿官方队列：完成打勾、编码中显示进度条 ——
    public bool IsCompleted => _status.Contains("完成") || _status.Contains("Completed");
    public bool IsError => _status.Contains("错误") || _status.Contains("Error");
    public bool InProgress => !IsCompleted && !IsError &&
        (_status.Contains("编码") || _status.Contains("PBR") || _status.Contains("MD5") || _status.Contains("校验"));
    // Avalonia 的 Control.IsVisible 为 bool，故用 bool 取代 WinUI3 的 Visibility 枚举。
    public bool CheckVisibility => IsCompleted;
    public bool ProgressVisibility => InProgress;

    private void RaiseDerived()
    {
        foreach (var n in new[] { nameof(IsCompleted), nameof(IsError), nameof(InProgress), nameof(CheckVisibility), nameof(ProgressVisibility) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        return true;
    }
}

/// <summary>路径无法交给引擎（含无法表示的字符）时抛出；Message 可直接展示给用户。</summary>
public sealed class EnginePathException(string message) : Exception(message);

/// <summary>
/// 任务队列服务：负责写 .cfg、提交编码命令、按引擎响应更新任务状态。
/// （对应原版 JobQue + JobQueue 的客户端职责）
/// </summary>
public sealed class JobQueueService
{
    private readonly EngineHost _host;
    private readonly string _queueDir;
    private int _nextHandle = 1;
    public ConcurrentDictionary<int, EncodeJob> Jobs { get; } = new();

    public event Action? Changed;

    /// <summary>某任务的引擎状态更新（进度 / 结束）；在 socket 读取线程触发。</summary>
    public event Action<EncodeJob, DtsResponse>? JobUpdated;

    private readonly object _bindLock = new();

    public JobQueueService(EngineHost host, string queueDir)
    {
        _host = host;
        _queueDir = queueDir;
        _host.ResponseParsed += OnResponse;
        // 引擎进程崩溃/退出 → socket 断开：把所有未结束的任务标记为失败，避免界面无限"编码中"
        _host.ConnectionStateChanged += connected => { if (!connected) FailActiveJobs("DtsJobQueue 引擎连接已断开（引擎进程可能已退出或崩溃），编码未完成。"); };
    }

    /// 提交编码任务：写 cfg → 转换为引擎可识别的路径 → 发送命令。
    /// 路径无法被引擎识别时抛出 <see cref="EnginePathException"/>（含可直接展示给用户的说明）。
    public EncodeJob Submit(EncodeSettings settings, bool folderBased)
    {
        int handle = _nextHandle++;

        // 写 .cfg（放在引擎可识别的目录；文件名用 ASCII，避免中文用户名/中文输出名导致引擎打不开 .cfg）
        var cfgName = EngineText.IsAscii(settings.SaveToFilename) && settings.SaveToFilename.Length > 0
            ? settings.SaveToFilename + ".cfg" : "encode.cfg";
        var cfgPath = Path.Combine(_queueDir, handle.ToString(), cfgName);
        string? safeCfg = null;
        foreach (var dir in QueueDirCandidates(handle))
        {
            try
            {
                cfgPath = Path.Combine(dir, cfgName);
                new CfgWriter(settings).WriteToFile(cfgPath);
                safeCfg = EngineText.SafeExistingPath(cfgPath);
                if (safeCfg != null) break;
            }
            catch { /* 目录不可写 → 换下一个候选目录 */ }
        }
        settings.ConfigFilePath = cfgPath;
        if (safeCfg == null)
            throw new EnginePathException($"无法创建引擎可读取的 .cfg 文件：{cfgPath}");

        var paths = BuildEnginePaths(settings, safeCfg);

        var job = new EncodeJob { Handle = handle, Name = settings.SaveToFilename, FolderBased = folderBased };
        Jobs[handle] = job;
        Changed?.Invoke();   // 先入队显示，避免发送异常时队列不更新

        string cmd = DtsCommandBuilder.BuildEncodeCommand(settings, folderBased, paths);
        LastCommand = cmd;
        try { _host.Send(cmd); }
        catch (Exception ex) { job.Status = "提交失败: " + ex.Message; job.MarkFinal(-3, ex.Message); Changed?.Invoke(); throw; }
        return job;
    }

    /// <summary>实际发给引擎的命令（路径已转换），供日志记录。</summary>
    public string? LastCommand { get; private set; }

    private IEnumerable<string> QueueDirCandidates(int handle)
    {
        yield return Path.Combine(_queueDir, handle.ToString());
        // %TEMP% 含引擎无法识别的字符（如中文用户名且卷禁用 8.3 短名）时的备用位置
        string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrEmpty(common))
            yield return Path.Combine(common, "DTS-HD_Encoder_Queue", handle.ToString());
        yield return Path.Combine(AppContext.BaseDirectory, "EncoderQueue", handle.ToString());
    }

    /// <summary>把输入/输出/CSV/AAF 路径转换为引擎可识别的形式；任何一个无法转换都抛出明确错误。</summary>
    private EnginePaths BuildEnginePaths(EncodeSettings s, string safeCfg)
    {
        var bad = new List<string>();

        string Existing(string path)
        {
            var safe = EngineText.SafeExistingPath(path);
            if (safe == null) { bad.Add(path); return path; }
            return safe;
        }

        // 输入：InputFileOrder 是若干个带引号的路径
        var inputs = Regex.Matches(s.InputFileOrder, "\"([^\"]*)\"")
            .Select(m => "\"" + Existing(m.Groups[1].Value) + "\"");
        string inputOrder = string.Join(" ", inputs);

        string outputPrefix = EngineText.SafeOutputPrefix(s.SaveToDirectory, s.SaveToFilename)
            ?? AddBad(s.SaveToDirectory + s.SaveToFilename + s.FileType);
        string csv = s.SeamlessCsvBranchPoints && !string.IsNullOrEmpty(s.SeamlessCsvFilename) ? Existing(s.SeamlessCsvFilename) : s.SeamlessCsvFilename;
        string aaf = s.UsingAaf && !string.IsNullOrEmpty(s.AafFilename) ? Existing(s.AafFilename) : s.AafFilename;

        if (bad.Count > 0)
            throw new EnginePathException(
                "以下路径包含 DTS 引擎无法识别的字符（当前系统代码页无法表示，且该磁盘未启用 8.3 短文件名）：\n  " +
                string.Join("\n  ", bad) +
                "\n\n请把文件/文件夹改名为英文或数字后重试。");
        return new EnginePaths(inputOrder, safeCfg, outputPrefix, csv, aaf);

        string AddBad(string p) { bad.Add(p); return p; }
    }

    /// <summary>本地句柄 → 发给引擎的句柄（已绑定引擎句柄时用引擎的）。</summary>
    private int EngineHandleOf(int handle) =>
        Jobs.TryGetValue(handle, out var j) && j.EngineHandle >= 0 ? j.EngineHandle : handle;

    public void Cancel(int handle)
    {
        if (Jobs.TryGetValue(handle, out var j))
            _host.Send(DtsCommandBuilder.CancelCommand(EngineHandleOf(handle), j.FolderBased));
    }

    public void Remove(int handle) => _host.Send(DtsCommandBuilder.Remove(EngineHandleOf(handle)));
    public void MoveUp(int handle) => _host.Send(DtsCommandBuilder.MoveUp(EngineHandleOf(handle)));
    public void MoveDown(int handle) => _host.Send(DtsCommandBuilder.MoveDown(EngineHandleOf(handle)));

    /// <summary>
    /// 根据引擎帧找到对应的本地任务：优先按已绑定的引擎句柄；否则把该引擎句柄绑定到
    /// 最早提交、尚未绑定且未结束的任务（DtsJobQueue 单客户端、按提交顺序执行）。
    /// </summary>
    private EncodeJob? Resolve(DtsResponse r)
    {
        if (r.JobHandle < 0) return null;
        lock (_bindLock)
        {
            var bound = Jobs.Values.FirstOrDefault(j => j.EngineHandle == r.JobHandle);
            if (bound != null) return bound;
            var pending = Jobs.Values.Where(j => j.EngineHandle < 0 && !j.IsFinal).OrderBy(j => j.Handle).FirstOrDefault();
            if (pending != null) pending.EngineHandle = r.JobHandle;
            return pending;
        }
    }

    private void FailActiveJobs(string message)
    {
        bool any = false;
        foreach (var j in Jobs.Values)
        {
            if (j.IsFinal) continue;
            j.MarkFinal(-2, message);
            j.Status = "错误: 引擎连接断开";
            any = true;
            JobUpdated?.Invoke(j, new DtsResponse(DtsResponseKind.Finished, 'X', j.EngineHandle, ResultCode: -2, Text: message));
        }
        if (any) Changed?.Invoke();
    }

    /// 从本地队列移除单个任务（不影响已生成的输出文件）。
    public void RemoveLocal(int handle)
    {
        if (Jobs.TryRemove(handle, out _)) Changed?.Invoke();
    }

    /// 清除所有"已完成/出错"的任务（仿官方 Clear All Complete）。
    public void ClearCompleted()
    {
        bool any = false;
        foreach (var kv in Jobs.ToArray())
            if (kv.Value.IsCompleted || kv.Value.IsError) any |= Jobs.TryRemove(kv.Key, out _);
        if (any) Changed?.Invoke();
    }

    /// 清空整个本地队列。
    public void ClearAll()
    {
        if (!Jobs.IsEmpty) { Jobs.Clear(); Changed?.Invoke(); }
    }

    private void OnResponse(DtsResponse r)
    {
        if (r.Kind is not (DtsResponseKind.JobStarted or DtsResponseKind.Pbr or DtsResponseKind.Progress
                           or DtsResponseKind.Finished or DtsResponseKind.Removed)) return;
        var job = Resolve(r);
        if (job == null) return;
        job.LastEngineFrameUtc = DateTime.UtcNow;
        switch (r.Kind)
        {
            case DtsResponseKind.JobStarted: job.Status = "编码中"; break;
            case DtsResponseKind.Pbr: job.Status = "PBR 分析"; break;
            case DtsResponseKind.Progress:
                if (r.Percent >= 0) { job.Percent = r.Percent; job.EnginePercent = r.Percent; }
                job.Status = r.Percent == 100 ? "校验 MD5" : $"编码中 {r.Percent}%";
                break;
            case DtsResponseKind.Finished:
                if (r.ResultCode == 0) job.Percent = 100;
                job.Status = r.ResultCode == 0 ? "完成" : r.ResultCode == 1 ? "已取消" : $"错误: {r.Text}";
                job.MarkFinal(r.ResultCode, r.Text);
                break;
            case DtsResponseKind.Removed: Jobs.TryRemove(job.Handle, out _); break;
        }
        JobUpdated?.Invoke(job, r);
        Changed?.Invoke();
    }
}
