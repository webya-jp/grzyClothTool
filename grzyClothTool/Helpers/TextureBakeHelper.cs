using grzyClothTool.Models.Texture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace grzyClothTool.Helpers;

#nullable enable

/// <summary>What happens to the file a texture pointed at before it was baked.</summary>
public enum TextureBakeOriginalHandling
{
    /// <summary>Leave the old file in place so the bake can be undone.</summary>
    Keep,

    /// <summary>
    /// Delete the old file once nothing references it any more. Only files inside the project
    /// assets folder are ever deleted, a file the user owns is never touched.
    /// </summary>
    Delete
}

public sealed class TextureBakeOptions
{
    public TextureBakeOriginalHandling OriginalHandling { get; set; } = TextureBakeOriginalHandling.Keep;
}

/// <summary>Everything needed to undo one baked texture.</summary>
public sealed class TextureBakeRecord
{
    public required GTexture Texture { get; init; }
    public required string PreviousFilePath { get; init; }
    public required string PreviousExtension { get; init; }
    public GTextureDetails? PreviousTxtDetails { get; init; }
    public GTextureDetails? PreviousOptimizeDetails { get; init; }
    public bool PreviousIsOptimizedDuringBuild { get; init; }

    /// <summary>Relative name of the file that was written into the project assets folder.</summary>
    public required string NewFilePath { get; init; }

    /// <summary>Absolute path of the file that was written.</summary>
    public required string NewFullPath { get; init; }

    /// <summary>Absolute path of the file the texture pointed at before, when it still exists.</summary>
    public string? PreviousFullPath { get; init; }

    public long PreviousFileBytes { get; init; }
    public long NewFileBytes { get; init; }
    public long PreviousMemoryBytes { get; init; }
    public long NewMemoryBytes { get; init; }
}

public sealed class TextureBakeResult
{
    public List<TextureBakeRecord> Records { get; } = [];
    public List<string> Failures { get; } = [];

    public int Count => Records.Count;
    public long FileBytesBefore => Records.Sum(r => r.PreviousFileBytes);
    public long FileBytesAfter => Records.Sum(r => r.NewFileBytes);
    public long FileBytesSaved => FileBytesBefore - FileBytesAfter;
    public long MemoryBytesBefore => Records.Sum(r => r.PreviousMemoryBytes);
    public long MemoryBytesAfter => Records.Sum(r => r.NewMemoryBytes);
    public long MemoryBytesSaved => MemoryBytesBefore - MemoryBytesAfter;

    public int DeletedOriginals { get; set; }

    public bool WasCancelled { get; set; }
}

public readonly record struct TextureBakeProgress(int Current, int Total, string TextureName)
{
    public double Percentage => Total <= 0 ? 0 : Current * 100d / Total;
}

/// <summary>
/// Writes the result of the bulk optimizer to disk right away, instead of waiting for the next
/// resource build. Every texture is re-encoded into a new .ytd inside the project assets folder and
/// the texture is repointed at it, so the 3D preview and the texture preview show the real quality
/// before anything is built.
/// The file a texture came from is never overwritten, which matters for external projects where that
/// file belongs to the user.
/// </summary>
public static class TextureBakeHelper
{
    /// <summary>
    /// Bakes the given plan entries. <paramref name="assetsPath"/> must be the project assets folder;
    /// every generated file is written there under a fresh GUID name.
    /// </summary>
    public static async Task<TextureBakeResult> BakeAsync(
        IEnumerable<BulkOptimizePlanEntry> entries,
        string assetsPath,
        TextureBakeOptions? options = null,
        IEnumerable<GTexture>? allProjectTextures = null,
        IProgress<TextureBakeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrEmpty(assetsPath);

        options ??= new TextureBakeOptions();
        Directory.CreateDirectory(assetsPath);

        var list = entries.ToList();
        var result = new TextureBakeResult();

        for (var i = 0; i < list.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.WasCancelled = true;
                break;
            }

            var entry = list[i];
            var texture = entry.Texture;
            progress?.Report(new TextureBakeProgress(i + 1, list.Count, texture?.DisplayName ?? string.Empty));

            if (texture?.FilePath == null)
            {
                continue;
            }

            try
            {
                var record = await BakeOneAsync(entry, assetsPath, cancellationToken).ConfigureAwait(false);
                if (record == null)
                {
                    result.Failures.Add(texture.DisplayName);
                    continue;
                }

                result.Records.Add(record);
            }
            catch (OperationCanceledException)
            {
                result.WasCancelled = true;
                break;
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{texture.DisplayName}: {ex.Message}");
                LogHelper.Log($"Could not write the optimized texture '{texture.DisplayName}': {ex.Message}", Views.LogType.Warning);
            }
        }

        if (options.OriginalHandling == TextureBakeOriginalHandling.Delete)
        {
            result.DeletedOriginals = DeleteOriginals(result.Records, assetsPath, allProjectTextures);
        }

