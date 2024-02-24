using Microsoft.Extensions.Configuration;
using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Extensions.Embeds;
using Remora.Rest.Results;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using System.ComponentModel;
using System.Drawing;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SerenaBot.Commands;

public class OwlbotCommands : BaseCommandGroup
{
    new private static HttpClient Http = null!;

    public override void Initialize()
    {
        base.Initialize();

        if (Http == null)
        {
            Http = new();
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", Config.GetRequiredValue<string>("OWLBOT_TOKEN"));
        }
    }

    [Command("define")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Defines a given word using the Owlbot Dictionary API (https://owlbot.info/)")]
    public async Task<IResult> DefineWordAsync(
        [Description("The word to define")] string word)
    {
        HttpResponseMessage httpResp = await Http.GetAsync($"https://owlbot.info/api/v4/dictionary/{word.ToLower()}?format=json");
        if (!httpResp.IsSuccessStatusCode)
        {
            if (httpResp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return await Feedback.SendContextualWarningAsync($"Could not find definition for word '{word}'");
            }

            return Result.FromError(new HttpResultError(httpResp.StatusCode, httpResp.ReasonPhrase));
        }

        string str = await httpResp.Content.ReadAsStringAsync();
        DictionaryWord def = JsonSerializer.Deserialize<DictionaryWord>(str);

        Result<Embed> getEmbed = def.GetEmbed(Feedback.Theme.Primary);
        if (!getEmbed.IsSuccess) return getEmbed;

        return await Feedback.SendContextualEmbedAsync(getEmbed.Entity);
    }

#pragma warning disable IDE1006
    private struct DictionaryWord
    {
        public string word { get; set; }
        public string? pronunciation { get; set; }
        public Definition[] definitions { get; set; }

        public Result<Embed> GetEmbed(Color color)
        {
            EmbedBuilder embed = new()
            {
                Title = pronunciation != null
                    ? $"{word} /{pronunciation}/"
                    : word,
                Colour = color
            };

            for (int i = 0; i < definitions.Length; i++)
            {
                string name = $"Entry {i + 1}/{definitions.Length}{definitions[i].GetNameSuffix()}";
                string value = definitions[i].GetValue();
                if (embed.AddField(name, value) is { IsSuccess: false } error)
                {
                    return Result<Embed>.FromError(error);
                }
            }

            // All the additional data at the end of the image url kills discord
            string? thumbnail = Array.Find(definitions, d => d.image_url != null).image_url;
            if (thumbnail != null)
            {
                int i = thumbnail.IndexOf('?');
                if (i >= 0)
                {
                    embed.ThumbnailUrl = thumbnail[0..i];
                }
            }

            embed.Footer = new("https://owlbot.info/", "https://owlbot.info/static/dictionary/img/favicon-32x32.png");
            return embed.Build();
        }
    }

    private struct Definition
    {
        public string? type { get; set; }
        public string definition { get; set; }
        public string? example { get; set; }
        public string? image_url { get; set; }
        public string? emoji { get; set; }

        public string GetNameSuffix()
            => $"{(type != null ? $" ({type})" : string.Empty)} {emoji ?? string.Empty}";

        public string GetValue()
            => char.ToUpper(definition[0])
                + definition[1..]
                + (example != null
                    ? "\n\nExample: " + example
                    : string.Empty);
    }
#pragma warning restore IDE1006
}
