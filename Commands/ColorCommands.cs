using OneOf;
using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Rest.Core;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using SerenaBot.Util;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SerenaBot.Commands;

[Group("color", "colour")]
public class ColorCommands : BaseCommandGroup
{
    [Command("test")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Displays a test message using the given color")]
    public async Task<IResult> TestColorAsync(
        [Description("The color in hex format, for example #A592FF. Leave empty for a random color")] string? colorString = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await Feedback.SendContextualWarningAsync($"The current bot runtime '{RuntimeInformation.RuntimeIdentifier}' is not supported by this command");
        }

        Color color;
        if (colorString == null)
        {
            color = Color.FromArgb(Random.Next(256), Random.Next(256), Random.Next(256));
        }
        else if (!TryParseColor(colorString, out color))
        {
            return await Feedback.SendContextualWarningAsync($"Could not parse '{colorString}' as color code, please give them in the format of '#A592FF'");
        }

        string? name = Context.Member?.Nickname.GetValue() ?? Context.Member?.User.GetValue()?.Username;
        string? avatarUrl = Context.User?.BuildAvatarUrl();
        if (name == null || avatarUrl == null)
        {
            return await Feedback.SendContextualWarningAsync("Failed fetching user name and avatar");
        }

        Stream avatarStream = await Http.GetStreamAsync(avatarUrl);

        using Bitmap bmp = new(375, 75);
        using Graphics graphics = Graphics.FromImage(bmp);

        graphics.Clear(DiscordBrushes.Background.Color);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Draw square avatar
        Rectangle avatarPos = new(8, 18, 40, 40);

        using (Bitmap avatar = new(avatarStream))
        {
            graphics.DrawImage(avatar, avatarPos);
        }

        // Fill image with background color, excluding a circle centered over the avatar
        using (GraphicsPath path = new())
        {
            path.AddEllipse(avatarPos);

            using GraphicsPath path2 = new();
            path2.AddRectangle(new Rectangle(0, 0, bmp.Width, bmp.Height));
            path.AddPath(path2, true);

            graphics.FillPath(DiscordBrushes.Background, path);
        }

        // Draw username
        using SolidBrush usernameBrush = new(color);
        graphics.DrawString(name, ResourceFont.GGSansSemibold.GetFont(12), usernameBrush, 61, 17);

        // Draw text
        graphics.DrawString($"This is how I look with the color {ColorCode(color)}!",
            ResourceFont.GGSansNormal.GetFont(12), DiscordBrushes.Text, 61, 39);

        // Output to stream and send to discord
        graphics.Flush();

        using MemoryStream mem = new();
        bmp.Save(mem, ImageFormat.Png);
        mem.Position = 0;

        return await Feedback.SendContextualAsync(options: new(Attachments: new(new List<OneOf<FileData, IPartialAttachment>>() { new FileData("ColorTest.png", mem) })));
    }

