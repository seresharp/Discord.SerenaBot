using System.Net.Http.Headers;

namespace SerenaBot.Extensions
{
    public static class HttpExtensions
    {
        public static async Task<HttpResponseMessage> GetAsync(this HttpClient Http, string? requestUri, AuthenticationHeaderValue auth, CancellationToken ct = default)
            => await SendAsync(Http, HttpMethod.Get, requestUri, null, auth, ct);

        public static async Task<HttpResponseMessage> PostAsync(this HttpClient Http, string? requestUri, HttpContent? content, AuthenticationHeaderValue auth, CancellationToken ct = default)
            => await SendAsync(Http, HttpMethod.Post, requestUri, content, auth, ct);

        private static async Task<HttpResponseMessage> SendAsync(HttpClient Http, HttpMethod method, string? requestUri, HttpContent? content, AuthenticationHeaderValue auth, CancellationToken ct)
        {
            using HttpRequestMessage request = new(method, requestUri)
            {
                Content = content
            };

            request.Headers.Authorization = auth;
            return await Http.SendAsync(request, ct);
        }
    }
}
