using System;
using System.IO;
using System.Windows;
using DEMBuilder.Dialogs;
using System.Windows.Interop;
using DEMBuilder.Services;

namespace DEMBuilder.Pages
{
    // Class to hold the data for the FolderSelected event
    public class GpsDataLoadedEventArgs : EventArgs
    {
        public required List<Models.GpsPoint> GpsPoints { get; init; }
        public int DuplicatesSkipped { get; init; }
        public int TotalPointsProcessed { get; init; }
        public bool DuplicateDetectionEnabled { get; init; }
    }

    public partial class LoadDataPage : System.Windows.Controls.UserControl
    {
        // Event to notify the main window when a folder has been selected.
        public event EventHandler<GpsDataLoadedEventArgs>? GpsDataLoaded;

        private string? _selectedFolderPath;
        
        /// <summary>
        /// Existing GPS points in the database for duplicate detection
        /// </summary>
        public List<Models.GpsPoint>? ExistingGpsPoints { get; set; }

        public LoadDataPage()
        {
            InitializeComponent();
            // Set a default folder path for development convenience.
            // USER: Please replace "C:\\Dev\\GPS_Data_Default" with your actual desired default path.
            FolderPathTextBox.Text = "C:\\Dev\\GPS_Data_Default";
        }

                private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderSelectDialog
            {
                InitialDirectory = FolderPathTextBox.Text,
                Title = "Select Folder"
            };

            var window = System.Windows.Window.GetWindow(this);
            var handle = new WindowInteropHelper(window).Handle;

            if (dialog.ShowDialog(handle))
            {
                _selectedFolderPath = dialog.SelectedPath;
                ProgressPanel.Visibility = Visibility.Collapsed;

                if (string.IsNullOrEmpty(_selectedFolderPath))
                {
                    System.Windows.MessageBox.Show("Could not determine the folder path.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                FolderPathTextBox.Text = _selectedFolderPath!;

                FileCountTextBlock.Text = "Folder selected. Ready to import.";
                ImportPanel.Visibility = Visibility.Visible;
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFolderPath))
            {
                System.Windows.MessageBox.Show("Please select a folder first.", "No Folder Selected", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ImportPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            ImportProgressBar.Value = 0;
            ProgressFileText.Text = "Preparing to import...";
            ProgressPointText.Text = "";

            var parser = new NmeaParserService();
            var includeSubfolders = IncludeSubfoldersCheckBox.IsChecked ?? false;

            var progress = new Progress<ImportProgress>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    ImportProgressBar.Maximum = p.TotalBytes;
                    ImportProgressBar.Value = p.BytesProcessed;
                }
                ProgressFileText.Text = p.CurrentFile;
                ProgressPointText.Text = $"{p.PointsFound:N0} points found";
                ProgressFileCountText.Text = $"{p.FilesProcessed} of {p.TotalFiles} files";
            });

            try
            {
                var points = await parser.ParseFolderAsync(_selectedFolderPath, includeSubfolders, progress);
                
                // Apply duplicate detection if enabled
                var skipDuplicates = SkipDuplicatesCheckBox.IsChecked ?? false;
                var finalPoints = points;
                var duplicatesSkipped = 0;
                var totalProcessed = points.Count;
                
                if (skipDuplicates && ExistingGpsPoints != null && ExistingGpsPoints.Count > 0)
                {
                    ProgressFileText.Text = "Checking for duplicate GPS points...";
                    ProgressPointText.Text = "Starting duplicate detection...";
                    
                    var duplicateDetector = new DuplicateDetectionService();
                    
                    // Create progress reporter for real-time duplicate count updates
                    var duplicateProgress = new Progress<(int processed, int duplicatesFound)>(p =>
                    {
                        ProgressFileText.Text = $"Checking duplicates: {p.processed:N0} of {points.Count:N0} points processed";
                        ProgressPointText.Text = $"Duplicates found: {p.duplicatesFound:N0} | Unique: {p.processed - p.duplicatesFound:N0}";
                    });
                    
                    var duplicateResult = duplicateDetector.FilterDuplicates(ExistingGpsPoints, points, duplicateProgress);
                    
                    finalPoints = duplicateResult.UniquePoints;
                    duplicatesSkipped = duplicateResult.DuplicatesFound;
                    
                    ProgressFileText.Text = $"Duplicate detection complete: {duplicatesSkipped:N0} duplicates skipped";
                    ProgressPointText.Text = $"{finalPoints.Count:N0} unique points ready for import";
                }
                
                GpsDataLoaded?.Invoke(this, new GpsDataLoadedEventArgs 
                { 
                    GpsPoints = finalPoints,
                    DuplicatesSkipped = duplicatesSkipped,
                    TotalPointsProcessed = totalProcessed,
                    DuplicateDetectionEnabled = skipDuplicates
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"An error occurred during import: {ex.Message}", "Import Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                ProgressPanel.Visibility = Visibility.Collapsed;
                ImportPanel.Visibility = Visibility.Visible;
            }
        }
    }
}
