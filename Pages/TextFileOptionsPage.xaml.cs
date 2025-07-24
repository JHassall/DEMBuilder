using DEMBuilder.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TriangleNet.Geometry;

namespace DEMBuilder.Pages
{
    public class TextFileExportCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public long FileSize { get; set; }
        public TimeSpan ExportTime { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public partial class TextFileOptionsPage : System.Windows.Controls.UserControl
    {
        public event EventHandler<TextFileExportCompletedEventArgs>? ExportCompleted;
        public event EventHandler? BackRequested;

        private double[,]? _rasterData;
        private TriangleNet.Geometry.Rectangle? _bounds;
        private double _referenceLatitude;
        private double _referenceLongitude;
        private string _farmName = string.Empty;
        private string _fieldName = string.Empty;
        private CancellationTokenSource? _exportCancellation;

        public TextFileOptionsPage()
        {
            InitializeComponent();
            UpdatePreview();
            
            // Wire up events for real-time preview updates
            AsciiGridRadioButton.Checked += (s, e) => UpdatePreview();
            PlainTextRadioButton.Checked += (s, e) => UpdatePreview();
            ResolutionTextBox.TextChanged += (s, e) => UpdatePreview();
            IncludeHeaderCheckBox.Checked += (s, e) => UpdatePreview();
            IncludeHeaderCheckBox.Unchecked += (s, e) => UpdatePreview();
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
                var isAsciiGrid = AsciiGridRadioButton.IsChecked == true;
                var includeHeader = IncludeHeaderCheckBox.IsChecked == true;

                // Calculate estimated file size
                var width = _rasterData.GetLength(1);
                var height = _rasterData.GetLength(0);
                var pixelCount = width * height;
                
                // Estimate bytes per value (including spaces and newlines)
                var bytesPerValue = isAsciiGrid ? 8 : 25; // ASCII grid is more compact
                var headerSize = includeHeader ? 200 : 0;
                var estimatedSize = (pixelCount * bytesPerValue) + headerSize;

                var fileSizeText = FormatFileSize(estimatedSize);

                // Generate filename
                var date = DateTime.Now.ToString("ddMMyy");
                var extension = isAsciiGrid ? ".asc" : ".txt";
                var filename = $"{_farmName}_{_fieldName}_{date}{extension}";
                if (string.IsNullOrEmpty(_farmName) || string.IsNullOrEmpty(_fieldName))
                    filename = $"DEM_Export_{date}{extension}";

                // Update preview text
                var formatText = isAsciiGrid ? "ASCII Grid" : "Plain Text";
                if (includeHeader) formatText += " with header";
                
                PreviewTextBlock.Text = $"Estimated file size: ~{fileSizeText} | Format: {formatText}";
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel the ongoing export
            _exportCancellation?.Cancel();
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
                var isAsciiGrid = AsciiGridRadioButton.IsChecked == true;
                var filter = isAsciiGrid ? "ASCII Grid Files (*.asc)|*.asc|All Files (*.*)|*.*" 
                                        : "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                var defaultExt = isAsciiGrid ? "asc" : "txt";

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Text File",
                    Filter = filter,
                    DefaultExt = defaultExt
                };

                // Generate default filename
                var date = DateTime.Now.ToString("ddMMyy");
                var extension = isAsciiGrid ? ".asc" : ".txt";
                var defaultName = !string.IsNullOrEmpty(_farmName) && !string.IsNullOrEmpty(_fieldName) 
                    ? $"{_farmName}_{_fieldName}_{date}{extension}"
                    : $"DEM_Export_{date}{extension}";
                saveDialog.FileName = defaultName;

                if (saveDialog.ShowDialog() != true)
                    return;

                // Disable UI during export
                SetUIEnabled(false);
                ShowProgress("Preparing text file export...", 0);

                // Create cancellation token
                _exportCancellation = new CancellationTokenSource();

                // Perform export
                var startTime = DateTime.Now;
                await ExportTextFileAsync(saveDialog.FileName, _exportCancellation.Token);
                var exportTime = DateTime.Now - startTime;

                HideProgress();
                SetUIEnabled(true);

                // Get file size
                var fileInfo = new FileInfo(saveDialog.FileName);
                var fileSize = fileInfo.Exists ? fileInfo.Length : 0;

                // Notify completion
                ExportCompleted?.Invoke(this, new TextFileExportCompletedEventArgs
                {
                    Success = true,
                    FilePath = saveDialog.FileName,
                    FileSize = fileSize,
                    ExportTime = exportTime,
                    Message = $"Text file exported successfully in {exportTime.TotalSeconds:F1} seconds"
                });
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

        private async Task ExportTextFileAsync(string filePath, CancellationToken cancellationToken)
        {
            if (_rasterData == null || _bounds == null) return;

            // Capture UI state on UI thread before Task.Run
            var isAsciiGrid = AsciiGridRadioButton.IsChecked == true;
            var includeHeader = IncludeHeaderCheckBox.IsChecked == true;
            var useNoData = IncludeNoDataCheckBox.IsChecked == true;
            var resolution = GetResolution();

            await Task.Run(() =>
            {
                using var writer = new StreamWriter(filePath);
                var width = _rasterData.GetLength(1);
                var height = _rasterData.GetLength(0);

                if (isAsciiGrid && includeHeader)
                {
                    // Write ASCII Grid header
                    writer.WriteLine($"ncols {width}");
                    writer.WriteLine($"nrows {height}");
                    writer.WriteLine($"xllcorner {_bounds.Left:F6}");
                    writer.WriteLine($"yllcorner {_bounds.Bottom:F6}");
                    writer.WriteLine($"cellsize {resolution:F6}");
                    if (useNoData)
                        writer.WriteLine("NODATA_value -9999");
                }

                // Write data
                for (int row = 0; row < height; row++)
                {
                    ShowProgress($"Writing data row {row + 1} of {height}...", 
                        (int)((row / (double)height) * 100));

                    if (isAsciiGrid)
                    {
                        // ASCII Grid format - space-separated values
                        for (int col = 0; col < width; col++)
                        {
                            var value = _rasterData[row, col];
                            if (double.IsNaN(value) && useNoData)
                                writer.Write("-9999");
                            else
                                writer.Write($"{value:F3}");
                            
                            if (col < width - 1) writer.Write(" ");
                        }
                        writer.WriteLine();
                    }
                    else
                    {
                        // Plain text format - X Y Z coordinates
                        for (int col = 0; col < width; col++)
                        {
                            var x = _bounds.Left + (col * resolution);
                            var y = _bounds.Top - (row * resolution);
                            var z = _rasterData[row, col];
                            
                            if (!double.IsNaN(z) || !useNoData)
                            {
                                writer.WriteLine($"{x:F6} {y:F6} {z:F3}");
                            }
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }, cancellationToken);
        }

        private void ShowProgress(string message, int percentage)
        {
            Dispatcher.Invoke(() =>
            {
                ExportProgressBar.Visibility = Visibility.Visible;
                ProgressTextBlock.Visibility = Visibility.Visible;
                ExportProgressBar.Value = percentage;
                ProgressTextBlock.Text = $"{message} ({percentage}%)";
            });
        }

        private void HideProgress()
        {
            ExportProgressBar.Visibility = Visibility.Collapsed;
            ProgressTextBlock.Visibility = Visibility.Collapsed;
        }

        private void SetUIEnabled(bool enabled)
        {
            AsciiGridRadioButton.IsEnabled = enabled;
            PlainTextRadioButton.IsEnabled = enabled;
            ResolutionTextBox.IsEnabled = enabled;
            IncludeHeaderCheckBox.IsEnabled = enabled;
            IncludeNoDataCheckBox.IsEnabled = enabled;
            ExportButton.IsEnabled = enabled;
            BackButton.IsEnabled = enabled;
            
            // Show Cancel button during export, hide when not exporting
            CancelButton.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
