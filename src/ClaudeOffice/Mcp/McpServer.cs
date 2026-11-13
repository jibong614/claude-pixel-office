using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeOffice.Hooks;

namespace ClaudeOffice.Mcp;

/// <summary>
/// Claude 데스크톱 앱과 Model Context Protocol(stdio)로 대화하는 경량 MCP 서버.
/// Claude Desktop이 "ClaudeOffice.exe --mcp"로 프로세스를 띄우고 stdin/stdout으로 통신한다.
/// 클로드 앱 내 '프로젝트' 세션에서 office_report_status 도구를 호출하면 프로젝트와 작업 상태를 받아
/// 127.0.0.1:47821(HookServer)로 전달하여 사무실 책상 화면에 출근시킨다.
/// 또한 사용자 지시가 대기 중이면 도구 반환값으로 즉시 전달하여 양방향 지시를 수행하게 한다.
/// </summary>
internal static class McpServer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static void Run()
    {
        try
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch { }

        using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            try
            {
                var response = ProcessRequest(line);
                if (response != null)
                {
                    writer.WriteLine(response);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClaudeOffice.Mcp] 요청 처리 오류: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 단일 JSON-RPC 요청 문자열을 받아 처리 후 응답 JSON 문자열을 돌려준다 (단위 테스트 지원).
    /// 알림(id가 없는 메시지)인 경우 null을 돌려준다.
    /// </summary>
    public static string? ProcessRequest(string json, Func<JsonObject, (bool, string)>? customSender = null)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch
        {
            return BuildError(null, -32700, "Parse error");
        }

        if (parsed is not JsonObject req) return null;

        var method = req["method"]?.ToString();
        var id = req["id"];

        // 알림인 경우 응답하지 않음
        if (id == null) return null;

        return method switch
        {
            "initialize" => BuildInitializeResponse(id, req["params"] as JsonObject),
            "ping" => BuildResult(id, new JsonObject()),
            "tools/list" => BuildToolsListResponse(id),
            "tools/call" => BuildToolsCallResponse(id, req["params"] as JsonObject, customSender),
            _ => BuildError(id, -32601, $"Method not found: {method}")
        };
    }

    private static string BuildInitializeResponse(object id, JsonObject? @params)
    {
        var protocolVersion = @params?["protocolVersion"]?.ToString() ?? "2024-11-05";
        var result = new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false
                }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "claude-pixel-office",
                ["version"] = App.Version
            }
        };
        return BuildResult(id, result);
    }

    private static string BuildToolsListResponse(object id)
    {
        var tools = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "office_report_status",
                ["description"] = "Report current working status, task progress, or completion to Claude Pixel Office. Returns any pending user instructions sent remotely.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["project_name"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Name of the current project (e.g. '과학공부어플')"
                        },
                        ["session_name"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Name of the current session or chat (e.g. '메인컴', '서버컴')"
                        },
                        ["status"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray { "working", "thinking", "idle", "done" },
                            ["description"] = "Working state: 'working' (default), 'thinking', 'idle', or 'done'"
                        },
                        ["message"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Brief description of the work being performed"
                        },
                        ["detail"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional additional detail or log entry"
                        }
                    },
                    ["required"] = new JsonArray { "message" }
                }
            },
            new JsonObject
            {
                ["name"] = "office_check_instruction",
                ["description"] = "Check if the user has sent any new instructions or tasks from Claude Pixel Office or mobile.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["project_name"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Name of the current project (e.g. '과학공부어플')"
                        },
                        ["session_name"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Name of the current session or chat (e.g. '메인컴', '서버컴')"
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"] = "office_get_status",
                ["description"] = "Get the current real-time office status and list of active employees from Claude Pixel Office.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }
            }
        };

        var result = new JsonObject
        {
            ["tools"] = tools
        };
        return BuildResult(id, result);
    }

    private static string BuildToolsCallResponse(object id, JsonObject? @params, Func<JsonObject, (bool, string)>? customSender)
    {
        var toolName = @params?["name"]?.ToString();
        var arguments = @params?["arguments"] as JsonObject ?? new JsonObject();

        if (toolName == "office_report_status")
        {
            var sender = customSender ?? SendUpdateToOffice;
            var (ok, resp) = sender(arguments);
            return BuildToolResult(id, resp, !ok);
        }

        if (toolName == "office_check_instruction")
        {
            var (ok, resp) = CheckInstructionFromOffice(arguments);
            return BuildToolResult(id, resp, !ok);
        }

        if (toolName == "office_get_status")
        {
            var (ok, resp) = GetOfficeStatus();
            return BuildToolResult(id, resp, !ok);
        }

        return BuildError(id, -32602, "Unknown tool: " + toolName);
    }

    private static (bool, string) SendUpdateToOffice(JsonObject args)
    {
        try
        {
            var port = HookServer.Port;
            var token = HookSetup.Token;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Add("X-ClaudeOffice-Token", token);
            }
            var content = new StringContent(args.ToJsonString(JsonOpts), Encoding.UTF8, "application/json");
            var res = client.PostAsync($"http://127.0.0.1:{port}/mcp/update", content).GetAwaiter().GetResult();
            if (res.IsSuccessStatusCode)
            {
                var respBody = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                string? instruction = null;
                try
                {
                    using var doc = JsonDocument.Parse(respBody);
                    if (doc.RootElement.TryGetProperty("instruction", out var instProp) && instProp.ValueKind == JsonValueKind.String)
                    {
                        instruction = instProp.GetString();
                    }
                }
                catch { }

                var proj = args["project_name"]?.ToString() ?? "프로젝트";
                var sess = args["session_name"]?.ToString();
                var msg = args["message"]?.ToString() ?? "작업 진행";
                var target = string.IsNullOrEmpty(sess) ? proj : $"{proj} / {sess}";

                if (!string.IsNullOrWhiteSpace(instruction))
                {
                    return (true, $"✅ Claude Pixel Office에 상태가 반영되었습니다 ({target}: {msg})\n\n📢 [새로운 사용자 지시사항이 도착했습니다!]:\n\"{instruction}\"\n\n위 사용자 지시사항을 확인하고 지금 즉시 사용자의 요청을 수행하세요.");
                }

                return (true, $"Claude Pixel Office에 반영되었습니다. ({target}: {msg})");
            }
            return (false, "Claude Pixel Office 응답 상태 코드가 비정상입니다: " + res.StatusCode);
        }
        catch
        {
            return (false, "Claude Pixel Office가 실행되어 있지 않습니다. 프로그램을 실행하면 상태가 사무실에 실시간 반영됩니다.");
        }
    }

    private static (bool, string) CheckInstructionFromOffice(JsonObject args)
    {
        try
        {
            var port = HookServer.Port;
            var token = HookSetup.Token;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Add("X-ClaudeOffice-Token", token);
            }
            var content = new StringContent(args.ToJsonString(JsonOpts), Encoding.UTF8, "application/json");
            var res = client.PostAsync($"http://127.0.0.1:{port}/mcp/instruction", content).GetAwaiter().GetResult();
            if (res.IsSuccessStatusCode)
            {
                var respBody = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                string? instruction = null;
                try
                {
                    using var doc = JsonDocument.Parse(respBody);
                    if (doc.RootElement.TryGetProperty("instruction", out var instProp) && instProp.ValueKind == JsonValueKind.String)
                    {
                        instruction = instProp.GetString();
                    }
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(instruction))
                {
                    return (true, $"📢 [새로운 사용자 지시사항이 도착했습니다!]:\n\"{instruction}\"\n\n위 사용자 지시사항을 확인하고 지금 즉시 사용자의 요청을 수행하세요.");
                }

                return (true, "현재 대기 중인 사용자 지시사항이 없습니다.");
            }
            return (false, "지시 확인 실패: " + res.StatusCode);
        }
        catch
        {
            return (false, "Claude Pixel Office가 실행되어 있지 않습니다.");
        }
    }

    private static (bool, string) GetOfficeStatus()
    {
        try
        {
            var port = HookServer.Port;
            var token = HookSetup.Token;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Add("X-ClaudeOffice-Token", token);
            }
            var res = client.GetAsync($"http://127.0.0.1:{port}/mcp/status").GetAwaiter().GetResult();
            if (res.IsSuccessStatusCode)
            {
                var body = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return (true, body);
            }
            return (false, "상태 조회 실패: " + res.StatusCode);
        }
        catch
        {
            return (false, "Claude Pixel Office가 실행되어 있지 않습니다.");
        }
    }

    private static string BuildToolResult(object id, string text, bool isError = false)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = isError
        };
        return BuildResult(id, result);
    }

    private static string BuildResult(object id, JsonObject result)
    {
        var res = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is JsonNode node ? node.DeepClone() : JsonValue.Create(id),
            ["result"] = result
        };
        return res.ToJsonString(JsonOpts);
    }

    private static string BuildError(object? id, int code, string message)
    {
        var res = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is JsonNode node ? node.DeepClone() : id != null ? JsonValue.Create(id) : null,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return res.ToJsonString(JsonOpts);
    }
}
