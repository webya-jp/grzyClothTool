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

namespace grzyClothTool.Views;

#nullable enable

/// <summary>
/// Finds textures that hold the same image and offers two ways to deal with them:
/// a safe one that makes the duplicates share a single file inside the project, and a destructive
/// one that deletes redundant variations of the same drawable.
/// The build output of the safe action is identical to before, GTA needs one .ytd per variation.
/// </summary>
public partial class TextureDuplicateWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AddonManager? _addonManager;
    private CancellationTokenSource? _cts;

    public ObservableCollection<TextureDuplicateGroup> Groups { get; } = [];

    public IReadOnlyList<BulkOptimizeChoice<BulkOptimizeScope>> ScopeOptions { get; }

    private BulkOptimizeChoice<BulkOptimizeScope> _selectedScope;
    public BulkOptimizeChoice<BulkOptimizeScope> SelectedScope
    {
        get => _selectedScope;
        set { _selectedScope = value; OnPropertyChanged(); }
    }

    private bool _compareImageContent;
    public bool CompareImageContent
    {
        get => _compareImageContent;
        set { _compareImageContent = value; OnPropertyChanged(); }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private string _summaryText = string.Empty;
    public string SummaryText
    {
        get => _summaryText;
        set { _summaryText = value; OnPropertyChanged(); }
    }

    private string _externalWarning = string.Empty;
    public string ExternalWarning
    {
        get => _externalWarning;
        set { _externalWarning = value; OnPropertyChanged(); }
    }

    private Visibility _externalWarningVisibility = Visibility.Collapsed;
    public Visibility ExternalWarningVisibility
    {
        get => _externalWarningVisibility;
        set { _externalWarningVisibility = value; OnPropertyChanged(); }
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

    public TextureDuplicateWindow() : this(MainWindow.AddonManager)
    {
    }

    public TextureDuplicateWindow(AddonManager? addonManager)
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

        SummaryText = Loc.T("TexDup_SummaryEmpty");
        StatusMessage = Loc.T("TexDup_Idle");

        if (IsExternalProject)
        {
            // The files of an external project belong to the user, so nothing may be repointed or deleted.
            ShareButton.IsEnabled = false;
            ExternalWarning = Loc.T("TexDup_ExternalDisabled");
            ExternalWarningVisibility = Visibility.Visible;
        }
    }

    private bool IsExternalProject => _addonManager?.IsExternalProject == true;

    /// <summary>Every texture of the chosen scope, together with the drawable that owns it.</summary>
    public List<TextureDuplicateCandidate> CollectCandidates()
    {
        var result = new List<TextureDuplicateCandidate>();
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
                    result.Add(new TextureDuplicateCandidate
                    {
                        Texture = texture,
                        Drawable = drawable,
                        DrawableName = drawable.Name ?? string.Empty
                    });
                }
            }
        }

        return result;
    }

    /// <summary>Every texture of the project, used to tell whether a file is still referenced.</summary>
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

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    public async Task ScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var candidates = CollectCandidates();
        Groups.Clear();

        if (candidates.Count == 0)
        {
            SummaryText = Loc.T("TexDup_SummaryEmpty");
            StatusMessage = Loc.T("TexDup_NoTextures");
            return;
        }

        ScanButton.IsEnabled = false;
        ProgressVisibility = Visibility.Visible;
        ProgressValue = 0;

        var progress = new Progress<TextureDuplicateProgress>(p =>
        {
            ProgressValue = p.Percentage;
            StatusMessage = Loc.T("TexDup_ScanProgress", p.Current, p.Total);
        });

        try
        {
            var mode = CompareImageContent
                ? TextureDuplicateMatchMode.ImageContent
                : TextureDuplicateMatchMode.FileContent;

            var groups = await TextureDuplicateDetector.FindDuplicatesAsync(candidates, mode, progress, token);

            foreach (var group in groups)
            {
                group.PropertyChanged += GroupChanged;
                Groups.Add(group);
            }

            UpdateSummary();
            StatusMessage = Loc.T("TexDup_ScanDone", candidates.Count, groups.Count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("TexDup_Idle");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            LogHelper.Log($"Texture duplicate scan failed: {ex.Message}", LogType.Error);
        }
        finally
        {
            ProgressVisibility = Visibility.Collapsed;
            ScanButton.IsEnabled = true;
        }
    }

    public void UpdateSummary()
    {
        var selected = Groups.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SummaryText = Groups.Count == 0 ? Loc.T("TexDup_NoDuplicates") : Loc.T("TexDup_SummaryEmpty");
            return;
        }

        SummaryText = Loc.T(
            "TexDup_Summary",
            selected.Count,
            selected.Sum(g => g.Count),
            TextureSizeHelper.FormatBytesAsMegabytes(selected.Sum(g => g.SharableBytes)));
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
        if (IsExternalProject)
        {
            CustomMessageBox.Show(Loc.T("TexDup_ExternalDisabled"), Loc.T("TexDup_Title"),
                CustomMessageBox.CustomMessageBoxButtons.OKOnly, CustomMessageBox.CustomMessageBoxIcon.Warning);
            return;
        }

        var selected = Groups.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            CustomMessageBox.Show(Loc.T("TexDup_NothingSelected"));
            return;
        }

        string assetsPath;
        try
        {
            assetsPath = FileHelper.GetProjectAssetsPath();
        }
        catch (Exception ex)
        {
            CustomMessageBox.Show(ex.Message, Loc.T("TexDup_Title"),
                CustomMessageBox.CustomMessageBoxButtons.OKOnly, CustomMessageBox.CustomMessageBoxIcon.Warning);
            return;
        }

        var result = TextureDuplicateDetector.ShareFiles(selected, CollectAllProjectTextures(), assetsPath);

        foreach (var texture in result.ChangedTextures)
        {
            _ = texture.LoadDetails();
        }

        SaveHelper.SetUnsavedChanges(true);

        var message = Loc.T(
            "TexDup_Shared",
            result.GroupCount,
            result.RepointedTextures,
            result.DeletedFiles,
            TextureSizeHelper.FormatBytesAsMegabytes(result.FreedBytes));

        LogHelper.Log(message);
        CustomMessageBox.Show(message + Environment.NewLine + Loc.T("TexDup_ShareNote"));

        StatusMessage = message;
        Groups.Clear();
        SummaryText = Loc.T("TexDup_SummaryEmpty");
    }

    private void RemoveVariations_Click(object sender, RoutedEventArgs e)
    {
        var selected = Groups.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            CustomMessageBox.Show(Loc.T("TexDup_NothingSelected"));
            return;
        }

        var removals = TextureDuplicateDetector.PlanVariationRemovals(selected);
        if (removals.Count == 0)
        {
            CustomMessageBox.Show(Loc.T("TexDup_NoVariationsToRemove"));
            return;
        }

        // Deleting a variation changes the texture count of the drawable, so it changes the
        // in-game texture numbers and the shop meta. The user has to see exactly what goes away.
        var preview = string.Join(Environment.NewLine, removals.Take(30).Select(r => "  " + r.Description));
        if (removals.Count > 30)
        {
            preview += Environment.NewLine + Loc.T("TexDup_AndMore", removals.Count - 30);
        }

        var question = Loc.T("TexDup_RemoveConfirm", removals.Count)
                       + Environment.NewLine + Environment.NewLine + preview
                       + Environment.NewLine + Environment.NewLine + Loc.T("TexDup_RemoveWarning");

        var answer = CustomMessageBox.Show(question, Loc.T("TexDup_RemoveConfirmTitle"),
            CustomMessageBox.CustomMessageBoxButtons.YesNo, CustomMessageBox.CustomMessageBoxIcon.Warning);

        if (answer != CustomMessageBox.CustomMessageBoxResult.Yes)
        {
            return;
        }

        var removed = TextureDuplicateDetector.RemoveVariations(removals);
        SaveHelper.SetUnsavedChanges(true);

        var message = Loc.T("TexDup_Removed", removed);
        LogHelper.Log(message);
        CustomMessageBox.Show(message);

        StatusMessage = message;
        Groups.Clear();
        SummaryText = Loc.T("TexDup_SummaryEmpty");
    }

    private void GroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextureDuplicateGroup.IsSelected))
        {
            UpdateSummary();
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetSelection(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetSelection(false);

    private void SetSelection(bool value)
    {
        foreach (var group in Groups)
        {
            group.IsSelected = value;
        }

        UpdateSummary();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
