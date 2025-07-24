using DEMBuilder.Models;
using DEMBuilder.Services.Dem;
using DEMBuilder.Services.Export;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows.Media;

namespace DEMBuilder.Pages
{
    public partial class ExportPage : System.Windows.Controls.UserControl
    {
        public event EventHandler? RestartRequested;
        public event EventHandler? GoBackRequested;

        private readonly DemGenerationService _demGenerationService;
        private GdalExportService? _gdalExportService;
        private readonly RgFdemExportService _rgFdemExportService;
        private List<GpsPoint>? _projectedPoints;
        private double _referenceLatitude;
        private double _referenceLongitude;
        private string? _farmName;
        private string? _fieldName;
        private bool _isExporting = false;

        public ExportPage()
        {
            InitializeComponent();
            _demGenerationService = new DemGenerationService();
            _rgFdemExportService = new RgFdemExportService();

            // Set up event handlers for format selection
            TxtFormatRadio.Checked += FormatRadio_CheckedChanged;
            GeoTiffFormatRadio.Checked += FormatRadio_CheckedChanged;
            RgFdemFormatRadio.Checked += FormatRadio_CheckedChanged;
            
            // Initialize GDAL service - try multiple times if needed
            InitializeGdalService();
        }

        public void SetData(List<GpsPoint>? projectedPoints, double refLat, double refLon, string? farmName, string? fieldName)
        {
            _projectedPoints = projectedPoints;
            _referenceLatitude = refLat;
            _referenceLongitude = refLon;
            _farmName = farmName;
            _fieldName = fieldName;

            if (_projectedPoints?.Count > 0)
            {
                StatusText.Text = $"Ready to export DEM data from {_projectedPoints.Count:N0} GPS points.";
            }
            else
            {
                StatusText.Text = "No GPS points available for export.";
                ExportButton.IsEnabled = false;
            }
        }

        private void InitializeGdalService()
        {
            try
            {
                _gdalExportService = new GdalExportService();
                
                var successMsg = "Ready to export DEM data. GDAL service initialized successfully.";
                ShowStatusMessage(successMsg, isError: false);
                
                // Enable GeoTIFF option if GDAL is available
                if (GeoTiffFormatRadio != null)
                {
                    GeoTiffFormatRadio.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                _gdalExportService = null;
                string errorDetails = ex.Message;
                if (ex.InnerException != null)
                {
                    errorDetails += $" Inner: {ex.InnerException.Message}";
                }
                
                var errorMsg = $"GDAL initialization failed: {errorDetails}";
                ShowStatusMessage(errorMsg, isError: true);
                
                // Disable GeoTIFF option if GDAL fails
                if (GeoTiffFormatRadio != null)
                {
                    GeoTiffFormatRadio.IsEnabled = false;
                    TxtFormatRadio.IsChecked = true;
                }
            }
        }

        private void FormatRadio_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (GeoTiffParametersGroup != null)
            {
                GeoTiffParametersGroup.IsEnabled = GeoTiffFormatRadio.IsChecked == true;
            }
            
            if (RgFdemParametersGroup != null)
            {
                RgFdemParametersGroup.IsEnabled = RgFdemFormatRadio.IsChecked == true;
            }
            
            // Try to reinitialize GDAL if user selects GeoTIFF and it's not available
            if (GeoTiffFormatRadio.IsChecked == true && _gdalExportService == null)
            {
                InitializeGdalService();
            }
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_projectedPoints == null || _projectedPoints.Count == 0)
            {
                ShowStatusMessage("No data available for preview.", isError: true);
                return;
            }

            try
            {
                var (isValid, resolution, tileSize, errorMessage) = ValidateExportParameters();
                if (!isValid)
                {
                    ShowStatusMessage(errorMessage!, isError: true);
                    return;
                }

                // Calculate estimated output size and provide preview
                var bounds = CalculateDataBounds(_projectedPoints);
                double dataWidth = bounds.Right - bounds.Left;
                double dataHeight = bounds.Top - bounds.Bottom;
                
                int pixelsX = (int)Math.Ceiling(dataWidth / resolution);
                int pixelsY = (int)Math.Ceiling(dataHeight / resolution);
                
                string previewText = $"Export Preview:\n";
                previewText += $"Data extent: {dataWidth:F1}m × {dataHeight:F1}m\n";
                previewText += $"Resolution: {resolution:F2}m per pixel\n";
                previewText += $"Output size: {pixelsX:N0} × {pixelsY:N0} pixels\n";
                
                if (GeoTiffFormatRadio.IsChecked == true && EnableTilingCheckBox.IsChecked == true && tileSize > 0)
                {
                    int tilesX = (int)Math.Ceiling(dataWidth / tileSize);
                    int tilesY = (int)Math.Ceiling(dataHeight / tileSize);
                    previewText += $"Tiling: {tilesX} × {tilesY} = {tilesX * tilesY} tiles\n";
                    previewText += $"Tile size: {tileSize:F0}m per tile";
                }
                else
                {
                    previewText += "Single file output";
                }

                System.Windows.MessageBox.Show(previewText, "Export Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Preview error: {ex.Message}", isError: true);
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isExporting)
            {
                ShowStatusMessage("Export already in progress...", isError: true);
                return;
            }

