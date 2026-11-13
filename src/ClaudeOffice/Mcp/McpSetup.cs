using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Mcp;

/// <summary>
/// Claude 데스크톱 앱의 MCP 설정 파일(claude_desktop_config.json)을 찾아
/// Claude Pixel Office MCP 서버("claude-pixel-office")를 등록하거나 해제한다.
/// </summary>
internal static class McpSetup
{
    public const string ServerName = "claude-pixel-office";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Claude 데스크톱 앱이 참조하는 config 파일 경로 목록을 돌려준다.
    /// 1. 표준 Roaming: %APPDATA%\Claude\claude_desktop_config.json
    /// 2. Windows Store/MSIX 패키지: %LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json
    /// </summary>
    public static List<string> GetConfigPaths()
    {
        var paths = new List<string>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            paths.Add(Path.Combine(appData, "Claude", "claude_desktop_config.json"));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var claudeDataDir = Path.Combine(localAppData, "Claude-Data");
            if (Directory.Exists(claudeDataDir))
            {
                try
                {
                    foreach (var d in Directory.EnumerateDirectories(claudeDataDir, "claude_*"))
                    {
                        paths.Add(Path.Combine(d, "claude_desktop_config.json"));
                    }
                }
                catch { }
            }

            var msixClaudeDir = Path.Combine(localAppData, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude");
            if (Directory.Exists(msixClaudeDir))
            {
                paths.Add(Path.Combine(msixClaudeDir, "claude_desktop_config.json"));
            }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>PC에 Claude 데스크톱 앱이 설치되어 있는지 확인한다.</summary>
    public static bool IsClaudeDesktopInstalled()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var msixPkgDir = Path.Combine(localAppData, "Packages", "Claude_pzs8sxrjxfjjc");
            if (Directory.Exists(msixPkgDir)) return true;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData) && Directory.Exists(Path.Combine(appData, "Claude")))
            return true;

        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(progFiles))
        {
            var winApps = Path.Combine(progFiles, "WindowsApps");
            if (Directory.Exists(winApps))
            {
                try
                {
                    if (Directory.EnumerateDirectories(winApps, "Claude_*").Any()) return true;
                }
                catch { }
            }
        }

        return false;
    }

    /// <summary>어느 한 곳이라도 claude-pixel-office MCP 설정이 등록되어 있는지 여부.</summary>
    public static bool IsInstalled()
    {
        foreach (var path in GetConfigPaths())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                if (text.Contains($"\"{ServerName}\"", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// Claude 데스크톱 config에 claude-pixel-office MCP 서버를 등록한다.
    /// 기존 다른 MCP 서버 설정은 그대로 보존된다.
    /// </summary>
    public static bool Install(string? customExePath = null)
    {
        var exePath = customExePath
                      ?? Environment.ProcessPath
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudePixelOffice", "ClaudeOffice.exe");

        var paths = GetConfigPaths();
        bool anySuccess = false;

        foreach (var path in paths)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                JsonObject root;
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path, Encoding.UTF8);
                    root = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                if (!root.TryGetPropertyValue("mcpServers", out var mcpNode) || mcpNode is not JsonObject mcpServers)
                {
                    mcpServers = new JsonObject();
                    root["mcpServers"] = mcpServers;
                }

                mcpServers[ServerName] = new JsonObject
                {
                    ["command"] = exePath,
                    ["args"] = new JsonArray { "--mcp" }
                };

                File.WriteAllText(path, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
                anySuccess = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClaudeOffice] MCP 설정 등록 실패 ({path}): {ex.Message}");
            }
        }

        return anySuccess;
    }

    /// <summary>Claude 데스크톱 config에서 claude-pixel-office MCP 서버 등록을 제거한다.</summary>
    public static bool Uninstall()
    {
        var paths = GetConfigPaths();
        bool anySuccess = false;

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                if (JsonNode.Parse(text) is JsonObject root &&
                    root.TryGetPropertyValue("mcpServers", out var mcpNode) &&
                    mcpNode is JsonObject mcpServers)
                {
                    if (mcpServers.Remove(ServerName))
                    {
                        File.WriteAllText(path, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
                        anySuccess = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClaudeOffice] MCP 설정 해제 실패 ({path}): {ex.Message}");
            }
        }

        return anySuccess;
    }

            /// <summary>사용자가 Claude 프로젝트 '지침(Instructions)'에 복사해 넣을 수 있는 권장 안내 문구 (양방향 연동 지원).</summary>
    public static string GetInstructionsPrompt()
    {
        return L.En
            ? @"[Claude Pixel Office Integration Instructions]
You are a team member working in the Claude Pixel Office.
Always follow these guidelines strictly during conversations and tasks:

1. [Status Reporting (Check-in)]:
   When starting to answer, during major progress steps, or when completing work, always call the MCP tool `office_report_status` to report your status:
   - project_name: Current project name (e.g. ""App Development"")
   - session_name: Current conversation or session title (e.g. ""Server"", ""Client"")
   - status: Choose from ""working"", ""thinking"", ""idle"", or ""done""
   - message: Short 1-line summary of what you are doing (shown in speech bubble)
   - detail: Optional extra log or next steps

2. [Remote User Instructions (Bidirectional Control)]:
   - When calling `office_report_status` or `office_check_instruction`, check the returned result.
   - If it contains ""📢 [새로운 사용자 지시사항이 도착했습니다!]"", it is a priority command sent remotely by the user from Pixel Office or mobile.
   - Immediately pause other work and execute that instruction with top priority."
            : @"[Claude Pixel Office 사무실 연동 지침]
당신은 Claude Pixel Office 가상 사무실에서 협업하는 직원입니다.
작업을 진행할 때 다음 지침을 엄격히 준수하세요:

1. [사무실 상태 보고 (출근)]:
   질문에 답변을 시작할 때, 작업 진행 중 주요 단계, 그리고 작업을 마쳤을 때 반드시 MCP 도구 `office_report_status`를 호출하여 사무실에 상태를 보고하세요.
   - project_name: 현재 프로젝트 이름 (예: ""어플 개발"")
   - session_name: 현재 대화 세션 이름 (예: ""서버"", ""클라이언트"")
   - status: ""working""(작업중), ""thinking""(생각중), ""idle""(대기), ""done""(완료) 중 선택
   - message: 현재 하고 있는 작업에 대한 간결한 한 줄 요약 (사무실 캐릭터 말풍선에 표시됨)
   - detail: 추가 상세 내용이나 다음 단계

2. [원격 사용자 지시 수신 (양방향 연동)]:
   - `office_report_status` 또는 `office_check_instruction` 도구 호출 시 반환값에 ""📢 [새로운 사용자 지시사항이 도착했습니다!]"" 내용이 있으면, 사용자가 Pixel Office 화면이나 모바일에서 당신에게 직접 내린 원격 업무 지시입니다.
   - 지시사항이 확인되면 다른 작업을 멈추고 해당 지시 내용을 최우선으로 즉시 수행하세요.";
    }
}
