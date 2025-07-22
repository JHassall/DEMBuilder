using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DEMBuilder.Models;
using DEMBuilder.Services.Projection; // Replaced AOG with our own service

namespace DEMBuilder.Pages
{
    public partial class ProjectionPage : System.Windows.Controls.UserControl
    {
        public event EventHandler<Models.ProjectionCompletedEventArgs>? ProjectionCompleted;

        private List<GpsPoint>? _filteredPoints;

        public ProjectionPage() => InitializeComponent();

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

            ProjectPointsButton.IsEnabled = false;
            ProjectionProgressBar.Visibility = Visibility.Visible;
            ProjectionProgressBar.Value = 0;

            var refPoint = _filteredPoints[0];
            var refLat = refPoint.Latitude;
            var refLon = refPoint.Longitude;

            await Task.Run(() =>
            {
                // Use our new self-contained projection class
                var projection = new AogProjection();
                
                // Use the first point as the reference for the projection
                projection.SetReferencePoint(refLat, refLon);

                for (int i = 0; i < _filteredPoints.Count; i++)
                {
                    var point = _filteredPoints[i];
                    
                    // Project the point
                    var projectedCoords = projection.ToEastingNorthing(point.Latitude, point.Longitude);
                    point.Easting = projectedCoords.easting;
                    point.Northing = projectedCoords.northing;

                    // Update progress bar on the UI thread
                    Dispatcher.Invoke(() =>
                    {
                        ProjectionProgressBar.Value = (i + 1) * 100 / _filteredPoints.Count;
                    });
                }
            });

            StatusTextBlock.Text = $"Projection completed for {_filteredPoints.Count} points.";
            ProjectionCompleted?.Invoke(this, new ProjectionCompletedEventArgs(_filteredPoints, refLat, refLon));
        }
    }
}
