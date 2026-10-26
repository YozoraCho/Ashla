using StbImageSharp;

namespace Ashla.Rendering;

public sealed record RenderImage(int Width, int Height, byte[] Rgba)
{
    public static RenderImage Decode(ReadOnlySpan<byte> encoded)
    {
        var image = ImageResult.FromMemory(encoded.ToArray(), ColorComponents.RedGreenBlueAlpha);
        return new RenderImage(image.Width, image.Height, image.Data);
    }
}

public static class IconFile
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static IReadOnlyList<RenderImage> Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<RenderImage> Parse(byte[] data)
    {
        if (data.Length < 6 || BitConverter.ToUInt16(data, 0) != 0 || BitConverter.ToUInt16(data, 2) != 1) return [];
        int count = BitConverter.ToUInt16(data, 4);
        var images = new List<RenderImage>();
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            if (entry + 16 > data.Length) break;
            int size = BitConverter.ToInt32(data, entry + 8);
            int offset = BitConverter.ToInt32(data, entry + 12);
            if (offset < 0 || size <= 0 || offset + size > data.Length) continue;
            var bytes = data.AsSpan(offset, size);
            if (!bytes.StartsWith(PngSignature)) continue;
            try
            {
                images.Add(RenderImage.Decode(bytes));
            }
            catch (Exception)
            {
            }
        }
        return [.. images.OrderBy(image => image.Width)];
    }
}