        return result;
    }

    private static async Task<TextureBakeRecord?> BakeOneAsync(
        BulkOptimizePlanEntry entry,
        string assetsPath,
        CancellationToken cancellationToken)
    {
        var texture = entry.Texture;

        var previousFilePath = texture.FilePath;
        var previousExtension = texture.Extension;
        var previousDetails = Clone(texture.TxtDetails);
        var previousOptimizeDetails = Clone(texture.OptimizeDetails);
        var previousIsOptimizedDuringBuild = texture.IsOptimizedDuringBuild;

        string? previousFullPath = null;
        long previousFileBytes = 0;
        try
        {
            previousFullPath = texture.FullFilePath;
            if (File.Exists(previousFullPath))
            {
                previousFileBytes = new FileInfo(previousFullPath).Length;
            }
        }
        catch (Exception)
        {
            previousFullPath = null;
        }

        // ImgHelper.Optimize reads OptimizeDetails, so the target has to be in place before the call.
        texture.OptimizeDetails = new GTextureDetails
        {
            Width = entry.After.Width,
            Height = entry.After.Height,
            MipMapCount = entry.After.MipMapCount,
            Compression = entry.After.Compression,
            Name = entry.After.Name,
            Type = entry.After.Type,
            IsOptimizeNeeded = false
        };

        byte[]? bytes;
        try
        {
            bytes = await ImgHelper.Optimize(texture).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Restore(texture, previousOptimizeDetails, previousIsOptimizedDuringBuild);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (bytes == null || bytes.Length == 0)
        {
            Restore(texture, previousOptimizeDetails, previousIsOptimizedDuringBuild);
            return null;
        }

        var fileName = $"{Guid.NewGuid()}.ytd";
        var fullPath = Path.Combine(assetsPath, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken).ConfigureAwait(false);

        texture.FilePath = fileName;
        texture.Extension = ".ytd";

        if (!File.Exists(texture.FullFilePath))
        {
            // The relative name does not resolve back to the folder we just wrote into, which happens
            // when the assets folder is not the one of the loaded project. Keep the absolute path
            // rather than leaving the texture pointing at nothing.
            texture.FilePath = fullPath;
        }

        // Re-read the file so the details shown in the UI are what actually landed on disk.
        await texture.LoadDetails().ConfigureAwait(false);

        var newDetails = texture.TxtDetails;
        texture.IsOptimizedDuringBuild = false;
        texture.OptimizeDetails = newDetails == null
            ? previousOptimizeDetails!
            : new GTextureDetails
            {
                Width = newDetails.Width,
                Height = newDetails.Height,
                MipMapCount = newDetails.MipMapCount,
                Compression = newDetails.Compression,
                Name = newDetails.Name,
                Type = newDetails.Type
            };

        newDetails?.Validate();

        return new TextureBakeRecord
        {
            Texture = texture,
            PreviousFilePath = previousFilePath,
            PreviousExtension = previousExtension,
            PreviousTxtDetails = previousDetails,
            PreviousOptimizeDetails = previousOptimizeDetails,
            PreviousIsOptimizedDuringBuild = previousIsOptimizedDuringBuild,
            NewFilePath = texture.FilePath,
            NewFullPath = fullPath,
            PreviousFullPath = previousFullPath,
            PreviousFileBytes = previousFileBytes,
            NewFileBytes = bytes.LongLength,
            PreviousMemoryBytes = entry.BeforeBytes,
            NewMemoryBytes = newDetails == null
                ? entry.AfterBytes
                : TextureSizeHelper.GetTextureSizeInBytes(newDetails.Width, newDetails.Height, newDetails.MipMapCount, newDetails.Compression)
        };
    }

    /// <summary>
    /// Puts the textures back on the file they used before the bake and removes the generated files.
    /// </summary>
    public static async Task<int> RevertAsync(IEnumerable<TextureBakeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var count = 0;
        foreach (var record in records)
        {
            if (record.PreviousFullPath == null || !File.Exists(record.PreviousFullPath))
            {
                // The old file is gone (the user chose not to keep it), so there is nothing to go back to.
                continue;
            }

            record.Texture.FilePath = record.PreviousFilePath;
            record.Texture.Extension = record.PreviousExtension;
            await record.Texture.LoadDetails().ConfigureAwait(false);

            record.Texture.IsOptimizedDuringBuild = record.PreviousIsOptimizedDuringBuild;
            if (record.PreviousOptimizeDetails != null)
            {
                record.Texture.OptimizeDetails = record.PreviousOptimizeDetails;
            }

            record.Texture.TxtDetails?.Validate();

            try
            {
                if (File.Exists(record.NewFullPath))
                {
                    File.Delete(record.NewFullPath);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"Could not remove the baked texture file '{record.NewFullPath}': {ex.Message}", Views.LogType.Warning);
            }

            count++;
        }

        return count;
    }

    private static int DeleteOriginals(IEnumerable<TextureBakeRecord> records, string assetsPath, IEnumerable<GTexture>? allProjectTextures)
    {
        var stillUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var texture in allProjectTextures ?? [])
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
                // Unresolvable path, nothing to protect.
            }
        }

        var deleted = 0;
        foreach (var path in records
                     .Select(r => r.PreviousFullPath)
                     .Where(p => !string.IsNullOrEmpty(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (stillUsed.Contains(path!) || !IsInside(assetsPath, path!) || !File.Exists(path!))
            {
                continue;
            }

            try
            {
                File.Delete(path!);
                deleted++;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"Could not delete the original texture file '{path}': {ex.Message}", Views.LogType.Warning);
            }
        }

        return deleted;
    }

    private static void Restore(GTexture texture, GTextureDetails? optimizeDetails, bool isOptimizedDuringBuild)
    {
        if (optimizeDetails != null)
        {
            texture.OptimizeDetails = optimizeDetails;
        }

        texture.IsOptimizedDuringBuild = isOptimizedDuringBuild;
    }

    private static GTextureDetails? Clone(GTextureDetails? details)
    {
        if (details == null)
        {
            return null;
        }

        return new GTextureDetails
        {
            Width = details.Width,
            Height = details.Height,
            MipMapCount = details.MipMapCount,
            Compression = details.Compression,
            Name = details.Name,
            Type = details.Type
        };
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
            return Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
