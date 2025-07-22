using DEMBuilder.Models;
using DEMBuilder.Services.Dem;
using System;
using System.Collections.Generic;
using System.Windows;

namespace DEMBuilder.Pages
{
    public partial class DemGenerationPage : System.Windows.Controls.UserControl
    {
        public event EventHandler<DemGenerationCompletedEventArgs>? DemGenerationCompleted;

        private readonly DemGenerationService _demGenerationService;
        private List<GpsPoint>? _projectedPoints;

        public DemGenerationPage()
        {
            InitializeComponent();
            _demGenerationService = new DemGenerationService();
        }

        public void SetData(List<GpsPoint>? projectedPoints)
        {
            _projectedPoints = projectedPoints;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_projectedPoints == null || _projectedPoints.Count == 0)
            {
                StatusText.Text = "Error: No projected points available to generate DEM.";
                DemProgressBar.IsIndeterminate = false;
                return;
            }

            StatusText.Text = "Generating DEM from projected points...\nPlease wait, this may take a few moments.";
            DemProgressBar.IsIndeterminate = true;

            try
            {
                var points = _projectedPoints; // Create a local non-nullable copy for the task
                var (rasterData, bounds) = await Task.Run(() => _demGenerationService.GenerateDemPreviewDataAsync(points));

                StatusText.Text = "DEM Generation Complete!";
                DemProgressBar.IsIndeterminate = false;
                DemProgressBar.Value = DemProgressBar.Maximum;

                DemGenerationCompleted?.Invoke(this, new DemGenerationCompletedEventArgs(rasterData, bounds));
            }
            catch (Exception ex)
            {
                StatusText.Text = $"An error occurred during DEM generation: {ex.Message}";
                DemProgressBar.IsIndeterminate = false;
            }
        }
    }
}
