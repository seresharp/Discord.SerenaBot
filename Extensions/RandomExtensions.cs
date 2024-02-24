namespace SerenaBot.Extensions;

public static class RandomExtensions
{
    private static readonly Random Random = new();

    public static bool Percent(this Random random, int percent)
        => percent switch
        {
            <= 0 => false,
            >= 100 => true,
            _ => random.Next(100) <= percent - 1
        };

    public static bool Percent(this Random random, double percent)
        => percent switch
        {
            <= 0 => false,
            >= 100 => true,
            _ => random.NextDouble() * 100 <= percent
        };

    public static TArray GetRandomItem<TArray>(this IList<TArray> array)
        => array switch
        {
            // No way to tell the compiler this is non-null when Count > 0, using ! here instead
            { Count: <= 0 } => default!,
            _ => array[Random.Next(array.Count)]
        };
}
