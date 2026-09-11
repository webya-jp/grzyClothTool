using grzyClothTool.Constants;
using grzyClothTool.Helpers;
using grzyClothTool.Localization;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static grzyClothTool.Controls.CustomMessageBox;

namespace grzyClothTool.Views
{
    /// <summary>
    /// Interaction logic for Home.xaml
    /// </summary>
    public partial class Home : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private List<string> _patreonList;
        public List<string> PatreonList
        {
            get => _patreonList;
            set
            {
                _patreonList = value;
                OnPropertyChanged(nameof(PatreonList));
            }
        }

        private string _latestVersion;
        public string LatestVersion
        {
            get => _latestVersion;
            set
            {
                _latestVersion = value;
                OnPropertyChanged(nameof(LatestVersion));
            }
        }

        private List<string> _changelogHighlights;
        public List<string> ChangelogHighlights
        {
            get => _changelogHighlights;
            set
            {
                _changelogHighlights = value;
                OnPropertyChanged(nameof(ChangelogHighlights));
            }
        }

        private List<ToolInfo> _otherTools;
        public List<ToolInfo> OtherTools
        {
            get => _otherTools;
            set
            {
                _otherTools = value;
                OnPropertyChanged(nameof(OtherTools));
            }
        }

        private ObservableCollection<RecentProject> _recentlyOpened;
        public ObservableCollection<RecentProject> RecentlyOpened
        {
            get => _recentlyOpened;
            set
            {
                _recentlyOpened = value;
                OnPropertyChanged(nameof(RecentlyOpened));
                OnPropertyChanged(nameof(ShowNoRecentProjects));
            }
        }

        public bool ShowNoRecentProjects => RecentlyOpened == null || RecentlyOpened.Count == 0;

        private static readonly string[] didYouKnowKeys = [
            "Home_DidYouKnow1",
            "Home_DidYouKnow2",
            "Home_DidYouKnow3",
            "Home_DidYouKnow4",
            "Home_DidYouKnow5",
            "Home_DidYouKnow6",
            "Home_DidYouKnow7",
            "Home_DidYouKnow8",
            "Home_DidYouKnow9",
        ];

        public string RandomDidYouKnow => Loc.T(didYouKnowKeys[new Random().Next(0, didYouKnowKeys.Length)]);

        public Home()
        {
            InitializeComponent();
            DataContext = this;

            OtherTools = [
                new ToolInfo
                {
                    Name = "grzyOptimizer",
                    Description = Loc.T("Home_ToolOptimizerDesc"),
                    Url = GlobalConstants.GRZY_TOOLS_URL
                },
                new ToolInfo
                {
                    Name = "grzyTattooTool",
                    Description = Loc.T("Home_ToolTattooDesc"),
                    Url = GlobalConstants.GRZY_TOOLS_URL
                }
            ];

            LoadRecentProjects();

            Loaded += Home_Loaded;
        }

        private void LoadRecentProjects()
        {
            var recentProjects = PersistentSettingsHelper.Instance.RecentlyOpenedProjects;
            var validProjects = recentProjects.Where(p => File.Exists(p.FilePath)).ToList();
            
            if (validProjects.Count != recentProjects.Count)
            {
                PersistentSettingsHelper.Instance.RecentlyOpenedProjects = validProjects;
            }
            
            RecentlyOpened = new ObservableCollection<RecentProject>(validProjects);
        }

