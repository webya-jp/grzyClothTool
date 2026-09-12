using grzyClothTool.Models.Drawable;
using grzyClothTool.Models.Texture;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace grzyClothTool.Helpers;

#nullable enable

/// <summary>
/// How far the duplicate search goes when comparing two textures.
/// </summary>
public enum TextureDuplicateMatchMode
{
    /// <summary>Only textures whose files are byte for byte identical are grouped.</summary>
    FileContent,

    /// <summary>
    /// Textures whose decoded image is identical are grouped as well, even when the files differ
    /// (for example the same image stored once as .png and once as .ytd). This has to decode every
    /// texture, so it is noticeably slower.
    /// </summary>
    ImageContent
}

/// <summary>One texture considered by the duplicate search.</summary>
public sealed class TextureDuplicateCandidate
{
    public required GTexture Texture { get; init; }

    /// <summary>Drawable the texture belongs to; null when the caller does not track it.</summary>
    public GDrawable? Drawable { get; init; }

    public string DrawableName { get; init; } = string.Empty;

    /// <summary>Absolute path used for hashing. Defaults to the resolved texture path.</summary>
    public string? OverridePath { get; init; }

    public string ResolvedPath => OverridePath ?? Texture.FullFilePath;
}

/// <summary>One member of a duplicate group.</summary>
public sealed class TextureDuplicateItem
{
    public required GTexture Texture { get; init; }
    public GDrawable? Drawable { get; init; }
    public string DrawableName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }

    public string TextureName => Texture.DisplayName;
    public string VariationName => $"{Texture.TxtLetter} ({Texture.TxtNumber})";
    public string Description => $"{DrawableName} / {Texture.DisplayName}";
}

/// <summary>A set of textures that all hold the same image.</summary>
public sealed class TextureDuplicateGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Key { get; init; }
    public required List<TextureDuplicateItem> Items { get; init; }

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public string CountText => Localization.Loc.T("TexDup_CountFormat", Count);
    public string SizeText => TextureSizeHelper.FormatBytesAsMegabytes(FileSizeBytes);
    public string SavableText => TextureSizeHelper.FormatBytesAsMegabytes(SharableBytes);

    /// <summary>Size of a single copy, in bytes.</summary>
    public long FileSizeBytes { get; init; }

    public int Count => Items.Count;

    /// <summary>How many distinct files on disk the group currently uses.</summary>
    public int DistinctFileCount => Items
        .Select(i => i.FullPath)
        .Where(p => !string.IsNullOrEmpty(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    /// <summary>Bytes freed on disk when every member points at one shared file.</summary>
    public long SharableBytes => Math.Max(0, DistinctFileCount - 1) * FileSizeBytes;

    /// <summary>Drawables that use this image, with how many variations each of them uses.</summary>
    public IReadOnlyList<string> UsedByDrawables => Items
        .GroupBy(i => i.DrawableName)
        .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key)
        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public string UsedByText => string.Join(", ", UsedByDrawables);

    public string VariationsText => string.Join(", ", Items
        .OrderBy(i => i.DrawableName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(i => i.Texture.TxtNumber)
        .Select(i => i.Texture.DisplayName));

    /// <summary>
    /// Variations that sit in the same drawable as another member of the group. Those are the only
    /// ones that can be deleted outright, a cross drawable duplicate is a legitimate texture.
    /// </summary>
    public bool HasSameDrawableDuplicates => Items
        .Where(i => i.Drawable != null)
        .GroupBy(i => i.Drawable!)
        .Any(g => g.Count() > 1);
}

/// <summary>Progress of a running duplicate scan.</summary>
public readonly record struct TextureDuplicateProgress(int Current, int Total, string TextureName)
{
    public double Percentage => Total <= 0 ? 0 : Current * 100d / Total;
}

/// <summary>One variation that the destructive "remove duplicate variations" action would delete.</summary>
public sealed class TextureVariationRemoval
{
    public required GDrawable Drawable { get; init; }
    public required GTexture Texture { get; init; }

    /// <summary>Variation that stays and carries the image.</summary>
    public required GTexture KeptTexture { get; init; }

    public string DrawableName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }

    public string RemovedName => Texture.DisplayName;
    public string KeptName => KeptTexture.DisplayName;
    public string Description => $"{DrawableName}: {Texture.DisplayName} → {KeptTexture.DisplayName}";
}

