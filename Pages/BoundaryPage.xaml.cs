using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using GMap.NET; // Added for PointLatLng

namespace DEMBuilder.Pages
{
    public partial class BoundaryPage : System.Windows.Controls.UserControl
    {
        public event EventHandler? StartDrawing;
        public event EventHandler? FinishDrawing;
        public event EventHandler? ClearBoundary;
        public event EventHandler<List<PointLatLng>>? BoundaryApplied;

        private List<PointLatLng> _currentBoundaryPoints = new List<PointLatLng>();

        public BoundaryPage()
        {
            InitializeComponent();
        }

        private void StartDrawingButton_Click(object sender, RoutedEventArgs e)
        {
            StartDrawing?.Invoke(this, EventArgs.Empty);
            BoundaryStatusTextBlock.Text = "Click on the map to add boundary points.";
            StartDrawingButton.IsEnabled = false;
            FinishDrawingButton.IsEnabled = true;
            ApplyBoundaryButton.IsEnabled = false; // Ensure apply is disabled when starting
        }

        private void FinishDrawingButton_Click(object sender, RoutedEventArgs e)
        {
            FinishDrawing?.Invoke(this, EventArgs.Empty);
            BoundaryStatusTextBlock.Text = "Boundary definition complete.";
            StartDrawingButton.IsEnabled = true;
            FinishDrawingButton.IsEnabled = false;
            ApplyBoundaryButton.IsEnabled = _currentBoundaryPoints.Count > 2; // Enable if valid polygon
        }

        private void ClearBoundaryButton_Click(object sender, RoutedEventArgs e)
        {
            ClearBoundary?.Invoke(this, EventArgs.Empty);
            _currentBoundaryPoints.Clear();
            BoundaryStatusTextBlock.Text = "Boundary cleared. You can start drawing again.";
            StartDrawingButton.IsEnabled = true;
            FinishDrawingButton.IsEnabled = false;
            ApplyBoundaryButton.IsEnabled = false;
        }

        private void ApplyBoundaryButton_Click(object sender, RoutedEventArgs e)
        {
            BoundaryApplied?.Invoke(this, _currentBoundaryPoints);
            BoundaryStatusTextBlock.Text = $"Boundary applied. Points filtered."; // Or some other status
        }

        // This method will be called from MainWindow whenever the polygon is updated
        public void SetCurrentBoundaryPoints(List<PointLatLng> points)
        {
            _currentBoundaryPoints = points;
            // Optionally, re-evaluate ApplyBoundaryButton state here if needed
            // ApplyBoundaryButton.IsEnabled = _currentBoundaryPoints.Count > 2;
        }
    }
}
