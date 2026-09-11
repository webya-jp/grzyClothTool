using grzyClothTool.Helpers;

namespace grzyClothTool.UnitTests.Helpers;

public class TextureSizeHelperTests
{
    [Theory]
    // 2048x2048 BC1 without mips: (2048/4)^2 blocks * 8 bytes
    [InlineData(2048, 2048, 1, "D3DFMT_DXT1", 2_097_152L)]
    // same size as BC3: 16 bytes per block
    [InlineData(2048, 2048, 1, "D3DFMT_DXT5", 4_194_304L)]
    [InlineData(2048, 2048, 1, "D3DFMT_DXT3", 4_194_304L)]
    // BC4 behaves like BC1, BC5/BC7 like BC3
    [InlineData(1024, 1024, 1, "D3DFMT_ATI1", 524_288L)]
    [InlineData(1024, 1024, 1, "D3DFMT_ATI2", 1_048_576L)]
    [InlineData(1024, 1024, 1, "D3DFMT_BC7", 1_048_576L)]
    // uncompressed formats
    [InlineData(256, 256, 1, "D3DFMT_A8R8G8B8", 262_144L)]
    [InlineData(256, 256, 1, "D3DFMT_A8B8G8R8", 262_144L)]
    [InlineData(256, 256, 1, "D3DFMT_A1R5G5B5", 131_072L)]
    [InlineData(256, 256, 1, "D3DFMT_L8", 65_536L)]
    [InlineData(256, 256, 1, "D3DFMT_A8", 65_536L)]
    public void GetTextureSizeInBytes_MatchesHandCalculatedValues(int width, int height, int mips, string compression, long expected)
    {
        Assert.Equal(expected, TextureSizeHelper.GetTextureSizeInBytes(width, height, mips, compression));
    }

    [Fact]
    public void GetTextureSizeInBytes_SumsEveryMipLevel()
    {
        // 4096x4096 BC3 with 11 mip levels:
        // 16777216 + 4194304 + 1048576 + 262144 + 65536 + 16384 + 4096 + 1024 + 256 + 64 + 16
        Assert.Equal(22_369_616L, TextureSizeHelper.GetTextureSizeInBytes(4096, 4096, 11, "D3DFMT_DXT5"));
        Assert.Equal(21.33, Math.Round(TextureSizeHelper.GetTextureSizeInMegabytes(4096, 4096, 11, "D3DFMT_DXT5"), 2));
    }

    [Fact]
    public void GetTextureSizeInBytes_MipsAddAboutOneThird()
    {
        var withoutMips = TextureSizeHelper.GetTextureSizeInBytes(1024, 1024, 1, "D3DFMT_DXT5");
        var withMips = TextureSizeHelper.GetTextureSizeInBytes(1024, 1024, 11, "D3DFMT_DXT5");

        Assert.Equal(1_048_576L, withoutMips);
        Assert.InRange(withMips / (double)withoutMips, 1.32, 1.34);
    }

    [Fact]
    public void GetTextureSizeInBytes_RoundsBlockCompressedSizesUpToWholeBlocks()
    {
        // 100 pixels -> 25 blocks, no rounding needed
        Assert.Equal(5_000L, TextureSizeHelper.GetTextureSizeInBytes(100, 100, 1, "D3DFMT_DXT1"));
        // 99 pixels -> 25 blocks as well, because a partial block still costs a full one
        Assert.Equal(5_000L, TextureSizeHelper.GetTextureSizeInBytes(99, 99, 1, "D3DFMT_DXT1"));
        // a 1x1 BC1 texture still uses a whole 8 byte block
        Assert.Equal(8L, TextureSizeHelper.GetTextureSizeInBytes(1, 1, 1, "D3DFMT_DXT1"));
    }

    [Fact]
    public void GetTextureSizeInBytes_StopsAtTheSmallestMipLevel()
    {
        // 4x4 -> 2x2 -> 1x1, the requested 10 levels cannot exist
        Assert.Equal(24L, TextureSizeHelper.GetTextureSizeInBytes(4, 4, 10, "D3DFMT_DXT1"));
    }

    [Fact]
    public void GetTextureSizeInBytes_TreatsUnknownFormatAsOneBytePerPixel()
    {
        // jpg/png sources are re-encoded as DXT5 during build, so 8 bpp is the safe assumption
        Assert.Equal(1_048_576L, TextureSizeHelper.GetTextureSizeInBytes(1024, 1024, 1, "UNKNOWN"));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("D3DFMT_A8R8G8B8", 0)]
    [InlineData("D3DFMT_DXT1", 8)]
    [InlineData("d3dfmt_dxt5", 16)]
    public void GetBlockBytes_RecognisesBlockCompressedFormats(string? compression, int expected)
    {
        Assert.Equal(expected, TextureSizeHelper.GetBlockBytes(compression));
        Assert.Equal(expected > 0, TextureSizeHelper.IsBlockCompressed(compression));
    }

    [Fact]
    public void GetTextureSizeInBytes_ReturnsZeroForInvalidSize()
    {
        Assert.Equal(0L, TextureSizeHelper.GetTextureSizeInBytes(0, 512, 1, "D3DFMT_DXT1"));
        Assert.Equal(0L, TextureSizeHelper.GetTextureSizeInBytes(512, -1, 1, "D3DFMT_DXT1"));
    }
}
