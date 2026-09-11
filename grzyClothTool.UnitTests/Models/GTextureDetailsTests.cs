using grzyClothTool.Helpers;
using grzyClothTool.Models.Texture;

namespace grzyClothTool.UnitTests.Models;

public class GTextureDetailsTests
{
    [Fact]
    public void Validate_DoesNotWarnForTextureWithinLimitsWithExpectedMipMaps()
    {
        var details = new GTextureDetails
        {
            Width = 1024,
            Height = 1024,
            MipMapCount = ImgHelper.GetCorrectMipMapAmount(1024, 1024)
        };

        details.Validate();

        Assert.False(details.IsOptimizeNeeded);
        Assert.Equal(string.Empty, details.IsOptimizeNeededTooltip);
    }

    [Fact]
    public void Validate_WarnsForNonPowerOfTwoTexture()
    {
        var details = new GTextureDetails
        {
            Width = 300,
            Height = 512,
            MipMapCount = ImgHelper.GetCorrectMipMapAmount(256, 512),
            Type = "diffuse"
        };

        details.Validate();

        Assert.True(details.IsOptimizeNeeded);
        Assert.Contains("not power of 2", details.IsOptimizeNeededTooltip);
    }

    [Fact]
    public void Validate_WarnsWhenOnlyOneMipMapExistsButMoreAreExpected()
    {
        var details = new GTextureDetails
        {
            Width = 1024,
            Height = 1024,
            MipMapCount = 1,
            Type = "diffuse"
        };

        details.Validate();

        Assert.True(details.IsOptimizeNeeded);
        Assert.Contains("mip maps", details.IsOptimizeNeededTooltip);
    }

    [Theory]
    [InlineData("diffuse", 4096, 2048)]
    [InlineData("normal", 4096, 2048)]
    [InlineData("specular", 4096, 2048)]
    [InlineData(null, 4096, 2048)]
    public void Validate_WarnsWhenTextureExceedsResolutionLimit(string? type, int width, int height)
    {
        var details = new GTextureDetails
        {
            Width = width,
            Height = height,
            MipMapCount = ImgHelper.GetCorrectMipMapAmount(width, height),
            Type = type
        };

        details.Validate();

        Assert.True(details.IsOptimizeNeeded);
        Assert.Contains("Texture resolution", details.IsOptimizeNeededTooltip);
    }

    [Fact]
    public void Validate_WarnsWhenMipMapCountIsBelowTheExpectedAmount()
    {
        var details = new GTextureDetails
        {
            Width = 1024,
            Height = 1024,
            MipMapCount = 2,
            Type = "diffuse"
        };

        details.Validate();

        Assert.True(details.IsOptimizeNeeded);
        Assert.Contains("mip maps", details.IsOptimizeNeededTooltip);
    }

    [Fact]
    public void Validate_DoesNotWarnWhenMipMapCountIsAboveTheExpectedAmount()
    {
        var details = new GTextureDetails
        {
            Width = 1024,
            Height = 1024,
            MipMapCount = 11,
            Type = "diffuse",
            Compression = "D3DFMT_DXT5"
        };

        details.Validate();

        Assert.False(details.IsOptimizeNeeded);
        Assert.Equal(string.Empty, details.IsOptimizeNeededTooltip);
    }

    [Fact]
    public void Validate_WarnsWhenTheTextureIsOverTheMemoryBudget()
    {
        var details = new GTextureDetails
        {
            Width = 2048,
            Height = 2048,
            MipMapCount = 12,
            Type = "diffuse",
            Compression = "D3DFMT_A8R8G8B8"
        };

        details.Validate();

        Assert.True(details.IsOptimizeNeeded);
        Assert.Contains("of memory", details.IsOptimizeNeededTooltip);
    }
}
