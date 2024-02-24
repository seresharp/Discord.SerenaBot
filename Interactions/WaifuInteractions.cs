using Remora.Discord.Interactivity;
using Remora.Discord.Interactivity.Services;
using Remora.Rest.Core;
using Remora.Results;
using SerenaBot.Commands.Util;

using static SerenaBot.Commands.WaifuCommands;

namespace SerenaBot.Interactions
{
    public class WaifuInteractions : BaseCommandGroup
    {
        private readonly InMemoryDataService<Snowflake, WaifuBuilder> WaifuBuilders = null!;

        [Button("waifubuilder-button-first")]
        public async Task<IResult> FirstAsync()
            => await GoToPageAsync(_ => 0);

        [Button("waifubuilder-button-previous")]
        public async Task<IResult> PreviousAsync()
            => await GoToPageAsync(b => b.Page - 1);

        [Button("waifubuilder-button-next")]
        public async Task<IResult> NextAsync()
            => await GoToPageAsync(b => b.Page + 1);

        [Button("waifubuilder-button-last")]
        public async Task<IResult> LastAsync()
            => await GoToPageAsync(b => b.Girls.Count - 1);

        [Button("waifubuilder-button-color")]
        public async Task<IResult> TuneColorAsync()
            => await ApplyStepAsync(1);

        [Button("waifubuilder-button-details")]
        public async Task<IResult> TuneDetailsAsync()
            => await ApplyStepAsync(2);

        [Button("waifubuilder-button-pose")]
        public async Task<IResult> TunePoseAsync()
            => await ApplyStepAsync(3);

        [Button("waifubuilder-button-finalize")]
        public async Task<IResult> FinalizeWaifuAsync()
        {
            if (Context is not { MessageID: Snowflake messageID })
            {
                return Result.FromError(new InvalidOperationError("Interaction has no associated channel ID"));
            }

            var getLease = await WaifuBuilders.LeaseDataAsync(messageID, CancellationToken);
            if (!getLease.IsSuccess) return getLease;

            await using DataLease<Snowflake, WaifuBuilder> lease = getLease.Entity;
            if (Context.User?.ID != lease.Data.UserID)
            {
                return Result.FromSuccess();
            }

            return await lease.Data.FinalizeAsync();
        }

        private async Task<IResult> GoToPageAsync(Func<WaifuBuilder, int> pagePredicate)
        {
            if (Context is not { MessageID: Snowflake messageID })
            {
                return Result.FromError(new InvalidOperationError("Interaction has no associated channel ID"));
            }

            var getLease = await WaifuBuilders.LeaseDataAsync(messageID, CancellationToken);
            if (!getLease.IsSuccess) return getLease;

            await using DataLease<Snowflake, WaifuBuilder> lease = getLease.Entity;
            int page = pagePredicate(lease.Data);
            if (Context.User?.ID != lease.Data.UserID || page < 0 || page > lease.Data.Girls.Count - 1)
            {
                return Result.FromSuccess();
            }

            return await lease.Data.GoToPageAsync(pagePredicate(lease.Data));
        }

        private async Task<IResult> ApplyStepAsync(int step)
        {
            if (Context is not { MessageID: Snowflake messageID })
            {
                return Result.FromError(new InvalidOperationError("Interaction has no associated channel ID"));
            }

            var getLease = await WaifuBuilders.LeaseDataAsync(messageID, CancellationToken);
            if (!getLease.IsSuccess) return getLease;

            await using DataLease<Snowflake, WaifuBuilder> lease = getLease.Entity;
            if (Context.User?.ID != lease.Data.UserID)
            {
                return Result.FromSuccess();
            }

            return await lease.Data.ApplyStepAsync(step);
        }
    }
}
