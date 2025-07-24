using DEMBuilder.Models;
using DEMBuilder.Services.Filter;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DEMBuilder.Pages
{
    public partial class FilterDataPage : System.Windows.Controls.UserControl
    {
        public event EventHandler<FilterAppliedEventArgs>? FilterApplied;

        private List<GpsPoint> _allGpsPoints = new List<GpsPoint>();
        private readonly HighPerformanceFilter _filterService;
        private CancellationTokenSource? _filterCancellation;

        public FilterDataPage()
        {
            InitializeComponent();
            _filterService = new HighPerformanceFilter();
        }

        public void SetGpsPoints(List<GpsPoint>? points)
        {
            _allGpsPoints = points ?? new List<GpsPoint>();

            if (_allGpsPoints.Any())
            {
                // Calculate and display HDOP range
                var minHdop = _allGpsPoints.Min(p => p.Hdop);
                var maxHdop = _allGpsPoints.Max(p => p.Hdop);
                HdopRangeText.Text = $"Range: {minHdop:F2} to {maxHdop:F2}";
                MaxHdopTextBox.Text = Math.Round(maxHdop, 2).ToString(CultureInfo.InvariantCulture);

                // Calculate and display Age of Differential range
                var minAge = _allGpsPoints.Min(p => p.AgeOfDiff);
                var maxAge = _allGpsPoints.Max(p => p.AgeOfDiff);
                AgeOfDiffRangeText.Text = $"Range: {minAge:F2} to {maxAge:F2}";
                MaxAgeOfDiffTextBox.Text = Math.Round(maxAge, 2).ToString(CultureInfo.InvariantCulture);

                FilterSummaryTextBlock.Text = $"Loaded {_allGpsPoints.Count} points. Adjust filters and click 'Apply Filter'.";

                TotalPointsText.Text = $"Total Points: {_allGpsPoints.Count}";
                FilteredPointsText.Text = "Included Points: N/A";
                ExcludedPointsText.Text = "Excluded Points: N/A";
            }
            else
            {
                HdopRangeText.Text = "Range: N/A";
                AgeOfDiffRangeText.Text = "Range: N/A";
                FilterSummaryTextBlock.Text = "No points loaded.";
                TotalPointsText.Text = "Total Points: 0";
                FilteredPointsText.Text = "Included Points: 0";
                ExcludedPointsText.Text = "Excluded Points: 0";
            }
        }

        private async void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_allGpsPoints.Any()) return;

            try
            {
                // Disable button during processing
                ApplyFilterButton.IsEnabled = false;
                
                // Show progress bar and start filtering
                ShowProgressBar();
                ShowStatusMessage("Applying filters to GPS points...");

                // Build filter criteria
                var criteria = new FilterCriteria();
                
                // 1. HDOP filter
                if (double.TryParse(MaxHdopTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxHdop))
                {
                    criteria.MaxHdop = maxHdop;
                }

                // 2. RTK Status filter
                var selectedFixQualities = new HashSet<int>();
                foreach (System.Windows.Controls.CheckBox checkBox in RtkStatusPanel.Children.OfType<System.Windows.Controls.CheckBox>())
                {
                    if (checkBox.IsChecked == true && int.TryParse(checkBox.Tag.ToString(), out int tag))
                    {
                        selectedFixQualities.Add(tag);
                    }
                }
                criteria.AllowedFixQualities = selectedFixQualities;

                // 3. Age of Differential filter
                if (double.TryParse(MaxAgeOfDiffTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxAge))
                {
                    criteria.MaxAgeOfDiff = maxAge;
                }

                // Setup cancellation
                _filterCancellation = new CancellationTokenSource();

                // Use high-performance filtering service
                var result = await _filterService.FilterPointsAsync(
                    _allGpsPoints, criteria,
                    new Progress<FilterProgress>(progress =>
                    {
                        // Update progress bar and status on UI thread
                        Dispatcher.Invoke(() =>
                        {
                            UpdateProgress(progress.PercentComplete);
                            ShowStatusMessage($"Processing: {progress.PercentComplete:F0}% complete...");
                        });
                    }),
                    _filterCancellation.Token);

                if (result.Success)
                {
                    HideProgressBar();
                    var successMessage = $"✅ Filter applied: {result.PointsIncluded:N0} included, {result.PointsExcluded:N0} excluded";
                    ShowStatusMessage(successMessage);
                    
                    TotalPointsText.Text = $"Total Points: {_allGpsPoints.Count}";
                    FilteredPointsText.Text = $"Included Points: {result.PointsIncluded}";
                    ExcludedPointsText.Text = $"Excluded Points: {result.PointsExcluded}";

                    FilterApplied?.Invoke(this, new FilterAppliedEventArgs(result.FilteredPoints, result.ExcludedPoints));
                }
                else
                {
                    HideProgressBar();
                    ShowStatusMessage($"❌ Filtering failed: {result.ErrorMessage}");
                }
            }
            catch (OperationCanceledException)
            {
                HideProgressBar();
                ShowStatusMessage("⏹️ Filtering cancelled by user.");
            }
            catch (Exception ex)
            {
                HideProgressBar();
                ShowStatusMessage($"⚠️ Filtering error: {ex.Message}");
            }
            finally
            {
                _filterCancellation?.Dispose();
                _filterCancellation = null;
                ApplyFilterButton.IsEnabled = true;
            }
        }

        public void ShowProgressBar()
        {
            FilterProgressBar.Visibility = Visibility.Visible;
            FilterProgressBar.Value = 0;
        }

        public void HideProgressBar()
        {
            FilterProgressBar.Visibility = Visibility.Collapsed;
        }

        public void UpdateProgress(double percentage)
        {
            FilterProgressBar.Value = Math.Max(0, Math.Min(100, percentage));
        }

        public void ShowStatusMessage(string message)
        {
            FilterSummaryTextBlock.Text = message;
        }
    }
}
