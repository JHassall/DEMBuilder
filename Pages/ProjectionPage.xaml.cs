using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DEMBuilder.Models;
using DEMBuilder.Services.Projection;

namespace DEMBuilder.Pages
{
    public partial class ProjectionPage : System.Windows.Controls.UserControl
    {
        public event EventHandler<Models.ProjectionCompletedEventArgs>? ProjectionCompleted;

        private List<GpsPoint>? _filteredPoints;
        private readonly HighPerformanceProjection _projectionService;
        private CancellationTokenSource? _cancellationTokenSource;

        public ProjectionPage()
        {
            InitializeComponent();
            _projectionService = new HighPerformanceProjection();
        }

        public void SetFilteredPoints(List<GpsPoint>? points)
        {
            _filteredPoints = points;
            if (_filteredPoints is { Count: > 0 })
            {
                StatusTextBlock.Text = $"{_filteredPoints.Count} points ready for projection.";
                ProjectPointsButton.IsEnabled = true;
            }
            else
            {
                StatusTextBlock.Text = "No filtered points available.";
                ProjectPointsButton.IsEnabled = false;
            }
            ProjectionProgressBar.Visibility = Visibility.Collapsed;
        }

        private async void ProjectPointsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_filteredPoints is not { Count: > 0 })
            {
                StatusTextBlock.Text = "No points to project.";
                return;
            }

            // Show performance recommendation
            var recommendation = _projectionService.GetPerformanceRecommendation(_filteredPoints.Count);
            StatusTextBlock.Text = $"Starting projection: {recommendation.GetRecommendationText()}";

            // Setup UI for processing
            ProjectPointsButton.IsEnabled = false;
            ProjectionProgressBar.Visibility = Visibility.Visible;
            ProjectionProgressBar.Value = 0;
            
            // Setup cancellation
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Use first point as reference
            var refPoint = _filteredPoints[0];
            var refLat = refPoint.Latitude;
            var refLon = refPoint.Longitude;

            try
            {
                // Use high-performance projection service
                var result = await _projectionService.ProjectPointsAsync(
                    _filteredPoints, refLat, refLon,
                    new Progress<ProjectionProgress>(progress =>
                    {
                        // Update UI on UI thread (much less frequently)
                        Dispatcher.Invoke(() =>
                        {
                            ProjectionProgressBar.Value = progress.PercentComplete;
                            StatusTextBlock.Text = progress.Message;
                        });
                    }),
                    _cancellationTokenSource.Token);

                if (result.Success)
                {
                    StatusTextBlock.Text = $"Projection completed! {result.PointsProcessed:N0} points in {result.ProcessingTime.TotalSeconds:F1}s ({result.PointsPerSecond:F0} points/sec)";
                    ProjectionCompleted?.Invoke(this, new ProjectionCompletedEventArgs(_filteredPoints, refLat, refLon));
                }
                else
                {
                    StatusTextBlock.Text = $"Projection failed: {result.ErrorMessage}";
                    ProjectPointsButton.IsEnabled = true;
                }
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Projection cancelled by user.";
                ProjectPointsButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Projection error: {ex.Message}";
                ProjectPointsButton.IsEnabled = true;
            }
            finally
            {
                ProjectionProgressBar.Visibility = Visibility.Collapsed;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
    }
}
