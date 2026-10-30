using Microsoft.Win32;

namespace ClaudeOffice;

/// <summary>
/// Windows 시작 시 자동 실행 — 현재 사용자의 시작 프로그램 목록(HKCU\…\Run)에 "ClaudeOffice" 로 등록한다.
/// 관리자 권한이 필요 없고, 끄면 그 값만 지운다. 자동 실행 때는 --autostart 를 붙여 알림 영역에 숨긴 채 켜진다.
/// </summary>
internal static class AutoStart
{
    private const string ValueName = "ClaudeOffice";

    /// <summary>점검 도구가 실제 시작 프로그램 목록을 건드리지 않게 CLAUDEOFFICE_RUN_KEY 로 다른 키를 가리킬 수 있다.</summary>
    private static string KeyPath =>
        Environment.GetEnvironmentVariable("CLAUDEOFFICE_RUN_KEY") is { Length: > 0 } k ? k : @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    private static string? Registered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string;
    }

    public static bool Enabled => Registered() != null;

    /// <summary>등록은 돼 있는데 다른 위치의 Claude Office 를 가리킴(복사본·옛 빌드) — 다시 켜면 지금 프로그램으로 바뀐다.</summary>
    public static bool PointsElsewhere => Registered() is { } r && !string.Equals(r, Command, StringComparison.OrdinalIgnoreCase);

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (on) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
