using grzyClothTool.Models.Texture;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace grzyClothTool.Helpers;

#nullable enable

/// <summary>
/// How a non power of two texture should be snapped to a power of two.
/// </summary>
public enum PowerOfTwoMode
{
    /// <summary>Snap down to the previous power of two (smaller, saves memory).</summary>
    Down,
    /// <summary>Snap up to the next power of two (larger, keeps every pixel).</summary>
    Up
}

/// <summary>
/// Rules used by the bulk texture optimizer.
/// </summary>
public class BulkOptimizeOptions
{
    /// <summary>Fill in the missing mip levels of textures that do not have a full chain.</summary>
    public bool GenerateMipMaps { get; set; } = true;

    /// <summary>
    /// Re-encode uncompressed textures (A8R8G8B8, X8R8G8B8, A8B8G8R8, ...) as DXT5.
    /// Uncompressed diffuse maps are by far the biggest memory offenders: a single uncompressed
    /// 2048x2048 already needs 21.3 MB with its mip chain, while the same texture as DXT5 needs 5.3 MB.
    /// DXT5 is used unconditionally because it keeps the alpha channel, so the image never
    /// has to be read to decide.
    /// </summary>
    public bool CompressUncompressed { get; set; } = true;

    /// <summary>Format uncompressed textures are converted to by <see cref="CompressUncompressed"/>.</summary>
    public const string UncompressedReplacementFormat = "D3DFMT_DXT5";

    /// <summary>Snap non power of two textures to a power of two.</summary>
    public bool FixPowerOfTwo { get; set; } = true;

    public PowerOfTwoMode PowerOfTwoMode { get; set; } = PowerOfTwoMode.Down;

    /// <summary>Clamp the texture resolution to the per-type limits.</summary>
    public bool EnforceResolutionLimit { get; set; } = true;

    public int ResolutionLimitDiffuse { get; set; } = DefaultResolutionLimitDiffuse;
    public int ResolutionLimitNormal { get; set; } = DefaultResolutionLimitNormal;
    public int ResolutionLimitSpecular { get; set; } = DefaultResolutionLimitSpecular;

    /// <summary>
    /// Defaults used by the bulk optimizer dialog. They are deliberately different from the
    /// global validation limits in the settings screen, which stay untouched.
    /// </summary>
    public const int DefaultResolutionLimitDiffuse = 2048;
    public const int DefaultResolutionLimitNormal = 512;
    public const int DefaultResolutionLimitSpecular = 256;

    /// <summary>Halve the texture until its estimated memory (mips included) fits the budget.</summary>
    public bool EnforceMemoryLimit { get; set; } = true;

    /// <summary>Memory budget per texture, in megabytes.</summary>
    public int MaxTextureMemoryMB { get; set; } = 16;

    /// <summary>
    /// Pick DXT1 for textures without a usable alpha channel and DXT5 for the ones that have one.
    /// This has to read the actual image, which is slow, so it is off by default.
    /// </summary>
    public bool OptimizeCompression { get; set; }

    /// <summary>Smallest size the memory budget rule is allowed to produce.</summary>
    public const int MinimumSize = 4;

    public static BulkOptimizeOptions FromSettings()
    {
        return new BulkOptimizeOptions
        {
            MaxTextureMemoryMB = SettingsHelper.Instance.MaxTextureMemoryMB
        };
    }

    public int GetResolutionLimit(string? type)
    {
        if (type != null)
        {
            if (type.Contains("normal", StringComparison.OrdinalIgnoreCase))
            {
                return ResolutionLimitNormal;
            }

            if (type.Contains("specular", StringComparison.OrdinalIgnoreCase))
            {
                return ResolutionLimitSpecular;
            }
        }

        return ResolutionLimitDiffuse;
    }
}

