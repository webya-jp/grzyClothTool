using System;

namespace grzyClothTool.Helpers;

#nullable enable

/// <summary>
/// Estimates how much video memory a texture occupies once it is loaded by the game.
/// This mirrors what FiveM reports as "physical memory" for a .ytd asset, which is the
/// sum of every mip level of the texture surface (not the compressed file size on disk).
/// </summary>
public static class TextureSizeHelper
{
    public const long BytesPerMegabyte = 1024L * 1024L;

    /// <summary>
    /// Number of bytes a single 4x4 block occupies, or 0 when the format is not block compressed.
    /// </summary>
    public static int GetBlockBytes(string? compression) => Normalize(compression) switch
    {
        // 0.5 byte per pixel
        "D3DFMT_DXT1" or "D3DFMT_ATI1" or "BC1" or "BC4" => 8,
        // 1 byte per pixel
        "D3DFMT_DXT2" or "D3DFMT_DXT3" or "D3DFMT_DXT4" or "D3DFMT_DXT5"
            or "D3DFMT_ATI2" or "D3DFMT_BC7"
            or "BC2" or "BC3" or "BC5" or "BC7" => 16,
        _ => 0,
    };

    public static bool IsBlockCompressed(string? compression) => GetBlockBytes(compression) > 0;

    /// <summary>
    /// Bits per pixel for the uncompressed formats CodeWalker can produce.
    /// </summary>
    public static int GetUncompressedBitsPerPixel(string? compression) => Normalize(compression) switch
    {
        "D3DFMT_A8R8G8B8" or "D3DFMT_X8R8G8B8" or "D3DFMT_A8B8G8R8" => 32,
        "D3DFMT_A1R5G5B5" => 16,
        "D3DFMT_L8" or "D3DFMT_A8" => 8,
        // Anything we do not recognise is re-encoded as DXT5 during build, so assume 8 bpp.
        _ => 8,
    };

    /// <summary>
    /// Size in bytes of one mip level (level 0 = <paramref name="width"/> x <paramref name="height"/>).
    /// Block compressed formats are rounded up to whole 4x4 blocks.
    /// </summary>
    public static long GetLevelSizeInBytes(int width, int height, string? compression)
    {
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        var blockBytes = GetBlockBytes(compression);
        if (blockBytes > 0)
        {
            long blocksWide = (width + 3) / 4;
            long blocksHigh = (height + 3) / 4;
            return blocksWide * blocksHigh * blockBytes;
        }

        long bits = (long)width * height * GetUncompressedBitsPerPixel(compression);
        return (bits + 7) / 8;
    }

    /// <summary>
    /// Total size in bytes of the texture including every mip level.
    /// <paramref name="mipMapCount"/> is the number of levels; values below 1 are treated as 1.
    /// </summary>
    public static long GetTextureSizeInBytes(int width, int height, int mipMapCount, string? compression)
    {
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        var levels = Math.Max(1, mipMapCount);
        long total = 0;
        var w = width;
        var h = height;

        for (var i = 0; i < levels; i++)
        {
            total += GetLevelSizeInBytes(w, h, compression);

            if (w == 1 && h == 1)
            {
                // No further mip levels exist, stop even if a larger count was requested.
                break;
            }

            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        return total;
    }

    public static double GetTextureSizeInMegabytes(int width, int height, int mipMapCount, string? compression)
        => GetTextureSizeInBytes(width, height, mipMapCount, compression) / (double)BytesPerMegabyte;

    public static string FormatMegabytes(double megabytes) => $"{megabytes:0.##} MB";

    public static string FormatBytesAsMegabytes(long bytes) => FormatMegabytes(bytes / (double)BytesPerMegabyte);

    private static string Normalize(string? compression)
        => string.IsNullOrWhiteSpace(compression) ? string.Empty : compression.Trim().ToUpperInvariant();
}
