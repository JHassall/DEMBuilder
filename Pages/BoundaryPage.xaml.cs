using System;
using System.Collections.Generic;
using System.Windows;
using DEMBuilder.Models;
using System.Windows.Input;
using GMap.NET;

namespace DEMBuilder.Pages
{
    public partial class BoundaryPage : System.Windows.Controls.UserControl
    {
        public event EventHandler? StartDrawing;
        public event EventHandler? FinishDrawing;
        public event EventHandler? ClearBoundary;
        public event EventHandler<BoundaryAppliedEventArgs>? BoundaryApplied;
        public event EventHandler<BoundaryAppliedEventArgs>? DeletePointsRequested;

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
            FarmFieldInputPanel.IsEnabled = false;
            DeletePointsButton.IsEnabled = false;
            ApplyBoundaryButton.IsEnabled = false;
        }

        private void FinishDrawingButton_Click(object sender, RoutedEventArgs e)
        {
            FinishDrawing?.Invoke(this, EventArgs.Empty);
            BoundaryStatusTextBlock.Text = "Boundary definition complete. Enter Farm/Field info.";
            StartDrawingButton.IsEnabled = true;
            FinishDrawingButton.IsEnabled = false;
            bool hasBoundary = _currentBoundaryPoints.Count > 2;
            FarmFieldInputPanel.IsEnabled = hasBoundary;
            DeletePointsButton.IsEnabled = hasBoundary;
            ApplyBoundaryButton.IsEnabled = hasBoundary;
        }

        private void ClearBoundaryButton_Click(object sender, RoutedEventArgs e)
        {
            ClearBoundary?.Invoke(this, EventArgs.Empty);
            _currentBoundaryPoints.Clear();
            BoundaryStatusTextBlock.Text = "Boundary cleared. You can start drawing again.";
            StartDrawingButton.IsEnabled = true;
            FinishDrawingButton.IsEnabled = false;
            FarmFieldInputPanel.IsEnabled = false;
            DeletePointsButton.IsEnabled = false;
            ApplyBoundaryButton.IsEnabled = false;
            FarmNameTextBox.Text = string.Empty;
            FieldNameTextBox.Text = string.Empty;
        }

        private void ApplyBoundaryButton_Click(object sender, RoutedEventArgs e)
        {
            var eventArgs = new BoundaryAppliedEventArgs(
                new List<PointLatLng>(_currentBoundaryPoints),
                FarmNameTextBox.Text,
                FieldNameTextBox.Text
            );
            BoundaryApplied?.Invoke(this, eventArgs);
        }

        private void DeletePointsButton_Click(object sender, RoutedEventArgs e)
        {
            var eventArgs = new BoundaryAppliedEventArgs(
                new List<PointLatLng>(_currentBoundaryPoints),
                string.Empty, // Farm name not needed
                string.Empty  // Field name not needed
            );
            DeletePointsRequested?.Invoke(this, eventArgs);
        }

        public void SetCurrentBoundaryPoints(List<PointLatLng> points)
        {
            _currentBoundaryPoints = points;
            if (!FinishDrawingButton.IsEnabled)
            {
                bool hasBoundary = _currentBoundaryPoints.Count > 2;
                FarmFieldInputPanel.IsEnabled = hasBoundary;
                DeletePointsButton.IsEnabled = hasBoundary;
                ApplyBoundaryButton.IsEnabled = hasBoundary;
            }
        }

        public void ShowStatusMessage(string message)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        public void Reset()
        {
            FarmNameTextBox.Text = string.Empty;
            FieldNameTextBox.Text = string.Empty;
            BoundaryStatusTextBlock.Text = "Click 'Start Drawing' to define a new boundary.";
            StatusTextBlock.Visibility = Visibility.Collapsed;
            StartDrawingButton.IsEnabled = true;
            FinishDrawingButton.IsEnabled = false;
            FarmFieldInputPanel.IsEnabled = false;
            DeletePointsButton.IsEnabled = false;
            ApplyBoundaryButton.IsEnabled = false;
            _currentBoundaryPoints.Clear();
        }
    }
}