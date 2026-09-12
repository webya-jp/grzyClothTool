using grzyClothTool.Constants;
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


        /// <summary>
        /// Presets for the per-addon component limit. Props are not covered by FiveM's
        /// component sync patch, so their limit stays at 128 and is not part of this choice.
        /// </summary>
        public enum DrawableLimitPresetOption
        {
            Compatible128,
            Extended255,
            Custom
        }

        // Remembers an explicit Custom choice. Without it, picking Custom while the number
        // happens to equal a preset value would immediately snap back to that preset.
        private bool _customLimitChosen;

        public DrawableLimitPresetOption DrawableLimitPreset
        {
            get => _customLimitChosen
                ? DrawableLimitPresetOption.Custom
                : SettingsHelper.Instance.MaxDrawableNumber switch
                {
                    127 => DrawableLimitPresetOption.Compatible128,
                    GlobalConstants.MAX_DRAWABLE_NUMBER_LIMIT => DrawableLimitPresetOption.Extended255,
                    _ => DrawableLimitPresetOption.Custom
                };
            set
            {
                _customLimitChosen = value == DrawableLimitPresetOption.Custom;

                switch (value)
                {
                    case DrawableLimitPresetOption.Compatible128:
                        SettingsHelper.Instance.MaxDrawableNumber = 127;
                        break;
                    case DrawableLimitPresetOption.Extended255:
                        SettingsHelper.Instance.MaxDrawableNumber = GlobalConstants.MAX_DRAWABLE_NUMBER_LIMIT;
                        break;
                    case DrawableLimitPresetOption.Custom:
                        // keep whatever number is configured; the text box becomes editable
                        break;
                }

                RefreshDrawableLimitState();
            }
        }

        /// <summary>Shows the free-form number box only while the Custom preset is selected.</summary>
        public bool IsCustomDrawableLimit => DrawableLimitPreset == DrawableLimitPresetOption.Custom;

        /// <summary>Warns as soon as the limit goes past what an unpatched client can sync.</summary>
        public bool ShowExtendedLimitWarning => SettingsHelper.Instance.MaxDrawableNumber > 127;

        private void RefreshDrawableLimitState()
        {
            OnPropertyChanged(nameof(DrawableLimitPreset));
            OnPropertyChanged(nameof(IsCustomDrawableLimit));
            OnPropertyChanged(nameof(ShowExtendedLimitWarning));
        }

        public static IReadOnlyList<LanguageOption> LanguageOptions => LocalizationManager.LanguageOptions;

        private LanguageOption _selectedLanguage;
        public LanguageOption SelectedLanguage
        {
            get
            {
                _selectedLanguage ??= LanguageOptions.FirstOrDefault(o => o.Code == LocalizationManager.Instance.CurrentLanguageCode)
                                      ?? LanguageOptions[0];
                return _selectedLanguage;
            }
            set
            {
                if (value != null && _selectedLanguage != value)
                {
                    _selectedLanguage = value;
                    PersistentSettingsHelper.Instance.Language = value.Code;
                    LocalizationManager.Instance.SetLanguage(value.Code);
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

            // keep the preset radio buttons and the warning in sync when the number is edited directly
            SettingsHelper.Instance.PropertyChanged += SettingsHelper_PropertyChanged;
            Unloaded += (_, _) => SettingsHelper.Instance.PropertyChanged -= SettingsHelper_PropertyChanged;

            DataContext = this;
        }

        private void SettingsHelper_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsHelper.MaxDrawableNumber))
            {
                RefreshDrawableLimitState();
            }
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
