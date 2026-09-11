using grzyClothTool.Helpers;
using grzyClothTool.Models.Texture;

namespace grzyClothTool.UnitTests.Helpers;

public class BulkOptimizeHelperTests
{
    private static BulkOptimizeOptions Options(
        bool mips = true,
        bool compressUncompressed = true,
        bool powerOfTwo = true,
        PowerOfTwoMode mode = PowerOfTwoMode.Down,
        bool resolutionLimit = false,
        int limit = 1024,
        bool memoryLimit = true,
        int maxMemoryMB = 16)
    {
        return new BulkOptimizeOptions
        {
            GenerateMipMaps = mips,
            CompressUncompressed = compressUncompressed,
            FixPowerOfTwo = powerOfTwo,
            PowerOfTwoMode = mode,
            EnforceResolutionLimit = resolutionLimit,
            ResolutionLimitDiffuse = limit,
            ResolutionLimitNormal = limit,
            ResolutionLimitSpecular = limit,
            EnforceMemoryLimit = memoryLimit,
            MaxTextureMemoryMB = maxMemoryMB,
            OptimizeCompression = false
        };
    }

    private static GTextureDetails Details(int width, int height, int mips, string compression, string type = "diffuse")
        => new() { Width = width, Height = height, MipMapCount = mips, Compression = compression, Type = type };

    [Fact]
    public void ComputeTarget_ShrinksOversizedTextureUntilItFitsTheMemoryBudget()
    {
        // 4096x4096 BC3 needs ~21.3 MiB once the mip chain is added, so it has to be halved.
        var current = Details(4096, 4096, 1, "D3DFMT_DXT5");

        var target = BulkOptimizeHelper.ComputeTarget(current, Options());

        Assert.Equal(2048, target.Width);
        Assert.Equal(2048, target.Height);
        Assert.Equal(10, target.MipMapCount);
        Assert.Equal("D3DFMT_DXT5", target.Compression);

        var size = TextureSizeHelper.GetTextureSizeInBytes(target.Width, target.Height, target.MipMapCount, target.Compression);
        Assert.True(size <= 16 * TextureSizeHelper.BytesPerMegabyte, $"{size} bytes is still above the budget");
    }

    [Fact]
    public void ComputeTarget_OnlyAddsMipMapsWhenTheTextureAlreadyFits()
    {
        // 2048x2048 BC1 with mips is ~2.7 MiB, so the size must stay untouched.
        var current = Details(2048, 2048, 1, "D3DFMT_DXT1");

        var target = BulkOptimizeHelper.ComputeTarget(current, Options());

        Assert.Equal(2048, target.Width);
        Assert.Equal(2048, target.Height);
        Assert.Equal(10, target.MipMapCount);
        Assert.Equal("D3DFMT_DXT1", target.Compression);
    }

    [Fact]
    public void ComputeTarget_SnapsNonPowerOfTwoDownByDefault()
    {
        var current = Details(1000, 504, 1, "D3DFMT_DXT5");

        var target = BulkOptimizeHelper.ComputeTarget(current, Options());

        Assert.Equal(512, target.Width);
        Assert.Equal(256, target.Height);
    }

    [Fact]
    public void ComputeTarget_CanSnapNonPowerOfTwoUp()
    {
        var current = Details(1000, 504, 1, "D3DFMT_DXT5");

        var target = BulkOptimizeHelper.ComputeTarget(current, Options(mode: PowerOfTwoMode.Up));

        Assert.Equal(1024, target.Width);
        Assert.Equal(512, target.Height);
    }

    [Fact]
    public void ComputeTarget_AppliesThePerTypeResolutionLimit()
    {
        var options = Options(resolutionLimit: true, limit: 512);
        options.ResolutionLimitNormal = 128;

        var diffuse = BulkOptimizeHelper.ComputeTarget(Details(2048, 2048, 1, "D3DFMT_DXT5", "diffuse"), options);
        var normal = BulkOptimizeHelper.ComputeTarget(Details(2048, 2048, 1, "D3DFMT_DXT5", "normal"), options);

        Assert.Equal(512, diffuse.Width);
        Assert.Equal(128, normal.Width);
    }

    [Fact]
    public void ComputeTarget_LeavesCompliantTextureUnchanged()
    {
        var current = Details(1024, 1024, 11, "D3DFMT_DXT5");

        var target = BulkOptimizeHelper.ComputeTarget(current, Options(resolutionLimit: true, limit: 1024));

        Assert.False(BulkOptimizeHelper.IsChanged(current, target));
    }

