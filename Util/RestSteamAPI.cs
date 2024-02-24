using Microsoft.Extensions.Configuration;
using Remora.Commands.Results;
using Remora.Discord.Caching.Abstractions.Services;
using Remora.Discord.Rest.Extensions;
using Remora.Rest;
using Remora.Results;
using SerenaBot.Extensions;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace SerenaBot.Util;

public class RestSteamAPI
{
    protected string ApiKey { get; init; }

    private IRestHttpClient RestHttpClient { get; init; }
    private ICacheProvider RateLimitCache { get; init; }

    public RestSteamAPI(IRestHttpClient http, ICacheProvider cache, IConfiguration config)
    {
        RestHttpClient = http;
        RateLimitCache = cache;
        ApiKey = config.GetRequiredValue<string>("STEAM_TOKEN");
    }

    public async Task<Result<ulong>> ResolveVanityURLAsync(string vanityurl, CancellationToken ct)
    {
        Result<VanityURLResponse> resp = await RestHttpClient.GetAsync<VanityURLResponse>
        (
            BuildWebAPIURL("ISteamUser", "ResolveVanityURL"),
            b => b.AddQueryParameter("key", ApiKey)
                .AddQueryParameter("vanityurl", vanityurl)
                .WithRateLimitContext(RateLimitCache, isExemptFromGlobalLimits: true),
            ct: ct
        );

        return resp switch
        {
            { IsSuccess: false } => Result<ulong>.FromError(resp),
            { Entity.Response.IsSuccess: false } => Result<ulong>.FromError(new NotFoundError($"ResolveVanityURL?vanityurl={vanityurl} -> '{resp.Entity.Response.Message}'")),
            _ => !ulong.TryParse(resp.Entity.Response.SteamID, out ulong steamid)
                ? Result<ulong>.FromError(new ParsingError<ulong>(resp.Entity.Response.SteamID, "Failed parsing returned SteamID as ulong"))
                : Result<ulong>.FromSuccess(steamid),
        };
    }

    public async Task<Result<int[]>> GetOwnedGamesAsync(ulong steamid, bool includePlayedFreeGames = false, CancellationToken ct = default)
    {
        Result<OwnedGamesResponse> resp = await RestHttpClient.GetAsync<OwnedGamesResponse>
        (
            BuildWebAPIURL("IPlayerService", "GetOwnedGames"),
            b => b.AddQueryParameter("key", ApiKey)
                .AddQueryParameter("steamid", steamid.ToString())
                .AddQueryParameter("include_played_free_games", includePlayedFreeGames.ToString().ToLower())
                .WithRateLimitContext(RateLimitCache, isExemptFromGlobalLimits: true),
            ct: ct
        );

        return resp switch
        {
            { IsSuccess: false } => Result<int[]>.FromError(resp),
            { Entity.Response.Games.Length: 0 } => Result<int[]>.FromSuccess(Array.Empty<int>()),
            _ => Result<int[]>.FromSuccess(resp.Entity.Response.Games.Select(g => g.AppID).ToArray())
        };
    }

    public async Task<Result<SteamAppDetails>> GetAppDetailsAsync(int appid, CancellationToken ct = default)
    {
        Result<Dictionary<string, AppDetailsResponse>> resp = await RestHttpClient.GetAsync<Dictionary<string, AppDetailsResponse>>
        (
            BuildStorefrontAPIURL("appdetails"),
            b => b.AddQueryParameter("appids", appid.ToString())
                .WithRateLimitContext(RateLimitCache, isExemptFromGlobalLimits: true),
            ct: ct
        );

        return resp switch
        {
            { IsSuccess: false } => Result<SteamAppDetails>.FromError(resp),
            _ => !resp.Entity.TryGetValue(appid.ToString(), out AppDetailsResponse? appDetails)
                ? Result<SteamAppDetails>.FromError(new NotFoundError("App details not present in data returned from Steam API appdetails call"))
                : Result<SteamAppDetails>.FromSuccess(appDetails.Response)
        };
    }

    private static string BuildWebAPIURL(string category, string method)
        => $"https://api.steampowered.com/{category}/{method}/v1";

    private static string BuildStorefrontAPIURL(string method)
        => $"https://store.steampowered.com/api/{method}";

    private record VanityURLResponse([property: JsonPropertyName("response"), JsonRequired] VanityURLResponse.InnerResponse Response)
    {
        public record InnerResponse(
            [property: JsonPropertyName("success")] int Status,
            [property: JsonPropertyName("steamid")] string? SteamID,
            [property: JsonPropertyName("message")] string? Message)
        {
            [MemberNotNullWhen(true, nameof(SteamID))]
            [MemberNotNullWhen(false, nameof(Message))]
            public bool IsSuccess => Status == 1;
        }
    }

    private record OwnedGamesResponse([property: JsonPropertyName("response"), JsonRequired] OwnedGamesResponse.InnerResponse Response)
    {
        public record InnerResponse([property: JsonPropertyName("games"), JsonRequired] Game[] Games);

        public record Game([property: JsonPropertyName("appid")] int AppID);
    }

    private record AppDetailsResponse([property: JsonPropertyName("data"), JsonRequired] SteamAppDetails Response);
}

public record SteamAppDetails(
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("short_description"), JsonRequired] string Description,
    [property: JsonPropertyName("header_image"), JsonRequired] string ThumbnailURL,
    [property: JsonPropertyName("developers")] string[]? Developers);
