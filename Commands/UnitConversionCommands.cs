using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Results;
using SerenaBot.Commands.Util;
using System.ComponentModel;

namespace SerenaBot.Commands
{
    [Group("convert")]
    public class UnitConversionCommands : BaseCommandGroup
    {
        [Group("fahrenheit")]
        public class FahrenheitToCelsius : BaseCommandGroup
        {
            [Command("celsius")]
            [CommandType(ApplicationCommandType.ChatInput)]
            [Description("Convert a value in fahrenheit to celsius")]
            public async Task<IResult> FahrenheitToCelsiusAsync(float f)
            {
                float c = (f - 32) * (5 / 9f);
                return await Feedback.SendContextualInfoAsync($"{f:0.##}f is {c:0.##}c");
            }
        }

        [Group("celsius")]
        public class CelsiusToFahrenheit : BaseCommandGroup
        {
            [Command("fahrenheit")]
            [CommandType(ApplicationCommandType.ChatInput)]
            [Description("Convert a value in celsius to fahrenheit")]
            public async Task<IResult> CelsiusToFahrenheitAsync(float c)
            {
                float f = (c * (9 / 5f)) + 32;
                return await Feedback.SendContextualInfoAsync($"{c:0.##}c is {f:0.##}f");
            }
        }
    }
}
