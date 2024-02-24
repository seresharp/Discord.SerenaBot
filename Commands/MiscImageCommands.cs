using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Rest.Results;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SerenaBot.Commands
{
    public class MiscImageCommands : BaseCommandGroup
    {
        [Command("picrew")]
        [CommandType(ApplicationCommandType.ChatInput)]
        [Description("Attaches an image scraped from https://picrew.me/discovery/")]
        public async Task<IResult> SendPicrewAsync()
        {
            HttpResponseMessage httpResp = await Http.GetAsync("https://picrew.me/discovery");
            if (!httpResp.IsSuccessStatusCode)
            {
                return Result.FromError(new HttpResultError(httpResp.StatusCode, httpResp.ReasonPhrase));
            }

            string html = await httpResp.Content.ReadAsStringAsync();
            (string, string)[] picrews = PicrewRegex.Matches(html)
                .Select(match => (match.Groups["maker"].Value, match.Groups["image"].Value))
                .ToArray();

            var (maker, image) = picrews.GetRandomItem();
            httpResp = await Http.GetAsync(image);
            if (!httpResp.IsSuccessStatusCode)
            {
                return Result.FromError(new HttpResultError(httpResp.StatusCode, httpResp.ReasonPhrase));
            }

            return await Feedback.SendContextualEmbedAsync(new Embed($"https://picrew.me{maker}", Image: new EmbedImage("attachment://picrew.png")),
                options: new(Attachments: Attach(new FileData("picrew.png", await httpResp.Content.ReadAsStreamAsync()))));
        }

        [Command("cat")]
        [CommandType(ApplicationCommandType.ChatInput)]
        [Description("Attaches an image of a cat, taken from https://thesecatsdonotexist.com/")]
        public async Task<IResult> SendCatAsync()
        {
            HttpResponseMessage httpResp = await Http.GetAsync($"https://d2ph5fj80uercy.cloudfront.net/0{Random.Next(6) + 1}/cat{Random.Next(5000) + 1}.jpg");
            if (!httpResp.IsSuccessStatusCode)
            {
                return Result.FromError(new HttpResultError(httpResp.StatusCode, httpResp.ReasonPhrase));
            }

            return await Feedback.SendContextualAsync(options: new(Attachments: Attach(new FileData("cat.png", await httpResp.Content.ReadAsStreamAsync()))));
        }

        [Command("imagesearch")]
        [CommandType(ApplicationCommandType.ChatInput)]
        public async Task<IResult> ImageSearchAsync(string query)
        {
            string token = Config.GetRequiredValue<string>("GOOGLE_SEARCH_TOKEN");
            string cx = Config.GetRequiredValue<string>("GOOGLE_SEARCH_ID");

            string json = await Http.GetStringAsync($"https://customsearch.googleapis.com/customsearch/v1?key={token}&cx={cx}&searchType=image&q={query}");
            GoogleSearch results = JsonSerializer.Deserialize<GoogleSearch>(json) ?? throw new InvalidDataException("Failed deserializing search results");

            if (results?.items?.Length is null or 0)
            {
                return await Feedback.SendContextualInfoAsync("No results");
            }

            using Stream stream = await Http.GetStreamAsync(results.items[0].link);
            return await Feedback.SendContextualAsync(options: new(Attachments: Attach(new FileData("thing.png", stream))));
        }


        public class GoogleSearch
        {
            public Item[]? items { get; set; }

            public class Item
            {
                public string? link { get; set; }
            }
        }

        private static readonly Regex PicrewRegex
            = new("<div class=\"discovery_list_img\"><a href=\"(?<maker>[^\"]+)\"><img data-src=\"(?<image>[^\"]+)\" class=\"lazyload\"></a></div>", RegexOptions.Compiled);
    }
}
