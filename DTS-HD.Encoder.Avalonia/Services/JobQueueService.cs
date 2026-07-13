using System;
using System.Collections.Concurrent;
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

    public JobQueueService(EngineHost host, string queueDir)
    {
        _host = host;
        _queueDir = queueDir;
        _host.ResponseParsed += OnResponse;
    }

    /// 提交编码任务：写 cfg → 发送命令。
    public EncodeJob Submit(EncodeSettings settings, bool folderBased)
    {
        int handle = _nextHandle++;
        var jobDir = Path.Combine(_queueDir, handle.ToString());
        Directory.CreateDirectory(jobDir);

        // 写 .cfg 并回填路径
        var cfgPath = Path.Combine(jobDir, settings.SaveToFilename + ".cfg");
        new CfgWriter(settings).WriteToFile(cfgPath);
        settings.ConfigFilePath = cfgPath;

        var job = new EncodeJob { Handle = handle, Name = settings.SaveToFilename, FolderBased = folderBased };
        Jobs[handle] = job;
        Changed?.Invoke();   // 先入队显示，避免发送异常时队列不更新

        string cmd = DtsCommandBuilder.BuildEncodeCommand(settings, folderBased);
        try { _host.Send(cmd); }
        catch (Exception ex) { job.Status = "提交失败: " + ex.Message; Changed?.Invoke(); throw; }
        return job;
    }

    public void Cancel(int handle)
    {
        if (Jobs.TryGetValue(handle, out var j))
            _host.Send(DtsCommandBuilder.CancelCommand(handle, j.FolderBased));
    }

    public void Remove(int handle) => _host.Send(DtsCommandBuilder.Remove(handle));
    public void MoveUp(int handle) => _host.Send(DtsCommandBuilder.MoveUp(handle));
    public void MoveDown(int handle) => _host.Send(DtsCommandBuilder.MoveDown(handle));

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
        if (r.JobHandle < 0 || !Jobs.TryGetValue(r.JobHandle, out var job)) return;
        switch (r.Kind)
        {
            case DtsResponseKind.JobStarted: job.Status = "编码中"; break;
            case DtsResponseKind.Pbr: job.Status = "PBR 分析"; break;
            case DtsResponseKind.Progress:
                if (r.Percent >= 0) job.Percent = r.Percent;
                job.Status = r.Percent == 100 ? "校验 MD5" : $"编码中 {r.Percent}%";
                break;
            case DtsResponseKind.Finished:
                job.Percent = 100;
                job.Status = r.ResultCode == 0 ? "完成" : r.ResultCode == 1 ? "已取消" : $"错误: {r.Text}";
                break;
            case DtsResponseKind.Removed: Jobs.TryRemove(r.JobHandle, out _); break;
        }
        Changed?.Invoke();
    }
}
