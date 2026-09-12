using grzyClothTool.Controls;
using grzyClothTool.Helpers;
using grzyClothTool.Localization;
using grzyClothTool.Models;
using grzyClothTool.Models.Drawable;
using grzyClothTool.Models.Texture;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace grzyClothTool.Views;

#nullable enable

/// <summary>
/// Which part of the project the bulk optimizer looks at.
/// </summary>
public enum BulkOptimizeScope
{
    WholeProject,
    SelectedAddon,
    SelectedDrawables
}

/// <summary>
/// A combo box entry that carries a value and a localized label.
/// </summary>
public class BulkOptimizeChoice<T>(T value, string displayName)
{
    public T Value { get; } = value;
    public string DisplayName { get; } = displayName;
    public override string ToString() => DisplayName;
}

/// <summary>
/// Bulk texture optimizer: applies a set of rules to every texture of the project (or of the
/// current addon / drawable selection), shows what would change, and flags the accepted textures
/// so that the optimized version is generated during the next resource build.
/// The source files on disk are never modified.
/// </summary>
public partial class BulkOptimizeWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AddonManager? _addonManager;
    private CancellationTokenSource? _cts;
    private string? _sortProperty;
    private bool _sortDescending = true;

    public ObservableCollection<BulkOptimizePlanEntry> Plan { get; } = [];

    public IReadOnlyList<BulkOptimizeChoice<BulkOptimizeScope>> ScopeOptions { get; }
    public IReadOnlyList<BulkOptimizeChoice<PowerOfTwoMode>> PowerOfTwoModes { get; }
    public IReadOnlyList<int> ResolutionOptions { get; } = [64, 128, 256, 512, 1024, 2048, 4096];
    public IReadOnlyList<int> MemoryOptions { get; } = [2, 4, 8, 12, 16, 24, 32, 48, 64, 128];

    private BulkOptimizeChoice<BulkOptimizeScope> _selectedScope;
    public BulkOptimizeChoice<BulkOptimizeScope> SelectedScope
    {
        get => _selectedScope;
        set { _selectedScope = value; OnPropertyChanged(); }
    }

    private BulkOptimizeChoice<PowerOfTwoMode> _selectedPowerOfTwoMode;
    public BulkOptimizeChoice<PowerOfTwoMode> SelectedPowerOfTwoMode
    {
        get => _selectedPowerOfTwoMode;
        set { _selectedPowerOfTwoMode = value; OnPropertyChanged(); }
    }

    private bool _generateMipMaps = true;
    public bool GenerateMipMaps
    {
        get => _generateMipMaps;
        set { _generateMipMaps = value; OnPropertyChanged(); }
    }

    private bool _compressUncompressed = true;
    public bool CompressUncompressed
    {
        get => _compressUncompressed;
        set { _compressUncompressed = value; OnPropertyChanged(); }
    }

    private bool _fixPowerOfTwo = true;
    public bool FixPowerOfTwo
    {
        get => _fixPowerOfTwo;
        set { _fixPowerOfTwo = value; OnPropertyChanged(); }
    }

    private bool _enforceResolutionLimit = true;
    public bool EnforceResolutionLimit
    {
        get => _enforceResolutionLimit;
        set { _enforceResolutionLimit = value; OnPropertyChanged(); }
    }

    private bool _enforceMemoryLimit = true;
    public bool EnforceMemoryLimit
    {
        get => _enforceMemoryLimit;
        set { _enforceMemoryLimit = value; OnPropertyChanged(); }
    }

    private bool _optimizeCompression;
    public bool OptimizeCompression
    {
        get => _optimizeCompression;
        set { _optimizeCompression = value; OnPropertyChanged(); }
    }

    private int _limitDiffuse;
    public int LimitDiffuse
    {
        get => _limitDiffuse;
        set { _limitDiffuse = value; OnPropertyChanged(); }
    }

    private int _limitNormal;
    public int LimitNormal
    {
        get => _limitNormal;
        set { _limitNormal = value; OnPropertyChanged(); }
    }

    private int _limitSpecular;
    public int LimitSpecular
    {
        get => _limitSpecular;
        set { _limitSpecular = value; OnPropertyChanged(); }
    }

    private int _maxMemoryMB;
    public int MaxMemoryMB
    {
        get => _maxMemoryMB;
        set { _maxMemoryMB = value; OnPropertyChanged(); }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private string _summaryDetail = string.Empty;
    public string SummaryDetail
    {
        get => _summaryDetail;
        set { _summaryDetail = value; OnPropertyChanged(); }
    }

    private string _summaryText = string.Empty;
    public string SummaryText
    {
        get => _summaryText;
        set { _summaryText = value; OnPropertyChanged(); }
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    private Visibility _progressVisibility = Visibility.Collapsed;
    public Visibility ProgressVisibility
    {
        get => _progressVisibility;
        set { _progressVisibility = value; OnPropertyChanged(); }
    }

    private Visibility _cancelVisibility = Visibility.Collapsed;
    public Visibility CancelVisibility
    {
        get => _cancelVisibility;
        set { _cancelVisibility = value; OnPropertyChanged(); }
    }

    private Visibility _undoBakeVisibility = Visibility.Collapsed;
    public Visibility UndoBakeVisibility
    {
        get => _undoBakeVisibility;
        set { _undoBakeVisibility = value; OnPropertyChanged(); }
    }

    private bool _keepOriginalFiles = true;
    /// <summary>
    /// Keep the file a texture came from, so "write now" can be undone. When this is off the old
    /// asset is deleted - but only when it lives inside the project, never a file of the user.
    /// </summary>
    public bool KeepOriginalFiles
    {
        get => _keepOriginalFiles;
        set { _keepOriginalFiles = value; OnPropertyChanged(); }
    }

    private CancellationTokenSource? _bakeCts;
    private readonly List<TextureBakeRecord> _bakeRecords = [];

    public BulkOptimizeWindow() : this(MainWindow.AddonManager)
    {
    }

    public BulkOptimizeWindow(AddonManager? addonManager)
    {
        InitializeComponent();
        DataContext = this;

        _addonManager = addonManager;

        ScopeOptions =
        [
            new BulkOptimizeChoice<BulkOptimizeScope>(BulkOptimizeScope.WholeProject, Loc.T("BulkOpt_ScopeProject")),
            new BulkOptimizeChoice<BulkOptimizeScope>(BulkOptimizeScope.SelectedAddon, Loc.T("BulkOpt_ScopeAddon")),
            new BulkOptimizeChoice<BulkOptimizeScope>(BulkOptimizeScope.SelectedDrawables, Loc.T("BulkOpt_ScopeDrawable"))
        ];
        _selectedScope = ScopeOptions[0];

        PowerOfTwoModes =
        [
            new BulkOptimizeChoice<PowerOfTwoMode>(PowerOfTwoMode.Down, Loc.T("BulkOpt_PowerOfTwoDown")),
            new BulkOptimizeChoice<PowerOfTwoMode>(PowerOfTwoMode.Up, Loc.T("BulkOpt_PowerOfTwoUp"))
        ];
        _selectedPowerOfTwoMode = PowerOfTwoModes[0];

        var defaults = BulkOptimizeOptions.FromSettings();
        _limitDiffuse = ClosestOption(ResolutionOptions, defaults.ResolutionLimitDiffuse);
        _limitNormal = ClosestOption(ResolutionOptions, defaults.ResolutionLimitNormal);
        _limitSpecular = ClosestOption(ResolutionOptions, defaults.ResolutionLimitSpecular);
        _maxMemoryMB = ClosestOption(MemoryOptions, defaults.MaxTextureMemoryMB);

        SummaryText = Loc.T("BulkOpt_SummaryEmpty");
        StatusMessage = Loc.T("BulkOpt_Idle");
    }

    public BulkOptimizeOptions BuildOptions() => new()
    {
        GenerateMipMaps = GenerateMipMaps,
        CompressUncompressed = CompressUncompressed,
        FixPowerOfTwo = FixPowerOfTwo,
        PowerOfTwoMode = SelectedPowerOfTwoMode?.Value ?? PowerOfTwoMode.Down,
        EnforceResolutionLimit = EnforceResolutionLimit,
        ResolutionLimitDiffuse = LimitDiffuse,
        ResolutionLimitNormal = LimitNormal,
        ResolutionLimitSpecular = LimitSpecular,
        EnforceMemoryLimit = EnforceMemoryLimit,
        MaxTextureMemoryMB = MaxMemoryMB,
        OptimizeCompression = OptimizeCompression
    };

    /// <summary>
    /// Every texture in the chosen scope, paired with the name of the drawable it belongs to.
    /// </summary>
    public List<(string DrawableName, GTexture Texture)> CollectTextures()
    {
        var result = new List<(string, GTexture)>();
        if (_addonManager?.Addons == null)
        {
            return result;
        }

        IEnumerable<GDrawable> drawables;
        switch (SelectedScope?.Value ?? BulkOptimizeScope.WholeProject)
        {
            case BulkOptimizeScope.SelectedAddon:
                drawables = _addonManager.SelectedAddon?.Drawables ?? Enumerable.Empty<GDrawable>();
                break;
            case BulkOptimizeScope.SelectedDrawables:
                var addon = _addonManager.SelectedAddon;
                if (addon == null)
                {
                    drawables = [];
                }
                else if (addon.SelectedDrawables is { Count: > 0 })
                {
                    drawables = addon.SelectedDrawables;
                }
                else
                {
                    drawables = addon.SelectedDrawable != null ? [addon.SelectedDrawable] : [];
                }
                break;
            default:
                drawables = _addonManager.Addons.SelectMany(a => a.Drawables ?? []);
                break;
        }

        foreach (var drawable in drawables)
        {
            if (drawable?.Textures == null || drawable is GDrawableReserved)
            {
                continue;
            }

            foreach (var texture in drawable.Textures)
            {
                if (texture != null)
                {
                    result.Add((drawable.Name ?? string.Empty, texture));
                }
            }
        }

        return result;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeAsync();
    }

    public async Task AnalyzeAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var options = BuildOptions();
        var textures = CollectTextures();

        if (textures.Count == 0)
        {
            Plan.Clear();
            SummaryText = Loc.T("BulkOpt_SummaryEmpty");
            StatusMessage = Loc.T("BulkOpt_NoTextures");
            return;
        }

        AnalyzeButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        ProgressVisibility = Visibility.Visible;
        ProgressValue = 0;
        Plan.Clear();

        var progress = new Progress<BulkOptimizeProgress>(p =>
        {
            ProgressValue = p.Percentage;
            StatusMessage = Loc.T("BulkOpt_AnalyzeProgress", p.Current, p.Total);
        });

        try
        {
            var plan = await Task.Run(() => BulkOptimizeHelper.CreatePlan(textures, options, progress, token), token);

            foreach (var entry in plan)
            {
                entry.PropertyChanged += PlanEntryChanged;
                Plan.Add(entry);
            }

            _sortProperty = nameof(BulkOptimizePlanEntry.BeforeBytes);
            _sortDescending = true;

            UpdateSummary();
            StatusMessage = Loc.T("BulkOpt_AnalyzeDone", textures.Count, plan.Count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("BulkOpt_Idle");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            LogHelper.Log($"Bulk texture optimization failed: {ex.Message}", LogType.Error);
        }
        finally
        {
            ProgressVisibility = Visibility.Collapsed;
            AnalyzeButton.IsEnabled = true;
            ApplyButton.IsEnabled = true;
        }
    }

    public void UpdateSummary()
    {
        var selected = Plan.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SummaryText = Plan.Count == 0 ? Loc.T("BulkOpt_NoChanges") : Loc.T("BulkOpt_SummaryEmpty");
            SummaryDetail = string.Empty;
            return;
        }

        var before = selected.Sum(p => p.BeforeBytes);
        var after = selected.Sum(p => p.AfterBytes);
        var saved = before - after;
        var percent = before > 0 ? saved * 100d / before : 0;

        SummaryText = Loc.T(
            "BulkOpt_Summary",
            selected.Count,
            TextureSizeHelper.FormatBytesAsMegabytes(before),
            TextureSizeHelper.FormatBytesAsMegabytes(after),
            TextureSizeHelper.FormatBytesAsMegabytes(saved),
            percent.ToString("0.#"));

        SummaryDetail = Loc.T(
            "BulkOpt_SummaryBreakdown",
            TextureSizeHelper.FormatBytesAsMegabytes(selected.Sum(p => p.CompressionSavedBytes)),
            selected.Count(p => p.IsCompressionChanged),
            TextureSizeHelper.FormatBytesAsMegabytes(selected.Sum(p => p.ResizeSavedBytes)),
            selected.Count(p => p.IsResized));
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var selected = Plan.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            CustomMessageBox.Show(Loc.T("BulkOpt_NothingSelected"));
            return;
        }

        var before = selected.Sum(p => p.BeforeBytes);
        var after = selected.Sum(p => p.AfterBytes);

        var count = BulkOptimizeHelper.Apply(selected);
        SaveHelper.SetUnsavedChanges(true);

        var message = Loc.T("BulkOpt_Applied", count, TextureSizeHelper.FormatBytesAsMegabytes(before - after));
        LogHelper.Log(message);
        CustomMessageBox.Show(message + Environment.NewLine + Loc.T("BulkOpt_BuildNote"));

        foreach (var entry in selected)
        {
            Plan.Remove(entry);
        }

        UpdateSummary();
        StatusMessage = message;
    }

    /// <summary>
    /// Encodes the selected textures right away and repoints them at the generated file, instead of
    /// only flagging them for the next build. That makes the 3D preview and the texture preview show
    /// the real quality before anything is built.
    /// </summary>
    private async void BakeNow_Click(object sender, RoutedEventArgs e)
    {
        var selected = Plan.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            CustomMessageBox.Show(Loc.T("BulkOpt_NothingSelected"));
            return;
        }

        string assetsPath;
        try
        {
            assetsPath = FileHelper.GetProjectAssetsPath();
        }
        catch (Exception ex)
        {
            CustomMessageBox.Show(ex.Message, Loc.T("BulkOpt_Title"),
                CustomMessageBox.CustomMessageBoxButtons.OKOnly, CustomMessageBox.CustomMessageBoxIcon.Warning);
            return;
        }

        var isExternal = _addonManager?.IsExternalProject == true;
        var question = Loc.T("BulkOpt_BakeConfirm", selected.Count);
        if (isExternal)
        {
            // The source files belong to the user here, so the result is written next to the project
            // and the texture is repointed - the original file on disk is left alone.
            question += Environment.NewLine + Environment.NewLine + Loc.T("BulkOpt_BakeExternalNote");
        }
        else if (!KeepOriginalFiles)
        {
            question += Environment.NewLine + Environment.NewLine + Loc.T("BulkOpt_BakeDeleteNote");
        }

        var answer = CustomMessageBox.Show(question, Loc.T("BulkOpt_BakeConfirmTitle"),
            CustomMessageBox.CustomMessageBoxButtons.YesNo, CustomMessageBox.CustomMessageBoxIcon.Question);
        if (answer != CustomMessageBox.CustomMessageBoxResult.Yes)
        {
            return;
        }

        _bakeCts?.Cancel();
        _bakeCts?.Dispose();
        _bakeCts = new CancellationTokenSource();

        AnalyzeButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        BakeButton.IsEnabled = false;
        CancelVisibility = Visibility.Visible;
        ProgressVisibility = Visibility.Visible;
        ProgressValue = 0;

        var progress = new Progress<TextureBakeProgress>(p =>
        {
            ProgressValue = p.Percentage;
            StatusMessage = Loc.T("BulkOpt_BakeProgress", p.Current, p.Total, p.TextureName);
        });

        TextureBakeResult result;
        try
        {
            var options = new TextureBakeOptions
            {
                // An external project never gets its source deleted, whatever the checkbox says.
                OriginalHandling = KeepOriginalFiles || isExternal
                    ? TextureBakeOriginalHandling.Keep
                    : TextureBakeOriginalHandling.Delete
            };

            var allTextures = CollectAllProjectTextures();
            result = await TextureBakeHelper.BakeAsync(selected, assetsPath, options, allTextures, progress, _bakeCts.Token);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            LogHelper.Log($"Writing the optimized textures failed: {ex.Message}", LogType.Error);
            CustomMessageBox.Show(ex.Message, Loc.T("BulkOpt_Title"),
                CustomMessageBox.CustomMessageBoxButtons.OKOnly, CustomMessageBox.CustomMessageBoxIcon.Error);
            return;
        }
        finally
        {
            ProgressVisibility = Visibility.Collapsed;
            CancelVisibility = Visibility.Collapsed;
            AnalyzeButton.IsEnabled = true;
            ApplyButton.IsEnabled = true;
            BakeButton.IsEnabled = true;
        }

        if (result.Count == 0)
        {
            StatusMessage = Loc.T("BulkOpt_BakeNone");
            CustomMessageBox.Show(Loc.T("BulkOpt_BakeNone"));
            return;
        }

        _bakeRecords.AddRange(result.Records);
        UndoBakeVisibility = KeepOriginalFiles && !_bakeRecords.All(r => r.PreviousFullPath == null)
            ? Visibility.Visible
            : Visibility.Collapsed;

        SaveHelper.SetUnsavedChanges(true);

        var message = Loc.T(
            "BulkOpt_BakeDone",
            result.Count,
            TextureSizeHelper.FormatBytesAsMegabytes(result.FileBytesSaved),
            TextureSizeHelper.FormatBytesAsMegabytes(result.MemoryBytesSaved));

        if (result.WasCancelled)
        {
            message += Environment.NewLine + Loc.T("BulkOpt_BakeCancelled");
        }

        if (result.Failures.Count > 0)
        {
            message += Environment.NewLine + Loc.T("BulkOpt_BakeFailures", result.Failures.Count);
        }

        if (result.DeletedOriginals > 0)
        {
            message += Environment.NewLine + Loc.T("BulkOpt_BakeDeleted", result.DeletedOriginals);
        }

        LogHelper.Log(message);
        CustomMessageBox.Show(message);
        StatusMessage = message;

        foreach (var entry in result.Records.Select(r => r.Texture).ToHashSet()
                     .Select(t => Plan.FirstOrDefault(p => p.Texture == t))
                     .Where(p => p != null)
                     .ToList())
        {
            Plan.Remove(entry!);
        }

        UpdateSummary();
    }

    private void CancelBake_Click(object sender, RoutedEventArgs e)
    {
        _bakeCts?.Cancel();
        StatusMessage = Loc.T("BulkOpt_BakeCancelling");
    }

    private async void UndoBake_Click(object sender, RoutedEventArgs e)
    {
        if (_bakeRecords.Count == 0)
        {
            CustomMessageBox.Show(Loc.T("BulkOpt_UndoBakeNone"));
            return;
        }

        var count = await TextureBakeHelper.RevertAsync(_bakeRecords);
        _bakeRecords.Clear();
        UndoBakeVisibility = Visibility.Collapsed;

        if (count == 0)
        {
            CustomMessageBox.Show(Loc.T("BulkOpt_UndoBakeNone"));
            return;
        }

        SaveHelper.SetUnsavedChanges(true);

        var message = Loc.T("BulkOpt_UndoBakeDone", count);
        LogHelper.Log(message);
        CustomMessageBox.Show(message);
        StatusMessage = message;
    }

    /// <summary>Every texture of the project, used to keep a file that is still referenced.</summary>
    private List<GTexture> CollectAllProjectTextures()
    {
        if (_addonManager?.Addons == null)
        {
            return [];
        }

        return [.. _addonManager.Addons
            .SelectMany(a => a.Drawables ?? [])
            .Where(d => d is not GDrawableReserved && d?.Textures != null)
            .SelectMany(d => d.Textures)
            .Where(t => t != null)];
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        var textures = CollectTextures().Select(t => t.Texture).ToList();
        var count = BulkOptimizeHelper.Revert(textures);

        if (count == 0)
        {
            CustomMessageBox.Show(Loc.T("BulkOpt_RevertNone"));
            return;
        }

        SaveHelper.SetUnsavedChanges(true);

        var message = Loc.T("BulkOpt_Reverted", count);
        LogHelper.Log(message);
        CustomMessageBox.Show(message);

        Plan.Clear();
        SummaryText = Loc.T("BulkOpt_SummaryEmpty");
        StatusMessage = message;
    }

    private void PlanEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BulkOptimizePlanEntry.IsSelected))
        {
            UpdateSummary();
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetSelection(_ => true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetSelection(_ => false);

    private void SelectOverBudget_Click(object sender, RoutedEventArgs e) => SetSelection(p => p.IsOverBudget);

    private void SetSelection(Func<BulkOptimizePlanEntry, bool> predicate)
    {
        foreach (var entry in Plan)
        {
            entry.IsSelected = predicate(entry);
        }

        UpdateSummary();
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader { Tag: string property })
        {
            return;
        }

        if (_sortProperty == property)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortProperty = property;
            _sortDescending = true;
        }

        Sort();
    }

    private void Sort()
    {
        if (_sortProperty == null)
        {
            return;
        }

        Func<BulkOptimizePlanEntry, object> key = _sortProperty switch
        {
            nameof(BulkOptimizePlanEntry.DrawableName) => p => p.DrawableName,
            nameof(BulkOptimizePlanEntry.TextureName) => p => p.TextureName,
            nameof(BulkOptimizePlanEntry.FormatText) => p => p.FormatText,
            nameof(BulkOptimizePlanEntry.AfterBytes) => p => p.AfterBytes,
            nameof(BulkOptimizePlanEntry.SavedBytes) => p => p.SavedBytes,
            _ => p => p.BeforeBytes
        };

        var sorted = (_sortDescending ? Plan.OrderByDescending(key) : Plan.OrderBy(key)).ToList();

        Plan.Clear();
        foreach (var entry in sorted)
        {
            Plan.Add(entry);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _bakeCts?.Cancel();
        _bakeCts?.Dispose();
        base.OnClosed(e);
    }

    private static int ClosestOption(IReadOnlyList<int> options, int value)
        => options.Contains(value) ? value : options.OrderBy(o => Math.Abs(o - value)).First();

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
