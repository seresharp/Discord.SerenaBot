using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Services;
using Remora.Discord.Gateway.Responders;
using Remora.Rest.Core;
using Remora.Results;
using SerenaBot.Extensions;
using SerenaBot.Util;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace SerenaBot.Responders;

public class GPT2MessageResponder : IResponder<IMessageCreate>
{
    private readonly ContextInjectionService ContextInjection;

    private readonly DiscordAPICache APICache;
    private readonly IDiscordRestWebhookAPI WebhookAPI;

    private readonly ILogger Logger;
    private readonly Random Random;

    private static readonly Dictionary<ulong, DateTime> LastTalkedInChannel = new();

    public GPT2MessageResponder(ContextInjectionService contextInjection, DiscordAPICache apiCache,
        IDiscordRestWebhookAPI webhookAPI, ILogger<Program> logger, Random random)
    {
        ContextInjection = contextInjection;
        APICache = apiCache;
        WebhookAPI = webhookAPI;
        Logger = logger;
        Random = random;
    }

    public async Task<Result> RespondAsync(IMessageCreate msg, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(msg.Content) || !msg.GuildID.IsDefined(out Snowflake guildID))
        {
            return Result.FromSuccess();
        }

        double percentReduction = 0;
        if (LastTalkedInChannel.TryGetValue(msg.ChannelID.Value, out DateTime lastTalked))
        {
            double timeSince = (DateTime.Now - lastTalked).TotalMinutes;
            if (timeSince > 0 && timeSince < 10) percentReduction = (10 - timeSince) / 10;
        }

        LazyAPICall<IApplication> getApplication = new(() => APICache.GetCurrentBotApplicationInformationAsync(ct).AsTask());
        if (!Random.Percent(1 - percentReduction))
        {
            if (!msg.Content.Equals("test test among us 123", StringComparison.InvariantCultureIgnoreCase))
            {
                return Result.FromSuccess();
            }

            await getApplication.DoAPICallAsync();
            if (!getApplication.IsSuccess) return Result.FromError(getApplication.Error.Value);

            if (getApplication.Entity.Owner?.ID.IsDefined(out Snowflake ownerID) is null or false || msg.Author.ID != ownerID)
            {
                return Result.FromError(new InvalidOperationError("Only the bot owner is allowed to trigger GPT-2/Markov messages"));
            }
        }

        ContextInjection.Context = new MessageContext(msg, guildID);

        await getApplication.DoAPICallAsync();
        if (!getApplication.IsSuccess) return Result.FromError(getApplication.Error.Value);

        Result<IWebhook> getWebhook = await GetChannelWebhook(msg.ChannelID, getApplication.Entity.ID, ct);
        if (getWebhook is not { IsSuccess: true, Entity: IWebhook webhook }) return Result.FromError(getWebhook);

        (Generator gen, Snowflake? userID, string? botMsg) = await GetMessageForGuild(guildID);
        if (userID == null || botMsg == null) return Result.FromSuccess();

        Result<IUser> getUser = await APICache.GetUserAsync(userID.Value, ct);
        string webhookName = $"{(getUser.IsSuccess ? getUser.Entity.Username : $"Unknown User ({userID.Value.Value})")} ({gen.GetDisplayName()})";
        Optional<string> avatarUrl = GetAvatarURL(getUser.Entity);

        Result<IMessage?> executeWebhook = await WebhookAPI.ExecuteWebhookAsync(webhook.ID, webhook.Token.Value, shouldWait: true, botMsg, webhookName, avatarUrl, ct: ct);
        if (executeWebhook is not { IsSuccess: true, Entity: IMessage webhookMsg }) return Result.FromError(executeWebhook);

        LastTalkedInChannel[msg.ChannelID.Value] = DateTime.Now;