    [Fact]
    public void ComputeTarget_DoesNotAddMipMapsWhenTheOptionIsOff()
    {
        var current = Details(2048, 2048, 1, "D3DFMT_DXT1");

        var target = BulkOptimizeHelper.ComputeTarget(current, Options(mips: false));

        Assert.Equal(1, target.MipMapCount);
        Assert.False(BulkOptimizeHelper.IsChanged(current, target));
    }


    [Fact]
    public void ComputeTarget_CompressesUncompressedTexturesToDxt5WithoutDownscaling()
    {
        // 4096x2048 uncompressed needs 42.7 MB, as DXT5 only 10.7 MB, so it fits the
        // budget through the format change alone and keeps its resolution.
        var current = Details(4096, 2048, 1, "D3DFMT_A8R8G8B8");

        var target = BulkOptimizeHelper.ComputeTarget(current, Options(resolutionLimit: false));

        Assert.Equal("D3DFMT_DXT5", target.Compression);
        Assert.Equal(4096, target.Width);
        Assert.Equal(2048, target.Height);
        Assert.Equal(10, target.MipMapCount);

        var size = TextureSizeHelper.GetTextureSizeInBytes(target.Width, target.Height, target.MipMapCount, target.Compression);
        Assert.True(size <= 16 * TextureSizeHelper.BytesPerMegabyte);
    }

    [Theory]
    [InlineData("D3DFMT_A8R8G8B8")]
    [InlineData("D3DFMT_X8R8G8B8")]
    [InlineData("D3DFMT_A8B8G8R8")]
    [InlineData("D3DFMT_L8")]
    [InlineData("UNKNOWN")]
    public void ComputeTarget_CompressesEveryNonBlockCompressedFormat(string compression)
    {
        var target = BulkOptimizeHelper.ComputeTarget(Details(1024, 1024, 11, compression), Options());

        Assert.Equal("D3DFMT_DXT5", target.Compression);
    }

    [Fact]
    public void ComputeTarget_LeavesUncompressedTextureAloneWhenTheRuleIsOff()
    {
        var current = Details(512, 512, 10, "D3DFMT_A8R8G8B8");

        var target = BulkOptimizeHelper.ComputeTarget(current, Options(compressUncompressed: false));

        Assert.Equal("D3DFMT_A8R8G8B8", target.Compression);
        Assert.False(BulkOptimizeHelper.IsChanged(current, target));
    }

    [Fact]
    public void CreatePlan_SplitsTheSavingIntoCompressionAndDownscale()
    {
        var texture = CreateTexture(Details(4096, 2048, 1, "D3DFMT_A8R8G8B8"));

        var plan = BulkOptimizeHelper.CreatePlan([("a", texture)], Options(resolutionLimit: false));

        var entry = Assert.Single(plan);
        Assert.True(entry.IsCompressionChanged);
        Assert.False(entry.IsResized);
        Assert.Equal("A8R8G8B8 → DXT5", entry.FormatText);
        Assert.True(entry.CompressionSavedBytes > 0);
        // the added mip chain costs a little, so the downscale part is slightly negative here
        Assert.True(entry.ResizeSavedBytes < 0);
        Assert.Equal(entry.SavedBytes, entry.CompressionSavedBytes + entry.ResizeSavedBytes);
    }

    [Fact]
    public void ComputeTarget_SwitchesCompressionWhenAlphaUsageIsKnown()
    {
        var options = Options();
        options.OptimizeCompression = true;

        var opaque = BulkOptimizeHelper.ComputeTarget(Details(512, 512, 10, "D3DFMT_DXT5"), options, hasAlpha: false);
        var transparent = BulkOptimizeHelper.ComputeTarget(Details(512, 512, 8, "D3DFMT_DXT1"), options, hasAlpha: true);
        var unknown = BulkOptimizeHelper.ComputeTarget(Details(512, 512, 10, "D3DFMT_DXT5"), options, hasAlpha: null);

        Assert.Equal("D3DFMT_DXT1", opaque.Compression);
        Assert.Equal("D3DFMT_DXT5", transparent.Compression);
        Assert.Equal("D3DFMT_DXT5", unknown.Compression);
    }

    [Fact]
    public void ComputeTarget_NeverGoesBelowTheMinimumSize()
    {
        var current = Details(4096, 4096, 1, "D3DFMT_A8R8G8B8");

        var target = BulkOptimizeHelper.ComputeTarget(current, Options(maxMemoryMB: 1));

        Assert.True(target.Width >= 4);
        Assert.True(target.Height >= 4);
    }