    [Command("set")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Sets your topmost role with no other members to the given color")]
    public async Task<IResult> SetColorAsync(
        [Description("The color in hex format, for example #A592FF")] string colorString)
    {
        if (!TryParseColor(colorString, out Color color))
        {
            return await Feedback.SendContextualWarningAsync("Could not parse input as color code, please give them in the format of '#A592FF'");
        }

        if (Context.GuildID is not Snowflake guildID)
        {
            return await Feedback.SendContextualWarningAsync("No guild ID present for this interaction");
        }

        if (Context.Member is not { User.HasValue: true } member)
        {
            return await Feedback.SendContextualWarningAsync("No member present for this interaction");
        }

        Result<IRole> getColorRole = await GetTopRole(guildID, member, r => !r.IsPronounRole(), true);
        if (!getColorRole.IsSuccess && getColorRole.Error is not NotFoundError) return getColorRole;

        if (getColorRole.IsSuccess)
        {
            Color oldColor = getColorRole.Entity.Colour;
            return await GuildAPI.ModifyGuildRoleAsync(guildID, getColorRole.Entity.ID, color: color) is { IsSuccess: false } modifyRoleError
                ? modifyRoleError
                : await Feedback.SendContextualInfoAsync($"Updated color of <@&{getColorRole.Entity.ID}>: {ColorCode(oldColor)} -> {ColorCode(color)}");
        }

        string roleName = member.User.Value.Username + "#" + member.User.Value.Discriminator;
        Result<IRole> createRole = await GuildAPI.CreateGuildRoleAsync(guildID, roleName, colour: color);
        if (!createRole.IsSuccess) return createRole;

        return await GuildAPI.AddGuildMemberRoleAsync(guildID, member.User.Value.ID, createRole.Entity.ID) is { IsSuccess: false } addRoleError
            ? addRoleError
            : await Feedback.SendContextualInfoAsync($"Created and applied role <@&{createRole.Entity.ID}> with color {ColorCode(color)}");
    }

    [Command("get")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Returns the color of the given user")]
    public async Task<IResult> GetColorAsync(
        [Description("The guild member to fetch the color from")] IGuildMember user)
    {
        if (Context.GuildID is not Snowflake guildID)
        {
            return await Feedback.SendContextualWarningAsync("No guild ID present for this interaction");
        }

        Result<IRole> getRole = await GetTopRole(guildID, user, role => role.Colour.R > 0 || role.Colour.G > 0 || role.Colour.B > 0);
        if (!getRole.IsSuccess)
        {
            return getRole.Error is NotFoundError
                ? await Feedback.SendContextualInfoAsync($"No roles with color found, <@{user.User.Value.ID}> should have the default name color #FFFFFF")
                : getRole;
        }

        return await Feedback.SendContextualInfoAsync($"User <@{user.User.Value.ID}> has color {ColorCode(getRole.Entity.Colour)} from role <@&{getRole.Entity.ID}>");
    }

    [Command("rename")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Sets the name of your color role to the given string")]
    public async Task<IResult> RenameColorRoleAsync(
        [Description("The color in hex format, for example #A592FF")] string name)
    {
        if (Context.GuildID is not Snowflake guildID)
        {
            return await Feedback.SendContextualWarningAsync("No guild ID present for this interaction");
        }

        if (Context.Member is not { User.HasValue: true } member)
        {
            return await Feedback.SendContextualWarningAsync("No member present for this interaction");
        }

        Result<IRole> getColorRole = await GetTopRole(guildID, member, r => !r.IsPronounRole(), true);
        if (!getColorRole.IsSuccess)
        {
            return getColorRole.Error is NotFoundError
                ? await Feedback.SendContextualInfoAsync("No valid role found")
                : getColorRole;
        }

        string oldName = getColorRole.Entity.Name;
        return await GuildAPI.ModifyGuildRoleAsync(guildID, getColorRole.Entity.ID, name) is { IsSuccess: false } modifyRoleError
            ? modifyRoleError
            : await Feedback.SendContextualInfoAsync($"Updated name of <@&{getColorRole.Entity.ID}>: '{oldName}' -> '{name}'");
    }

    private async Task<Result<IRole>> GetTopRole(Snowflake guildID, IGuildMember member, Func<IRole, bool>? predicate = null, bool unique = false)
    {
        Result<IGuild> getGuild = await GuildAPI.GetGuildAsync(guildID);
        if (!getGuild.IsSuccess) return Result<IRole>.FromError(getGuild);

        IReadOnlyList<IGuildMember> guildMembers = Array.Empty<IGuildMember>();
        if (unique)
        {
            Result<IReadOnlyList<IGuildMember>> listGuildMembers = await GuildAPI.ListGuildMembersAsync(guildID, 1000);
            if (!listGuildMembers.IsSuccess) return Result<IRole>.FromError(listGuildMembers);

            guildMembers = listGuildMembers.Entity;
        }

        IRole? colorRole = member.Roles
            .Where(id => !unique || !guildMembers.Any(m => m.User.HasValue && m.User.Value.ID != member.User.Value.ID && m.Roles.Contains(id)))
            .Select(id => getGuild.Entity.Roles.First(r => r.ID == id))
            .Where(r => !r.IsManaged && (predicate == null || predicate(r)))
            .MaxBy(r => r.Position);

        if (colorRole != null)
        {
            return Result<IRole>.FromSuccess(colorRole);
        }

        return Result<IRole>.FromError(new NotFoundError("Requested role not found"));
    }

    private static bool TryParseColor(string colorString, out Color color)
    {
        colorString = colorString.Replace("#", "").Trim().ToUpper();
        if (colorString.Length != 6
            || !byte.TryParse(colorString[0..2], NumberStyles.HexNumber, null, out byte r)
            || !byte.TryParse(colorString[2..4], NumberStyles.HexNumber, null, out byte g)
            || !byte.TryParse(colorString[4..6], NumberStyles.HexNumber, null, out byte b))
        {
            color = default;
            return false;
        }

        color = Color.FromArgb(r, g, b);
        return true;
    }

    private static string ColorCode(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
