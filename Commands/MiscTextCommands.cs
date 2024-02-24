using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Conditions;
using Remora.Rest.Core;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using SerenaBot.Util;
using System.ComponentModel;

namespace SerenaBot.Commands;

public class MiscTextCommands : BaseCommandGroup
{
    private readonly RestSteamAPI RestSteamAPI = null!;

    [Command("random")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Returns a random choice from the given values")]
    public async Task<IResult> RandomChoiceAsync(string values)
    {
        string[] options = values.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return await Feedback.SendContextualInfoAsync(options.GetRandomItem());
    }

    [Command("steamroulette")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Returns a random game from the given profile")]
    public async Task<IResult> SteamRoulette(
        [Description("The profile id or link to pull games from")] string profile)
    {
        if (profile.StartsWith('<') && profile.EndsWith('>'))
        {
            profile = profile[1..^1];
        }

        if (profile.Contains('/'))
        {
            profile = profile.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Last();
        }

        if (!ulong.TryParse(profile, out ulong steamid))
        {
            Result<ulong> resolveVanityURL = await RestSteamAPI.ResolveVanityURLAsync(profile, CancellationToken);
            if (!resolveVanityURL.IsSuccess) return resolveVanityURL;

            steamid = resolveVanityURL.Entity;
        }

        Result<int[]> getGameIDs = await RestSteamAPI.GetOwnedGamesAsync(steamid, includePlayedFreeGames: true, CancellationToken);
        if (!getGameIDs.IsSuccess) return getGameIDs;
        int appid = getGameIDs.Entity.GetRandomItem();

        Result<SteamAppDetails> getAppDetails = await RestSteamAPI.GetAppDetailsAsync(appid, CancellationToken);
        if (!getAppDetails.IsSuccess) return getAppDetails;

        Embed embed = new
        (
            Title: getAppDetails.Entity.Name,
            Description: getAppDetails.Entity.Description,
            Url: $"https://store.steampowered.com/app/{appid}/",
            Thumbnail: new EmbedThumbnail(getAppDetails.Entity.ThumbnailURL),
            Author: new EmbedAuthor(string.Join(", ", getAppDetails.Entity.Developers ?? Array.Empty<string>())),
            Fields: new List<EmbedField>() { new(string.Empty, $"steam://run/{appid}") }
        );

        return await Feedback.SendContextualEmbedAsync(embed);
    }

    [Command("webhook")]
    [RequireOwner]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Send a message as a user")]
    public async Task<IResult> WebhookAsync(IUser user, string message)
    {
        if (Context.ChannelID is not Snowflake channelID)
        {
            return await Feedback.SendContextualWarningAsync("No channel ID present for this interaction");
        }

        string? avatarUrl = user.BuildAvatarUrl();
        using Stream? stream = avatarUrl != null
            ? await Http.GetStreamAsync(avatarUrl)
            : null;

        Result<IWebhook> createWebhook = await WebhookAPI.CreateWebhookAsync(channelID, user.Username, stream, ct: CancellationToken);
        if (!createWebhook.IsSuccess) return createWebhook;

        // Zero width space
        await Feedback.SendContextualAsync("\u200b");

        Result<IMessage?> executeWebhook = await WebhookAPI.ExecuteWebhookAsync(createWebhook.Entity.ID, createWebhook.Entity.Token.Value, content: message, ct: CancellationToken);
        if (!executeWebhook.IsSuccess) return executeWebhook;

        return await WebhookAPI.DeleteWebhookWithTokenAsync(createWebhook.Entity.ID, createWebhook.Entity.Token.Value);
    }

    [Command("searchwebhooks")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Returns a search string for finding all webhook messages from this bot")]
    public async Task<IResult> SearchWebhooksAsync()
    {
        if (Context.GuildID is not Snowflake guildID)
        {
            return await Feedback.SendContextualInfoAsync("No guild ID present for this interaction");
        }

        Result<IReadOnlyList<IWebhook>> getWebhooks = await WebhookAPI.GetGuildWebhooksAsync(guildID, ct: CancellationToken);
        if (!getWebhooks.IsSuccess) return getWebhooks;

        Result<IApplication> getApplication = await OAuthAPI.GetCurrentBotApplicationInformationAsync(CancellationToken);
        if (!getApplication.IsSuccess) return getApplication;

        string[] froms = getWebhooks.Entity
            .Where(w => w.User.IsDefined(out IUser? u) && u.ID == getApplication.Entity.ID)
            .Select(w => $"from:{w.ID.Value}")
            .ToArray();

        if (froms.Length == 0)
        {
            return await Feedback.SendContextualInfoAsync("I don't have any webhooks in this server");
        }

        return await Feedback.SendContextualInfoAsync($"Copy this into discord search to see all webhook messages sent by this bot\n```\n{string.Join(' ', froms)}\n```");
    }
}