/// <summary>Result of the non destructive "share one file" action.</summary>
public sealed class TextureShareResult
{
    public int GroupCount { get; set; }

    /// <summary>Textures whose <see cref="GTexture.FilePath"/> now points at another file.</summary>
    public int RepointedTextures { get; set; }

    public int DeletedFiles { get; set; }
    public long FreedBytes { get; set; }

    /// <summary>Textures whose details have to be reloaded because their file changed.</summary>
    public List<GTexture> ChangedTextures { get; } = [];
}

/// <summary>
/// Finds textures that hold the same image, across the whole project and across drawables.
/// Nothing here modifies a project on its own: the caller decides whether to share one file
/// between the duplicates or to delete redundant variations.
/// </summary>
public static class TextureDuplicateDetector
{
    /// <summary>
    /// Groups the given textures by content. Files are first bucketed by size, then hashed with MD5,
    /// and - in <see cref="TextureDuplicateMatchMode.ImageContent"/> - the leftovers are decoded and
    /// compared by their actual dimensions, format and pixels.
    /// Only groups with more than one member are returned.
    /// </summary>
    public static async Task<List<TextureDuplicateGroup>> FindDuplicatesAsync(
        IEnumerable<TextureDuplicateCandidate> candidates,
        TextureDuplicateMatchMode mode = TextureDuplicateMatchMode.FileContent,
        IProgress<TextureDuplicateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var items = candidates.Where(c => c?.Texture != null).ToList();
        var total = items.Count;
        var byKey = new Dictionary<string, List<TextureDuplicateItem>>(StringComparer.Ordinal);
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);

        // Textures whose file could not be hashed by content and that need the slow path.
        var unhashed = new List<TextureDuplicateCandidate>();

        // Bucket by file size first: two files of a different length can never be identical,
        // so a size that occurs once does not need to be hashed at all.
        var sized = new List<(TextureDuplicateCandidate Candidate, string Path, long Size)>();
        foreach (var candidate in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = candidate.ResolvedPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            sized.Add((candidate, path, new FileInfo(path).Length));
        }

        var sizeGroups = sized.GroupBy(s => s.Size).ToList();
        var processed = 0;

