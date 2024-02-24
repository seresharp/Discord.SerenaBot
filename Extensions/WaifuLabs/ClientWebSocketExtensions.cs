using Remora.Results;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Remora.Rest.Results;
using SerenaBot.Commands.Util.WaifuLabs;

namespace SerenaBot.Extensions.WaifuLabs;

public static class ClientWebSocketExtensions
{
    private static readonly HttpClient Http = new();

    public static async Task<IResult> ConnectWaifuLabsAsync(this ClientWebSocket socket, CancellationToken ct = default)
    {
        HttpResponseMessage httpResp = await Http.GetAsync("https://waifulabs.com/generate", ct);
        if (!httpResp.IsSuccessStatusCode)
        {
            return Result.FromError(new HttpResultError(httpResp.StatusCode, httpResp.ReasonPhrase));
        }

        Match tokenMatch = WaifuTokenRegex.Match(await httpResp.Content.ReadAsStringAsync(ct));
        if (!tokenMatch.Success)
        {
            return Result.FromError(new NotFoundError("WaifuLabs token not found on webpage"));
        }

        await socket.ConnectAsync(new($"wss://waifulabs.com/creator/socket/websocket?token={tokenMatch.Groups["token"].Value}&vsn=2.0.0"), ct);
        return Result.FromSuccess();
    }

    public static async Task<Result<WaifuSocketResponse>> GetResponseAsync(this ClientWebSocket socket, WaifuSocketMessage message, CancellationToken ct = default)
    {
        await message.SendAsync(socket, ct);
        return await WaifuSocketResponse.ReceiveAsync(socket, ct);
    }

    private static readonly Regex WaifuTokenRegex = new("window\\.authToken = \"(?<token>[^\"]+)\"", RegexOptions.Compiled);
}
