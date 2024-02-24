using Microsoft.Extensions.Configuration;
using Remora.Commands.Attributes;
using Remora.Discord.API;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Errors;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Conditions;
using Remora.Rest.Results;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SerenaBot.Commands
{
    public class PhotosLibraryCommands : BaseCommandGroup
    {
        private const string ALBUM_ID = "ABoBx6qzTPQG2dnvCGkjAz7PM_1Syw-wYMTi6tqNsE0rTMBKZxYTvgiQBM4WuTaHGFDVM28Trz9z";

        private static readonly List<string> FrankieImageIDs = new();
        private static long FrankieExpiration = -1;

        private static string AccessToken = string.Empty;
        private static long TokenExpiration = -1;

        [Command("frankie")]
        [CommandType(ApplicationCommandType.ChatInput)]
        [Description("Responds with a frankie picture")]
        public async Task<IResult> SendFrankieAsync()
        {
            Result<string> getToken = await GetTokenAsync();
            if (!getToken.IsSuccess) return getToken;
            string token = getToken.Entity;

            async Task<IResult> SendFrankieInternalAsync()
            {
                string id = FrankieImageIDs.GetRandomItem();
                HttpResponseMessage mediaItemResp = await Http.GetAsync
                (
                    $"https://photoslibrary.googleapis.com/v1/mediaItems/{id}",
                    new AuthenticationHeaderValue("Bearer", token),
                    CancellationToken
                );

                if (!mediaItemResp.IsSuccessStatusCode) return Result.FromError(new HttpResultError(mediaItemResp.StatusCode, mediaItemResp.ReasonPhrase));

                string itemJson = await mediaItemResp.Content.ReadAsStringAsync(CancellationToken);
                PhotosSearchResults.Item? item = JsonSerializer.Deserialize<PhotosSearchResults.Item>(itemJson);
                if (item?.baseUrl == null)
                {
                    return Result.FromError(new InvalidOperationError($"Deserialization of response data failed: {itemJson}"));
                }

                using HttpResponseMessage imageResp = await Http.GetAsync(item.baseUrl + "=w1920-h1920", CancellationToken);
                if (!imageResp.IsSuccessStatusCode) return Result.FromError(new HttpResultError(imageResp.StatusCode, imageResp.ReasonPhrase));

                using Stream stream = await imageResp.Content.ReadAsStreamAsync(CancellationToken);
                string? fileName = item.mimeType?.ToLowerInvariant() switch
                {
                    "image/jpeg" => "frankie.jpg",
                    "image/png" => "frankie.png",
                    _ => null
                };

                if (fileName == null)
                {
                    return Result.FromError(new UnsupportedImageFormatError(new List<CDNImageFormat>() { CDNImageFormat.JPEG, CDNImageFormat.PNG }));
                }

                if (!DateTime.TryParse(item.mediaMetadata?.creationTime, out DateTime photoTime))
                {
                    return await Feedback.SendContextualAsync(options: new(Attachments: Attach(new FileData(fileName, stream))));
                }

                string embedTitle = $"<t:{((DateTimeOffset)photoTime).ToUnixTimeSeconds()}>";
                return await Feedback.SendContextualEmbedAsync(new Embed(embedTitle, Image: new EmbedImage($"attachment://{fileName}")),
                    options: new(Attachments: Attach(new FileData(fileName, stream))));
            }

            if (FrankieExpiration > Environment.TickCount64)
            {
                return await SendFrankieInternalAsync();
            }

            bool frankieSent = false;
            if (FrankieImageIDs.Count > 0)
            {
                IResult frankieRes = await SendFrankieInternalAsync();
                if (!frankieRes.IsSuccess) return frankieRes;

                frankieSent = true;
            }

            FrankieImageIDs.Clear();
            string? pageToken = null;
            do
            {
                Dictionary<string, string> values = new()
                {
                    { "albumId", ALBUM_ID },
                    { "pageSize", "100" }
                };

                if (pageToken != null)
                {
                    values["pageToken"] = pageToken;
                }

                using HttpResponseMessage searchResp = await Http.PostAsync
                (
                    "https://photoslibrary.googleapis.com/v1/mediaItems:search",
                    new FormUrlEncodedContent(values),
                    new AuthenticationHeaderValue("Bearer", token),
                    CancellationToken
                );

                if (!searchResp.IsSuccessStatusCode) return Result.FromError(new HttpResultError(searchResp.StatusCode, searchResp.ReasonPhrase));

                string searchJson = await searchResp.Content.ReadAsStringAsync(CancellationToken);
                PhotosSearchResults items = JsonSerializer.Deserialize<PhotosSearchResults>(searchJson)
                    ?? throw new InvalidDataException($"Deserialization of response data failed: {searchJson}");

                if (items.mediaItems != null)
                {
                    FrankieImageIDs.AddRange(items.mediaItems
                        .Where(i => i.mimeType?.ToLowerInvariant() is "image/jpeg" or "image/png"
                            && !string.IsNullOrWhiteSpace(i.baseUrl)
                            && !string.IsNullOrWhiteSpace(i.id))
                        .Select(i => i.id ?? throw new InvalidDataException("id null even after checking?")));
                }

                // Fetching every page takes quite a while (17 API calls at the time of writing this)
                // Send a frankie from the first page to massively reduce wait time
                if (!frankieSent)
                {
                    IResult frankieRes = await SendFrankieInternalAsync();
                    if (!frankieRes.IsSuccess) return frankieRes;

                    frankieSent = true;
                }

                pageToken = items.nextPageToken;
            }
            while (pageToken != null);

            // Set image cache to expire after 24h so new images get pulled without needing to restart the bot
            FrankieExpiration = Environment.TickCount64 + (24 * 60 * 60 * 1000);
            return Result.FromSuccess();
        }

        [Command("photosauth")]
        [RequireOwner]
        [Ephemeral]
        [CommandType(ApplicationCommandType.ChatInput)]
        public async Task<IResult> RequestPhotosAuthAsync()
        {
            string verifier = GenerateCodeVerifier();
            string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
                .TrimEnd('=')
                .Replace('/', '_')
                .Replace('+', '-');

            string uri = "https://accounts.google.com/o/oauth2/v2/auth"
                + $"?client_id={Config.GetRequiredValue<string>("PHOTOS_CLIENT_ID")}"
                + "&redirect_uri=http://localhost:8080"
                + "&response_type=code"
                + $"&code_challenge={challenge}"
                + "&code_challenge_method=S256"
                + "&scope=https://www.googleapis.com/auth/photoslibrary.readonly"
                + "&access_type=offline";

            await Feedback.SendContextualInfoAsync(uri, ct: CancellationToken);

            using HttpListener listener = new();
            listener.Prefixes.Add("http://localhost:8080/");
            listener.Start();

            Task<HttpListenerContext> t = listener.GetContextAsync();
            if (await Task.WhenAny(t, Task.Delay(60 * 1000, CancellationToken)) != t)
            {
                return await Feedback.SendContextualInfoAsync("Auth link timed out", ct: CancellationToken);
            }

            HttpListenerContext ctx = t.Result;
            HttpListenerRequest request = ctx.Request;
            HttpListenerResponse response = ctx.Response;

            string? refresh = null;
            string? error = null;
            Match match = CodeRegex.Match(request.Url?.ToString() ?? string.Empty);
            if (!match.Success)
            {
                error = $"No authorization code found in response url: {request.Url?.ToString() ?? "null"}";
            }
            else
            {
                Dictionary<string, string> values = new()
                {
                    { "client_id", Config.GetRequiredValue<string>("PHOTOS_CLIENT_ID") },
                    { "client_secret", Config.GetRequiredValue<string>("PHOTOS_CLIENT_SECRET") },
                    { "code", match.Groups["code"].Value },
                    { "code_verifier", verifier },
                    { "grant_type", "authorization_code" },
                    { "redirect_uri", "http://localhost:8080" }
                };

                using HttpResponseMessage resp = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(values), CancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    error = $"Error {resp.StatusCode}: {resp.ReasonPhrase}";
                }
                else
                {
                    string json = await resp.Content.ReadAsStringAsync(CancellationToken);
                    OAuthResponse? oauth = JsonSerializer.Deserialize<OAuthResponse>(json);
                    if (oauth is not { access_token: not null, refresh_token: not null })
                    {
                        error = $"Deserialization of response data failed: {json}";
                    }
                    else
                    {
                        refresh = oauth.refresh_token;

                        // Expire the token locally 30 seconds early to be safe
                        TokenExpiration = Environment.TickCount64 + ((oauth.expires_in - 30) * 1000);
                        AccessToken = oauth.access_token;
                    }
                }
            }

            byte[] buffer = (refresh, error) switch
            {
                { refresh: not null } => Encoding.UTF8.GetBytes($"<html><body><h3>Place this in appsettings.json</h3><p>\"PHOTOS_REFRESH_TOKEN\": \"{refresh}\"</p></body></html>"),
                { error: not null } => Encoding.UTF8.GetBytes($"<html><body><p>{error}</p></body></html>"),
                _ => Encoding.UTF8.GetBytes("<html><body><p>Failed obtaining refresh token. Check network tab!</p></body></html>")
            };

            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
            listener.Stop();

            return Result.FromSuccess();
        }

        private static string GenerateCodeVerifier()
        {
            const string VALID_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            char[] verif = new char[128];
            for (int i = 0; i < verif.Length; i++)
            {
                verif[i] = VALID_CHARS[RandomNumberGenerator.GetInt32(VALID_CHARS.Length)];
            }

            return new(verif);
        }

        private async Task<Result<string>> GetTokenAsync()
        {
            if (TokenExpiration > Environment.TickCount64)
            {
                return AccessToken;
            }

            Dictionary<string, string> values = new()
            {
                { "client_id", Config.GetRequiredValue<string>("PHOTOS_CLIENT_ID") },
                { "client_secret", Config.GetRequiredValue<string>("PHOTOS_CLIENT_SECRET") },
                { "grant_type", "refresh_token" },
                { "refresh_token", Config.GetRequiredValue<string>("PHOTOS_REFRESH_TOKEN") }
            };

            using HttpResponseMessage resp = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(values), CancellationToken);
            if (!resp.IsSuccessStatusCode) return Result<string>.FromError(new HttpResultError(resp.StatusCode, resp.ReasonPhrase));

            string json = await resp.Content.ReadAsStringAsync(CancellationToken);
            OAuthResponse? oauth = JsonSerializer.Deserialize<OAuthResponse>(json);
            if (oauth?.access_token == null) return Result<string>.FromError(new InvalidOperationError($"Deserialization of response data failed: {json}"));

            // Expire the token locally 30 seconds early to be safe
            TokenExpiration = Environment.TickCount64 + ((oauth.expires_in - 30) * 1000);
            AccessToken = oauth.access_token;

            return AccessToken;
        }

        private class OAuthResponse
        {
            public string? access_token { get; set; }
            public int expires_in { get; set; }
            public string? refresh_token { get; set; }
        }

        private class PhotosSearchResults
        {
            public Item[]? mediaItems { get; set; }
            public string? nextPageToken { get; set; }

            public class Item
            {
                public string? id { get; set; }
                public string? baseUrl { get; set; }
                public string? mimeType { get; set; }
                public Meta? mediaMetadata { get; set; }
            }

            public class Meta
            {
                public string? creationTime { get; set; }
            }
        }

        private static readonly Regex CodeRegex = new(".*/?code=(?<code>[^&]+)", RegexOptions.Compiled);
    }
}
