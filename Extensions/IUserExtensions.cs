using Remora.Discord.API.Abstractions.Objects;

namespace SerenaBot.Extensions
{
    public static class IUserExtensions
    {
        public static string? BuildAvatarUrl(this IUser user)
            => user.Avatar != null
                ? $"https://cdn.discordapp.com/avatars/{user.ID.Value}/{user.Avatar.Value}"
                : null;

        public static string BuildMention(this IUser user)
            => $"<@{user.ID}>";
    }
}