        foreach (var sizeGroup in sizeGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var members = sizeGroup.ToList();
            if (members.Count < 2)
            {
                processed += members.Count;
                if (mode == TextureDuplicateMatchMode.ImageContent)
                {
                    unhashed.Add(members[0].Candidate);
                }

                progress?.Report(new TextureDuplicateProgress(processed, total, members[0].Candidate.Texture.DisplayName));
                continue;
            }

            foreach (var (candidate, path, size) in members)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hash = await ComputeFileHashAsync(path, cancellationToken).ConfigureAwait(false);
                processed++;
                progress?.Report(new TextureDuplicateProgress(processed, total, candidate.Texture.DisplayName));

                if (hash == null)
                {
                    continue;
                }

                var key = "file:" + hash;
                Add(byKey, sizes, key, candidate, path, size);
            }
        }

        if (mode == TextureDuplicateMatchMode.ImageContent)
        {
            // Anything that stayed alone after the file hash is compared by decoded pixels, which
            // also catches the same image stored in two different containers.
            var singles = byKey.Where(kvp => kvp.Value.Count < 2).SelectMany(kvp => kvp.Value).ToList();
            foreach (var key in byKey.Where(kvp => kvp.Value.Count < 2).Select(kvp => kvp.Key).ToList())
            {
                byKey.Remove(key);
                sizes.Remove(key);
            }

            var pending = unhashed
                .Select(c => (Candidate: c, Path: c.ResolvedPath))
                .Concat(singles.Select(s => (
                    Candidate: new TextureDuplicateCandidate { Texture = s.Texture, Drawable = s.Drawable, DrawableName = s.DrawableName, OverridePath = s.FullPath },
                    Path: s.FullPath)))
                .ToList();

            foreach (var (candidate, path) in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    continue;
                }

                var hash = await Task.Run(() => ComputeImageHash(path), cancellationToken).ConfigureAwait(false);
                if (hash == null)
                {
                    continue;
                }

                Add(byKey, sizes, "image:" + hash, candidate, path, new FileInfo(path).Length);
            }
        }

        return [.. byKey
            .Where(kvp => kvp.Value.Count > 1)
            .Select(kvp => new TextureDuplicateGroup
            {
                Key = kvp.Key,
                Items = [.. kvp.Value.OrderBy(i => i.DrawableName, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Texture.TxtNumber)],
                FileSizeBytes = sizes.TryGetValue(kvp.Key, out var s) ? s : 0
            })
            .OrderByDescending(g => g.SharableBytes)
            .ThenByDescending(g => g.Count)];

        static void Add(
            Dictionary<string, List<TextureDuplicateItem>> byKey,
            Dictionary<string, long> sizes,
            string key,
            TextureDuplicateCandidate candidate,
            string path,
            long size)
        {
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
                sizes[key] = size;
            }

            list.Add(new TextureDuplicateItem
            {
                Texture = candidate.Texture,
                Drawable = candidate.Drawable,
                DrawableName = candidate.DrawableName,
                FullPath = path,
                FileSizeBytes = size
            });
        }
    }

    /// <summary>MD5 of the raw file, or null when it cannot be read.</summary>
    public static async Task<string?> ComputeFileHashAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            var hash = await MD5.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Hash of the decoded image: width, height, pixel format and the raw pixels.
    /// Returns null when the image cannot be decoded.
    /// </summary>
    public static string? ComputeImageHash(string path)
    {
        try
        {
            using var img = ImgHelper.GetImage(path);
            if (img == null)
            {
                return null;
            }

            // The pixels have to be read through the pixel collection: encoding the image to a raw
            // format would keep its own depth and palette, so the same picture stored as a paletted
            // .png and as a plain .dds would not produce the same bytes.
            img.Alpha(AlphaOption.Set);
            var pixels = img.GetPixels().ToByteArray(PixelMapping.RGBA) ?? [];
            var header = Encoding.UTF8.GetBytes($"{img.Width}x{img.Height}:");

            using var md5 = MD5.Create();
            md5.TransformBlock(header, 0, header.Length, null, 0);
            md5.TransformFinalBlock(pixels, 0, pixels.Length);
            return Convert.ToHexString(md5.Hash!);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes every texture of a group point at a single file and reports which of the other files
    /// became unreferenced. Nothing is deleted here; <paramref name="allProjectTextures"/> is used to
    /// make sure a file that is still referenced somewhere else is never reported as orphaned.
    /// </summary>
    /// <param name="groups">Groups to collapse.</param>
    /// <param name="allProjectTextures">Every texture of the project, used for the reference check.</param>
    /// <param name="assetsPath">
    /// Project assets folder. Only files below it may be deleted, so a texture that still points at a
    /// file outside the project (an external project) is never touched.
    /// </param>
    public static TextureShareResult ShareFiles(
        IEnumerable<TextureDuplicateGroup> groups,
        IEnumerable<GTexture> allProjectTextures,
        string assetsPath,
        Func<string, bool>? deleteFile = null)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(allProjectTextures);

        var result = new TextureShareResult();
        var orphanCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            if (group.Items.Count < 2)
            {
                continue;
            }

            // Keep the file of the first member, which after sorting is the lowest drawable/variation.
            var keeper = group.Items[0];
            if (string.IsNullOrEmpty(keeper.Texture.FilePath))
            {
                continue;
            }

            var changed = false;
            foreach (var item in group.Items.Skip(1))
            {
                if (string.Equals(item.Texture.FilePath, keeper.Texture.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                orphanCandidates.Add(item.FullPath);
                var extensionChanged = !string.Equals(item.Texture.Extension, keeper.Texture.Extension, StringComparison.OrdinalIgnoreCase);

                item.Texture.FilePath = keeper.Texture.FilePath;
                item.Texture.Extension = keeper.Texture.Extension;
                result.RepointedTextures++;
                changed = true;

                if (extensionChanged)
                {
                    result.ChangedTextures.Add(item.Texture);
                }
            }

            if (changed)
            {
                result.GroupCount++;
            }
        }

        if (orphanCandidates.Count == 0)
        {
            return result;
        }

        // Any file that is still referenced by some texture must stay.
        var stillUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var texture in allProjectTextures)
        {
            if (texture?.FilePath == null)
            {
                continue;
            }

            try
            {
                stillUsed.Add(texture.FullFilePath);
            }
            catch (Exception)
            {
                // A texture whose path cannot be resolved simply does not protect any file.
            }
        }

        deleteFile ??= DeleteFile;

        foreach (var path in orphanCandidates)
        {
            if (stillUsed.Contains(path) || !IsInside(assetsPath, path) || !File.Exists(path))
            {
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (Exception)
            {
                continue;
            }

            if (deleteFile(path))
            {
                result.DeletedFiles++;
                result.FreedBytes += size;
            }
        }

        return result;
    }

    /// <summary>
    /// Lists the variations that the destructive action would delete: per drawable, every duplicate
    /// after the first one. Duplicates that live in different drawables are never listed, the game
    /// needs one texture file per drawable.
    /// </summary>
    public static List<TextureVariationRemoval> PlanVariationRemovals(IEnumerable<TextureDuplicateGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var result = new List<TextureVariationRemoval>();

        foreach (var group in groups)
        {
            foreach (var perDrawable in group.Items.Where(i => i.Drawable != null).GroupBy(i => i.Drawable!))
            {
                var ordered = perDrawable.OrderBy(i => i.Texture.TxtNumber).ToList();
                if (ordered.Count < 2)
                {
                    continue;
                }

                var keeper = ordered[0];
                foreach (var item in ordered.Skip(1))
                {
                    result.Add(new TextureVariationRemoval
                    {
                        Drawable = perDrawable.Key,
                        Texture = item.Texture,
                        KeptTexture = keeper.Texture,
                        DrawableName = item.DrawableName,
                        FileSizeBytes = group.FileSizeBytes
                    });
                }
            }
        }

        return [.. result.OrderBy(r => r.DrawableName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Texture.TxtNumber)];
    }

    /// <summary>
    /// Removes the planned variations from their drawable and renumbers the remaining ones.
    /// This changes the texture count of the drawable, so the shop meta written at build time changes too.
    /// </summary>
    public static int RemoveVariations(IEnumerable<TextureVariationRemoval> removals)
    {
        ArgumentNullException.ThrowIfNull(removals);

        var touched = new HashSet<GDrawable>();
        var count = 0;

        foreach (var removal in removals)
        {
            if (removal.Drawable.Textures == null)
            {
                continue;
            }

            if (removal.Drawable.Textures.Remove(removal.Texture))
            {
                touched.Add(removal.Drawable);
                count++;
            }
        }

        foreach (var drawable in touched)
        {
            Extensions.ObservableCollectionExtensions.ReassignNumbers(drawable.Textures);
            drawable.ValidateDetails();
        }

        return count;
    }

    private static bool DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Log($"Could not delete duplicate texture file '{path}': {ex.Message}", Views.LogType.Warning);
            return false;
        }
    }

    private static bool IsInside(string folder, string path)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return false;
        }

        try
        {
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(path);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
