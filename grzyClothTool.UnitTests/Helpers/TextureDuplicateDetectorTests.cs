using System.Collections.ObjectModel;
using grzyClothTool.Helpers;
using grzyClothTool.Models.Drawable;
using grzyClothTool.Models.Texture;
using static grzyClothTool.Enums;

namespace grzyClothTool.UnitTests.Helpers;

public class TextureDuplicateDetectorTests
{
    [Fact]
    public async Task FindDuplicatesAsync_GroupsTexturesWithIdenticalFiles()
    {
        using var temp = new TestTempDirectory();

        var a = WriteFile(temp, "a.ytd", [1, 2, 3, 4, 5, 6, 7, 8]);
        var b = WriteFile(temp, "b.ytd", [1, 2, 3, 4, 5, 6, 7, 8]);

        var groups = await TextureDuplicateDetector.FindDuplicatesAsync(
        [
            Candidate(a, "jbib_000_u"),
            Candidate(b, "jbib_001_u")
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Equal(8, group.FileSizeBytes);
        Assert.Equal(8, group.SharableBytes);
    }

    [Fact]
    public async Task FindDuplicatesAsync_IgnoresFilesWithDifferentContent()
    {
        using var temp = new TestTempDirectory();

        var a = WriteFile(temp, "a.ytd", [1, 2, 3, 4]);
        var b = WriteFile(temp, "b.ytd", [4, 3, 2, 1]);

        var groups = await TextureDuplicateDetector.FindDuplicatesAsync([Candidate(a, "a"), Candidate(b, "b")]);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicatesAsync_IgnoresFilesWithDifferentSize()
    {
        using var temp = new TestTempDirectory();

        var a = WriteFile(temp, "a.ytd", [1, 2, 3, 4]);
        var b = WriteFile(temp, "b.ytd", [1, 2, 3, 4, 5]);

        var groups = await TextureDuplicateDetector.FindDuplicatesAsync([Candidate(a, "a"), Candidate(b, "b")]);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicatesAsync_GroupsAcrossDrawables()
    {
        using var temp = new TestTempDirectory();

        var bytes = new byte[] { 9, 9, 9, 9 };
        var first = CreateDrawable(0);
        var second = CreateDrawable(1);

        var a = AddTexture(first, WriteFile(temp, "a.ytd", bytes), 0);
        var b = AddTexture(second, WriteFile(temp, "b.ytd", bytes), 0);

        var groups = await TextureDuplicateDetector.FindDuplicatesAsync(
        [
            new TextureDuplicateCandidate { Texture = a, Drawable = first, DrawableName = first.Name },
            new TextureDuplicateCandidate { Texture = b, Drawable = second, DrawableName = second.Name }
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.False(group.HasSameDrawableDuplicates);
        Assert.Empty(TextureDuplicateDetector.PlanVariationRemovals(groups));
    }

    [Fact]
    public async Task ShareFiles_PointsEveryDuplicateAtOneFileAndDeletesTheOthers()
    {
        using var temp = new TestTempDirectory();

        var bytes = new byte[] { 7, 7, 7, 7, 7, 7 };
        var a = WriteFile(temp, "a.ytd", bytes);
        var b = WriteFile(temp, "b.ytd", bytes);
        var c = WriteFile(temp, "c.ytd", bytes);

        var textures = new[] { Texture(a, 0), Texture(b, 1), Texture(c, 2) };
        var groups = await TextureDuplicateDetector.FindDuplicatesAsync(
            textures.Select(t => new TextureDuplicateCandidate { Texture = t, DrawableName = "jbib_000_u" }));

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Count);
        Assert.Equal(2 * bytes.Length, group.SharableBytes);

        var result = TextureDuplicateDetector.ShareFiles(groups, textures, temp.Path);

        Assert.Equal(1, result.GroupCount);
        Assert.Equal(2, result.RepointedTextures);
        Assert.Equal(2, result.DeletedFiles);
        Assert.Equal(2 * bytes.Length, result.FreedBytes);

        // Every texture now resolves to the same single file, and only that file is left.
        Assert.Single(textures.Select(t => t.FullFilePath).Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.True(File.Exists(textures[0].FullFilePath));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task ShareFiles_KeepsFilesThatAreStillReferencedElsewhere()
    {
        using var temp = new TestTempDirectory();

        var bytes = new byte[] { 3, 3, 3, 3 };
        var a = WriteFile(temp, "a.ytd", bytes);
        var b = WriteFile(temp, "b.ytd", bytes);

        var shared = new[] { Texture(a, 0), Texture(b, 1) };
        // A texture outside the scanned scope still points at b, so b must survive.
        var outsider = Texture(b, 0);

        var groups = await TextureDuplicateDetector.FindDuplicatesAsync(
            shared.Select(t => new TextureDuplicateCandidate { Texture = t, DrawableName = "jbib_000_u" }));

        var result = TextureDuplicateDetector.ShareFiles(groups, [.. shared, outsider], temp.Path);

        Assert.Equal(0, result.DeletedFiles);
        Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task ShareFiles_NeverDeletesFilesOutsideTheProjectAssetsFolder()
    {
        using var temp = new TestTempDirectory();
        var assets = Directory.CreateDirectory(Path.Combine(temp.Path, "assets")).FullName;

        var bytes = new byte[] { 5, 5, 5, 5 };
        var inside = WriteFile(temp, Path.Combine("assets", "a.ytd"), bytes);
        var outside = WriteFile(temp, "user-owned.ytd", bytes);

        var textures = new[] { Texture(inside, 0), Texture(outside, 1) };
        var groups = await TextureDuplicateDetector.FindDuplicatesAsync(
            textures.Select(t => new TextureDuplicateCandidate { Texture = t, DrawableName = "jbib_000_u" }));

        TextureDuplicateDetector.ShareFiles(groups, textures, assets);

        Assert.True(File.Exists(outside));
    }

    [Fact]
    public async Task PlanVariationRemovals_OnlyListsDuplicatesInsideTheSameDrawable()
    {
        using var temp = new TestTempDirectory();

        var bytes = new byte[] { 1, 1, 1, 1 };
        var drawable = CreateDrawable(0);
        var other = CreateDrawable(1);

        var first = AddTexture(drawable, WriteFile(temp, "a.ytd", bytes), 0);
        var second = AddTexture(drawable, WriteFile(temp, "b.ytd", bytes), 1);
        var third = AddTexture(drawable, WriteFile(temp, "c.ytd", bytes), 2);
        var elsewhere = AddTexture(other, WriteFile(temp, "d.ytd", bytes), 0);

        var groups = await TextureDuplicateDetector.FindDuplicatesAsync(
        [
            new TextureDuplicateCandidate { Texture = first, Drawable = drawable, DrawableName = drawable.Name },
            new TextureDuplicateCandidate { Texture = second, Drawable = drawable, DrawableName = drawable.Name },
            new TextureDuplicateCandidate { Texture = third, Drawable = drawable, DrawableName = drawable.Name },
            new TextureDuplicateCandidate { Texture = elsewhere, Drawable = other, DrawableName = other.Name }
        ]);

        var group = Assert.Single(groups);
        Assert.True(group.HasSameDrawableDuplicates);

        var removals = TextureDuplicateDetector.PlanVariationRemovals(groups);

        // The lowest variation of the drawable stays, the other two go, the other drawable is untouched.
        Assert.Equal(2, removals.Count);
        Assert.All(removals, r => Assert.Same(drawable, r.Drawable));
        Assert.All(removals, r => Assert.Same(first, r.KeptTexture));
        Assert.DoesNotContain(elsewhere, removals.Select(r => r.Texture));

        var removed = TextureDuplicateDetector.RemoveVariations(removals);

        Assert.Equal(2, removed);
        Assert.Single(drawable.Textures);
        Assert.Same(first, drawable.Textures[0]);
        Assert.Equal(0, drawable.Textures[0].TxtNumber);
        Assert.Single(other.Textures);
    }

    [Fact]
    public async Task ComputeImageHashMode_GroupsTheSameImageStoredInDifferentFormats()
    {
        using var temp = new TestTempDirectory();

        // The very same pixels, once as .png and once as an uncompressed .dds.
        // The files differ, the decoded image does not.
        var png = Path.Combine(temp.Path, "a.png");
        var dds = Path.Combine(temp.Path, "b.dds");

        using (var img = new ImageMagick.MagickImage(new ImageMagick.MagickColor("#336699"), 64, 64))
        {
            img.Write(png);
            img.Format = ImageMagick.MagickFormat.Dds;
            img.Settings.SetDefine(ImageMagick.MagickFormat.Dds, "compression", "none");
            img.Write(dds);
        }

        var fileMode = await TextureDuplicateDetector.FindDuplicatesAsync(
            [Candidate(png, "a"), Candidate(dds, "b")],
            TextureDuplicateMatchMode.FileContent);
        Assert.Empty(fileMode);

        var imageMode = await TextureDuplicateDetector.FindDuplicatesAsync(
            [Candidate(png, "a"), Candidate(dds, "b")],
            TextureDuplicateMatchMode.ImageContent);

        var group = Assert.Single(imageMode);
        Assert.Equal(2, group.Count);
    }

    [Fact]
    public async Task FindDuplicatesAsync_ReportsProgressForEveryTexture()
    {
        using var temp = new TestTempDirectory();

        var a = WriteFile(temp, "a.ytd", [1, 2, 3, 4]);
        var b = WriteFile(temp, "b.ytd", [1, 2, 3, 4]);

        // Progress callbacks arrive on the thread pool, so the sink has to be thread safe.
        var reports = new System.Collections.Concurrent.ConcurrentQueue<TextureDuplicateProgress>();
        var progress = new Progress<TextureDuplicateProgress>(reports.Enqueue);

        await TextureDuplicateDetector.FindDuplicatesAsync(
            [Candidate(a, "a"), Candidate(b, "b")],
            TextureDuplicateMatchMode.FileContent,
            progress);

        for (var i = 0; i < 40 && reports.Count < 2; i++)
        {
            await Task.Delay(25);
        }

        var all = reports.ToArray();
        Assert.Equal(2, all.Length);
        Assert.All(all, r => Assert.Equal(2, r.Total));
        Assert.Equal(100, all.Max(r => r.Percentage));
    }

    private static string WriteFile(TestTempDirectory temp, string name, byte[] bytes)
    {
        var path = temp.FilePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static GTexture Texture(string path, int txtNumber)
        => new(Guid.NewGuid(), path, 11, 0, txtNumber, hasSkin: false, isProp: false);

    private static TextureDuplicateCandidate Candidate(string path, string drawableName)
        => new() { Texture = Texture(path, 0), DrawableName = drawableName };

    private static GDrawable CreateDrawable(int number)
        => new(
            Guid.NewGuid(),
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ydd"),
            SexType.male,
            isProp: false,
            typeNumeric: 11,
            number: number,
            hasSkin: false,
            []);

    private static GTexture AddTexture(GDrawable drawable, string path, int txtNumber)
    {
        var texture = Texture(path, txtNumber);
        drawable.Textures.Add(texture);
        return texture;
    }
}
