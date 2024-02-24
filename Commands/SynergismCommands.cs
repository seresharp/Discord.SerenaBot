using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Results;
using SerenaBot.Commands.Util;
using System.ComponentModel;
using System.Text;

namespace SerenaBot.Commands;

[Group("synergism")]
public class SynergismCommands : BaseCommandGroup
{
    [Group("shop")]
    public class ShopCommands : BaseCommandGroup
    {
        [Command("ascensionspeed")]
        [CommandType(ApplicationCommandType.ChatInput)]
        [Description("Optimize ascension speed shop upgrades")]
        public async Task<IResult> OptimizeAscensionSpeedAsync(int quarksToSpend)
        {
            int remainingQuarks = quarksToSpend;
            int asc1 = 0;
            int asc2 = 0;

            while (true)
            {
                bool asc1Afford = remainingQuarks >= 2000 + (asc1 * 500);
                bool asc2Afford = remainingQuarks >= 5000 + (asc2 * 1500);
                if (!asc1Afford && !asc2Afford)
                {
                    break;
                }

                double currentMult = (1 + (.012 * asc1)) * (1 + (.006 * asc2));

                double asc1Mult = asc1Afford ? (1 + (.012 * (asc1 + 1))) * (1 + (.006 * asc2)) : currentMult;
                double asc2Mult = asc2Afford ? (1 + (.012 * asc1)) * (1 + (.006 * (asc2 + 1))) : currentMult;

                double asc1Eff = (asc1Mult - currentMult) / (2000 + (asc1 * 500));
                double asc2Eff = (asc2Mult - currentMult) / (5000 + (asc2 * 1500));

                remainingQuarks -= asc1Eff >= asc2Eff
                    ? 2000 + (asc1++ * 500)
                    : 5000 + (asc2++ * 1500);
            }

            double finalMult = (1 + (.012 * asc1)) * (1 + (.006 * asc2));

            StringBuilder resp = new();
            resp.AppendLine($"Ascension Speed 1: {asc1}")
                .AppendLine($"Ascension Speed 2: {asc2}")
                .AppendLine($"Total Multiplier: {(100 * finalMult).ToString("F2")}%")
                .AppendLine($"Total Cost: {quarksToSpend - remainingQuarks}");

            return await Feedback.SendContextualEmbedAsync(new Embed(Description: resp.ToString()));
        }
    }
}
