using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ClaudeOffice.Monitoring;

/// <summary>
/// "승인했다"를 알려 주는 훅이 없어서(도구가 다 끝나야 PostToolUse), 승인 후 오래 도는 명령(빌드 등)은 끝날 때까지 손을 들고 있었다.
/// 명령 실행(Bash·PowerShell) 승인 대기 중에 그 명령을 돌리는 프로세스가 <b>대기 시작 뒤에</b> 새로 생겼으면 승인된 것으로 본다.
///   - 명령은 PermissionRequest 훅 입력(tool_input.command)에서. 프로세스 명령줄에 명령의 알아볼 만한 단어가 들어 있는지로 짝짓는다
///   - 명령을 인코딩해 넘기는 셸(-EncodedCommand)이면 못 알아본다 → 예전처럼 끝날 때 내린다
/// </summary>
internal static partial class ApprovalProbe
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "echo", "then", "else", "elif", "done", "true", "false", "exit", "null", "/dev/null", "2>&1", "Write-Output", "Write-Host",
        "Select-Object", "ForEach-Object", "Where-Object", "Out-String", "Out-Null", "-NoProfile", "-Command", "&&", "||",
    };

    // 경로·옵션·이름에 쓰이는 글자 4개 이상
    [GeneratedRegex(@"[\p{L}\p{N}_\-./\\:]{4,}")]
    private static partial Regex Word();

    /// <summary>명령에서 알아볼 단어(긴 것부터 최대 3개). 흔한 셸 단어·숫자만인 것은 뺀다.</summary>
    public static string[] Tokens(string command) =>
        Word().Matches(command).Select(m => m.Value.Trim('.', ':', '-'))
            .Where(t => t.Length >= 4 && !Common.Contains(t) && t.Any(char.IsLetter))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(t => t.Length)
            .Take(3)
            .ToArray();

    /// <summary>sinceUtc 뒤에 시작한 프로세스 중 명령줄에 command 의 단어가 (2개, 단어가 하나뿐이면 1개) 들어 있는 것이 있는지.</summary>
    public static bool IsRunning(string command, DateTime sinceUtc)
    {
        var tokens = Tokens(command);
        if (tokens.Length == 0) return false;
        int need = Math.Min(2, tokens.Length);
        int self = Environment.ProcessId;
        var minStart = sinceUtc.AddSeconds(-1);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id == self) continue;
                var line = CommandLineIfStartedAfter(p.Id, minStart);
                if (line is null) continue;
                if (tokens.Count(t => line.Contains(t, StringComparison.OrdinalIgnoreCase)) >= need) return true;
            }
        }
        return false;
    }

    /// <summary>minStartUtc 이상에 시작한 프로세스면 명령줄을, 아니거나 읽을 수 없으면 null(예외 없이 핸들 하나로 확인).</summary>
    private static string? CommandLineIfStartedAfter(int pid, DateTime minStartUtc)
    {
        var h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            if (!GetProcessTimes(h, out long created, out _, out _, out _) || DateTime.FromFileTimeUtc(created) < minStartUtc)
                return null;
            return ReadCommandLine(h);
        }
        finally { CloseHandle(h); }
    }

    /// <summary>다른 프로세스의 명령줄(같은 사용자의 프로세스만 읽힌다). Windows 8.1+ ProcessCommandLineInformation.</summary>
    internal static string? CommandLine(int pid)
    {
        var h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return null;
        try { return ReadCommandLine(h); }
        finally { CloseHandle(h); }
    }

    private static string? ReadCommandLine(IntPtr h)
    {
        NtQueryInformationProcess(h, ProcessCommandLineInformation, IntPtr.Zero, 0, out int len);
        if (len <= 0 || len > 1 << 20) return null;
        var buf = Marshal.AllocHGlobal(len);
        try
        {
            if (NtQueryInformationProcess(h, ProcessCommandLineInformation, buf, len, out _) != 0) return null;
            // UNICODE_STRING { USHORT Length; USHORT MaximumLength; PWSTR Buffer } — 글자는 같은 버퍼 뒤쪽에 있다
            int bytes = (ushort)Marshal.ReadInt16(buf);
            var text = Marshal.ReadIntPtr(buf, IntPtr.Size);
            return text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text, bytes / 2);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
