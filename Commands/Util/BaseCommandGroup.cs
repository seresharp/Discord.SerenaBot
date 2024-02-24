using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OneOf;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Discord.Commands.Services;
using Remora.Rest.Core;
using SerenaBot.Extensions;

namespace SerenaBot.Commands.Util;

public abstract class BaseCommandGroup : CommandGroup, IInitializer
{
    private readonly ContextInjectionService ContextInjection = null!;

    protected readonly IConfiguration Config = null!;

    protected readonly IDiscordRestChannelAPI ChannelAPI = null!;
    protected readonly IDiscordRestGuildAPI GuildAPI = null!;
    protected readonly IDiscordRestOAuth2API OAuthAPI = null!;
    protected readonly IDiscordRestUserAPI UserAPI = null!;
    protected readonly IDiscordRestWebhookAPI WebhookAPI = null!;

    protected readonly ILogger Logger = null!;
    protected readonly FeedbackService Feedback = null!;
    protected readonly HttpClient Http = null!;
    protected readonly Random Random = null!;

    protected IContextHelper Context { get; private set; } = null!;

    public virtual void Initialize()
        => Context = new ContextHelper(ContextInjection);

    // Offloading the ridiculously long type declarations here
    protected static Optional<IReadOnlyList<OneOf<FileData, IPartialAttachment>>> Attach(params OneOf<FileData, IPartialAttachment>[] files)
        => files;

    private class ContextHelper : IContextHelper
    {
        private readonly ContextInjectionService ContextInjection;

        public IUser? User
        {
            get => ContextInjection.Context switch
            {
                IInteractionContext ctx => ctx.Interaction.User.GetValue() ?? ctx.Interaction.Member.GetValue()?.User.GetValue(),
                IMessageContext ctx => ctx.Message.Author.GetValue(),
                _ => null
            };
        }

        public Snowflake? GuildID
        {
            get => ContextInjection.Context switch
            {
                IInteractionContext ctx => ctx.Interaction.GuildID.GetValue(),
                IMessageContext ctx => ctx.GuildID.GetValue(),
                _ => null
            };
        }

        public IGuildMember? Member
        {
            get => ContextInjection.Context switch
            {
                IInteractionContext ctx => ctx.Interaction.Member.GetValue(),
                IMessageContext => null, // TODO guild id + user id -> member
                _ => null
            };
        }

        public Snowflake? ChannelID
        {
            get => ContextInjection.Context switch
            {
                IInteractionContext ctx => ctx.Interaction.ChannelID.GetValue(),
                IMessageContext ctx => ctx.Message.ChannelID.GetValue(),
                _ => null
            };
        }

        public Snowflake? MessageID
        {
            get => ContextInjection.Context switch
            {
                IInteractionContext ctx => ctx.Interaction.Message.GetValue()?.ID,
                IMessageContext ctx => ctx.Message.ID.GetValue(),
                _ => null
            };
        }

        public ContextHelper(ContextInjectionService contextInjection)
        {
            ContextInjection = contextInjection;
        }
    }

    public interface IContextHelper
    {
        IUser? User { get; }
        Snowflake? GuildID { get; }
        IGuildMember? Member { get; }
        Snowflake? ChannelID { get; }
        Snowflake? MessageID { get; }
    }
}
