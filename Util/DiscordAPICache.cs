using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;
using Remora.Results;
using System.Collections.Concurrent;

namespace SerenaBot.Util
{
    public class DiscordAPICache
    {
        private readonly IDiscordRestChannelAPI ChannelAPI;
        private readonly IDiscordRestGuildAPI GuildAPI;
        private readonly IDiscordRestOAuth2API OAuthAPI;
        private readonly IDiscordRestUserAPI UserAPI;
        private readonly IDiscordRestWebhookAPI WebhookAPI;

        private Result<IApplication>? CachedApplication;
        private readonly ConcurrentDictionary<Snowflake, Result<IGuild>> CachedGuilds = new();
        private readonly ConcurrentDictionary<Snowflake, Result<IChannel>> CachedChannels = new();
        private readonly ConcurrentDictionary<Snowflake, Result<IUser>> CachedUsers = new();
        private readonly ConcurrentDictionary<Snowflake, Result<IWebhook>> CachedWebhooks = new();
        private readonly ConcurrentDictionary<Snowflake, Result<IReadOnlyList<IWebhook>>> CachedGuildWebhooks = new();
        private readonly ConcurrentDictionary<Snowflake, Result<IReadOnlyList<IWebhook>>> CachedChannelWebhooks = new();

        public DiscordAPICache(IDiscordRestChannelAPI channelAPI, IDiscordRestGuildAPI guildAPI,
            IDiscordRestOAuth2API oauthAPI, IDiscordRestUserAPI userAPI, IDiscordRestWebhookAPI webhookAPI)
        {
            ChannelAPI = channelAPI;
            GuildAPI = guildAPI;
            OAuthAPI = oauthAPI;
            UserAPI = userAPI;
            WebhookAPI = webhookAPI;

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromHours(12));
                    CachedApplication = null;
                    CachedGuilds.Clear();
                    CachedChannels.Clear();
                    CachedUsers.Clear();
                    CachedWebhooks.Clear();
                    CachedGuildWebhooks.Clear();
                    CachedChannelWebhooks.Clear();
                }
            });
        }

        public async ValueTask<Result<IApplication>> GetCurrentBotApplicationInformationAsync(CancellationToken ct = default)
        {
            if (CachedApplication.HasValue) return CachedApplication.Value;

            Result<IApplication> getApplication = await OAuthAPI.GetCurrentBotApplicationInformationAsync(ct);
            return getApplication.IsSuccess
                ? (CachedApplication = getApplication).Value
                : getApplication;
        }

        public ValueTask<Result<IGuild>> GetGuildAsync(Snowflake guildID, CancellationToken ct = default)
            => GetCacheOrAPIAsync(CachedGuilds, guildID, new(() => GuildAPI.GetGuildAsync(guildID, ct: ct)));

        public ValueTask<Result<IChannel>> GetChannelAsync(Snowflake channelID, CancellationToken ct = default)
            => GetCacheOrAPIAsync(CachedChannels, channelID, new(() => ChannelAPI.GetChannelAsync(channelID, ct: ct)));

        public ValueTask<Result<IUser>> GetUserAsync(Snowflake userID, CancellationToken ct = default)
            => GetCacheOrAPIAsync(CachedUsers, userID, new(() => UserAPI.GetUserAsync(userID, ct: ct)));

        public async ValueTask<Result<IWebhook>> GetWebhookAsync(Snowflake webhookID, CancellationToken ct = default)
        {
            if (CachedWebhooks.TryGetValue(webhookID, out Result<IWebhook> result)) return result;

            result = await WebhookAPI.GetWebhookAsync(webhookID, ct);
            if (!result.IsSuccess) return result;

            CachedWebhooks[webhookID] = result;
            UpdateCachedList(CachedGuildWebhooks, new[] { result.Entity }, w => w.GuildID.HasValue ? w.GuildID.Value : default, createIfNotExists: false);
            UpdateCachedList(CachedChannelWebhooks, new[] { result.Entity }, w => w.ChannelID, createIfNotExists: false);

            return result;
        }

        public async ValueTask<Result<IReadOnlyList<IWebhook>>> GetGuildWebhooksAsync(Snowflake guildID, CancellationToken ct = default)
        {
            if (CachedGuildWebhooks.TryGetValue(guildID, out Result<IReadOnlyList<IWebhook>> result)) return result;

            result = await WebhookAPI.GetGuildWebhooksAsync(guildID, ct);
            if (!result.IsSuccess) return result;

            CachedGuildWebhooks[guildID] = result;
            UpdateCachedList(CachedChannelWebhooks, result.Entity, w => w.ChannelID);

            return result;
        }

        public async ValueTask<Result<IReadOnlyList<IWebhook>>> GetChannelWebhooksAsync(Snowflake channelID, CancellationToken ct = default)
        {
            if (CachedChannelWebhooks.TryGetValue(channelID, out Result<IReadOnlyList<IWebhook>> result)) return result;

            result = await WebhookAPI.GetChannelWebhooksAsync(channelID, ct);
            if (!result.IsSuccess) return result;

            CachedChannelWebhooks[channelID] = result;
            UpdateCachedList(CachedGuildWebhooks, result.Entity, w => w.GuildID.HasValue ? w.GuildID.Value : default);

            return result;
        }

        private static async ValueTask<Result<TEntity>> GetCacheOrAPIAsync<TEntity>(
            ConcurrentDictionary<Snowflake, Result<TEntity>> cache, Snowflake entityID, LazyAPICall<TEntity> apiCall)
            where TEntity : class
        {
            if (cache.TryGetValue(entityID, out Result<TEntity> result)) return result;

            await apiCall.DoAPICallAsync();
            return apiCall.IsSuccess
                ? cache[entityID] = Result<TEntity>.FromSuccess(apiCall.Entity)
                : apiCall.Error.Value;
        }

        private static void UpdateCachedList(ConcurrentDictionary<Snowflake, Result<IReadOnlyList<IWebhook>>> cache,
            IEnumerable<IWebhook> newItems, Func<IWebhook, Snowflake?> idFactory, bool createIfNotExists = true)
        {
            foreach (IWebhook webhook in newItems)
            {
                if (idFactory(webhook) is not Snowflake id
                    || (!cache.TryGetValue(id, out Result<IReadOnlyList<IWebhook>> cachedResult) && !createIfNotExists))
                {
                    continue;
                }

                if (cachedResult.Entity is not List<IWebhook> list)
                {
                    list = cachedResult.Entity != null
                        ? new(cachedResult.Entity)
                        : new();

                    cache[id] = Result<IReadOnlyList<IWebhook>>.FromSuccess(list);
                }

                list.RemoveAll(w => w.ID == webhook.ID);
                list.Add(webhook);
            }
        }
    }
}