        private async void Home_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await FetchPatreons();
            } 
            catch
            {
                PatreonList = [Loc.T("Home_FailedFetchPatreons")];
            }

            try
            {
                await FetchLatestRelease();
            }
            catch
            {
                LatestVersion = Loc.T("Home_UnableFetchVersion");
                ChangelogHighlights = [Loc.T("Home_FailedLoadChangelog")];
            }
        }

        private async Task FetchPatreons()
        {
            var url = $"{GlobalConstants.GRZY_TOOLS_URL}/grzyClothTool/patreons";

            var response = await App.httpClient.GetAsync(url).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                PatreonList = JsonSerializer.Deserialize<List<string>>(content);
            }
        }

        private async Task FetchLatestRelease()
        {
            var url = "https://api.github.com/repos/grzybeek/grzyClothTool/releases/latest";

            App.httpClient.DefaultRequestHeaders.UserAgent.Clear();
            App.httpClient.DefaultRequestHeaders.Add("User-Agent", "grzyClothTool");

            var response = await App.httpClient.GetAsync(url).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var release = JsonSerializer.Deserialize<JsonElement>(content);
                
                await Dispatcher.InvokeAsync(() =>
                {
                    if (release.TryGetProperty("tag_name", out var tagName))
                    {
                        LatestVersion = tagName.GetString();
                    }

                    if (release.TryGetProperty("body", out var body))
                    {
                        ChangelogHighlights = ParseChangelogHighlights(body.GetString());
                    }
                });
            }
        }

        private static List<string> ParseChangelogHighlights(string changelogBody)
        {
            if (string.IsNullOrWhiteSpace(changelogBody))
                return [Loc.T("Home_NoChangelogAvailable")];

            var highlights = new List<string>();
            var lines = changelogBody.Split(['\r', '\n'], StringSplitOptions.None);
            var inChangelogSection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Contains("Changelog", StringComparison.OrdinalIgnoreCase))
                {
                    inChangelogSection = true;
                    continue;
                }

                if (inChangelogSection && string.IsNullOrWhiteSpace(trimmedLine))
                {
                    continue;
                }

                if (inChangelogSection && trimmedLine.StartsWith("##"))
                {
                    break;
                }

                if (inChangelogSection &&
                    (trimmedLine.StartsWith("-") || trimmedLine.StartsWith("*") || trimmedLine.StartsWith("•")))
                {
                    var cleanLine = trimmedLine.TrimStart('-', '*', '•', ' ').Trim();
                    if (!string.IsNullOrWhiteSpace(cleanLine))
                    {
                        highlights.Add(cleanLine);

                        if (highlights.Count >= 10)
                            break;
                    }
                }
            }

            return highlights.Count > 0 ? highlights : [Loc.T("Home_SeeFullChangelog")];
        }

        private void ViewChangelog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/grzybeek/grzyClothTool/releases",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("Home_FailedOpenChangelog", ex.Message), Loc.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenAllTools_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = GlobalConstants.GRZY_TOOLS_URL,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("Home_FailedOpenWebsite", ex.Message), Loc.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenToolUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is string url)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.T("Home_FailedOpenUrl", ex.Message), Loc.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void CreateNew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mainProjectsFolder = PersistentSettingsHelper.Instance.MainProjectsFolder;
                if (string.IsNullOrEmpty(mainProjectsFolder))
                {
                    Show(Loc.T("Home_ConfigureMainFolderMsg"), 
                         Loc.T("Home_ConfigurationRequiredTitle"), 
                         CustomMessageBoxButtons.OKOnly, 
                         CustomMessageBoxIcon.Warning);
                    return;
                }

                if (!Directory.Exists(mainProjectsFolder))
                {
                    Show(Loc.T("Home_MainFolderNotExistMsg", mainProjectsFolder), 
                         Loc.T("Home_FolderNotFoundTitle"), 
                         CustomMessageBoxButtons.OKOnly, 
                         CustomMessageBoxIcon.Warning);
                    return;
                }

                var dialog = ProjectSetupDialog.ShowForNewProject(Window.GetWindow(this));
                if (!dialog.Confirmed)
                {
                    return;
                }

                var projectName = dialog.ProjectName.Trim();
                var isExternal = !dialog.IsSelfContained;

                if (projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    Show(Loc.T("Home_InvalidProjectNameMsg"), 
                         Loc.T("Home_InvalidNameTitle"), 
                         CustomMessageBoxButtons.OKOnly, 
                         CustomMessageBoxIcon.Warning);
                    return;
                }

                var projectFolder = Path.Combine(mainProjectsFolder, projectName);

                if (Directory.Exists(projectFolder))
                {
                    ClearProjectFolder(projectFolder);
                }

                Directory.CreateDirectory(projectFolder);
                if (!isExternal)
                {
                    var assetsFolder = Path.Combine(projectFolder, GlobalConstants.ASSETS_FOLDER_NAME);
                    Directory.CreateDirectory(assetsFolder);
                }

                MainWindow.AddonManager.Addons.Clear();
                MainWindow.AddonManager.Groups.Clear();
                MainWindow.AddonManager.Tags.Clear();
                
                MainWindow.AddonManager.ProjectName = projectName;
                MainWindow.AddonManager.IsExternalProject = isExternal;
                MainWindow.AddonManager.CreateAddon();

                SaveHelper.SetUnsavedChanges(true);
                await SaveHelper.SaveAsync();

                var saveFileName = SaveHelper.GetSaveFileName(isExternal);
                var newProjectAutoSavePath = Path.Combine(projectFolder, saveFileName);
                PersistentSettingsHelper.Instance.AddRecentProject(
                    newProjectAutoSavePath,
                    projectName,
                    drawableCount: 0,
                    addonCount: 1,
                    isExternal: isExternal
                );
                
                LoadRecentProjects();

                var projectType = isExternal ? "External" : "Self-contained";
                LogHelper.Log($"Created new {projectType} project: {projectName} at {projectFolder}");
                MainWindow.NavigationHelper.Navigate("Project");
            }
            catch (Exception ex)
            {
                LogHelper.Log($"Failed to create new project: {ex.Message}", Views.LogType.Error);
                Show(Loc.T("Home_FailedCreateProject", ex.Message), 
                     Loc.T("Common_Error"), 
                     CustomMessageBoxButtons.OKOnly, 
                     CustomMessageBoxIcon.Error);
            }
        }

        private async void OpenAddon_Click(object sender, RoutedEventArgs e)
        {
            var success = await MainWindow.Instance.OpenAddonAsync(true);
            if (success)
            {
                MainWindow.NavigationHelper.Navigate("Project");
            }
        }

        private async void ImportProject_Click(object sender, RoutedEventArgs e)
        {
            var success = await MainWindow.Instance.ImportProjectAsync(true);
            if (success)
            {
                MainWindow.NavigationHelper.Navigate("Project");
            }
        }

        private async void OpenSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new()
                {
                    Title = Loc.T("Home_OpenSaveDialogTitle"),
                    Filter = Loc.T("Home_OpenSaveDialogFilter"),
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    if (!SaveHelper.CheckUnsavedChangesMessage())
                    {
                        return;
                    }

                    await SaveHelper.LoadSaveFileAsync(openFileDialog.FileName);
                    LoadRecentProjects();
                    MainWindow.NavigationHelper.Navigate("Project");
                }
            }
            catch (Exception ex)
            {
                Show(Loc.T("Home_FailedLoadSave", ex.Message), 
                     Loc.T("Common_Error"), 
                     CustomMessageBoxButtons.OKOnly, 
                     CustomMessageBoxIcon.Error);
            }
        }

        private async void RecentProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string filePath)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        Show(Loc.T("Home_SaveFileGoneMsg"), 
                             Loc.T("Home_FileNotFoundTitle"), 
                             CustomMessageBoxButtons.OKOnly, 
                             CustomMessageBoxIcon.Warning);
                        
                        var recentProjects = PersistentSettingsHelper.Instance.RecentlyOpenedProjects;
                        recentProjects.RemoveAll(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                        PersistentSettingsHelper.Instance.RecentlyOpenedProjects = recentProjects;
                        LoadRecentProjects();
                        return;
                    }

                    if (!SaveHelper.CheckUnsavedChangesMessage())
                    {
                        return;
                    }

                    await SaveHelper.LoadSaveFileAsync(filePath);
                    LoadRecentProjects();
                    MainWindow.NavigationHelper.Navigate("Project");
                }
                catch (Exception ex)
                {
                    Show(Loc.T("Home_FailedLoadSave", ex.Message), 
                         Loc.T("Common_Error"), 
                         CustomMessageBoxButtons.OKOnly, 
                         CustomMessageBoxIcon.Error);
                }
            }
        }

        private void RemoveRecentProject_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            
            if (sender is Button button && button.Tag is string filePath)
            {
                try
                {
                    var project = PersistentSettingsHelper.Instance.RecentlyOpenedProjects
                        .FirstOrDefault(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                    
                    var projectName = project?.ProjectName ?? "";
                    
                    var recentProjects = PersistentSettingsHelper.Instance.RecentlyOpenedProjects;
                    recentProjects.RemoveAll(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                    PersistentSettingsHelper.Instance.RecentlyOpenedProjects = recentProjects;
                    
                    LoadRecentProjects();
                    
                    LogHelper.Log($"Removed project from recent list: {projectName}");
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"Failed to remove recent project: {ex.Message}", Views.LogType.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.MainWindow.Close();
        }


        private static void ClearProjectFolder(string projectFolder)
        {
            try
            {
                var saveFiles = new[] 
                { 
                    Path.Combine(projectFolder, SaveHelper.AutoSaveFileName),
                    Path.Combine(projectFolder, SaveHelper.AutoSaveExternalFileName)
                };

                foreach (var saveFile in saveFiles)
                {
                    if (File.Exists(saveFile))
                    {
                        File.Delete(saveFile);
                        LogHelper.Log($"Deleted old save file: {saveFile}");
                    }
                }

                var backupFolder = Path.Combine(projectFolder, "save-backups");
                if (Directory.Exists(backupFolder))
                {
                    Directory.Delete(backupFolder, recursive: true);
                    LogHelper.Log($"Deleted old save backups folder: {backupFolder}");
                }

                var assetsFolder = Path.Combine(projectFolder, GlobalConstants.ASSETS_FOLDER_NAME);
                if (Directory.Exists(assetsFolder))
                {
                    Directory.Delete(assetsFolder, recursive: true);
                    LogHelper.Log($"Deleted old assets folder: {assetsFolder}");
                }

                LogHelper.Log($"Cleared project folder for overwrite: {projectFolder}");
            }
            catch (Exception ex)
            {
                LogHelper.Log($"Warning: Could not fully clear project folder: {ex.Message}", LogType.Warning);
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ToolInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
    }
}
