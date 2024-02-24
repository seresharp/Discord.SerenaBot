using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SerenaBot.Util;

[SupportedOSPlatform("windows")]
public class ResourceFont : IDisposable
{
    private readonly PrivateFontCollection FontCollection = new();
    private readonly nint FontHandle;

    private readonly Dictionary<int, Font> Fonts = new();

    public FontFamily Family => FontCollection.Families[0];

    public static readonly ResourceFont GGSansNormal = new("ggsans-Normal");
    public static readonly ResourceFont GGSansSemibold = new("ggsans-Semibold");

    public ResourceFont(string resName)
    {
        using Stream stream = typeof(Program).Assembly.GetManifestResourceStream($"{nameof(SerenaBot)}.Resources.{resName}.ttf")
            ?? throw new FileNotFoundException(resName);

        int len = (int)stream.Length;
        byte[] fontData = new byte[len];
        stream.Read(fontData, 0, len);

        FontHandle = Marshal.AllocCoTaskMem(len);
        Marshal.Copy(fontData, 0, FontHandle, len);

        FontCollection.AddMemoryFont(FontHandle, len);
    }

    public Font GetFont(int emSize)
        => Fonts.TryGetValue(emSize, out Font? font) ? font : Fonts[emSize] = new(Family, emSize);

    public void Dispose()
    {
        foreach ((_, Font f) in Fonts)
        {
            f.Dispose();
        }

        FontCollection.Dispose();
        Marshal.FreeCoTaskMem(FontHandle);

        GC.SuppressFinalize(this);
    }
}
