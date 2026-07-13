using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 复刻原版 com.dts.comm.CommClient 的线协议。
/// 帧 = [4 位 ASCII 十进制长度头][UTF-8 payload]。与 DtsJobQueue.exe (localhost:4444) 通信。
/// </summary>
public sealed class DtsCommClient : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public event Action<string>? ResponseReceived;
    /// <summary>连接断开时触发（含异常信息或 null 表示正常关闭）。</summary>
    public event Action<string?>? ConnectionLost;

    public bool IsConnected
    {
        get
        {
            if (_client == null) return false;
            if (!_client.Connected) return false;
            // 通过 Poll 检测连接是否真正可用（TcpClient.Connected 只反映上次 I/O 状态）
            try
            {
                return !(_client.Client.Poll(1, SelectMode.SelectRead) && _client.Client.Available == 0);
            }
            catch { return false; }
        }
    }

    /// <summary>真异步连接（不阻塞 UI 线程）。返回 0 成功。</summary>
    public async Task<int> ConnectAsync(string host, int port, int timeoutMs = 3000)
    {
        // 先清理旧连接
        Disconnect();
        try
        {
            var client = new TcpClient();
            using (var cts = new CancellationTokenSource(timeoutMs))
                await client.ConnectAsync(host, port, cts.Token);
            client.SendBufferSize = 4096;
            client.ReceiveBufferSize = 4096;
            _client = client;
            _stream = client.GetStream();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            return 0;
        }
        catch
        {
            _client = null;
            _stream = null;
            return 1;
        }
    }

    /// <summary>带重试的异步连接（不阻塞 UI）。</summary>
    public async Task<bool> ConnectWithRetryAsync(string host = "localhost", int port = 4444, int attempts = 10, int delayMs = 800)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (await ConnectAsync(host, port) == 0) return true;
            await Task.Delay(delayMs);
        }
        return false;
    }

    /// <summary>发送命令，自动加 4 位长度头。</summary>
    public void SendCommand(string command)
    {
        if (_stream == null) throw new InvalidOperationException("未连接到引擎。");
        byte[] payload = Encoding.UTF8.GetBytes(command);
        if (payload.Length > 9999) throw new ArgumentException("命令过长（>9999 字节）。");
        byte[] frame = Encoding.UTF8.GetBytes(payload.Length.ToString("D4") + command);
        _stream.Write(frame, 0, frame.Length);
        _stream.Flush();
    }

    /// <summary>主动断开连接并清理资源。</summary>
    public void Disconnect()
    {
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }   // CTS 实现 IDisposable，避免内核句柄泄漏
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _stream = null;
        _client = null;
        _cts = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buf = new byte[4096];
        var acc = new List<byte>();
        try
        {
            while (!token.IsCancellationRequested && _stream != null)
            {
                int n = await _stream.ReadAsync(buf.AsMemory(0, buf.Length), token);
                if (n <= 0) break;   // 服务端关闭连接
                for (int i = 0; i < n; i++) acc.Add(buf[i]);
                while (acc.Count >= 4)
                {
                    var arr = acc.ToArray();
                    if (!int.TryParse(Encoding.ASCII.GetString(arr, 0, 4), out int len)) { acc.Clear(); break; }
                    if (acc.Count < 4 + len) break;
                    string payload = Encoding.UTF8.GetString(arr, 4, len);
                    acc.RemoveRange(0, 4 + len);
                    ResponseReceived?.Invoke(payload);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // 连接异常断开，通知上层
            ConnectionLost?.Invoke(ex.Message);
        }
        // 正常退出（服务端关闭或取消）也通知
        if (!token.IsCancellationRequested)
            ConnectionLost?.Invoke(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}
