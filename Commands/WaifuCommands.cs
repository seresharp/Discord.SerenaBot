using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Feedback.Messages;
using Remora.Discord.Gateway.Responders;
using Remora.Discord.Interactivity;
using Remora.Discord.Interactivity.Services;
using Remora.Rest.Core;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Commands.Util.WaifuLabs;
using SerenaBot.Extensions;
using SerenaBot.Extensions.WaifuLabs;
using System.ComponentModel;
using System.Net.WebSockets;

namespace SerenaBot.Commands;

public class WaifuCommands : BaseCommandGroup
{
    private readonly InMemoryDataService<Snowflake, WaifuBuilder> WaifuBuilders = null!;

    [Command("waifu")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Attaches an image of a waifu, taken from https://waifulabs.com/")]
    public async Task<IResult> SendWaifuAsync()
    {
        using ClientWebSocket socket = new();
        if (await socket.ConnectWaifuLabsAsync(CancellationToken) is { IsSuccess: false } connectErr) return connectErr;

        int messageID = 3;
        if (await socket.GetResponseAsync(new WaifuJoinMessage(messageID), CancellationToken) is { IsSuccess: false } joinError) return joinError;
        messageID = 5; // Message ID jumps up by 2 instead of 1 after the join message when using the website, copying that behavior

        // Iterate through the 4 creation steps with random selections
        string? seed = null;
        for (int i = 0; i < 4; i++)
        {
            Result<WaifuSocketResponse> socketResp = await socket.GetResponseAsync(new WaifuGenerateMessage(messageID++, seed, i), CancellationToken);
            if (!socketResp.IsSuccess) return socketResp;

            seed = socketResp.Entity.Girls.GetRandomItem().Seed;
        }

        // Get the final larger res image
        Result<WaifuSocketResponse> finalGenerateResp = await socket.GetResponseAsync(new WaifuGenerateMessage(messageID, seed, Big: true), CancellationToken);
        if (!finalGenerateResp.IsSuccess) return finalGenerateResp;

        MemoryStream mem = new(Convert.FromBase64String(finalGenerateResp.Entity.Girls[0].ImageData));
        return await Feedback.SendContextualAsync(options: new(Attachments: Attach(new FileData("waifu.png", mem))), ct: CancellationToken);
    }

    [Command("waifubuilder")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Provides an interface for creating your own waifu using the https://waifulabs.com/ api")]
    public async Task<IResult> SendWaifuBuilderAsync(string? seed = null)
    {
        if (Context.User is not { ID: Snowflake userID })
        {
            return await Feedback.SendContextualWarningAsync("No user ID present for this interaction");
        }

        Result<WaifuBuilder> res = await WaifuBuilder.Begin(ChannelAPI, seed, CancellationToken);
        if (!res.IsSuccess) return res;

        Result<Embed> getEmbed = await res.Entity.GetEmbed();
        if (!getEmbed.IsSuccess)
        {
            res.Entity.Dispose();
            return getEmbed;
        }

        Result<IMessage> msg = await Feedback.SendContextualEmbedAsync(getEmbed.Entity, WaifuBuilder.ButtonOptions, CancellationToken);
        if (!msg.IsSuccess)
        {
            res.Entity.Dispose();
            return msg;
        }

        res.Entity.ChannelID = msg.Entity.ChannelID;
        res.Entity.MessageID = msg.Entity.ID;
        res.Entity.UserID = Context.User.ID;

        if (!WaifuBuilders.TryAddData(msg.Entity.ID, res.Entity))
        {
            res.Entity.Dispose();
            return Result.FromError(new InvalidOperationError("WaifuBuilder instance could not be saved to memory"));
        }

        return Result.FromSuccess();
    }

    public sealed class WaifuBuilder : IDisposable
    {
        public ClientWebSocket Socket { get; private set; }
        public List<Girl> Girls { get; init; } = new();
        public int WaifuMessageID { get; private set; }
        public int Page { get; private set; }

        public Snowflake ChannelID { get; set; }
        public Snowflake MessageID { get; set; }
        public Snowflake UserID { get; set; }

        private readonly IDiscordRestChannelAPI ChannelAPI;
        private readonly Dictionary<Girl, string> Attachments = new();

        public static async Task<Result<WaifuBuilder>> Begin(IDiscordRestChannelAPI channelAPI, string? seed = null, CancellationToken ct = default)
        {
            // Connect to waifulabs and get the initial set of waifus
            ClientWebSocket socket = new();
            try
            {
                if (await socket.ConnectWaifuLabsAsync(ct) is { IsSuccess: false } connectErr)
                {
                    socket.Dispose();
                    return Result<WaifuBuilder>.FromError((IResultError)connectErr);
                }

                if (await socket.GetResponseAsync(new WaifuJoinMessage(3), ct) is { IsSuccess: false } joinError)
                {
                    socket.Dispose();
                    return Result<WaifuBuilder>.FromError(joinError);
                }

                WaifuBuilder builder = new(socket, channelAPI) { WaifuMessageID = 6 };

                if (seed == null)
                {
                    Result<WaifuSocketResponse> socketResp = await socket.GetResponseAsync(new WaifuGenerateMessage(5, null, 0), ct);
                    if (!socketResp.IsSuccess)
                    {
                        socket.Dispose();
                        return Result<WaifuBuilder>.FromError(socketResp);
                    }

                    builder.Girls.AddRange(socketResp.Entity.Girls);
                }
                else
                {
                    Result<WaifuSocketResponse> socketResp = await socket.GetResponseAsync(new WaifuGenerateMessage(5, seed, Big: true), ct);
                    if (!socketResp.IsSuccess)
                    {
                        socket.Dispose();
                        return Result<WaifuBuilder>.FromError(socketResp);
                    }

                    socketResp.Entity.Girls[0] = new() { Seed = seed, ImageData = socketResp.Entity.Girls[0].ImageData };
                    builder.Girls.AddRange(socketResp.Entity.Girls);
                }

                return Result<WaifuBuilder>.FromSuccess(builder);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public async Task<IResult> ApplyStepAsync(int step)
        {
            if (step < 1 || step > 3) return Result.FromError(new ArgumentOutOfRangeError(nameof(step)));

            Result<WaifuSocketResponse> resp = await GetResponseAsync(new WaifuGenerateMessage(WaifuMessageID++, Girls[Page].Seed, step));
            if (!resp.IsSuccess) return resp;

            Girls.Clear();
            Girls.AddRange(resp.Entity.Girls);
            Attachments.Clear();
            return await GoToPageAsync(0, true);
        }

        public async Task<IResult> FinalizeAsync()
        {
            string? seed = Girls[Page].Seed;
            Result<WaifuSocketResponse> resp = await GetResponseAsync(new WaifuGenerateMessage(WaifuMessageID++, Girls[Page].Seed, Big: true));
            if (!resp.IsSuccess) return resp;

            Girls.Clear();
            Girls.Add(new() { Seed = seed, ImageData = resp.Entity.Girls[0].ImageData });
            Attachments.Clear();
            Page = 0;

            Result<Embed> getEmbed = await GetEmbed();
            if (!getEmbed.IsSuccess) return getEmbed;

            return await ChannelAPI.EditMessageAsync(ChannelID, MessageID, embeds: new List<Embed>() { getEmbed.Entity }, components: new List<IMessageComponent>());
        }

        public async Task<IResult> GoToPageAsync(int newPage, bool girlsModified = false)
        {
            if (newPage < 0 || newPage > Girls.Count - 1) return Result.FromError(new ArgumentOutOfRangeError(nameof(newPage)));
            if (!girlsModified && Page == newPage) return Result.FromSuccess();

            Page = newPage;
            Result<Embed> getEmbed = await GetEmbed();
            if (!getEmbed.IsSuccess) return getEmbed;

            return await ChannelAPI.EditMessageAsync(ChannelID, MessageID, embeds: new List<Embed>() { getEmbed.Entity });
        }

        public async Task<Result<Embed>> GetEmbed()
        {
            if (!Attachments.TryGetValue(Girls[Page], out string? url))
            {
                using MemoryStream stream = new(Convert.FromBase64String(Girls[Page].ImageData));
                Result<IMessage> uploadImage = await ChannelAPI.CreateMessageAsync
                (
                    new(1059506722097602662ul),
                    attachments: new(new List<OneOf.OneOf<FileData, IPartialAttachment>> { new FileData("waifu.png", stream) })
                );

                if (!uploadImage.IsSuccess) return Result<Embed>.FromError(uploadImage);
                Attachments[Girls[Page]] = url = uploadImage.Entity.Attachments[0].Url;
            }

            return new Embed
            (
                Title: $"Page {Page + 1} / {Girls.Count}",
                Description: Girls[Page].Seed ?? string.Empty,
                Image: new EmbedImage(url)
            );
        }

        private WaifuBuilder(ClientWebSocket socket, IDiscordRestChannelAPI channelAPI)
        {
            ChannelAPI = channelAPI;
            Socket = socket;
        }

        private async Task<Result<WaifuSocketResponse>> GetResponseAsync(WaifuSocketMessage message, CancellationToken ct = default)
        {
            try
            {
                return await Socket.GetResponseAsync(message, ct);
            }
            catch (WebSocketException)
            {
                Socket = new();
                if (await Socket.ConnectWaifuLabsAsync(ct) is { IsSuccess: false } connectErr) return Result<WaifuSocketResponse>.FromError(connectErr.Error, connectErr.Inner);
                if (message is not WaifuJoinMessage && await Socket.GetResponseAsync(new WaifuJoinMessage(3), ct) is { IsSuccess: false } joinErr)
                {
                    return joinErr;
                }

                return await Socket.GetResponseAsync(message, ct);
            }
        }

        public void Dispose()
        {
            Socket.Dispose();
        }

        internal sealed class MessageDeletedResponder : IResponder<IMessageDelete>, IResponder<IMessageDeleteBulk>
        {
            private readonly InMemoryDataService<Snowflake, WaifuBuilder> WaifuBuilders;

            public MessageDeletedResponder(InMemoryDataService<Snowflake, WaifuBuilder> waifuBuilders)
            {
                WaifuBuilders = waifuBuilders;
            }

            public async Task<Result> RespondAsync(IMessageDelete gatewayEvent, CancellationToken ct = default)
            {
                _ = await WaifuBuilders.TryRemoveDataAsync(gatewayEvent.ID);
                return Result.FromSuccess();
            }

            public async Task<Result> RespondAsync(IMessageDeleteBulk gatewayEvent, CancellationToken ct = default)
            {
                foreach (var id in gatewayEvent.IDs)
                {
                    _ = await WaifuBuilders.TryRemoveDataAsync(id);
                }

                return Result.FromSuccess();
            }
        }

        public static readonly FeedbackMessageOptions ButtonOptions = new(MessageComponents: new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new ButtonComponent
                (
                    ButtonComponentStyle.Secondary,
                    "First",
                    new PartialEmoji(Name: "⏮"),
                    CustomIDHelpers.CreateButtonID("waifubuilder-button-first")
                ),
                new ButtonComponent
                (
                    ButtonComponentStyle.Secondary,
                    "Previous",
                    new PartialEmoji(Name: "◀"),
                    CustomIDHelpers.CreateButtonID("waifubuilder-button-previous")
                ),
                new ButtonComponent
                (
                    ButtonComponentStyle.Secondary,
                    "Next",
                    new PartialEmoji(Name: "▶"),
                    CustomIDHelpers.CreateButtonID("waifubuilder-button-next")
                ),
                new ButtonComponent
                (
                    ButtonComponentStyle.Secondary,
                    "Last",
                    new PartialEmoji(Name: "⏭"),
                    CustomIDHelpers.CreateButtonID("waifubuilder-button-last")
                )
            }),
            new ActionRowComponent(new[]
            {
                new ButtonComponent
                (
                    ButtonComponentStyle.Secondary,
                    "Tune Color",
                    CustomID: CustomIDHelpers.CreateButtonID("waifubuilder-button-color")
                ),
                new ButtonComponent
                (
                    ButtonComponentStyle.Secondary,
                    "Tune Details",
                    CustomID: CustomIDHelpers.CreateButtonID("waifubuilder-button-details")
                ),
                new ButtonComponent
                (
                    ButtonComponentStyle.Secondary,
                    "Tune Pose",
                    CustomID: CustomIDHelpers.CreateButtonID("waifubuilder-button-pose")
                ),
                new ButtonComponent
                (
                    ButtonComponentStyle.Secondary,
                    "Finalize",
                    CustomID: CustomIDHelpers.CreateButtonID("waifubuilder-button-finalize")
                )
            })
        });
    }
}
