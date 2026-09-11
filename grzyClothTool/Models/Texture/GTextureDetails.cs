using System;
using grzyClothTool.Helpers;
using grzyClothTool.Localization;

namespace grzyClothTool.Models.Texture;
#nullable enable

public class GTextureDetails
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int MipMapCount { get; set; }
    public string Compression { get; set; } = string.Empty;
    public string? Name { get; set; } = string.Empty;
    public string? Type { get; set; } = string.Empty;

    public bool IsOptimizeNeeded { get; set; }
    public string IsOptimizeNeededTooltip { get; set; } = string.Empty;

    public void Validate()
    {
        IsOptimizeNeeded = false;
        IsOptimizeNeededTooltip = string.Empty;
        
        int resolutionLimit = 2048;
        
        if (Type != null)
        {
            if (Type.Contains("diffuse", StringComparison.OrdinalIgnoreCase))
            {
                resolutionLimit = SettingsHelper.Instance.TextureResolutionLimitDiffuse;
            }
            else if (Type.Contains("normal", StringComparison.OrdinalIgnoreCase))
            {
                resolutionLimit = SettingsHelper.Instance.TextureResolutionLimitNormal;
            }
            else if (Type.Contains("specular", StringComparison.OrdinalIgnoreCase))
            {
                resolutionLimit = SettingsHelper.Instance.TextureResolutionLimitSpecular;
            }
        }
        
        if (Width > resolutionLimit || Height > resolutionLimit)
        {
            IsOptimizeNeeded = true;
            IsOptimizeNeededTooltip += Loc.T("Texture_ResolutionExceedsLimit", Width, Height, resolutionLimit);
        }

        if ((Height & Height - 1) != 0 || (Width & Width - 1) != 0)
        {
            IsOptimizeNeeded = true;
            IsOptimizeNeededTooltip += Loc.T("Texture_NotPowerOfTwo");
        }

        var expectedMipMapCount = BulkOptimizeHelper.GetExpectedMipMapCount(Width, Height);
        if (MipMapCount < expectedMipMapCount)
        {
            IsOptimizeNeeded = true;
            IsOptimizeNeededTooltip += Loc.T("Texture_MipMapMismatch", MipMapCount, expectedMipMapCount);
        }

        var memoryLimitMB = SettingsHelper.Instance.MaxTextureMemoryMB;
        if (memoryLimitMB > 0)
        {
            var sizeInMB = TextureSizeHelper.GetTextureSizeInMegabytes(Width, Height, MipMapCount, Compression);
            if (sizeInMB > memoryLimitMB)
            {
                IsOptimizeNeeded = true;
                IsOptimizeNeededTooltip += Loc.T("Texture_MemoryExceedsLimit", TextureSizeHelper.FormatMegabytes(sizeInMB), memoryLimitMB);
            }
        }
    }

    /// <summary>
    /// Estimated video memory used by this texture once loaded, mip levels included.
    /// </summary>
    public long EstimatedMemoryBytes => TextureSizeHelper.GetTextureSizeInBytes(Width, Height, MipMapCount, Compression);
}
