using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace SerenaBot.Commands.Util.WaifuLabs;

public record WaifuSocketMessage(int MessageID, string API, string Endpoint, string JsonData)
{
    public async Task SendAsync(ClientWebSocket socket, CancellationToken ct = default) => await socket.SendAsync(
        Encoding.ASCII.GetBytes($@"[""3"",""{MessageID}"",""{API}"",""{Endpoint}"",{JsonData}]"),
        WebSocketMessageType.Text,
        true,
        ct
    );
}

public record WaifuJoinMessage(int MessageID) : WaifuSocketMessage(MessageID, "api", "phx_join", "{}");

public record WaifuGenerateMessage(int MessageID, string? Seed = null, int? Step = null, bool Big = false)
    : WaifuSocketMessage(
        MessageID,
        "api",
        Big ? "generate_big" : "generate",
        Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(new GenerateData(new(Seed, Step))))
    )
{
    private record GenerateData
    (
        [property: JsonPropertyName("params")] GenerateParameters Parameters,
        [property: JsonPropertyName("id")] int ID = 1
    );

    private record GenerateParameters
    (
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonPropertyName("currentGirl")] string? Seed,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonPropertyName("step")] int? Step
    );
}
