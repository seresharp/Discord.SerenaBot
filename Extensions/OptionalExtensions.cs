using Remora.Rest.Core;

namespace SerenaBot.Extensions
{
    public static class OptionalExtensions
    {
        public static T? GetValue<T>(this Optional<T> optional)
            => optional.IsDefined() ? optional.Value : default;

        public static Optional<IReadOnlyList<T>> AsOptionalList<T>(this T obj)
            => new(new[] { obj });
    }
}
