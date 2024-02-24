using Microsoft.Extensions.Configuration;

namespace SerenaBot.Extensions;

public static class IConfigurationExtensions
{
    public static TValue GetRequiredValue<TValue>(this IConfiguration config, string key)
        => config.GetValue<TValue>(key) ?? throw new KeyNotFoundException($"{key} environment variable not found in configuration");
}
