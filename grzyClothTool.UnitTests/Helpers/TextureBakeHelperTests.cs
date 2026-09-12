using grzyClothTool.Helpers;
using grzyClothTool.Models.Texture;
using ImageMagick;

namespace grzyClothTool.UnitTests.Helpers;

/// <summary>
/// Covers "write now": the optimized texture has to land on disk immediately, the texture has to
/// point at it, and the details shown afterwards have to be the real values of the written file.
/// </summary>
public class TextureBakeHelperTests
{
    [Fact]
    public async Task BakeAsync_WritesTheOptimizedFileAndClearsTheBuildFlag()
    {
        using var temp = new TestTempDirectory();

        var source = WritePng(temp, "source.png", 512, 512);
        var texture = await LoadTextureAsync(source, txtNumber: 0);

        Assert.Equal(512, texture.TxtDetails.Width);

        var entry = PlanEntry(texture, width: 128, height: 128, mips: 6, compression: "D3DFMT_DXT5");

        var result = await TextureBakeHelper.BakeAsync([entry], temp.Path);

        Assert.Equal(1, result.Count);
        Assert.Empty(result.Failures);

        var record = result.Records[0];
        Assert.True(File.Exists(record.NewFullPath));
        Assert.Equal(".ytd", texture.Extension);
        Assert.True(File.Exists(texture.FullFilePath));

        // The flag has to be cleared: the file on disk is already the optimized one, so the build
        // must copy it as is instead of re-encoding it a second time.
        Assert.False(texture.IsOptimizedDuringBuild);

        // The details are re-read from the file, so they are the real values, not the plan.
        Assert.Equal(128, texture.TxtDetails.Width);
        Assert.Equal(128, texture.TxtDetails.Height);
        Assert.Contains("DXT5", texture.TxtDetails.Compression, StringComparison.OrdinalIgnoreCase);

        // ImageMagick counts the "mipmaps" define as levels on top of the base one, so the written
        // file has at least as many levels as the plan asked for.
        Assert.True(texture.TxtDetails.MipMapCount >= 6, $"only {texture.TxtDetails.MipMapCount} mip levels");

        // OptimizeDetails is realigned with the file, not left on the planned values.
        Assert.Equal(128, texture.OptimizeDetails.Width);
        Assert.Equal(texture.TxtDetails.MipMapCount, texture.OptimizeDetails.MipMapCount);
        Assert.Equal(texture.TxtDetails.Compression, texture.OptimizeDetails.Compression);

        Assert.True(result.MemoryBytesSaved > 0, $"expected a memory saving, got {result.MemoryBytesSaved}");
    }

    [Fact]
    public async Task BakeAsync_GeneratesTheFullMipChain()
    {
        using var temp = new TestTempDirectory();

        var source = WritePng(temp, "source.png", 256, 256);
        var texture = await LoadTextureAsync(source, txtNumber: 0);

        // A .png carries a single level, the bake has to produce the chain.
        var entry = PlanEntry(texture, 256, 256, mips: 7, compression: "D3DFMT_DXT5");

        await TextureBakeHelper.BakeAsync([entry], temp.Path);

        Assert.True(texture.TxtDetails.MipMapCount >= 7, $"only {texture.TxtDetails.MipMapCount} mip levels");
        Assert.True(texture.TxtDetails.MipMapCount <= 9);
    }

    [Fact]
    public async Task BakeAsync_KeepsTheInternalTextureNameOfAYtdSource()
    {
        using var temp = new TestTempDirectory();

        // Build a .ytd whose internal texture name belongs to variation "a"...
        var png = WritePng(temp, "source.png", 128, 128);
        var original = await LoadTextureAsync(png, txtNumber: 0);
        var ytdBytes = ImgHelper.GetDDSBytes(original);
        var ytdPath = temp.FilePath("source.ytd");
        await File.WriteAllBytesAsync(ytdPath, ytdBytes);

        // ... and load it into a texture whose own display name is variation "b".
        var texture = await LoadTextureAsync(ytdPath, txtNumber: 1);
        var internalName = texture.TxtDetails.Name;

        Assert.Equal(original.DisplayName, internalName);
        Assert.NotEqual(texture.DisplayName, internalName);

        var entry = PlanEntry(texture, 64, 64, mips: 5, compression: "D3DFMT_DXT5");
        await TextureBakeHelper.BakeAsync([entry], temp.Path);

        // The drawable shader references the texture by this name, so it must not change.
        Assert.Equal(internalName, texture.TxtDetails.Name);
        Assert.Equal(64, texture.TxtDetails.Width);
    }

