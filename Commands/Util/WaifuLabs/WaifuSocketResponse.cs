using Remora.Commands.Results;
using Remora.Results;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using WebSocketError = Remora.Discord.Gateway.Results.WebSocketError;

namespace SerenaBot.Commands.Util.WaifuLabs;

public record WaifuSocketResponse(int MessageID, string Endpoint, string Status, Girl[] Girls, string JsonData)
{
    public static async Task<Result<WaifuSocketResponse>> ReceiveAsync(ClientWebSocket socket, CancellationToken ct = default)
    {
        ArraySegment<byte> buffer = new(new byte[4096]);
        using MemoryStream mem = new();

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, ct);
            mem.Write(buffer.Array ?? Array.Empty<byte>(), buffer.Offset, result.Count);
        }
        while (!result.EndOfMessage);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            return Result<WaifuSocketResponse>.FromError(new WebSocketError(WebSocketState.CloseReceived));
        }

        mem.Seek(0, SeekOrigin.Begin);

        using var reader = new StreamReader(mem, Encoding.UTF8);
        string resp = await reader.ReadToEndAsync(ct);
        Match respMatch = WaifuResponseRegex.Match(resp);
        if (!respMatch.Success
            || !int.TryParse(respMatch.Groups["Magic"].Value, out _)
            || !int.TryParse(respMatch.Groups[nameof(MessageID)].Value, out int msgID)
            || JsonSerializer.Deserialize<WaifuSocketResponseData>(respMatch.Groups[nameof(JsonData)].Value) is not WaifuSocketResponseData respData)
        {
            return Result<WaifuSocketResponse>.FromError(new ParsingError<WaifuSocketResponse>(resp));
        }

        if (respData.Status != "ok")
        {
            return Result<WaifuSocketResponse>.FromError(new WebSocketError(WebSocketState.None, $"WaifuLabs returned status '{respData.Status ?? "null"}', expected 'ok'"));
        }

        List<Girl> girls = new();
        if (respData.Response.Data?.Girls is Girl[] respDataGirls) girls.AddRange(respDataGirls);
        if (respData.Response.Data?.Girl is string girlStr) girls.Add(new() { ImageData = girlStr });

        return Result<WaifuSocketResponse>.FromSuccess(new WaifuSocketResponse
        (
            msgID,
            respMatch.Groups[nameof(Endpoint)].Value,
            respData.Status,
            girls.ToArray(),
            respMatch.Groups[nameof(JsonData)].Value
        ));
    }

    private class WaifuSocketResponseData
    {
        [JsonPropertyName("status")] public string Status { get; init; } = null!;
        [JsonPropertyName("response")] public GirlWrapperWrapper Response { get; init; } = null!;
    }

    private class GirlWrapperWrapper
    {
        [JsonPropertyName("data")] public GirlWrapper? Data { get; init; }
        [JsonPropertyName("id")] public int ID { get; init; }
    }

    private class GirlWrapper
    {
        [JsonPropertyName("newGirls")] public Girl[]? Girls { get; init; }
        [JsonPropertyName("girl")] public string? Girl { get; init; }
    }

    private static readonly Regex WaifuResponseRegex
        = new(@"\[""(?<Magic>[0-9]+)"",""(?<MessageID>[0-9]+)"",""(?<API>[^""]+)"",""(?<Endpoint>[^""]+)"",(?<JsonData>.+)]", RegexOptions.Compiled);
}

public class Girl
{
    [JsonPropertyName("image")] public string ImageData { get; init; } = null!;
    [JsonPropertyName("seeds")] public string? Seed { get; init; }
}
