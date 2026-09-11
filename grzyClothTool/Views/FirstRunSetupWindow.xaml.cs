using grzyClothTool.Helpers;
using grzyClothTool.Localization;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace grzyClothTool.Views
{
    /// <summary>
    /// Interaction logic for FirstRunSetupWindow.xaml
    /// </summary>
    public partial class FirstRunSetupWindow : Window
    {
        public bool SetupCompleted { get; private set; }

        public FirstRunSetupWindow()
        {
            InitializeComponent();
            
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string defaultFolder = Path.Combine(documentsPath, "grzyClothTool Projects");
            FolderPathTextBox.Text = defaultFolder;
            ContinueButton.IsEnabled = true;

            Closing += FirstRunSetupWindow_Closing;
        }

        private void FirstRunSetupWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!SetupCompleted)
            {
                var result = System.Windows.MessageBox.Show(
                    Loc.T("FirstRun_ExitConfirmMessage"),
                    Loc.T("FirstRun_ExitConfirmCaption"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
                else
                {
                    // User wants to exit the application entirely
                    System.Windows.Application.Current.Shutdown();
                }
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = Loc.T("FirstRun_BrowseDialogDescription");
            dialog.ShowNewFolderButton = true;

            if (!string.IsNullOrWhiteSpace(FolderPathTextBox.Text) && Directory.Exists(FolderPathTextBox.Text))
            {
                dialog.SelectedPath = FolderPathTextBox.Text;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FolderPathTextBox.Text = dialog.SelectedPath;
                ValidationMessage.Visibility = Visibility.Collapsed;
                ContinueButton.IsEnabled = true;
            }
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedPath = FolderPathTextBox.Text;

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                ValidationMessage.Text = Loc.T("FirstRun_ValidationNoFolder");
                ValidationMessage.Visibility = Visibility.Visible;
                return;
            }

            if (PersistentSettingsHelper.IsRootDrive(selectedPath))
            {
                ValidationMessage.Text = Loc.T("FirstRun_ValidationRootDrive");
                ValidationMessage.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                if (!Directory.Exists(selectedPath))
                {
                    Directory.CreateDirectory(selectedPath);
                }

                string testFile = Path.Combine(selectedPath, ".grzyClothTool_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                PersistentSettingsHelper.Instance.MainProjectsFolder = selectedPath;
                PersistentSettingsHelper.Instance.IsFirstRun = false;

                SetupCompleted = true;
                DialogResult = true;
                Close();
            }
            catch (UnauthorizedAccessException)
            {
                ValidationMessage.Text = Loc.T("FirstRun_ValidationAccessDenied");
                ValidationMessage.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ValidationMessage.Text = Loc.T("FirstRun_ValidationError", ex.Message);
                ValidationMessage.Visibility = Visibility.Visible;
            }
        }
    }
}
