using System;
using System.IO;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>轻量 WAV/RIFF 头解析（仅读头，不载入音频数据）。</summary>
public sealed record WavInfo(
    int Channels, int Bits, int SampleRate, double Seconds,
    ushort FormatTag = 0, uint ChannelMask = 0)
{
    public string Ch => Channels <= 0 ? "-" : Channels == 1 ? "M" : Channels.ToString();
    public string Bw => Bits > 0 ? Bits.ToString() : "-";
    public string Fs => SampleRate > 0 ? SampleRate.ToString() : "-";
    public string Duration => Seconds > 0
        ? TimeSpan.FromSeconds(Seconds).ToString(@"hh\:mm\:ss\.fff")
        : "-";

    /// <summary>WAVE_FORMAT_EXTENSIBLE (0xFFFE) 时返回扩展头里的 SubFormat 前 2 字节（PCM=1）。</summary>
    public bool IsExtensible => FormatTag == 0xFFFE;

    public static WavInfo? TryRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != 0x46464952) return null;            // "RIFF"
            br.ReadUInt32();                                          // riff size
            if (br.ReadUInt32() != 0x45564157) return null;           // "WAVE"

            int channels = 0, sampleRate = 0, bits = 0;
            long dataBytes = 0;
            int byteRate = 0;
            ushort formatTag = 0;
            uint channelMask = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                uint id = br.ReadUInt32();
                uint size = br.ReadUInt32();
                long chunkStart = fs.Position;
                long next = chunkStart + size + (size & 1); // chunks 2 字节对齐
                if (id == 0x20746d66) // "fmt "
                {
                    formatTag = br.ReadUInt16();
                    channels = br.ReadUInt16();
                    sampleRate = (int)br.ReadUInt32();
                    byteRate = (int)br.ReadUInt32();
                    br.ReadUInt16();                 // blockAlign
                    bits = br.ReadUInt16();
                    // WAVE_FORMAT_EXTENSIBLE 扩展头
                    if (formatTag == 0xFFFE && size >= 22)
                    {
                        ushort cbSize = br.ReadUInt16();        // 扩展头字节数（通常 22）
                        if (cbSize >= 22)
                        {
                            br.ReadUInt16();                   // wValidBitsPerSample
                            channelMask = br.ReadUInt32();      // dwChannelMask
                            // SubFormat 16 字节（PCM = {00000001-0000-0010-8000-00aa00389b71}）
                        }
                    }
                }
                else if (id == 0x61746164) // "data"
                {
                    // 流式/未知大小时用剩余文件长度
                    dataBytes = (size == 0 || size == 0xFFFFFFFF) ? (fs.Length - chunkStart) : size;
                }
                if (next <= chunkStart || next > fs.Length) break; // 防错/越界
                fs.Position = next;                                 // 始终跳到下一块（读没读满都跳）
            }
            double seconds = 0;
            if (dataBytes > 0)
            {
                if (byteRate <= 0 && sampleRate > 0 && channels > 0 && bits > 0)
                    byteRate = sampleRate * channels * (bits / 8);
                if (byteRate > 0) seconds = (double)dataBytes / byteRate;
            }
            return new WavInfo(channels, bits, sampleRate, seconds, formatTag, channelMask);
        }
        catch { return null; }
    }
}
