using System;
using System.Windows;

namespace DEMBuilder.Pages
{
    // Class to hold the data for the FolderSelected event
    public class FolderSelectedEventArgs : EventArgs
    {
        public required string FolderPath { get; init; }
        public required bool IncludeSubfolders { get; init; }
    }

    public partial class LoadDataPage : System.Windows.Controls.UserControl
    {
        // Event to notify the main window when a folder has been selected.
        public event EventHandler<FolderSelectedEventArgs>? FolderSelected;

        public LoadDataPage()
        { 
            InitializeComponent();
            // Set a default folder path for development convenience.
            // USER: Please replace "C:\Dev\GPS_Data_Default" with your actual desired default path.
            FolderPathTextBox.Text = "C:\\Dev\\GPS_Data_Default"; 
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Note: This uses the Windows Forms FolderBrowserDialog.
            // You may need to add a reference to System.Windows.Forms.dll to your project.
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select the folder containing GPS data files";
                dialog.UseDescriptionForTitle = true;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    FolderPathTextBox.Text = dialog.SelectedPath;
                    // Raise the event to pass the selected path and the checkbox state to the main window.
                    FolderSelected?.Invoke(this, new FolderSelectedEventArgs
                    {
                        FolderPath = dialog.SelectedPath,
                        IncludeSubfolders = IncludeSubfoldersCheckBox.IsChecked ?? false
                    });
                }
            }
        }
    }
}
