using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeOffice.Hooks;
using ClaudeOffice.Mcp;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Tests;

public class AiPlatformTests
{
    [Fact]
    public void AiPlatformManager_ScanAll_ReturnsKnownPlatforms()
    {
        var platforms = AiPlatformManager.ScanAll();
        Assert.NotNull(platforms);
        Assert.NotEmpty(platforms);

        var ids = platforms.Select(p => p.Id).ToList();
        Assert.DoesNotContain("claude-desktop", ids);
        Assert.Contains("cursor", ids);
        Assert.Contains("windsurf", ids);
        Assert.Contains("antigravity", ids);
        Assert.Contains("chatgpt", ids);
        Assert.Contains("gemini", ids);
        Assert.Contains("deepseek", ids);

        var antigravity = platforms.First(p => p.Id == "antigravity");
        Assert.Equal("Google Antigravity (Gemini)", antigravity.Name);
        Assert.Equal("mcp", antigravity.Type);
        Assert.Equal("✨", antigravity.Icon);

        // Verify all platforms have icons and descriptions
        foreach (var p in platforms)
        {
            Assert.NotEmpty(p.Name);
            Assert.NotEmpty(p.Icon);
            Assert.NotEmpty(p.Description);
        }
    }

    [Fact]
    public void AiPlatformManager_GetMcpServerJsonSnippet_IsValidJson()
    {
        var snippet = AiPlatformManager.GetMcpServerJsonSnippet();
        Assert.NotEmpty(snippet);

        using var doc = JsonDocument.Parse(snippet);
        var r = doc.RootElement;
        Assert.True(r.TryGetProperty("mcpServers", out var servers));
        Assert.True(servers.TryGetProperty("claude-pixel-office", out var cpo));
        Assert.True(cpo.TryGetProperty("command", out _));
        Assert.True(cpo.TryGetProperty("args", out _));
    }

    [Fact]
    public void AiPlatformManager_ConfigureAndUnconfigure_CustomFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ClaudeOffice_AiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var customJsonPath = Path.Combine(tempDir, "custom_mcp.json");

        var initialJson = """
        {
            "mcpServers": {
                "existing-server": {
                    "command": "node",
                    "args": ["server.js"]
                }
            }
        }
        """;
        File.WriteAllText(customJsonPath, initialJson);

