using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Results;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Conditions;
using Remora.Rest.Core;
using Remora.Rest.Results;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace SerenaBot.Commands
{
    public class MarkovChainCommands : BaseCommandGroup
    {
        [Command("mimic")]
        [CommandType(ApplicationCommandType.ChatInput)]
        [Description("Pretend to be another user")]
        public async Task<IResult> MimicUserAsync(IUser user)
        {
            if (Context.GuildID is not Snowflake guildId)
            {
                return await Feedback.SendContextualWarningAsync("No guild ID present for this interaction", ct: CancellationToken);
            }

            FileInfo jsonFile = new($"Resources/Markov/{guildId.Value}/{user.ID.Value}.json");
            if (!jsonFile.Exists)
            {
                return await Feedback.SendContextualWarningAsync($"No model found for user <@{user.ID}>", ct: CancellationToken);
            }

            JsonSerializerOptions opt = new() { IncludeFields = true };
            MarkovChain chain = JsonSerializer.Deserialize<MarkovChain>(File.ReadAllText(jsonFile.FullName), opt)
                ?? throw new NullReferenceException("Deserialization result null");

            return await Feedback.SendContextualInfoAsync(chain.CreateMessage(), ct: CancellationToken);
        }

        [Command("downloadmessages")]
        [Ephemeral]
        [RequireOwner]
        [CommandType(ApplicationCommandType.ChatInput)]
        [Description("Fetches every message ever sent in the server and stores it in a json file")]
        public async Task<IResult> GenerateModelsAsync()
        {
            if (Context.GuildID is not Snowflake guildId)
            {
                return await Feedback.SendContextualWarningAsync("No guild ID present for this interaction", ct: CancellationToken);
            }

            await Feedback.SendContextualInfoAsync("Ok, I'll get started. This may take a while", ct: CancellationToken);

            MarkovChain globalChain = new();
            Dictionary<Snowflake, MarkovChain> userChains = new();
            Dictionary<Snowflake, List<string>> userMessageDict = new();

            var getChannels = await GuildAPI.GetGuildChannelsAsync(guildId, ct: CancellationToken);
            if (!getChannels.IsSuccess) return getChannels;
            foreach (IChannel channel in getChannels.Entity)
            {
                if (channel.Type != ChannelType.GuildText)
                {
                    continue;
                }

                Optional<Snowflake> lastMessage = default;
                while (true)
                {
                    var getMessages = await ChannelAPI.GetChannelMessagesAsync(channel.ID, before: lastMessage, limit: 100, ct: CancellationToken);
                    if (!getMessages.IsSuccess)
                    {
                        if (getMessages.Error is RestResultError<RestError> { Error.Code: DiscordError.MissingAccess })
                        {
                            break;
                        }

                        Logger.LogError("{error}", getMessages.Error);
                        break;
                    }

                    if (getMessages.Entity.Count == 0)
                    {
                        break;
                    }

                    foreach (IMessage message in getMessages.Entity)
                    {
                        if (string.IsNullOrWhiteSpace(message.Content) || message.Content.Contains("||") || message.Author.IsBot.GetValue())
                        {
                            continue;
                        }

                        if (!userMessageDict.TryGetValue(message.Author.ID, out List<string>? userMessages)) userMessageDict[message.Author.ID] = userMessages = new();
                        userMessages.Add(message.Content);

                        string[] words = message.Content.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        if (words.Length == 0)
                        {
                            continue;
                        }

                        if (!userChains.TryGetValue(message.Author.ID, out MarkovChain? userChain)) userChains[message.Author.ID] = userChain = new();

                        userChain.Start.IncrementChild(words[0]);
                        globalChain.Start.IncrementChild(words[0]);
                        for (int i = 1; i < words.Length; i++)
                        {
                            userChain[words[i - 1]].IncrementChild(words[i]);
                            globalChain[words[i - 1]].IncrementChild(words[i]);
                        }

                        userChain[words[^1]].IncrementChild(userChain.End.Word);
                        globalChain[words[^1]].IncrementChild(globalChain.End.Word);
                    }

                    lastMessage = getMessages.Entity[getMessages.Entity.Count - 1].ID;
                }
            }

            JsonSerializerOptions opt = new() { IncludeFields = true, WriteIndented = true };
            FileInfo globalFile = new($"{guildId.Value}/global.json");
            globalFile.Directory?.Create();
            File.WriteAllText(globalFile.FullName, JsonSerializer.Serialize(globalChain, opt));
            foreach ((Snowflake userId, MarkovChain userChain) in userChains)
            {
                FileInfo userFile = new($"Resources/Markov/{guildId.Value}/{userId.Value}.json");
                userFile.Directory?.Create();
                File.WriteAllText(userFile.FullName, JsonSerializer.Serialize(userChain, opt));
            }

            foreach ((Snowflake userId, List<string> userMessages) in userMessageDict)
            {
                FileInfo userFile = new($"Resources/Markov/{guildId.Value}/{userId.Value}_messages.json");
                userFile.Directory?.Create();
                File.WriteAllText(userFile.FullName, JsonSerializer.Serialize(userMessages, opt));
            }

            return Result.FromSuccess();
        }

        private class MarkovChain
        {
            public Dictionary<string, MarkovNode> Words = new();
            public MarkovNode Start;
            public MarkovNode End;

            public MarkovChain()
            {
                Start = this[string.Empty];
                End = this["\0"];
            }

            public MarkovNode this[string word]
            {
                get => Words.TryGetValue(word, out MarkovNode? node)
                    ? node
                    : Words[word] = new(word);
            }

            public string CreateMessage()
            {
                List<string> words = new();
                MarkovNode node = this[Start.GetNextWord()];
                while (node.Word != End.Word)
                {
                    words.Add(node.Word);
                    node = this[node.GetNextWord()];
                }

                return string.Join(' ', words);
            }
        }

        private class MarkovNode
        {
            public string Word;
            public Dictionary<string, int> Weights = new();

            public MarkovNode(string word)
            {
                Word = word;
            }

            public void IncrementChild(string word)
            {
                if (!Weights.TryGetValue(word, out int val))
                {
                    Weights[word] = val = 0;
                }

                Weights[word] = val + 1;
            }

            public string GetNextWord()
            {
                if (Weights.Count == 0)
                {
                    return "\0";
                }
                else if (Weights.Count == 1)
                {
                    return Weights.First().Key;
                }

                (string word, int weight)[] pairs = new (string, int)[Weights.Count];

                int i = 0;
                foreach ((string word, int weight) in Weights)
                {
                    int sum = i == 0
                        ? weight - 1
                        : pairs[i - 1].weight + weight;

                    pairs[i++] = (word, sum);
                }

                int rnd = RandomNumberGenerator.GetInt32(pairs[^1].weight + 1);
                string? choice = null;
                for (i = pairs.Length - 1; i >= 0; i--)
                {
                    if (rnd > pairs[i].weight)
                    {
                        break;
                    }

                    choice = pairs[i].word;
                }

                return choice ?? throw new InvalidOperationException("Weighted random failure to choose");
            }
        }
    }
}
