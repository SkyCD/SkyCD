using System.Text.Json;

var scenario = args.FirstOrDefault() ?? "normal";
while (await Console.In.ReadLineAsync() is { } line)
{
    using var document = JsonDocument.Parse(line);
    var request = document.RootElement;
    var method = request.GetProperty("method").GetString();
    if (!request.TryGetProperty("id", out var requestId)) continue;
    var id = requestId.GetInt32();
    object result = method switch
    {
        "initialize" => new { protocolVersion = 1, agentCapabilities = new { promptCapabilities = new { image = true, audio = false } }, agentInfo = new { name = "fake-acp", version = "1" } },
        "session/new" => new { sessionId = "fake-session", models = new { currentModelId = "fake-model", availableModels = new[] { new { modelId = "fake-model", name = "Fake" } } } },
        _ => new { }
    };
    if (method == "session/prompt")
    {
        if (scenario == "hang") { await Task.Delay(Timeout.Infinite); return; }
        if (scenario == "crash") return;
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "session/update",
            @params = new { sessionId = "fake-session", update = new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "A concise cached-content description." } } }
        }));
        result = new { stopReason = "end_turn" };
    }
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
    await Console.Out.FlushAsync();
}
