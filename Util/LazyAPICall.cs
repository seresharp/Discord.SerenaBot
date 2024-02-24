using Remora.Discord.Extensions.Errors;
using Remora.Results;
using System.Diagnostics.CodeAnalysis;

namespace SerenaBot.Util
{
    public class LazyAPICall<TEntity> : Lazy<Task<Result<TEntity>>> where TEntity : class
    {
        private Task<Result<TEntity>> Task => Value;

        [MemberNotNullWhen(true, nameof(Entity))]
        [MemberNotNullWhen(false, nameof(Error))] // this is only true if the task has been awaited, but there's no attribute for that condition
        public bool IsSuccess => Task.IsCompletedSuccessfully && Task.Result.IsSuccess;

        public TEntity? Entity => !Task.IsCompletedSuccessfully ? null : Task.Result.Entity;

        public Result<TEntity>? Error => Task switch
        {
            { IsFaulted: true, Exception.InnerExceptions: [Exception e] } => Result<TEntity>.FromError(new ExceptionError(e)),
            { IsFaulted: true, Exception.InnerExceptions: { Count: > 1 } exceptions }
                => Result<TEntity>.FromError(new AggregateError(exceptions.Select(e => (IResult)Result.FromError(new ExceptionError(e))).ToArray())),
            { IsFaulted: true } => Result<TEntity>.FromError(new ValidationError(nameof(Task), $"{nameof(Task.IsFaulted)}: true")),
            { IsCanceled: true } => Result<TEntity>.FromError(new ExceptionError(new TaskCanceledException(Task))),
            { IsCompletedSuccessfully: true, Result.IsSuccess: false } => Task.Result,
            _ => (Result<TEntity>?)null
        };

        public LazyAPICall(Func<Task<Result<TEntity>>> apiCallFactory) : base(apiCallFactory) { }

        public ValueTask DoAPICallAsync()
        {
            if (Task.IsCompleted) return ValueTask.CompletedTask;
            return new(Task);
        }
    }
}