        _ = Task.Run(GPT2.GenerateMessageFiles, CancellationToken.None);
        return await LogMessage(webhookMsg, webhookName, ct);
    }

    private async Task<(Generator gen, Snowflake? userID, string? message)> GetMessageForGuild(Snowflake guildID)
    {
        Generator gen = Random.Percent(33) ? Generator.Markov : Generator.GPT2;
        async Task<(Snowflake? userID, string? message)> GetMessage()
            => gen == Generator.Markov
                ? Markov.GetMessageForGuild(guildID)
                : await GPT2.GetMessageForGuild(guildID);

        (Snowflake? userID, string? message) = await GetMessage();
        if (userID == null || string.IsNullOrWhiteSpace(message))
        {
            gen = gen == Generator.Markov ? Generator.GPT2 : Generator.Markov;
            (userID, message) = await GetMessage();
        }

        return (gen, userID, message);
    }

    private async Task<Result<IWebhook>> GetChannelWebhook(Snowflake channelID, Snowflake botUserID, CancellationToken ct = default)
    {
        Result<IReadOnlyList<IWebhook>> getChannelWebhooks = await APICache.GetChannelWebhooksAsync(channelID, ct);
        if (!getChannelWebhooks.IsSuccess) return Result<IWebhook>.FromError(getChannelWebhooks);

        IWebhook? webhook = getChannelWebhooks.Entity.FirstOrDefault(w => w.User.IsDefined(out IUser? creator) && creator.ID == botUserID);
        if (webhook != null) return Result<IWebhook>.FromSuccess(webhook);

        return await WebhookAPI.CreateWebhookAsync(channelID, "SerenaBot GPT2 Webhook", null, ct: ct);
    }

    private static Optional<string> GetAvatarURL(IUser? user)
    {
        if (user?.ID is not Snowflake userID || userID == default) return default;

        string? savedAvatar = JsonSerializer.Deserialize<Dictionary<ulong, string>>(File.ReadAllText("Resources/GPT2/avatars.json"))
            ?.TryGetValue(userID.Value, out string? savedUrl) == true ? savedUrl : null;

        return (savedAvatar, user) switch
        {
            { savedAvatar: not null } => new(savedAvatar),
            { user: not null } when user.BuildAvatarUrl() is string userUrl => new(userUrl),
            _ => default
        };
    }

    private async Task<Result> LogMessage(IMessage message, string? username = null, CancellationToken ct = default)
    {
        string? guildName = null;
        string? channelName = null;
        Snowflake guildID = default;
        Result<IGuild> getGuild = default;
        Result<IChannel> getChannel = await APICache.GetChannelAsync(message.ChannelID, ct);
        if (getChannel.IsSuccess)
        {
            getChannel.Entity.Name.IsDefined(out channelName);
            if (getChannel.Entity.GuildID.IsDefined(out guildID))
            {
                getGuild = await APICache.GetGuildAsync(guildID, ct: ct);
                if (getGuild.IsSuccess) guildName = getGuild.Entity.Name;
            }
        }

        guildName ??= "<Error fetching guild name>";
        channelName ??= "<Error fetching channel name>";
        username ??= message.Author.Username;

        Logger.LogInformation
        (
            "[<{guild}> #{channel}] {name}: {message}{link}",
            guildName,
            channelName,
            username,
            message.Content,
            guildID != default ? $"{Environment.NewLine}https://discord.com/channels/{guildID}/{message.ChannelID}/{message.ID}" : string.Empty
        );

        return (getGuild, getChannel) switch
        {
            // default(Result<T>).IsSuccess = true
            { getGuild.IsSuccess: false, getChannel.IsSuccess: false } => Result.FromError(new AggregateError(getGuild, getChannel)),
            { getGuild.IsSuccess: false } => Result.FromError(getGuild),
            { getChannel.IsSuccess: false } => Result.FromError(getChannel),
            _ => Result.FromSuccess()
        };
    }

    private enum Generator
    {
        [Display(Name = "GPT-2")] GPT2,
        Markov
    }
}
