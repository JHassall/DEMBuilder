using DEMBuilder.Models;
using DEMBuilder.Services.Export;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TriangleNet.Geometry;

namespace DEMBuilder.Pages
{
    public class RgFdemExportCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public long FileSize { get; set; }
        public TimeSpan ExportTime { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public partial class RgFdemOptionsPage : System.Windows.Controls.UserControl
    {
        public event EventHandler<RgFdemExportCompletedEventArgs>? ExportCompleted;
        public event EventHandler? BackRequested;

        private double[,]? _rasterData;
        private TriangleNet.Geometry.Rectangle? _bounds;
        private double _referenceLatitude;
        private double _referenceLongitude;
        private string _farmName = string.Empty;
        private string _fieldName = string.Empty;
        private CancellationTokenSource? _exportCancellation;

        public RgFdemOptionsPage()
        {
            InitializeComponent();
            UpdatePreview();
            
            // Wire up events for real-time preview updates
            ResolutionTextBox.TextChanged += (s, e) => UpdatePreview();
        }

        public void SetExportData(double[,] rasterData, TriangleNet.Geometry.Rectangle bounds, 
            double referenceLatitude, double referenceLongitude, string farmName, string fieldName)
        {
            _rasterData = rasterData;
            _bounds = bounds;
            _referenceLatitude = referenceLatitude;
            _referenceLongitude = referenceLongitude;
            _farmName = farmName;
            _fieldName = fieldName;
            
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            try
            {
                if (_rasterData == null || _bounds == null)
                {
                    PreviewTextBlock.Text = "No data loaded for preview";
                    FilenamePreviewTextBlock.Text = "";
                    return;
                }

                // Get current settings
                var resolution = GetResolution();

                // Calculate estimated file size
                var width = _rasterData.GetLength(1);
                var height = _rasterData.GetLength(0);
                var pixelCount = width * height;
                
                // Estimate RgF DEM file size (ZIP container with binary data + metadata)
                var binaryDataSize = pixelCount * 4; // Float32 elevation data
                var metadataSize = 2048; // JSON metadata + coordinate system + README
                var estimatedSize = (long)((binaryDataSize + metadataSize) * 0.7); // ZIP compression ~30%

                var fileSizeText = FormatFileSize(estimatedSize);

                // Generate filename
                var date = DateTime.Now.ToString("ddMMyy");
                var filename = $"{_farmName}_{_fieldName}_{date}.RgFdem";
                if (string.IsNullOrEmpty(_farmName) || string.IsNullOrEmpty(_fieldName))
                    filename = $"DEM_Export_{date}.RgFdem";

                // Update preview text
                PreviewTextBlock.Text = $"Estimated file size: ~{fileSizeText} | Single RgF DEM file";
                FilenamePreviewTextBlock.Text = $"Filename: {filename}";
            }
            catch (Exception ex)
            {
                PreviewTextBlock.Text = $"Preview error: {ex.Message}";
                FilenamePreviewTextBlock.Text = "";
            }
        }

        private double GetResolution()
        {
            if (double.TryParse(ResolutionTextBox.Text, out double resolution) && resolution > 0)
                return resolution;
            return 0.25; // Default
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            else
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel any ongoing export
            _exportCancellation?.Cancel();
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_rasterData == null || _bounds == null)
            {
                System.Windows.MessageBox.Show("No DEM data available for export.", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Validate resolution
                var resolution = GetResolution();
                if (resolution <= 0 || resolution > 10)
                {
                    System.Windows.MessageBox.Show("Please enter a valid resolution between 0.01 and 10 meters.", 
                        "Invalid Resolution", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ResolutionTextBox.Focus();
                    return;
                }

                // Show file save dialog
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save RgF DEM File",
                    Filter = "RgF DEM Files (*.RgFdem)|*.RgFdem|All Files (*.*)|*.*",
                    DefaultExt = "RgFdem"
                };

                // Generate default filename
                var date = DateTime.Now.ToString("ddMMyy");
                var defaultName = !string.IsNullOrEmpty(_farmName) && !string.IsNullOrEmpty(_fieldName) 
                    ? $"{_farmName}_{_fieldName}_{date}.RgFdem"
                    : $"DEM_Export_{date}.RgFdem";
                saveDialog.FileName = defaultName;

                if (saveDialog.ShowDialog() != true)
                    return;

                // Disable UI during export
                SetUIEnabled(false);
                ShowProgress("Preparing RgF DEM export...", 0);

                // Create cancellation token
                _exportCancellation = new CancellationTokenSource();

                // Create export service and perform export
                var exportService = new RgFdemExportService();
                var progress = new Progress<RgFdemProgress>(UpdateExportProgress);

                var result = await exportService.ExportRgFdemAsync(
                    _rasterData, _bounds, saveDialog.FileName, resolution,
                    _referenceLatitude, _referenceLongitude, _farmName, _fieldName,
                    progress, _exportCancellation.Token);

                HideProgress();
                SetUIEnabled(true);

                if (result.Success)
                {
                    // Notify completion
                    ExportCompleted?.Invoke(this, new RgFdemExportCompletedEventArgs
                    {
                        Success = true,
                        FilePath = saveDialog.FileName,
                        FileSize = result.FileSizeBytes,
                        ExportTime = result.ExportTime,
                        Message = "Export completed successfully"
                    });
                }
                else
                {
                    System.Windows.MessageBox.Show($"Export failed: {result.ErrorMessage}", "Export Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                HideProgress();
                SetUIEnabled(true);
                System.Windows.MessageBox.Show("Export was cancelled.", "Export Cancelled", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                HideProgress();
                SetUIEnabled(true);
                System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateExportProgress(RgFdemProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                ShowProgress(progress.Message, (int)progress.PercentComplete);
            });
        }

        private void ShowProgress(string message, int percentage)
        {
            ExportProgressBar.Visibility = Visibility.Visible;
            ProgressTextBlock.Visibility = Visibility.Visible;
            ExportProgressBar.Value = percentage;
            ProgressTextBlock.Text = $"{message} ({percentage}%)";
        }

        private void HideProgress()
        {
            ExportProgressBar.Visibility = Visibility.Collapsed;
            ProgressTextBlock.Visibility = Visibility.Collapsed;
        }

        private void SetUIEnabled(bool enabled)
        {
            ResolutionTextBox.IsEnabled = enabled;
            ExportButton.IsEnabled = enabled;
            BackButton.IsEnabled = enabled;
        }
    }
}