            if (_projectedPoints == null || _projectedPoints.Count == 0)
            {
                ShowStatusMessage("Error: No projected points available to export.", isError: true);
                return;
            }

            try
            {
                _isExporting = true;
                ExportButton.IsEnabled = false;
                PreviewButton.IsEnabled = false;

                if (TxtFormatRadio.IsChecked == true)
                {
                    await ExportAsTextFile();
                }
                else if (GeoTiffFormatRadio.IsChecked == true)
                {
                    await ExportAsGeoTiff();
                }
                else if (RgFdemFormatRadio.IsChecked == true)
                {
                    await ExportAsRgFdem();
                }
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Export failed: {ex.Message}", isError: true);
            }
            finally
            {
                _isExporting = false;
                ExportButton.IsEnabled = true;
                PreviewButton.IsEnabled = true;
                ExportProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task ExportAsTextFile()
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|ASCII Grid (*.asc)|*.asc|All files (*.*)|*.*",
                Title = "Save DEM as Text File",
                FileName = $"{_farmName}_{_fieldName}_DEM.txt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                ShowProgress("Exporting DEM to text file...", 0);
                await _demGenerationService.ExportDemAsAscAsync(_projectedPoints!, saveFileDialog.FileName);
                
                ShowExportSuccess(saveFileDialog.FileName, "Text file");
            }
        }

