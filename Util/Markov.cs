using Remora.Rest.Core;
using SerenaBot.Extensions;
using System.Text.Json;

namespace SerenaBot.Util;

public static class Markov
{
    private static readonly Random Random = new();

    public static (Snowflake? userID, string? message) GetMessageForGuild(Snowflake guildID)
    {
        DirectoryInfo guildFolder = new($"Resources/Markov/{guildID.Value}");
        if (!guildFolder.Exists) return (null, null);

        FileInfo[] userFiles = guildFolder.GetFiles()
            .Where(f => f.Name.EndsWith(".json") && ulong.TryParse(f.Name[0..^5], out ulong userID)
                && !Directory.Exists($"Resources/GPT2/models/{guildID.Value}/{userID}"))
            .ToArray();

        if (userFiles.Length == 0) return (null, null);

        FileInfo userFile = userFiles.GetRandomItem();
        MarkovChain? chain = JsonSerializer.Deserialize<MarkovChain>(File.ReadAllText(userFile.FullName), new JsonSerializerOptions { IncludeFields = true });
        if (chain == null) return (null, null);

        return (new(ulong.Parse(userFile.Name[0..^5])), chain.CreateMessage());
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

            int rnd = Random.Next(pairs[^1].weight + 1);
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
