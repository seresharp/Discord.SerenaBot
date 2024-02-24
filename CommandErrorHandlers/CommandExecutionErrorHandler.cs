using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Discord.Commands.Services;
using Remora.Discord.Gateway.Results;
using Remora.Results;
using System.Text;

namespace SerenaBot.CommandErrorHandlers;

public class CommandExecutionErrorHandler : IPostExecutionEvent
{
    private readonly FeedbackService Feedback;

    public CommandExecutionErrorHandler(FeedbackService feedback)
    {
        Feedback = feedback;
    }

    public async Task<Result> AfterExecutionAsync(ICommandContext context, IResult commandResult, CancellationToken ct = default)
    {
        if (commandResult.IsSuccess)
        {
            return Result.FromSuccess();
        }

        string? errorOverride = null;
        if (commandResult.Error is ExceptionError ex)
        {
            StringBuilder exStr = new("```");
            foreach (string line in ex.Exception.ToString().Split(Environment.NewLine))
            {
                if (!line.TrimStart().StartsWith("at "))
                {
                    exStr.AppendLine(line);
                    continue;
                }

                string? baseNamespace = line.TrimStart()[3..].Split('.', '(').FirstOrDefault();
                if (baseNamespace != "System")
                {
                    exStr.AppendLine(line);
                }
            }

            errorOverride = exStr.Append("```").ToString();
        }

        return (Result)await Feedback.SendContextualAsync(errorOverride ?? commandResult.Error.ToString() ?? "Command execution failure", ct: ct);
    }

    private string GetMessage(IResult commandResult, int tabbing = 0)
    {
        string tab = new(' ', tabbing * 4);
        string message = commandResult.Error switch
        {
            AggregateError err => $"{tab}{nameof(AggregateError)}:{err.Message}{err.Errors.Select(e => "\n" + GetMessage(e, tabbing + 1)).Aggregate(string.Concat)}",
            ExceptionError err => $"{tab}{err.Exception}",
            GatewayWebSocketError or GatewayDiscordError or GatewayError => $"{tab}Gateway error: {commandResult.Error.Message}",
            not null => $"{tab}{commandResult.Error.GetType().Name}:{commandResult.Error.Message}",
            _ => string.Empty
        };

        if (commandResult.Inner is IResult inner)
        {
            message += "\nInner: " + GetMessage(inner, tabbing + 1);
        }

        return message;
    }
}
