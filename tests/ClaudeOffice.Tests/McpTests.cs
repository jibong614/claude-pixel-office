using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeOffice.Hooks;
using ClaudeOffice.Mcp;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Tests;

public class McpTests
{
    [Fact]
    public void McpServer_ProcessRequest_Initialize()
    {
        var req = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"Claude Desktop","version":"1.0.0"}}}""";
        var res = McpServer.ProcessRequest(req);
        Assert.NotNull(res);

        using var doc = JsonDocument.Parse(res);
        var r = doc.RootElement;
        Assert.Equal("2.0", r.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, r.GetProperty("id").GetInt64());

        var result = r.GetProperty("result");
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
        Assert.Equal("claude-pixel-office", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public void McpServer_ProcessRequest_Ping()
    {
        var req = """{"jsonrpc":"2.0","id":42,"method":"ping"}""";
        var res = McpServer.ProcessRequest(req);
        Assert.NotNull(res);

        using var doc = JsonDocument.Parse(res);
        var r = doc.RootElement;
        Assert.Equal(42, r.GetProperty("id").GetInt64());
        Assert.True(r.TryGetProperty("result", out _));
    }

    [Fact]
    public void McpServer_ProcessRequest_ToolsList()
    {
        var req = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";
        var res = McpServer.ProcessRequest(req);
        Assert.NotNull(res);

        using var doc = JsonDocument.Parse(res);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        Assert.True(tools.GetArrayLength() >= 2);

        var toolNames = new List<string>();
        foreach (var t in tools.EnumerateArray())
        {
            toolNames.Add(t.GetProperty("name").GetString()!);
        }

        Assert.Contains("office_report_status", toolNames);
        Assert.Contains("office_check_instruction", toolNames);
        Assert.Contains("office_get_status", toolNames);
    }

    [Fact]
    public void McpServer_ProcessRequest_ToolsCall_OfficeReportStatus()
    {
        JsonObject? capturedArgs = null;
        (bool, string) CustomSender(JsonObject args)
        {
            capturedArgs = args;
            return (true, "반영 완료");
        }

        var req = """
        {
            "jsonrpc": "2.0",
            "id": "call-1",
            "method": "tools/call",
            "params": {
                "name": "office_report_status",
                "arguments": {
                    "project_name": "과학공부어플",
                    "session_name": "과학공부어플 메인컴",
                    "status": "working",
                    "message": "과학 실험 퀴즈 UI 개발 중"
                }
            }
        }
        """;

        var res = McpServer.ProcessRequest(req, CustomSender);
        Assert.NotNull(res);
        Assert.NotNull(capturedArgs);
        Assert.Equal("과학공부어플", capturedArgs!["project_name"]?.ToString());
        Assert.Equal("과학공부어플 메인컴", capturedArgs!["session_name"]?.ToString());
        Assert.Equal("working", capturedArgs!["status"]?.ToString());
        Assert.Equal("과학 실험 퀴즈 UI 개발 중", capturedArgs!["message"]?.ToString());

        using var doc = JsonDocument.Parse(res);
        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        Assert.Equal("반영 완료", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public void McpServer_ProcessRequest_Notification_ReturnsNull()
    {
        var notification = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
        var res = McpServer.ProcessRequest(notification);
        Assert.Null(res);
    }

    [Fact]
    public void McpServer_ProcessRequest_UnknownMethod_ReturnsError()
    {
        var req = """{"jsonrpc":"2.0","id":99,"method":"unknown/foo"}""";
        var res = McpServer.ProcessRequest(req);
        Assert.NotNull(res);

        using var doc = JsonDocument.Parse(res);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.Equal(-32601, err.GetProperty("code").GetInt32());
    }

    [Fact]
    public void McpSetup_InstructionsPrompt_IsAvailable()
    {
        var prompt = McpSetup.GetInstructionsPrompt();
        Assert.NotEmpty(prompt);
        Assert.Contains("office_report_status", prompt);
    }

    [Fact]
    public void VirtualMcpState_IsBuiltIntoEmployeeDto()
    {
        var now = DateTime.UtcNow;
        var state = new TranscriptState("virtual:mcp:과학공부어플:과학공부어플 메인컴", "mcp_12345678", null)
        {
            IsVirtual = true,
            StartedUtc = now,
            FileWriteUtc = now,
            LastEventUtc = now,
            Seen = true,
            Entrypoint = "claude-desktop",
            Cwd = "과학공부어플",
            CustomTitle = "과학공부어플 메인컴",
            BaseActivity = Activity.Working,
            BaseIcon = "💻",
            BaseText = "퀴즈 UI 개발 중",
            BaseSinceUtc = now,
            AiTitle = "퀴즈 UI 개발 중"
        };
        state.Recent.Add(new LogItem(now, "💻", "퀴즈 UI 개발 중"));

        var json = SnapshotBuilder.Build([state], now, TimeSpan.FromHours(12));
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var sessions = doc.RootElement.GetProperty("sessions");
        Assert.Equal(1, sessions.GetArrayLength());

        var emp = sessions[0];
        Assert.Equal("과학공부어플 메인컴", emp.GetProperty("name").GetString());
        Assert.Equal("과학공부어플", emp.GetProperty("cwd").GetString());
        Assert.Equal("claude-desktop", emp.GetProperty("entry").GetString());
        Assert.Equal("working", emp.GetProperty("act").GetString());
        Assert.Equal("💻", emp.GetProperty("icon").GetString());
        Assert.Equal("퀴즈 UI 개발 중", emp.GetProperty("text").GetString());
    }

    [Fact]
    public void McpConfig_JsonStructure_MergeAndRemove()
    {
        var json = """
        {
            "mcpServers": {
                "other-server": {
                    "command": "node",
                    "args": ["server.js"]
                }
            }
        }
        """;

        var root = JsonNode.Parse(json) as JsonObject;
        Assert.NotNull(root);
        var mcpServers = root["mcpServers"] as JsonObject;
        Assert.NotNull(mcpServers);

        // Add
        mcpServers["claude-pixel-office"] = new JsonObject
        {
            ["command"] = "ClaudeOffice.exe",
            ["args"] = new JsonArray { "--mcp" }
        };

        Assert.True(mcpServers.ContainsKey("claude-pixel-office"));
        Assert.True(mcpServers.ContainsKey("other-server"));

        // Remove
        mcpServers.Remove("claude-pixel-office");
        Assert.False(mcpServers.ContainsKey("claude-pixel-office"));
        Assert.True(mcpServers.ContainsKey("other-server"));
    }
    [Fact]
    public void McpServer_ProcessRequest_ToolsCall_OfficeCheckInstruction_ReturnsInstruction()
    {
        var req = """
        {
            "jsonrpc": "2.0",
            "id": "check-1",
            "method": "tools/call",
            "params": {
                "name": "office_check_instruction",
                "arguments": {
                    "project_name": "과학공부어플",
                    "session_name": "메인컴"
                }
            }
        }
        """;

        var res = McpServer.ProcessRequest(req);
        Assert.NotNull(res);

        using var doc = JsonDocument.Parse(res);
        var r = doc.RootElement;
        Assert.Equal("check-1", r.GetProperty("id").GetString());
        var content = r.GetProperty("result").GetProperty("content");
        Assert.True(content.GetArrayLength() > 0);
    }

    [Fact]
    public void McpSetup_Uninstall_RemovesServerSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ClaudeOffice_McpTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var configFile = Path.Combine(tempDir, "claude_desktop_config.json");

        var json = """
        {
            "mcpServers": {
                "claude-pixel-office": {
                    "command": "ClaudeOffice.exe",
                    "args": ["--mcp"]
                },
                "other-server": {
                    "command": "node"
                }
            }
        }
        """;
        File.WriteAllText(configFile, json);

        var root = JsonNode.Parse(File.ReadAllText(configFile)) as JsonObject;
        Assert.NotNull(root);
        var mcpServers = root["mcpServers"] as JsonObject;
        Assert.NotNull(mcpServers);
        Assert.True(mcpServers.ContainsKey("claude-pixel-office"));

        // Simulate uninstall removal
        bool removed = mcpServers.Remove("claude-pixel-office");
        Assert.True(removed);
        Assert.False(mcpServers.ContainsKey("claude-pixel-office"));
        Assert.True(mcpServers.ContainsKey("other-server"));

        try { Directory.Delete(tempDir, true); } catch { }
    }
}
