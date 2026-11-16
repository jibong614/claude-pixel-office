using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeOffice.Mcp;

/// <summary>AI 플랫폼별 감지 및 연동 상태 정보.</summary>
public sealed record AiPlatformInfo(
    string Id,
    string Name,
    string Type,
    string Icon,
    bool IsInstalled,
    bool IsConfigured,
    string? ConfigPath,
    string Description,
    string? InstructionsPrompt = null);

/// <summary>
/// 다양한 AI 플랫폼(Claude 데스크톱, Cursor, Windsurf, ChatGPT, Gemini, DeepSeek 등)의
/// 설치 및 MCP 설정을 감지하고 원클릭 자동 등록/해제를 제공하는 관리자.
/// </summary>
internal static class AiPlatformManager
{
    public const string ServerName = McpSetup.ServerName; // "claude-pixel-office"

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>내 PC의 주요 AI 플랫폼 설치 및 MCP 연동 상태를 전수 스캔한다.</summary>
    public static List<AiPlatformInfo> ScanAll()
    {
        var list = new List<AiPlatformInfo>();

        // 2. Cursor 에디터
        var cursorConfig = GetCursorConfigPath();
        bool cursorInstalled = IsCursorInstalled(cursorConfig);
        bool cursorConfigured = IsMcpFileConfigured(cursorConfig);
        list.Add(new AiPlatformInfo(
            "cursor",
            "Cursor 에디터",
            "mcp",
            "⚡",
            cursorInstalled,
            cursorConfigured,
            cursorConfig,
            "Cursor AI 에디터의 프로젝트/전역 MCP 도구 연동 (~/.cursor/mcp.json)",
            GetUniversalInstructionsPrompt("Cursor")
        ));

        // 3. Windsurf 에디터
        var windsurfConfig = GetWindsurfConfigPath();
        bool windsurfInstalled = IsWindsurfInstalled(windsurfConfig);
        bool windsurfConfigured = IsMcpFileConfigured(windsurfConfig);
        list.Add(new AiPlatformInfo(
            "windsurf",
            "Windsurf 에디터",
            "mcp",
            "🏄",
            windsurfInstalled,
            windsurfConfigured,
            windsurfConfig,
            "Codeium Windsurf 에디터의 Cascade MCP 도구 연동",
            GetUniversalInstructionsPrompt("Windsurf")
        ));

        // 4. Google Antigravity (Gemini)
        var antigravityConfig = GetAntigravityConfigPath();
        bool antigravityInstalled = IsAntigravityInstalled(antigravityConfig);
        bool antigravityConfigured = IsMcpFileConfigured(antigravityConfig);
        list.Add(new AiPlatformInfo(
            "antigravity",
            "Google Antigravity (Gemini)",
            "mcp",
            "✨",
            antigravityInstalled,
            antigravityConfigured,
            antigravityConfig,
            "Google Antigravity IDE의 Gemini 에이전트 전역 MCP 연동 (~/.gemini/config/mcp_config.json)",
            GetUniversalInstructionsPrompt("Antigravity")
        ));

        // 5. ChatGPT 데스크톱 / 웹
        bool chatGptConfigured = IsPlatformManuallyConfigured("chatgpt");
        bool chatGptInstalled = IsChatGptInstalled() || chatGptConfigured;
        list.Add(new AiPlatformInfo(
            "chatgpt",
            "ChatGPT (데스크톱/웹)",
            "webhook",
            "🟢",
            chatGptInstalled,
            chatGptConfigured,
            null,
            "OpenAI ChatGPT 대화 및 맞춤형 지침(Custom Instructions) 웹훅 연동",
            GetChatGptInstructionsPrompt()
        ));

        // 6. Google Gemini (웹 / API)
        bool geminiConfigured = IsPlatformManuallyConfigured("gemini");
        list.Add(new AiPlatformInfo(
            "gemini",
            "Google Gemini (웹/API)",
            "webhook",
            "✨",
            geminiConfigured,
            geminiConfigured,
            null,
            "Google Gemini 대화, 이미지 생성(Imagen) 및 스크립트 웹훅 연동",
            GetGeminiInstructionsPrompt()
        ));

        // 7. DeepSeek / 로컬 LLM
        bool deepSeekConfigured = IsPlatformManuallyConfigured("deepseek");
        bool deepSeekInstalled = IsOllamaOrDeepSeekInstalled() || deepSeekConfigured;
        list.Add(new AiPlatformInfo(
            "deepseek",
            "DeepSeek / 로컬 LLM",
            "webhook",
            "🐬",
            deepSeekInstalled,
            deepSeekConfigured,
            null,
            "DeepSeek 및 Ollama 등 내 PC에서 실행되는 로컬 AI 에이전트 연동",
            GetLocalLlmInstructionsPrompt()
        ));

        return list;
    }