    [Theory]
    [InlineData(1000, PowerOfTwoMode.Down, 512)]
    [InlineData(1000, PowerOfTwoMode.Up, 1024)]
    [InlineData(1024, PowerOfTwoMode.Down, 1024)]
    [InlineData(1024, PowerOfTwoMode.Up, 1024)]
    [InlineData(3, PowerOfTwoMode.Down, 2)]
    [InlineData(1, PowerOfTwoMode.Down, 1)]
    public void SnapToPowerOfTwo_RoundsInTheRequestedDirection(int value, PowerOfTwoMode mode, int expected)
    {
        Assert.Equal(expected, BulkOptimizeHelper.SnapToPowerOfTwo(value, mode));
    }

    [Fact]
    public void ResolveMipMapCount_NeverExceedsWhatTheSizeAllows()
    {
        Assert.Equal(3, BulkOptimizeHelper.ResolveMipMapCount(4, 4, 99, generateMipMaps: true));
        Assert.Equal(3, BulkOptimizeHelper.ResolveMipMapCount(4, 4, 99, generateMipMaps: false));
        Assert.Equal(10, BulkOptimizeHelper.ResolveMipMapCount(2048, 2048, 1, generateMipMaps: true));
        Assert.Equal(1, BulkOptimizeHelper.ResolveMipMapCount(2048, 2048, 1, generateMipMaps: false));
    }

    [Fact]
    public void CreatePlan_SkipsTexturesThatDoNotNeedAnyChange()
    {
        var needsWork = CreateTexture(Details(4096, 4096, 11, "D3DFMT_DXT5"));
        var alreadyFine = CreateTexture(Details(1024, 1024, 11, "D3DFMT_DXT5"));

        var plan = BulkOptimizeHelper.CreatePlan(
            [("jbib_000", needsWork), ("jbib_001", alreadyFine)],
            Options(resolutionLimit: true, limit: 4096));

        var entry = Assert.Single(plan);
        Assert.Same(needsWork, entry.Texture);
        Assert.Equal("jbib_000", entry.DrawableName);
        Assert.Equal(2048, entry.After.Width);
        Assert.True(entry.IsOverBudget);
        Assert.True(entry.SavedBytes > 0);
        Assert.True(entry.IsSelected);
    }

    [Fact]
    public void CreatePlan_OrdersBySizeDescendingAndReportsProgress()
    {
        var small = CreateTexture(Details(1000, 1000, 1, "D3DFMT_DXT1"));
        var big = CreateTexture(Details(4096, 4096, 1, "D3DFMT_DXT5"));

        var reports = new List<BulkOptimizeProgress>();
        var progress = new SynchronousProgress<BulkOptimizeProgress>(reports.Add);

        var plan = BulkOptimizeHelper.CreatePlan([("a", small), ("b", big)], Options(), progress);

        Assert.Equal(2, plan.Count);
        Assert.Same(big, plan[0].Texture);
        Assert.Equal(2, reports.Count);
        Assert.Equal(2, reports[^1].Total);
        Assert.Equal(100, reports[^1].Percentage);
    }

    [Fact]
    public void CreatePlan_IgnoresTexturesWithoutDetails()
    {
        var texture = CreateTexture(null);

        var plan = BulkOptimizeHelper.CreatePlan([("a", texture)], Options());

        Assert.Empty(plan);
    }

    [Fact]
    public void ApplyAndRevert_OnlyToggleTheBuildTimeFlag()
    {
        var texture = CreateTexture(Details(4096, 4096, 1, "D3DFMT_DXT5"));
        var plan = BulkOptimizeHelper.CreatePlan([("a", texture)], Options());

        Assert.Equal(1, BulkOptimizeHelper.Apply(plan));
        Assert.True(texture.IsOptimizedDuringBuild);
        Assert.Equal(2048, texture.OptimizeDetails.Width);
        // the source details are untouched, only the build time output changes
        Assert.Equal(4096, texture.TxtDetails.Width);

        Assert.Equal(1, BulkOptimizeHelper.Revert([texture]));
        Assert.False(texture.IsOptimizedDuringBuild);
        Assert.Equal(4096, texture.OptimizeDetails.Width);
        Assert.Equal(1, texture.OptimizeDetails.MipMapCount);

        // reverting twice is a no-op
        Assert.Equal(0, BulkOptimizeHelper.Revert([texture]));
    }

    private static GTexture CreateTexture(GTextureDetails? details)
    {
        var texture = new GTexture(
            Guid.NewGuid(),
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ytd"),
            typeNumeric: 11,
            number: 0,
            txtNumber: 0,
            hasSkin: false,
            isProp: false)
        {
            TxtDetails = details!
        };

        return texture;
    }

    /// <summary>
    /// <see cref="Progress{T}"/> posts to the synchronization context, which makes assertions racy
    /// in a test, so report synchronously instead.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
