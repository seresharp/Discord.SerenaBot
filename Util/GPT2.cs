using Remora.Rest.Core;
using SerenaBot.Extensions;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SerenaBot.Util;

public static class GPT2
{
    private static readonly Random Random = new();
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public static async Task<(Snowflake? userID, string? message)> GetMessageForGuild(Snowflake guildID)
    {
        await FileLock.WaitAsync();

        try
        {
            DirectoryInfo guildFolder = new($"Resources/GPT2/messages/{guildID.Value}");
            if (!guildFolder.Exists) return (null, null);

            DirectoryInfo[] userFolders = guildFolder.GetDirectories();
            if (userFolders.Length == 0) return (null, null);

            DirectoryInfo userFolder = userFolders.GetRandomItem();
            FileInfo[] userFiles = userFolder.GetFiles();
            if (userFiles.Length == 0) return (null, null);

            FileInfo userFile = userFiles.GetRandomItem();
            List<string> messages = new(await File.ReadAllLinesAsync(userFile.FullName));
            if (messages.Count == 0) return (null, null);

            List<string> messageGroup = new();
            while (messages.Count > 0 && (messageGroup.Count == 0 || ShouldGroupMessages(messageGroup[^1], messages[0])))
            {
                messageGroup.Add(messages[0]);
                messages.RemoveAt(0);
            }

            userFile.Delete();
            if (messages.Count > 0)
            {
                await File.WriteAllLinesAsync(userFile.FullName, messages);
            }

            return (new(ulong.Parse(userFolder.Name)), string.Join('\n', messageGroup));
        }
        finally
        {
            FileLock.Release();
        }
    }

    public static async Task GenerateMessageFiles()
    {
        await FileLock.WaitAsync();

        try
        {
            foreach (DirectoryInfo guildFolder in new DirectoryInfo("Resources/GPT2/models").EnumerateDirectories())
            {
                foreach (DirectoryInfo userFolder in guildFolder.EnumerateDirectories())
                {
                    for (int i = 0; i < 8; i++)
                    {
                        FileInfo messagesFile = new($"Resources/GPT2/messages/{guildFolder.Name}/{userFolder.Name}/{i}.txt");
                        if (messagesFile.Exists)
                        {
                            continue;
                        }

                        messagesFile.Directory?.Create();
                        ProcessStartInfo start = new()
                        {
                            FileName = "Resources/GPT2/genmessages.exe",
                            ArgumentList =
                        {
                            $"{guildFolder.Name}/{userFolder.Name}",
                            messagesFile.FullName
                        },
                            WorkingDirectory = "Resources/GPT2"
                        };

                        if (guildFolder.Name == "525099478173220869")
                        {
                            start.ArgumentList.Add("-d");
                        }

                        Process genmessages = Process.Start(start) ?? throw new NullReferenceException("Process.Start returned null. genmessages.exe failure to start?");
                        Console.WriteLine($"Running 'genmessages.exe {string.Join(' ', genmessages.StartInfo.ArgumentList)}'");
                        await genmessages.WaitForExitAsync();

                        if (!new FileInfo(messagesFile.FullName).Exists)
                        {
                            throw new InvalidOperationException("Messages file still non-existent after genmessages.exe completion");
                        }
                    }
                }
            }
        }
        finally
        {
            FileLock.Release();
        }
    }

    public static bool ShouldGroupMessages(string msg1, string msg2)
    {
        msg1 = msg1.ToLower().Trim();
        msg2 = msg2.ToLower().Trim();

        string[] words1 = Regex.Split(msg1, "\\W|_");
        string[] words2 = Regex.Split(msg2, "\\W|_");

        // One of the messages is entirely non-word characters, no idea what's going on there
        if (words1.Length == 0 || words2.Length == 0) return false;

        // Starts with the same (non-url) word, very likely related
        if (words1[0] == words2[0] && !(words1[0] is "http" or "https" or "i")) return true;

        // Second message starts with a conjunction, likely a continuation
        if (new[] { "for", "and", "nor", "but", "or", "yet", "so" }.Contains(words2[0])) return true;

        // Greentext type messages
        if (msg1.StartsWith('>')) return true;

        // Numbered lists
        if (int.TryParse(words1[0], out _) && int.TryParse(words2[0], out _)) return true;

        // First message ends with :, expecting more after
        if (msg1.EndsWith(':')) return true;

        // Second message parenthesized, probably a side note type thing on the previous message
        if (msg2.StartsWith('(') && msg2.EndsWith(')')) return true;

        // First message ends with 'like', semi-common to send in the middle of thoughts after that
        if (words1[^1] == "like") return true;

        // Give short (1-2 word) messages a chance to group onto neighboring ones
        return words1.Length <= 2 && Random.Next(3) == 0;
    }
}
