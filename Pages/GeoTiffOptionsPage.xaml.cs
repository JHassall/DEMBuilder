using DEMBuilder.Models;
using DEMBuilder.Services.Export;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DEMBuilder.Pages
{
    public partial class GeoTiffOptionsPage : System.Windows.Controls.UserControl
    {
        public event EventHandler<GeoTiffExportCompletedEventArgs>? ExportCompleted;
        public event EventHandler? BackRequested;

        private List<GpsPoint>? _gpsPoints;
        private double _referenceLatitude;
        private double _referenceLongitude;
        private string _farmName = string.Empty;
        private string _fieldName = string.Empty;
        private CancellationTokenSource? _exportCancellation;

        public GeoTiffOptionsPage()
        {
            InitializeComponent();
            UpdatePreview();
            
            // Wire up events for real-time preview updates
            UtmRadioButton.Checked += (s, e) => UpdatePreview();
            Wgs84RadioButton.Checked += (s, e) => UpdatePreview();
            LocalRadioButton.Checked += (s, e) => UpdatePreview();
            SingleFileRadioButton.Checked += (s, e) => UpdatePreview();
            TiledRadioButton.Checked += (s, e) => UpdatePreview();
            CompressionCheckBox.Checked += (s, e) => UpdatePreview();
            CompressionCheckBox.Unchecked += (s, e) => UpdatePreview();
            ResolutionTextBox.TextChanged += (s, e) => UpdatePreview();
        }

        public void SetExportData(List<GpsPoint> gpsPoints, 
            double referenceLatitude, double referenceLongitude, string farmName, string fieldName)
        {
            _gpsPoints = gpsPoints;
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
                if (_gpsPoints == null || _gpsPoints.Count == 0)
                {
                    PreviewTextBlock.Text = "No GPS data loaded for preview";
                    FilenamePreviewTextBlock.Text = "";
                    return;
                }

                // Get current settings
                var resolution = GetResolution();
                var coordinateSystem = GetSelectedCoordinateSystem();
                var exportType = GetSelectedExportType();
                var useCompression = CompressionCheckBox.IsChecked == true;

                // Estimate file size based on GPS points and resolution
                // Calculate approximate bounds from GPS points
                var minEasting = _gpsPoints.Min(p => p.Easting);
                var maxEasting = _gpsPoints.Max(p => p.Easting);
                var minNorthing = _gpsPoints.Min(p => p.Northing);
                var maxNorthing = _gpsPoints.Max(p => p.Northing);
                
                var width = (int)Math.Ceiling((maxEasting - minEasting) / resolution);
                var height = (int)Math.Ceiling((maxNorthing - minNorthing) / resolution);
                var pixelCount = width * height;
                var bytesPerPixel = 4; // Float32
                var estimatedSize = (long)pixelCount * bytesPerPixel;
                
                if (useCompression)
                    estimatedSize = (long)(estimatedSize * 0.6); // Approximate 40% compression

                var fileSizeText = FormatFileSize(estimatedSize);

                // Generate filename
                var date = DateTime.Now.ToString("ddMMyy");
                var filename = $"{_farmName}_{_fieldName}_{date}.tif";
                if (string.IsNullOrEmpty(_farmName) || string.IsNullOrEmpty(_fieldName))
                    filename = $"DEM_Export_{date}.tif";

                // Update preview text
                PreviewTextBlock.Text = $"Estimated file size: ~{fileSizeText} | Format: {exportType} with {coordinateSystem}";
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

        private string GetSelectedCoordinateSystem()
        {
            if (UtmRadioButton.IsChecked == true)
                return "UTM coordinates";
            else if (Wgs84RadioButton.IsChecked == true)
                return "WGS84 Geographic";
            else if (LocalRadioButton.IsChecked == true)
                return "Local Tangent Plane";
            return "UTM coordinates";
        }

        private string GetSelectedExportType()
        {
            if (SingleFileRadioButton.IsChecked == true)
                return "Single GeoTIFF";
            else if (TiledRadioButton.IsChecked == true)
                return "Tiled GeoTIFF";
            return "Single GeoTIFF";
        }

        private GeoTiffExportOptions GetExportOptions()
        {
            return new GeoTiffExportOptions
            {
                CoordinateSystem = UtmRadioButton.IsChecked == true ? CoordinateSystemType.UTM :
                                 Wgs84RadioButton.IsChecked == true ? CoordinateSystemType.WGS84 :
                                 CoordinateSystemType.LocalTangentPlane,
                ExportType = SingleFileRadioButton.IsChecked == true ? GeoTiffExportType.SingleFile :
                           GeoTiffExportType.Tiled,
                UseCompression = CompressionCheckBox.IsChecked == true,
                IncludeColorPalette = ColorPaletteCheckBox.IsChecked == true,
                Resolution = GetResolution(),
                IncludeFarmName = FarmNameCheckBox.IsChecked == true,
                IncludeFieldName = FieldNameCheckBox.IsChecked == true,
                IncludeProcessingDate = ProcessingDateCheckBox.IsChecked == true,
                IncludeGpsCount = GpsCountCheckBox.IsChecked == true,
                IncludeElevationRange = ElevationRangeCheckBox.IsChecked == true
            };
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
            if (_gpsPoints == null || _gpsPoints.Count == 0)
            {
                System.Windows.MessageBox.Show("No GPS data available for export.", "Export Error", 
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
                    Title = "Save GeoTIFF File",
                    Filter = "GeoTIFF Files (*.tif)|*.tif|All Files (*.*)|*.*",
                    DefaultExt = "tif"
                };

                // Generate default filename
                var date = DateTime.Now.ToString("ddMMyy");
                var defaultName = !string.IsNullOrEmpty(_farmName) && !string.IsNullOrEmpty(_fieldName) 
                    ? $"{_farmName}_{_fieldName}_{date}.tif"
                    : $"DEM_Export_{date}.tif";
                saveDialog.FileName = defaultName;

                if (saveDialog.ShowDialog() != true)
                    return;

                // Disable UI during export
                SetUIEnabled(false);
                ShowProgress("Preparing GeoTIFF export...", 0);

                // Create cancellation token
                _exportCancellation = new CancellationTokenSource();

                // Get export options
                var options = GetExportOptions();

                // Create export service and perform export
                var exportService = new GisExportService();
                var progress = new Progress<GeoTiffExportProgress>(UpdateExportProgress);

                var result = await exportService.ExportGeoTiffAsync(
                    _gpsPoints!, saveDialog.FileName, options,
                    _referenceLatitude, _referenceLongitude, _farmName, _fieldName,
                    GetResolution(), progress, _exportCancellation.Token);

                HideProgress();
                SetUIEnabled(true);

                if (result.Success)
                {
                    // Notify completion
                    ExportCompleted?.Invoke(this, new GeoTiffExportCompletedEventArgs
                    {
                        Success = true,
                        FilePath = saveDialog.FileName,
                        FileSize = result.FileSize,
                        ExportTime = result.ExportTime,
                        Message = result.Message
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

        private void UpdateExportProgress(GeoTiffExportProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                ShowProgress(progress.Message, progress.PercentComplete);
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
            UtmRadioButton.IsEnabled = enabled;
            Wgs84RadioButton.IsEnabled = enabled;
            LocalRadioButton.IsEnabled = enabled;
            SingleFileRadioButton.IsEnabled = enabled;
            TiledRadioButton.IsEnabled = enabled;
            CompressionCheckBox.IsEnabled = enabled;
            ColorPaletteCheckBox.IsEnabled = enabled;
            ResolutionTextBox.IsEnabled = enabled;
            FarmNameCheckBox.IsEnabled = enabled;
            FieldNameCheckBox.IsEnabled = enabled;
            ProcessingDateCheckBox.IsEnabled = enabled;
            GpsCountCheckBox.IsEnabled = enabled;
            ElevationRangeCheckBox.IsEnabled = enabled;
            ExportButton.IsEnabled = enabled;
            BackButton.IsEnabled = enabled;
        }
    }
}
