using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Discord.Commands.Services;
using Remora.Results;

namespace SerenaBot.CommandErrorHandlers;

public class CommandPreparationErrorHandler : IPreparationErrorEvent
{
    private readonly IDiscordRestInteractionAPI InteractionAPI;

    private readonly ContextInjectionService ContextInjection;
    private readonly FeedbackService Feedback;

    public CommandPreparationErrorHandler(IDiscordRestInteractionAPI interactionAPI, ContextInjectionService contextInjection, FeedbackService feedback)
    {
        InteractionAPI = interactionAPI;
        ContextInjection = contextInjection;
        Feedback = feedback;
    }

    public async Task<Result> PreparationFailed(IOperationContext context, IResult preparationResult, CancellationToken ct = default)
    {
        if (preparationResult.IsSuccess)
        {
            return Result.FromSuccess();
        }

        if (context is IInteractionContext interactCtx)
        {
            InteractionResponse resp = new
            (
                InteractionCallbackType.ChannelMessageWithSource,
                new(new InteractionMessageCallbackData
                (
                    Content: preparationResult.Error.ToString() ?? "Command preparation failure",
                    Flags: MessageFlags.Ephemeral
                ))
            );

            return await InteractionAPI.CreateInteractionResponseAsync(interactCtx.Interaction.ID, interactCtx.Interaction.Token, resp, ct: ct);
        }

        return (Result)await Feedback.SendContextualErrorAsync(preparationResult.Error.ToString() ?? "Command preparation failure", ct: ct);
    }
}
