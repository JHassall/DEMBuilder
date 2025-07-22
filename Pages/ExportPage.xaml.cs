using DEMBuilder.Models;
using System;
using System.Collections.Generic;
using DEMBuilder.Services.Dem;
using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;
using System.IO;

namespace DEMBuilder.Pages
{
    public partial class ExportPage : System.Windows.Controls.UserControl
    {
        public event EventHandler? RestartRequested;
        public event EventHandler? GoBackRequested;

        private readonly DemGenerationService _demGenerationService;
        private List<GpsPoint>? _projectedPoints;
        private double _referenceLatitude;
        private double _referenceLongitude;
        private string? _farmName;
        private string? _fieldName;

        public ExportPage()
        {
            InitializeComponent();
            _demGenerationService = new DemGenerationService();
        }

        public void SetData(List<GpsPoint>? projectedPoints, double refLat, double refLon, string? farmName, string? fieldName)
        {
            _projectedPoints = projectedPoints;
            _referenceLatitude = refLat;
            _referenceLongitude = refLon;
            _farmName = farmName;
            _fieldName = fieldName;

            // Optionally, display a summary or confirmation message
            StatusText.Text = "Ready to export DEM data.";
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_projectedPoints == null || _projectedPoints.Count == 0)
            {
                StatusText.Text = "Error: No projected points available to export.";
                return;
            }

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "ASCII Grid (*.asc)|*.asc|All files (*.*)|*.*",
                Title = "Save DEM as ASCII Grid",
                FileName = $"{_farmName}_{_fieldName}_DEM.asc"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    ShowStatusMessage("Exporting DEM to intermediate format...");
                    await _demGenerationService.ExportDemAsAscAsync(_projectedPoints, saveFileDialog.FileName);

                    ShowStatusMessage("Converting to GeoTIFF using GDAL...");
                    var (success, errorMessage) = await _demGenerationService.ConvertAscToGeoTiffAsync(saveFileDialog.FileName, _referenceLatitude, _referenceLongitude);

                    if (success)
                    {
                        ShowStatusMessage($"Successfully exported DEM to {Path.ChangeExtension(saveFileDialog.FileName, ".tif")}");
                        ExportButton.Visibility = Visibility.Collapsed;
                        PostExportNavigationPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        ShowStatusMessage($"Failed to convert DEM to GeoTIFF: {errorMessage}", isError: true);
                    }
                }
                catch (Exception ex)
                {
                    ShowStatusMessage($"Error exporting file: {ex.Message}", isError: true);
                }
            }
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