    private static string GetConfiguredStatePath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "ClaudePixelOffice", "configured_ai_platforms.json");
    }

    public static bool IsPlatformManuallyConfigured(string platformId)
    {
        if (string.IsNullOrWhiteSpace(platformId)) return false;
        try
        {
            var path = GetConfiguredStatePath();
            if (!File.Exists(path)) return false;
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (JsonNode.Parse(text) is JsonArray arr)
            {
                return arr.Any(x => string.Equals(x?.GetValue<string>(), platformId, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch { }
        return false;
    }

    public static bool SetPlatformManuallyConfigured(string platformId, bool configured)
    {
        if (string.IsNullOrWhiteSpace(platformId)) return false;
        try
        {
            var path = GetConfiguredStatePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (!string.IsNullOrWhiteSpace(text) && JsonNode.Parse(text) is JsonArray existing)
                {
                    foreach (var item in existing)
                    {
                        var val = item?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(val)) set.Add(val);
                    }
                }
            }

            if (configured) set.Add(platformId.Trim().ToLowerInvariant());
            else set.Remove(platformId.Trim().ToLowerInvariant());

            var outArr = new JsonArray();
            foreach (var s in set.OrderBy(x => x)) outArr.Add(s);
            File.WriteAllText(path, outArr.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiPlatformManager] 플랫폼 상태 저장 실패 ({platformId}): {ex.Message}");
            return false;
        }
    }

    /// <summary>특정 플랫폼에 Pixel Office MCP 또는 웹훅 연동을 자동 등록한다.</summary>
    public static bool ConfigurePlatform(string platformId, string? customPath = null)
    {
        if (string.Equals(platformId, "claude-desktop", StringComparison.OrdinalIgnoreCase))
        {
            return McpSetup.Install();
        }
        if (string.Equals(platformId, "cursor", StringComparison.OrdinalIgnoreCase))
        {
            var path = !string.IsNullOrWhiteSpace(customPath) ? customPath : GetCursorConfigPath();
            return ConfigureMcpFile(path);
        }
        if (string.Equals(platformId, "windsurf", StringComparison.OrdinalIgnoreCase))
        {
            var path = !string.IsNullOrWhiteSpace(customPath) ? customPath : GetWindsurfConfigPath();
            return ConfigureMcpFile(path);
        }
        if (string.Equals(platformId, "antigravity", StringComparison.OrdinalIgnoreCase))
        {
            var path = !string.IsNullOrWhiteSpace(customPath) ? customPath : GetAntigravityConfigPath();
            return ConfigureMcpUrlFile(path, $"http://127.0.0.1:{Hooks.HookServer.Port}/mcp");
        }
        if (!string.IsNullOrWhiteSpace(customPath) && (File.Exists(customPath) || customPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            return ConfigureMcpFile(customPath);
        }
        if (string.Equals(platformId, "chatgpt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(platformId, "gemini", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(platformId, "deepseek", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(platformId))
        {
            return SetPlatformManuallyConfigured(platformId, true);
        }

        return false;
    }

    /// <summary>특정 플랫폼에서 Pixel Office MCP 또는 웹훅 연동 설정을 제거한다.</summary>
    public static bool UnconfigurePlatform(string platformId, string? customPath = null)
    {
        if (string.Equals(platformId, "claude-desktop", StringComparison.OrdinalIgnoreCase))
        {
            return McpSetup.Uninstall();
        }
        if (string.Equals(platformId, "cursor", StringComparison.OrdinalIgnoreCase))
        {
            var path = !string.IsNullOrWhiteSpace(customPath) ? customPath : GetCursorConfigPath();
            return UnconfigureMcpFile(path);
        }
        if (string.Equals(platformId, "windsurf", StringComparison.OrdinalIgnoreCase))
        {
            var path = !string.IsNullOrWhiteSpace(customPath) ? customPath : GetWindsurfConfigPath();
            return UnconfigureMcpFile(path);
        }
        if (string.Equals(platformId, "antigravity", StringComparison.OrdinalIgnoreCase))
        {
            var path = !string.IsNullOrWhiteSpace(customPath) ? customPath : GetAntigravityConfigPath();
            return UnconfigureMcpFile(path);
        }
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return UnconfigureMcpFile(customPath);
        }
        if (string.Equals(platformId, "chatgpt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(platformId, "gemini", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(platformId, "deepseek", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(platformId))
        {
            return SetPlatformManuallyConfigured(platformId, false);
        }

        return false;
    }

    public static string GetCursorConfigPath()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(user, ".cursor", "mcp.json");
    }

    public static string GetWindsurfConfigPath()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(user, ".codeium", "windsurf", "mcp_config.json");
    }

    public static string GetAntigravityConfigPath()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(user, ".gemini", "config", "mcp_config.json");
    }

    public static bool IsAntigravityInstalled(string configPath)
    {
        if (File.Exists(configPath)) return true;
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(Path.Combine(user, ".gemini"))) return true;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (File.Exists(Path.Combine(local, "Programs", "antigravity", "Antigravity.exe"))) return true;

        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (Directory.Exists(Path.Combine(appdata, "Antigravity"))) return true;

        return false;
    }

    public static bool IsCursorInstalled(string configPath)
    {
        if (File.Exists(configPath)) return true;
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(Path.Combine(user, ".cursor"))) return true;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (File.Exists(Path.Combine(local, "Programs", "cursor", "Cursor.exe"))) return true;

        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (Directory.Exists(Path.Combine(appdata, "Cursor"))) return true;

        return false;
    }

    public static bool IsWindsurfInstalled(string configPath)
    {
        if (File.Exists(configPath)) return true;
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(Path.Combine(user, ".codeium", "windsurf"))) return true;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (File.Exists(Path.Combine(local, "Programs", "Windsurf", "Windsurf.exe"))) return true;

        return false;
    }

    public static bool IsChatGptInstalled()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (File.Exists(Path.Combine(local, "Programs", "ChatGPT", "ChatGPT.exe"))) return true;
        if (Directory.Exists(Path.Combine(local, "ChatGPT"))) return true;
        var packagesDir = Path.Combine(local, "Packages");
        if (Directory.Exists(packagesDir))
        {
            try
            {
                if (Directory.EnumerateDirectories(packagesDir, "OpenAI.ChatGPT*").Any()) return true;
            }
            catch { }
        }
        return false;
    }

    public static bool IsOllamaOrDeepSeekInstalled()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (File.Exists(Path.Combine(local, "Programs", "Ollama", "ollama.exe"))) return true;

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(Path.Combine(user, ".ollama"))) return true;

        return false;
    }

    public static bool IsMcpFileConfigured(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            return text.Contains($"\"{ServerName}\"", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool ConfigureMcpUrlFile(string path, string url)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            JsonObject root;
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path, Encoding.UTF8).Trim();
                root = !string.IsNullOrWhiteSpace(text)
                    ? (JsonNode.Parse(text) as JsonObject ?? new JsonObject())
                    : new JsonObject();
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
                ["url"] = url
            };

            File.WriteAllText(path, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiPlatformManager] MCP URL 설정 등록 실패 ({path}): {ex.Message}");
            return false;
        }
    }

    public static bool ConfigureMcpFile(string path)
    {
        var exePath = Environment.ProcessPath
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudePixelOffice", "ClaudeOffice.exe");

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            JsonObject root;
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path, Encoding.UTF8).Trim();
                root = !string.IsNullOrWhiteSpace(text)
                    ? (JsonNode.Parse(text) as JsonObject ?? new JsonObject())
                    : new JsonObject();
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
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiPlatformManager] MCP 설정 등록 실패 ({path}): {ex.Message}");
            return false;
        }
    }

    public static bool UnconfigureMcpFile(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (!string.IsNullOrWhiteSpace(text) &&
                JsonNode.Parse(text) is JsonObject root &&
                root.TryGetPropertyValue("mcpServers", out var mcpNode) &&
                mcpNode is JsonObject mcpServers)
            {
                if (mcpServers.Remove(ServerName))
                {
                    File.WriteAllText(path, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiPlatformManager] MCP 설정 해제 실패 ({path}): {ex.Message}");
        }
        return false;
    }

    /// <summary>어떤 도구든 붙여넣기만 하면 되는 표준 MCP 설정 JSON 스니펫.</summary>
    public static string GetMcpServerJsonSnippet()
    {
        var exePath = Environment.ProcessPath
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudePixelOffice", "ClaudeOffice.exe");
        var escapedExe = exePath.Replace("\\", "\\\\");
        return $@"{{
  ""mcpServers"": {{
    ""claude-pixel-office"": {{
      ""command"": ""{escapedExe}"",
      ""args"": [""--mcp""]
    }}
  }}
}}";
    }

    public static string GetUniversalInstructionsPrompt(string platformName)
    {
        return $@"[{platformName} Pixel Office 연동 지침]
당신은 Pixel Office 가상 사무실에서 협업하는 {platformName} AI 직원입니다.
작업을 진행할 때 다음 지침을 준수하세요:

1. [사무실 상태 보고 (출근)]:
   질문에 답변을 시작할 때, 주요 개발 단계, 작업을 완료했을 때 반드시 MCP 도구 `office_report_status`를 호출하여 사무실에 상태를 보고하세요.
   - project_name: 현재 프로젝트 이름 (예: ""{platformName} 프로젝트"")
   - session_name: 현재 작업 세션 이름 (예: ""코드 개발"")
   - status: ""working""(작업중), ""thinking""(생각중), ""idle""(대기), ""done""(완료) 중 선택
   - message: 현재 작업 한 줄 요약 (캐릭터 말풍선에 표시됨)

2. [원격 지시 수신]:
   - 도구 호출 반환값에 원격 지시사항이 있으면 즉시 최우선으로 수행하세요.";
    }

    public static string GetChatGptInstructionsPrompt()
    {
        return @"[ChatGPT Pixel Office 연동 지침]
당신은 Pixel Office 가상 사무실의 ChatGPT 직원입니다.
작업 시 다음 웹훅(HTTP POST)을 호출하거나 안내된 형식으로 상태를 보고해 주세요:

보고 URL: http://localhost:47821/api/report
데이터 규격 (JSON):
{
  ""ai"": ""chatgpt"",
  ""project"": ""ChatGPT 분석"",
  ""session"": ""대화세션"",
  ""status"": ""working"",
  ""message"": ""데이터 분석 진행 중""
}";
    }

    public static string GetGeminiInstructionsPrompt()
    {
        return @"[Google Gemini Pixel Office 연동 지침]
당신은 Pixel Office 가상 사무실의 Google Gemini 직원입니다.
작업을 진행할 때 다음 웹훅(HTTP POST)을 호출하여 사무실에 상태를 보고하세요:

보고 URL: http://localhost:47821/api/report
데이터 규격 (JSON):
{
  ""ai"": ""gemini"",
  ""project"": ""디자인 & 멀티모달"",
  ""session"": ""이미지생성"",
  ""status"": ""working"",
  ""message"": ""배경 이미지 생성 중""
}";
    }

    public static string GetLocalLlmInstructionsPrompt()
    {
        return @"[DeepSeek / Ollama Pixel Office 연동 지침]
로컬 AI 에이전트 실행 시 아래의 1줄 curl 명령으로 즉시 가상 사무실에 출근 보고를 보낼 수 있습니다:

curl -X POST http://localhost:47821/api/report \
  -H ""Content-Type: application/json"" \
  -d '{""ai"":""deepseek"",""project"":""로컬서버"",""session"":""코드리팩토링"",""status"":""working"",""message"":""로컬 모델 연산 중""}'";
    }
}
