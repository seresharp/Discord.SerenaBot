using System.Drawing;
using System.Runtime.Versioning;

namespace SerenaBot.Util
{
    [SupportedOSPlatform("windows")]
    public static class DiscordBrushes
    {
        public static readonly SolidBrush Background = new(Color.FromArgb(54, 57, 63));
        public static readonly SolidBrush Text = new(Color.FromArgb(220, 221, 222));
    }
}
