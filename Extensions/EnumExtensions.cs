using Remora.Commands.Extensions;
using System.ComponentModel.DataAnnotations;

namespace SerenaBot.Extensions
{
    public static class EnumExtensions
    {
        private static readonly Dictionary<Enum, string> CachedNames = new();

        public static string GetDisplayName(this Enum value)
        {
            if (CachedNames.TryGetValue(value, out string? cachedName))
            {
                return cachedName;
            }

            Type t = value.GetType();
            string name = t.GetEnumName(value) ?? throw new ArgumentException($"'{value}' is not a named member of enum '{t.Name}'");
            return CachedNames[value] = t.GetMember(name).First().GetCustomAttribute<DisplayAttribute>()?.Name is not string displayName
                ? value.ToString()
                : displayName;
        }
    }
}