        private async Task ExportAsGeoTiff()
        {
            if (_gdalExportService == null)
            {
                ShowStatusMessage("GDAL service not available. Cannot export GeoTIFF.", isError: true);
                return;
            }

            var (isValid, resolution, tileSize, errorMessage) = ValidateExportParameters();
            if (!isValid)
            {
                ShowStatusMessage(errorMessage!, isError: true);
                return;
            }

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "GeoTIFF files (*.tif)|*.tif|All files (*.*)|*.*",
                Title = "Save DEM as GeoTIFF",
                FileName = $"{_farmName}_{_fieldName}_DEM.tif"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                ShowProgress("Generating DEM data...", 10);
                
                try
                {
                    // Generate DEM preview data (this creates the raster)
                    var (rasterData, bounds) = await _demGenerationService.GenerateDemPreviewDataAsync(_projectedPoints!, resolution);
                
                ShowProgress("Exporting GeoTIFF...", 30);
                
                // Use tiling if enabled and tile size > 0
                double effectiveTileSize = (EnableTilingCheckBox.IsChecked == true) ? tileSize : 0;
                
                var progress = new Progress<string>(message => 
                {
                    Dispatcher.Invoke(() => 
                    {
                        ExportProgressText.Text = message;
                        // Update progress bar based on message content
                        if (message.Contains("Processing tile"))
                        {
                            // Try to extract progress from tile messages
                            var parts = message.Split('/');
                            if (parts.Length >= 2 && int.TryParse(parts[0].Split(' ').Last(), out int current) && 
                                int.TryParse(parts[1].Split(' ')[0], out int total))
                            {
                                ExportProgressBar.Value = 30 + (int)((double)current / total * 60);
                            }
                        }
                    });
                });

                bool success = await _gdalExportService.ExportDemToGeoTiffAsync(
                    rasterData, bounds, saveFileDialog.FileName, resolution, effectiveTileSize,
                    _referenceLatitude, _referenceLongitude, progress);

                    if (success)
                    {
                        ShowExportSuccess(saveFileDialog.FileName, "GeoTIFF");
                    }
                    else
                    {
                        ShowStatusMessage("GeoTIFF export failed. Check the progress messages for details.", isError: true);
                    }
                }
                catch (Exception ex)
                {
                    ShowStatusMessage($"DEM generation failed: {ex.Message}", isError: true);
                }
            }
        }

        private async Task ExportAsRgFdem()
        {
            // Validate RgF DEM parameters
            if (!double.TryParse(RgFdemResolutionTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double resolution) || resolution <= 0)
            {
                ShowStatusMessage("Invalid resolution value. Please enter a positive number.", isError: true);
                return;
            }

            // Generate recommended filename
            var recommendedFileName = RgFdemExportService.GetRecommendedFileName(_farmName ?? "", _fieldName ?? "");
            
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "RgF DEM files (*.RgFdem)|*.RgFdem|All files (*.*)|*.*",
                Title = "Save DEM as RgF DEM",
                FileName = recommendedFileName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                ShowProgress("Generating DEM data...", 10);
                
                // Generate DEM raster data using the correct method
                var (rasterData, bounds) = await _demGenerationService.GenerateDemPreviewDataAsync(_projectedPoints!, resolution);
                
                ShowProgress("Creating RgF DEM file...", 30);
                
                // Export as RgF DEM with progress reporting
                var result = await _rgFdemExportService.ExportRgFdemAsync(
                    rasterData, bounds, saveFileDialog.FileName, resolution,
                    _referenceLatitude, _referenceLongitude,
                    _farmName ?? "", _fieldName ?? "",
                    new Progress<RgFdemProgress>(progress =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ShowProgress(progress.Message, (int)progress.PercentComplete);
                        });
                    }),
                    CancellationToken.None);

                if (result.Success)
                {
                    var fileSizeMB = result.FileSizeBytes / (1024.0 * 1024.0);
                    var successMessage = $"RgF DEM export completed successfully!\n" +
                                        $"File: {Path.GetFileName(result.FilePath)}\n" +
                                        $"Size: {fileSizeMB:F1} MB\n" +
                                        $"Export time: {result.ExportTime.TotalSeconds:F1} seconds\n\n" +
                                        $"📱 Ready for transfer to ABLS software\n" +
                                        $"💾 Optimized for thumb drives and wireless transfer";
                    
                    ShowExportSuccess(result.FilePath, "RgF DEM", successMessage);
                }
                else
                {
                    ShowStatusMessage($"RgF DEM export failed: {result.ErrorMessage}", isError: true);
                }
            }
        }

        private (bool isValid, double resolution, double tileSize, string? errorMessage) ValidateExportParameters()
        {
            // Validate resolution
            if (!double.TryParse(ResolutionTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double resolution) || resolution <= 0)
            {
                return (false, 0, 0, "Invalid resolution. Please enter a positive number.");
            }

            if (resolution < 0.01 || resolution > 100)
            {
                return (false, 0, 0, "Resolution must be between 0.01 and 100 meters.");
            }

            // Validate tile size
            if (!double.TryParse(TileSizeTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tileSize) || tileSize < 0)
            {
                return (false, 0, 0, "Invalid tile size. Please enter a non-negative number (0 for single file).");
            }

            if (tileSize > 0 && tileSize < 10)
            {
                return (false, 0, 0, "Tile size must be at least 10 meters or 0 for single file.");
            }

            return (true, resolution, tileSize, null);
        }

        private TriangleNet.Geometry.Rectangle CalculateDataBounds(List<GpsPoint> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var point in points)
            {
                minX = Math.Min(minX, point.Easting);
                maxX = Math.Max(maxX, point.Easting);
                minY = Math.Min(minY, point.Northing);
                maxY = Math.Max(maxY, point.Northing);
            }

            return new TriangleNet.Geometry.Rectangle(minX, minY, maxX, maxY);
        }

        private void ShowProgress(string message, int percentage)
        {
            ExportProgressPanel.Visibility = Visibility.Visible;
            ExportProgressText.Text = message;
            ExportProgressBar.Value = percentage;
        }

        private void ShowExportSuccess(string filePath, string fileType, string? customMessage = null)
        {
            string fileName = Path.GetFileName(filePath);
            string directory = Path.GetDirectoryName(filePath) ?? "";
            
            if (!string.IsNullOrEmpty(customMessage))
            {
                ExportSummaryText.Text = customMessage;
            }
            else
            {
                ExportSummaryText.Text = $"Successfully exported {fileType}: {fileName}\nLocation: {directory}";
            }
            
            ExportSummaryText.Visibility = Visibility.Visible;
            
            ShowStatusMessage($"Export completed successfully!", isError: false);
            ExportProgressBar.Value = 100;
            
            // Show post-export navigation
            PostExportNavigationPanel.Visibility = Visibility.Visible;
        }

        private void SelectMorePointsButton_Click(object sender, RoutedEventArgs e)
        {
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            GoBackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void ShowStatusMessage(string message, bool isError = false)
        {
            StatusText.Text = message;
            StatusText.Foreground = isError ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Black;
        }
    }
}