/// <summary>
/// One texture that the bulk optimizer wants to change, with its before/after state.
/// </summary>
public class BulkOptimizePlanEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public GTexture Texture { get; init; } = null!;
    public string DrawableName { get; init; } = string.Empty;
    public string TextureName { get; init; } = string.Empty;

    public GTextureDetails Before { get; init; } = null!;
    public GTextureDetails After { get; init; } = null!;

    public long BeforeBytes { get; init; }
    public long AfterBytes { get; init; }
    public long SavedBytes => BeforeBytes - AfterBytes;

    public double BeforeMB => BeforeBytes / (double)TextureSizeHelper.BytesPerMegabyte;
    public double AfterMB => AfterBytes / (double)TextureSizeHelper.BytesPerMegabyte;
    public double SavedMB => SavedBytes / (double)TextureSizeHelper.BytesPerMegabyte;

    /// <summary>True when the texture is over the configured per-texture budget before optimizing.</summary>
    public bool IsOverBudget { get; init; }

    /// <summary>
    /// Size the texture would have if only the compression format changed, with the original
    /// resolution and mip count. Used to split the saving into "compression" and "downscale".
    /// </summary>
    public long CompressionOnlyBytes { get; init; }

    /// <summary>Bytes saved by the format change alone.</summary>
    public long CompressionSavedBytes => BeforeBytes - CompressionOnlyBytes;

    /// <summary>Bytes saved by the resolution change (a mip chain that gets added counts negative here).</summary>
    public long ResizeSavedBytes => CompressionOnlyBytes - AfterBytes;

    public bool IsCompressionChanged => !string.Equals(Before.Compression, After.Compression, StringComparison.OrdinalIgnoreCase);
    public bool IsResized => Before.Width != After.Width || Before.Height != After.Height;

    public string BeforeText => Describe(Before, BeforeBytes);
    public string AfterText => Describe(After, AfterBytes);
    public string FormatText => IsCompressionChanged
        ? $"{ShortFormat(Before.Compression)} → {ShortFormat(After.Compression)}"
        : ShortFormat(Before.Compression);
    public string SavedText => TextureSizeHelper.FormatMegabytes(SavedMB);

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private static string Describe(GTextureDetails d, long bytes)
        => $"{d.Width}x{d.Height} / mip {d.MipMapCount} / {TextureSizeHelper.FormatBytesAsMegabytes(bytes)}";

    private static string ShortFormat(string? compression)
    {
        if (string.IsNullOrEmpty(compression))
        {
            return "?";
        }

        return compression.StartsWith("D3DFMT_", StringComparison.OrdinalIgnoreCase)
            ? compression["D3DFMT_".Length..]
            : compression;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Applies a set of rules to every texture of a project and produces a plan,
/// i.e. the list of textures that would change and what they would become.
/// Nothing is written to disk here: the plan is applied by setting
/// <see cref="GTexture.OptimizeDetails"/> and <see cref="GTexture.IsOptimizedDuringBuild"/>,
/// so the new texture is generated during the next resource build.
/// </summary>
public static class BulkOptimizeHelper
{
    /// <summary>
    /// Computes the target state for a texture. Returns the details unchanged when no rule applies.
    /// </summary>
    /// <param name="hasAlpha">
    /// Whether the image actually uses its alpha channel; null when unknown, in which case the
    /// compression rule is skipped.
    /// </param>
    public static GTextureDetails ComputeTarget(GTextureDetails current, BulkOptimizeOptions options, bool? hasAlpha = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(options);

        var width = current.Width;
        var height = current.Height;
        var compression = current.Compression;

        // 1. Compression first: it divides the memory by four, which often makes a downscale
        //    unnecessary (an uncompressed 4096x2048 needs 42.7 MB, as DXT5 only 10.7 MB).
        if (options.CompressUncompressed && !TextureSizeHelper.IsBlockCompressed(compression))
        {
            compression = BulkOptimizeOptions.UncompressedReplacementFormat;
        }

        if (options.OptimizeCompression && hasAlpha.HasValue && TextureSizeHelper.IsBlockCompressed(compression))
        {
            compression = hasAlpha.Value ? "D3DFMT_DXT5" : "D3DFMT_DXT1";
        }

        // 2. Power of two.
        if (options.FixPowerOfTwo)
        {
            width = SnapToPowerOfTwo(width, options.PowerOfTwoMode);
            height = SnapToPowerOfTwo(height, options.PowerOfTwoMode);
        }

        // 3. Per-type resolution limit.
        if (options.EnforceResolutionLimit)
        {
            var limit = options.GetResolutionLimit(current.Type);
            if (limit > 0)
            {
                while ((width > limit || height > limit) && width > BulkOptimizeOptions.MinimumSize && height > BulkOptimizeOptions.MinimumSize)
                {
                    width = Math.Max(BulkOptimizeOptions.MinimumSize, width / 2);
                    height = Math.Max(BulkOptimizeOptions.MinimumSize, height / 2);
                }
            }
        }

        // 4. Memory budget, evaluated with the mip levels that the result would actually have.
        if (options.EnforceMemoryLimit && options.MaxTextureMemoryMB > 0)
        {
            var budget = options.MaxTextureMemoryMB * TextureSizeHelper.BytesPerMegabyte;

            while (width > BulkOptimizeOptions.MinimumSize && height > BulkOptimizeOptions.MinimumSize)
            {
                var mips = ResolveMipMapCount(width, height, current.MipMapCount, options.GenerateMipMaps);
                if (TextureSizeHelper.GetTextureSizeInBytes(width, height, mips, compression) <= budget)
                {
                    break;
                }

                width = Math.Max(BulkOptimizeOptions.MinimumSize, width / 2);
                height = Math.Max(BulkOptimizeOptions.MinimumSize, height / 2);
            }
        }

        // 5. Mip count for the final size.
        var mipMapCount = ResolveMipMapCount(width, height, current.MipMapCount, options.GenerateMipMaps);

        return new GTextureDetails
        {
            Width = width,
            Height = height,
            MipMapCount = mipMapCount,
            Compression = compression,
            Name = current.Name,
            Type = current.Type
        };
    }

    public static bool IsChanged(GTextureDetails current, GTextureDetails target)
        => current.Width != target.Width
           || current.Height != target.Height
           || current.MipMapCount != target.MipMapCount
           || !string.Equals(current.Compression, target.Compression, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Highest number of mip levels that a texture of this size can have.
    /// </summary>
    public static int GetMaxMipMapCount(int width, int height)
    {
        var size = Math.Max(1, Math.Max(width, height));
        return (int)Math.Floor(Math.Log2(size)) + 1;
    }

    /// <summary>
    /// Mip level count the tool considers correct for this size, clamped to a valid range.
    /// </summary>
    public static int GetExpectedMipMapCount(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return 1;
        }

        var expected = ImgHelper.GetCorrectMipMapAmount(width, height);
        return Math.Clamp(expected, 1, GetMaxMipMapCount(width, height));
    }

    public static int ResolveMipMapCount(int width, int height, int currentMipMapCount, bool generateMipMaps)
    {
        var max = GetMaxMipMapCount(width, height);
        var current = Math.Clamp(currentMipMapCount <= 0 ? 1 : currentMipMapCount, 1, max);

        if (!generateMipMaps)
        {
            return current;
        }

        return Math.Max(current, GetExpectedMipMapCount(width, height));
    }

    public static int SnapToPowerOfTwo(int value, PowerOfTwoMode mode)
    {
        if (value <= 1)
        {
            return 1;
        }

        if ((value & (value - 1)) == 0)
        {
            return value;
        }

        var exponent = Math.Log2(value);
        var snapped = mode == PowerOfTwoMode.Down
            ? (int)Math.Pow(2, Math.Floor(exponent))
            : (int)Math.Pow(2, Math.Ceiling(exponent));

        return Math.Max(1, snapped);
    }

    /// <summary>
    /// Builds the plan for the given textures. Textures that would not change are left out,
    /// so applying the plan never flags a texture for a pointless re-encode.
    /// </summary>
    public static List<BulkOptimizePlanEntry> CreatePlan(
        IEnumerable<(string DrawableName, GTexture Texture)> textures,
        BulkOptimizeOptions options,
        IProgress<BulkOptimizeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(options);

        var items = textures.ToList();
        var result = new List<BulkOptimizePlanEntry>();
        var budget = options.MaxTextureMemoryMB * TextureSizeHelper.BytesPerMegabyte;

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (drawableName, texture) = items[i];
            progress?.Report(new BulkOptimizeProgress(i + 1, items.Count, texture?.DisplayName ?? string.Empty));

            var current = texture?.TxtDetails;
            if (texture == null || current == null || current.Width <= 0 || current.Height <= 0)
            {
                continue;
            }

            bool? hasAlpha = null;
            if (options.OptimizeCompression)
            {
                hasAlpha = DetectAlphaUsage(texture);
            }

            var target = ComputeTarget(current, options, hasAlpha);
            if (!IsChanged(current, target))
            {
                continue;
            }

            var beforeBytes = TextureSizeHelper.GetTextureSizeInBytes(current.Width, current.Height, current.MipMapCount, current.Compression);
            var afterBytes = TextureSizeHelper.GetTextureSizeInBytes(target.Width, target.Height, target.MipMapCount, target.Compression);
            var compressionOnlyBytes = TextureSizeHelper.GetTextureSizeInBytes(current.Width, current.Height, current.MipMapCount, target.Compression);

            result.Add(new BulkOptimizePlanEntry
            {
                CompressionOnlyBytes = compressionOnlyBytes,
                Texture = texture,
                DrawableName = drawableName,
                TextureName = texture.DisplayName,
                Before = current,
                After = target,
                BeforeBytes = beforeBytes,
                AfterBytes = afterBytes,
                IsOverBudget = options.MaxTextureMemoryMB > 0 && beforeBytes > budget
            });
        }

        return [.. result.OrderByDescending(e => e.BeforeBytes)];
    }

    /// <summary>
    /// Flags the textures of the given entries so that the next build writes the optimized version.
    /// The source files are never touched.
    /// </summary>
    public static int Apply(IEnumerable<BulkOptimizePlanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var count = 0;
        foreach (var entry in entries)
        {
            entry.Texture.OptimizeDetails = new GTextureDetails
            {
                Width = entry.After.Width,
                Height = entry.After.Height,
                MipMapCount = entry.After.MipMapCount,
                Compression = entry.After.Compression,
                Name = entry.After.Name,
                Type = entry.After.Type,
                IsOptimizeNeeded = false
            };
            entry.Texture.IsOptimizedDuringBuild = true;
            entry.Texture.TxtDetails?.Validate();
            count++;
        }

        return count;
    }

    /// <summary>
    /// Clears the "optimize during build" flag, so the textures are written unchanged again.
    /// </summary>
    public static int Revert(IEnumerable<GTexture> textures)
    {
        ArgumentNullException.ThrowIfNull(textures);

        var count = 0;
        foreach (var texture in textures)
        {
            if (!texture.IsOptimizedDuringBuild)
            {
                continue;
            }

            texture.IsOptimizedDuringBuild = false;

            var details = texture.TxtDetails;
            if (details != null)
            {
                texture.OptimizeDetails = new GTextureDetails
                {
                    Width = details.Width,
                    Height = details.Height,
                    MipMapCount = details.MipMapCount,
                    Compression = details.Compression,
                    Name = details.Name,
                    Type = details.Type
                };
                details.Validate();
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Reads the image to find out whether its alpha channel carries any information.
    /// Returns null when the image cannot be read.
    /// </summary>
    public static bool? DetectAlphaUsage(GTexture texture)
    {
        try
        {
            using var img = ImgHelper.GetImage(texture.FullFilePath);
            if (img == null)
            {
                return null;
            }

            return img.HasAlpha && !img.IsOpaque;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>Progress report of a running analysis.</summary>
public readonly record struct BulkOptimizeProgress(int Current, int Total, string TextureName)
{
    public double Percentage => Total <= 0 ? 0 : Current * 100d / Total;
}
