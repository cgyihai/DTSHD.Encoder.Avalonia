namespace DTSHD.Encoder.Avalonia.Services;

public enum DtsResponseKind
{
    License, DongleRemoved, FolderCount, JobStarted, Progress, Pbr,
    MovedUp, MovedDown, Removed, Finished, Unknown
}

public sealed record DtsResponse(
    DtsResponseKind Kind, char Raw, int JobHandle,
    int Percent = -1, int ResultCode = -1, string? Text = null, string? LicenseCode = null);

/// <summary>复刻 jobqueue.JobQueue.doResponseAction 的响应解析。</summary>
public static class JobResponseParser
{
    // payload: [0]=类型, [2..7]=hJob(6字符trim), 其后=数据
    public static DtsResponse Parse(string p)
    {
        if (string.IsNullOrEmpty(p)) return new(DtsResponseKind.Unknown, '\0', -1);
        char c = p[0];
        if (c == 'C') return new(DtsResponseKind.License, c, -1, LicenseCode: p.Length > 2 ? p[2..].Trim() : null);
        if (c == 'R') return new(DtsResponseKind.DongleRemoved, c, -1);

        int hJob = -1;
        if (p.Length >= 8) int.TryParse(p.Substring(2, 6).Trim(), out hJob);

        switch (c)
        {
            case 'F':
                return new(DtsResponseKind.FolderCount, c, hJob, ResultCode: SafeInt(Sub(p, 9)));
            case 'J':
                return new(DtsResponseKind.JobStarted, c, hJob, Text: Sub(p, 9));
            case 'u': return new(DtsResponseKind.MovedUp, c, hJob);
            case 'd': return new(DtsResponseKind.MovedDown, c, hJob);
            case 'r': return new(DtsResponseKind.Removed, c, hJob);
            case 'P': return new(DtsResponseKind.Pbr, c, hJob, Text: "PBR Analysis");
            case 'D':
            {
                string rest = Sub(p, 9);
                int pct = rest.Length >= 3 ? SafeInt(rest.Substring(0, 3).Trim()) : -1;
                return new(DtsResponseKind.Progress, c, hJob, Percent: pct,
                           Text: pct == 100 ? "Validating MD5" : rest.Length > 3 ? rest[3..].Trim() : rest);
            }
            case 'X':
            {
                int k = p.Length >= 14 ? SafeInt(p.Substring(9, 5).Trim()) : -1;
                string? msg = (k != 0 && k != 1 && p.Length > 15) ? p[15..] : null;
                return new(DtsResponseKind.Finished, c, hJob, ResultCode: k, Text: msg);
            }
            default:
                return new(DtsResponseKind.Unknown, c, hJob);
        }
    }

    private static string Sub(string s, int i) => s.Length > i ? s[i..] : "";
    private static int SafeInt(string s) => int.TryParse(s, out int v) ? v : -1;
}
