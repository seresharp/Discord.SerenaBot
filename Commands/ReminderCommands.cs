using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Rest.Core;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using System.ComponentModel;
using System.Text.Json;

namespace SerenaBot.Commands;

[Group("reminder")]
public class ReminderCommands : BaseCommandGroup
{
    private const string JSON_PATH = "Resources/reminders.json";
    private static readonly List<Reminder> Reminders = JsonSerializer.Deserialize<List<Reminder>>(File.Exists(JSON_PATH) ? File.ReadAllText(JSON_PATH) : "[]") ?? new();
    private static readonly SemaphoreSlim RemindersLock = new(1, 1);

    public override void Initialize()
    {
        base.Initialize();
        async Task CheckRemindersListAsync()
        {
            for (int i = Reminders.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (await CheckReminderAsync(Reminders[i], CancellationToken))
                    {
                        Reminders.RemoveAt(i);
                        File.WriteAllText(JSON_PATH, JsonSerializer.Serialize(Reminders, new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
                catch (Exception e) when (e is not ObjectDisposedException)
                {
                    Logger.LogError("Error in reminder loop: {error}", e);
                }
            }
        }

        Task.Run(async () =>
        {
            while (true)
            {
                await RemindersLock.WaitAsync();
                try
                {
                    await CheckRemindersListAsync();
                }
                catch (Exception e)
                {
                    // Not using Logger because it's very likely to have been disposed here
                    Console.WriteLine("Fatal error in reminder loop: {0}", e);
                    throw;
                }
                finally
                {
                    RemindersLock.Release();
                }

                await Task.Delay(1000);
            }
        });
    }

    [Command("set")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Notify the bot to ping you after the specified time has passed")]
    public async Task<IResult> SetReminderAsync(TimeSpan time, string? message = null)
    {
        if (Context.GuildID is not Snowflake guildID
            || Context.ChannelID is not Snowflake channelID
            || Context.User is not { ID: Snowflake userID })
        {
            return await Feedback.SendContextualInfoAsync("Guild ID, Channel ID, and User ID must be present for this interaction");
        }

        long timestamp = new DateTimeOffset(DateTime.Now + time).ToUnixTimeSeconds();
        string messageSuffix = message != null
            ? $" with message '{message}'"
            : string.Empty;

        Result<IReadOnlyList<IMessage>> botResponse = await Feedback.SendContextualInfoAsync($"Reminder will be sent at <t:{timestamp}>{messageSuffix}");
        if (botResponse.IsSuccess)
        {
            ulong messageID = botResponse.Entity[^1].ID.Value;

            await RemindersLock.WaitAsync();
            try
            {
                Reminders.Add(new(guildID.Value, channelID.Value, messageID, DateTime.Now + time, userID.Value, message));
                File.WriteAllText(JSON_PATH, JsonSerializer.Serialize(Reminders, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally
            {
                RemindersLock.Release();
            }
        }

        return botResponse;
    }

    [Command("cancel")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Cancel a previously set reminder")]
    public async Task<IResult> CancelReminderAsync(
        [Description("The message id from '/reminder set', or a link to it")] string reminder)
    {
        if (Context.User is not { ID.Value: ulong userID })
        {
            return await Feedback.SendContextualInfoAsync("User ID must be present for this interaction");
        }

        ulong guildID;
        ulong channelID;
        if (ulong.TryParse(reminder, out ulong messageID))
        {
            if (Context.GuildID is not Snowflake guildIDSnowflake
                || Context.ChannelID is not Snowflake channelIDSnowflake)
            {
                return await Feedback.SendContextualInfoAsync("Guild ID and Channel ID must be present for this interaction");
            }

            guildID = guildIDSnowflake.Value;
            channelID = channelIDSnowflake.Value;
        }
        else
        {
            string[] urlSections = reminder.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (urlSections.Length < 3
                || !ulong.TryParse(urlSections[^3], out guildID)
                || !ulong.TryParse(urlSections[^2], out channelID)
                || !ulong.TryParse(urlSections[^1], out messageID))
            {
                return await Feedback.SendContextualInfoAsync("Could not parse provided url");
            }
        }

        await RemindersLock.WaitAsync();
        try
        {
            Reminder? rem = Reminders.Find(r => r.GuildID == guildID && r.ChannelID == channelID && r.MessageID == messageID);
            if (rem == null)
            {
                return await Feedback.SendContextualInfoAsync("Reminder not found");
            }

            if (rem.UserID != userID)
            {
                return await Feedback.SendContextualInfoAsync($"Nice try, but only <@{rem.UserID}> is allowed to cancel this reminder");
            }

            Reminders.Remove(rem);
            File.WriteAllText(JSON_PATH, JsonSerializer.Serialize(Reminders, new JsonSerializerOptions { WriteIndented = true }));

            string remMessage = rem.Message != null
                ? $" for '{rem.Message}' "
                : " ";

            return await Feedback.SendContextualInfoAsync($"Reminder{remMessage}will no longer be sent");
        }
        finally
        {
            RemindersLock.Release();
        }
    }

    private async Task<bool> CheckReminderAsync(Reminder rem, CancellationToken ct)
    {
        if (rem.Time > DateTime.Now)
        {
            return false;
        }

        Result<IMessage> sendReminder = await ChannelAPI.CreateMessageAsync
        (
            new(rem.ChannelID),
            $"<@{rem.UserID}>",
            embeds: rem.Message != null
                ? new Embed(Description: rem.Message, Colour: Feedback.Theme.Primary).AsOptionalList<IEmbed>()
                : default,
            messageReference: new MessageReference(new Snowflake(rem.MessageID), new Snowflake(rem.ChannelID), new Snowflake(rem.GuildID), FailIfNotExists: false),
            ct: ct
        );

        return sendReminder.IsSuccess;
    }

    private record Reminder(ulong GuildID, ulong ChannelID, ulong MessageID, DateTime Time, ulong UserID, string? Message);
}
