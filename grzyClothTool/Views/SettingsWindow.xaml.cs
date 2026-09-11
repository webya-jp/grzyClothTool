using grzyClothTool.Controls;
using grzyClothTool.Helpers;
using grzyClothTool.Localization;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace grzyClothTool.Views
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : UserControl, INotifyPropertyChanged
    { 
        public static string GTAVPath => CWHelper.GTAVPath;

        public static bool IsDarkMode => Properties.Settings.Default.IsDarkMode;

        public int[] TextureResolutionOptions { get; } = [128, 256, 512, 1024, 2048, 4096];

        private string _mainProjectsFolder;
        public string MainProjectsFolder
        {
            get
            {
                _mainProjectsFolder = PersistentSettingsHelper.Instance.MainProjectsFolder;
                return _mainProjectsFolder;
            }
            set
            {
                if (_mainProjectsFolder != value)
                {
                    _mainProjectsFolder = value;
                    PersistentSettingsHelper.Instance.MainProjectsFolder = value;
                    OnPropertyChanged(nameof(MainProjectsFolder));
                }
            }
        }


        public static IReadOnlyList<LanguageOption> LanguageOptions => LocalizationManager.LanguageOptions;

        private LanguageOption _selectedLanguage;
        public LanguageOption SelectedLanguage
        {
            get
            {
                _selectedLanguage ??= LanguageOptions.FirstOrDefault(o => o.Language == LocalizationManager.Instance.CurrentLanguage)
                                      ?? LanguageOptions[0];
                return _selectedLanguage;
            }
            set
            {
                if (value != null && _selectedLanguage != value)
                {
                    _selectedLanguage = value;
                    PersistentSettingsHelper.Instance.Language = value.Language.ToString();
                    LocalizationManager.Instance.SetLanguage(value.Language);
                    OnPropertyChanged(nameof(SelectedLanguage));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public SettingsWindow()
        {
            InitializeComponent();
            
            _mainProjectsFolder = PersistentSettingsHelper.Instance.MainProjectsFolder;
            
            DataContext = this;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.AddonManager.Addons.Count > 0)
            {
                MainWindow.NavigationHelper.Navigate("Project");
            }
            else
            {
                MainWindow.NavigationHelper.Navigate("Home");
            }
        }

        private void GTAVPath_Click(object sender, RoutedEventArgs e)
        {
            //get title from e
            var title = e.Source.GetType().GetProperty("Title").GetValue(e.Source).ToString();

            OpenFolderDialog selectedGTAPath = new()
            {
                Title = title,
                Multiselect = false
            };

            if (selectedGTAPath.ShowDialog() == true)
            {
                var exeFilePath = selectedGTAPath.FolderName + "\\GTA5.exe";
                var isPathValid = File.Exists(exeFilePath);

                if (isPathValid && CWHelper.SetGTAFolder(selectedGTAPath.FolderName))
                {
                    OnPropertyChanged(nameof(GTAVPath));
                    LogHelper.Log($"GTA V path set to: {selectedGTAPath.FolderName}", LogType.Info);

                    MainWindow.Instance?.PreviewHost?.RetryInitialization();
                }
                else
                {
                    CustomMessageBox.Show(
                        Loc.T("Settings_InvalidGtavFolderMessage"),
                        Loc.T("Settings_InvalidGtavFolderTitle"),
                        CustomMessageBox.CustomMessageBoxButtons.OKOnly,
                        CustomMessageBox.CustomMessageBoxIcon.Warning);
                }
            }
        }

        private void MainProjectsFolder_Click(object sender, RoutedEventArgs e)
        {
            var title = e.Source.GetType().GetProperty("Title")?.GetValue(e.Source)?.ToString() ?? Loc.T("Settings_SelectMainProjectsFolderFallbackTitle");

            OpenFolderDialog selectedFolder = new()
            {
                Title = title,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(PersistentSettingsHelper.Instance.MainProjectsFolder) && 
                Directory.Exists(PersistentSettingsHelper.Instance.MainProjectsFolder))
            {
                selectedFolder.FolderName = PersistentSettingsHelper.Instance.MainProjectsFolder;
            }

            if (selectedFolder.ShowDialog() == true)
            {
                try
                {
                    if (PersistentSettingsHelper.IsRootDrive(selectedFolder.FolderName))
                    {
                        CustomMessageBox.Show(
                            Loc.T("Settings_RootDriveNotAllowedMessage"),
                            Loc.T("Settings_InvalidFolderTitle"),
                            CustomMessageBox.CustomMessageBoxButtons.OKOnly,
                            CustomMessageBox.CustomMessageBoxIcon.Warning);
                        return;
                    }

                    if (!Directory.Exists(selectedFolder.FolderName))
                    {
                        Directory.CreateDirectory(selectedFolder.FolderName);
                    }

                    string testFile = Path.Combine(selectedFolder.FolderName, ".grzyClothTool_test");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);

                    MainProjectsFolder = selectedFolder.FolderName;
                    LogHelper.Log($"Main projects folder updated to: {selectedFolder.FolderName}", LogType.Info);
                }
                catch (UnauthorizedAccessException)
                {
                    CustomMessageBox.Show(
                        Loc.T("Settings_AccessDeniedMessage"),
                        Loc.T("Common_Error"),
                        CustomMessageBox.CustomMessageBoxButtons.OKOnly,
                        CustomMessageBox.CustomMessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show(
                        Loc.T("Settings_SetMainProjectsFolderErrorMessage", ex.Message),
                        Loc.T("Common_Error"),
                        CustomMessageBox.CustomMessageBoxButtons.OKOnly,
                        CustomMessageBox.CustomMessageBoxIcon.Error);
                }
            }
        }

        public void PatreonAccount_Click(object sender, RoutedEventArgs e)
        {
            var accountsWindow = new AccountsWindow();
            accountsWindow.ShowDialog();
        }

        public void ThemeModeChange_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggleButton && toggleButton.IsChecked.HasValue)
            {
                var value = (bool)toggleButton.IsChecked;
                App.ChangeTheme(value);

                Properties.Settings.Default.IsDarkMode = value;
                Properties.Settings.Default.Save();
            }
        }

    }
}