    [Fact]
    public async Task BakeAsync_KeepsTheOriginalFileByDefaultAndCanBeUndone()
    {
        using var temp = new TestTempDirectory();

        var source = WritePng(temp, "source.png", 256, 256);
        var texture = await LoadTextureAsync(source, txtNumber: 0);
        var originalPath = texture.FullFilePath;

        var entry = PlanEntry(texture, 64, 64, mips: 5, compression: "D3DFMT_DXT5");
        var result = await TextureBakeHelper.BakeAsync([entry], temp.Path);

        Assert.True(File.Exists(originalPath));
        Assert.Equal(0, result.DeletedOriginals);

        var reverted = await TextureBakeHelper.RevertAsync(result.Records);

        Assert.Equal(1, reverted);
        Assert.Equal(originalPath, texture.FullFilePath);
        Assert.Equal(256, texture.TxtDetails.Width);
        Assert.False(File.Exists(result.Records[0].NewFullPath));
    }

    [Fact]
    public async Task BakeAsync_DeletesTheOriginalWhenAskedAndNothingElseUsesIt()
    {
        using var temp = new TestTempDirectory();

        var source = WritePng(temp, "source.png", 256, 256);
        var texture = await LoadTextureAsync(source, txtNumber: 0);
        var originalPath = texture.FullFilePath;

        var entry = PlanEntry(texture, 64, 64, mips: 5, compression: "D3DFMT_DXT5");
        var options = new TextureBakeOptions { OriginalHandling = TextureBakeOriginalHandling.Delete };

        var result = await TextureBakeHelper.BakeAsync([entry], temp.Path, options, [texture]);

        Assert.Equal(1, result.DeletedOriginals);
        Assert.False(File.Exists(originalPath));
    }

    [Fact]
    public async Task BakeAsync_KeepsAnOriginalThatIsStillSharedWithAnotherTexture()
    {
        using var temp = new TestTempDirectory();

        var source = WritePng(temp, "source.png", 256, 256);
        var texture = await LoadTextureAsync(source, txtNumber: 0);
        // A second texture that shares the very same file, as the duplicate merge produces.
        var sharing = await LoadTextureAsync(source, txtNumber: 1);
        var originalPath = texture.FullFilePath;

        var entry = PlanEntry(texture, 64, 64, mips: 5, compression: "D3DFMT_DXT5");
        var options = new TextureBakeOptions { OriginalHandling = TextureBakeOriginalHandling.Delete };

        var result = await TextureBakeHelper.BakeAsync([entry], temp.Path, options, [texture, sharing]);

        Assert.Equal(0, result.DeletedOriginals);
        Assert.True(File.Exists(originalPath));
        Assert.Equal(originalPath, sharing.FullFilePath);
    }

    private static string WritePng(TestTempDirectory temp, string name, uint width, uint height)
    {
        var path = temp.FilePath(name);
        using var img = new MagickImage(new MagickColor("#4477aa"), width, height);
        img.Write(path);
        return path;
    }

    private static async Task<GTexture> LoadTextureAsync(string path, int txtNumber)
    {
        var texture = new GTexture(Guid.NewGuid(), path, 11, 0, txtNumber, hasSkin: false, isProp: false);
        await texture.LoadDetails();
        Assert.NotNull(texture.TxtDetails);
        return texture;
    }

    private static BulkOptimizePlanEntry PlanEntry(GTexture texture, int width, int height, int mips, string compression)
    {
        var before = texture.TxtDetails;
        var after = new GTextureDetails
        {
            Width = width,
            Height = height,
            MipMapCount = mips,
            Compression = compression,
            Name = before.Name,
            Type = before.Type
        };

        return new BulkOptimizePlanEntry
        {
            Texture = texture,
            DrawableName = "jbib_000_u",
            TextureName = texture.DisplayName,
            Before = before,
            After = after,
            BeforeBytes = TextureSizeHelper.GetTextureSizeInBytes(before.Width, before.Height, before.MipMapCount, before.Compression),
            AfterBytes = TextureSizeHelper.GetTextureSizeInBytes(width, height, mips, compression)
        };
    }
}
