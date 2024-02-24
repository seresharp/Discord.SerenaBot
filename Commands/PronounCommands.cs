using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Rest.Core;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace SerenaBot.Commands;

[Group("pronouns")]
public class PronounCommands : BaseCommandGroup
{
    [Command("add")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Adds the given role")]
    public async Task<IResult> AddRoleAsync(
        [Description("The pronoun role to add")] PronounRoles role)
        => await SetPronounRoleActive(role, true);

    [Command("remove")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Removes the given role")]
    public async Task<IResult> RemoveRoleAsync(
        [Description("The pronoun role to remove")] PronounRoles role)
        => await SetPronounRoleActive(role, false);

    private async Task<IResult> SetPronounRoleActive(PronounRoles role, bool active)
    {
        if (Context.GuildID is not Snowflake guildID)
        {
            return await Feedback.SendContextualWarningAsync("No guild ID present for this interaction");
        }

        if (Context.User is not { ID: Snowflake userID })
        {
            return await Feedback.SendContextualWarningAsync("No user present for this interaction");
        }

        Result<IGuild> getGuild = await GuildAPI.GetGuildAsync(guildID);
        if (!getGuild.IsSuccess) return getGuild;

        Result<IRole> getRole = await GetPronounRole(getGuild.Entity, role.GetDisplayName());
        if (!getRole.IsSuccess) return getRole;

        Result roleAddRemove = active
            ? await GuildAPI.AddGuildMemberRoleAsync(guildID, userID, getRole.Entity.ID)
            : await GuildAPI.RemoveGuildMemberRoleAsync(guildID, userID, getRole.Entity.ID);

        if (!roleAddRemove.IsSuccess) return roleAddRemove;
        return await Feedback.SendContextualInfoAsync($"Role <@&{getRole.Entity.ID}> has been {(active ? "added" : "removed")}");
    }

    [Command("listmembers")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Lists the members of the given pronoun role")]
    public async Task<IResult> ListMembersAsync(
        [Description("The pronoun role to list the members of")] PronounRoles role)
    {
        if (Context.GuildID is not Snowflake guildID)
        {
            return await Feedback.SendContextualWarningAsync("No guild ID present for this interaction");
        }

        Result<IGuild> getGuild = await GuildAPI.GetGuildAsync(guildID);
        if (!getGuild.IsSuccess) return getGuild;

        Result<IRole> getRole = await GetPronounRole(getGuild.Entity, role.GetDisplayName());
        if (!getRole.IsSuccess) return getRole;

        Result<IReadOnlyList<IGuildMember>> listGuildMembers = await GuildAPI.ListGuildMembersAsync(guildID);
        if (!listGuildMembers.IsSuccess) return listGuildMembers;

        StringBuilder resp = new();
        resp.Append("Role <@&").Append(getRole.Entity.ID).Append("> has ");

        IGuildMember[] roleMembers = listGuildMembers.Entity.Where(m => m.User.HasValue && m.Roles.Contains(getRole.Entity.ID)).ToArray();
        if (roleMembers.Length == 0)
        {
            resp.Append("no members  ");
        }
        else
        {
            resp.Append("the members: ");
            foreach (IGuildMember member in roleMembers)
            {
                resp.Append("<@").Append(member.User.Value.ID).Append(">, ");
            }
        }

        return await Feedback.SendContextualInfoAsync(resp.ToString()[..^2]);
    }

    private async Task<Result<IRole>> GetPronounRole(IGuild guild, string roleName)
    {
        IRole? role = guild.Roles.FirstOrDefault(r => string.Equals(r.Name, roleName, StringComparison.InvariantCultureIgnoreCase));
        if (role != null)
        {
            return Result<IRole>.FromSuccess(role);
        }

        return await GuildAPI.CreateGuildRoleAsync(guild.ID, roleName);
    }

    public enum PronounRoles
    {
        [Display(Name = "he/him")] HeHim,
        [Display(Name = "she/her")] SheHer,
        [Display(Name = "they/them")] TheyThem,
        [Display(Name = "it/its")] ItIts,
        [Display(Name = "ae/aer")] AeAer,
        [Display(Name = "e/em")] EEm,
        [Display(Name = "fae/faer")] FaeFaer,
        [Display(Name = "per/per")] PerPer,
        [Display(Name = "ve/ver")] VeVer,
        [Display(Name = "xe/xem")] XeXem,
        [Display(Name = "zie/her")] ZieHer,
        [Display(Name = "any pronouns")] AnyPronouns
    }
}

public static class IRoleExtensions
{
    public static bool IsPronounRole(this IRole role)
    {
        foreach (PronounCommands.PronounRoles roleEnumValue in Enum.GetValues(typeof(PronounCommands.PronounRoles)))
        {
            if (string.Equals(role.Name, roleEnumValue.GetDisplayName(), StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