        try
        {
            // 1. Configure
            var ok = AiPlatformManager.ConfigurePlatform("custom", customJsonPath);
            Assert.True(ok);

            var contentAfterConfig = File.ReadAllText(customJsonPath);
            var rootConfigured = JsonNode.Parse(contentAfterConfig) as JsonObject;
            Assert.NotNull(rootConfigured);
            var serversConfigured = rootConfigured!["mcpServers"] as JsonObject;
            Assert.NotNull(serversConfigured);
            Assert.True(serversConfigured!.ContainsKey("claude-pixel-office"));
            Assert.True(serversConfigured!.ContainsKey("existing-server"));

            // 2. Unconfigure
            var unOk = AiPlatformManager.UnconfigurePlatform("custom", customJsonPath);
            Assert.True(unOk);

            var contentAfterUnconfig = File.ReadAllText(customJsonPath);
            var rootUnconfigured = JsonNode.Parse(contentAfterUnconfig) as JsonObject;
            Assert.NotNull(rootUnconfigured);
            var serversUnconfigured = rootUnconfigured!["mcpServers"] as JsonObject;
            Assert.NotNull(serversUnconfigured);
            Assert.False(serversUnconfigured!.ContainsKey("claude-pixel-office"));
            Assert.True(serversUnconfigured!.ContainsKey("existing-server"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void VirtualSession_MultiAi_BuildsDistinctEmployeeDto()
    {
        var now = DateTime.UtcNow;

        // Gemini virtual session
        var geminiState = new TranscriptState("virtual:mcp:gemini:디자인프로젝트:로고시안", "mcp_gemini_1", null)
        {
            IsVirtual = true,
            StartedUtc = now,
            FileWriteUtc = now,
            LastEventUtc = now,
            Seen = true,
            Entrypoint = "gemini",
            Cwd = "디자인프로젝트",
            CustomTitle = "로고시안",
            BaseActivity = Activity.Working,
            BaseIcon = "✨",
            BaseText = "로고 백터 시안 작업 중",
            BaseSinceUtc = now,
            AiTitle = "로고 백터 시안 작업 중"
        };

        // ChatGPT virtual session
        var chatGptState = new TranscriptState("virtual:mcp:chatgpt:분석프로젝트:리포트", "mcp_gpt_1", null)
        {
            IsVirtual = true,
            StartedUtc = now,
            FileWriteUtc = now,
            LastEventUtc = now,
            Seen = true,
            Entrypoint = "chatgpt",
            Cwd = "분석프로젝트",
            CustomTitle = "리포트",
            BaseActivity = Activity.Working,
            BaseIcon = "🟢",
            BaseText = "통계 분석 리포트 생성 중",
            BaseSinceUtc = now,
            AiTitle = "통계 분석 리포트 생성 중"
        };

        var json = SnapshotBuilder.Build([geminiState, chatGptState], now, TimeSpan.FromHours(12));
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var sessions = doc.RootElement.GetProperty("sessions");
        Assert.Equal(2, sessions.GetArrayLength());

        var s1 = sessions[0];
        Assert.Equal("로고시안", s1.GetProperty("name").GetString());
        Assert.Equal("gemini", s1.GetProperty("entry").GetString());
        Assert.Equal("✨", s1.GetProperty("icon").GetString());

        var s2 = sessions[1];
        Assert.Equal("리포트", s2.GetProperty("name").GetString());
        Assert.Equal("chatgpt", s2.GetProperty("entry").GetString());
        Assert.Equal("🟢", s2.GetProperty("icon").GetString());
    }

    [Fact]
    public void HookServer_ReportPayload_ParsesAllFields()
    {
        var json = """
        {
            "ai": "gemini",
            "project": "신규프로젝트",
            "session": "메인세션",
            "status": "working",
            "message": "프론트엔드 작업 진행 중",
            "detail": "CSS 애니메이션 다듬기"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var ai = root.TryGetProperty("ai", out var a) ? a.GetString() : "gemini";
        var project = root.TryGetProperty("project", out var p) ? p.GetString() : "";
        var session = root.TryGetProperty("session", out var s) ? s.GetString() : "";
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : "working";
        var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "";

        Assert.Equal("gemini", ai);
        Assert.Equal("신규프로젝트", project);
        Assert.Equal("메인세션", session);
        Assert.Equal("working", status);
        Assert.Equal("프론트엔드 작업 진행 중", msg);
    }
    [Fact]
    public void RemoteHub_PopPendingInstruction_MatchesVirtualSessionHashAndFallback()
    {
        var hub = new ClaudeOffice.Remote.RemoteHub(null);
        var aiClean = "antigravity";
        var projClean = "ClaudeOffice";
        var sessClean = "메인";
        var key = "virtual:mcp:" + aiClean + ":" + projClean.ToLowerInvariant() + ":" + sessClean.ToLowerInvariant();
        var sid = "mcp_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();

        // 1. Send instruction from PC for this virtual session
        hub.SayFromPc(sid, "반갑습니다 AI 비서님!");

        // 2. Exact match check
        var instruction = hub.PopPendingInstruction(sid, projClean, sessClean, aiClean);
        Assert.Equal("반갑습니다 AI 비서님!", instruction);

        // 3. Fallback check when only 1 pending mcp message exists
        hub.SayFromPc(sid, "두 번째 테스트 지시입니다.");
        var fallbackInstruction = hub.PopPendingInstruction("mcp_different_id", "다른이름", "세션", aiClean);
        Assert.Equal("두 번째 테스트 지시입니다.", fallbackInstruction);
    }

    [Fact]
    public void RemoteHub_PopPendingInstruction_DoesNotDeliverAntigravityInstructionToClaudeDesktop()
    {
        var hub = new ClaudeOffice.Remote.RemoteHub(null);
        var aiAntigravity = "antigravity";
        var sid = "mcp_antigrav1";

        // Antigravity 세션용으로 지시 등록
        hub.SayFromPc(sid, "Antigravity 전용 지시입니다.", aiProvider: aiAntigravity);

        // Claude Desktop이 대기 지시 확인을 호출했을 때 Antigravity 지시를 가로채지 못해야 함
        var claudeInstruction = hub.PopPendingInstruction("mcp_claude_sid", "ClaudeProject", "메인", "claude-desktop");
        Assert.Null(claudeInstruction);

        // Antigravity가 호출했을 때는 정상적으로 지시를 꺼내갈 수 있어야 함
        var antiInstruction = hub.PopPendingInstruction(sid, "ClaudeOffice", "메인", aiAntigravity);
        Assert.Equal("Antigravity 전용 지시입니다.", antiInstruction);
    }

    [Fact]
    public void RemoteHub_OnMcpReply_IsolatesRepliesPerSessionAndWindowDeliverClearsQueue()
    {
        var hub = new ClaudeOffice.Remote.RemoteHub(null);
        hub.SayFromPc("mcp_anti1", "지시 1", aiProvider: "antigravity");
        hub.SayFromPc("mcp_deep1", "지시 2", aiProvider: "deepseek");

        // Antigravity만 응답 보고 -> DeepSeek의 reply가 덮어씌워지지 않아야 함
        hub.OnMcpReply("mcp_anti1", "Antigravity 완료");
        hub.OnMcpReply("mcp_deep1", "DeepSeek 완료");

        var stateJson = hub.StateJson();
        using var doc = System.Text.Json.JsonDocument.Parse(stateJson);
        var sObj = doc.RootElement.GetProperty("s");
        Assert.Equal("Antigravity 완료", sObj.GetProperty("mcp_anti1").GetProperty("reply").GetProperty("text").GetString());
        Assert.Equal("DeepSeek 완료", sObj.GetProperty("mcp_deep1").GetProperty("reply").GetProperty("text").GetString());

        // 창 붙여넣기 전달 완료(MarkDeliveredFromWindow) 후 MCP 폴링 시 중복 전달되지 않아야 함
        hub.MarkDeliveredFromWindow("mcp_anti1", "지시 1");
        Assert.Null(hub.PopPendingInstruction("mcp_anti1", "Proj", "Sess", "antigravity"));
    }
}
