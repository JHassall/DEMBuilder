using DEMBuilder.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DEMBuilder.Pages
{
    public partial class FilterDataPage : System.Windows.Controls.UserControl
    {
        public event EventHandler<FilterAppliedEventArgs>? FilterApplied;

        private List<GpsPoint> _allGpsPoints = new List<GpsPoint>();

        public FilterDataPage()
        {
            InitializeComponent();
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

        private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_allGpsPoints.Any()) return;

            var originalCount = _allGpsPoints.Count;
            IEnumerable<GpsPoint> filteredPoints = _allGpsPoints;

            // 1. Filter by HDOP
            if (double.TryParse(MaxHdopTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxHdop))
            {
                filteredPoints = filteredPoints.Where(p => p.Hdop <= maxHdop);
            }

            // 2. Filter by RTK Status
            var selectedFixQualities = new HashSet<int>();
            foreach (System.Windows.Controls.CheckBox checkBox in RtkStatusPanel.Children.OfType<System.Windows.Controls.CheckBox>())
            {
                if (checkBox.IsChecked == true && int.TryParse(checkBox.Tag.ToString(), out int tag))
                {
                    selectedFixQualities.Add(tag);
                }
            }

            if (selectedFixQualities.Any())
            {
                filteredPoints = filteredPoints.Where(p => selectedFixQualities.Contains(p.FixQuality));
            }

            // 3. Filter by Age of Differential
            if (double.TryParse(MaxAgeOfDiffTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxAge))
            {
                filteredPoints = filteredPoints.Where(p => p.AgeOfDiff <= maxAge);
            }

            var finalList = filteredPoints.ToList();
            var excludedList = _allGpsPoints.Except(finalList).ToList();
            var newCount = finalList.Count;
            var excludedCount = excludedList.Count;

            FilterSummaryTextBlock.Text = $"Filter applied. Please click 'Next' to continue.";
            TotalPointsText.Text = $"Total Points: {originalCount}";
            FilteredPointsText.Text = $"Included Points: {newCount}";
            ExcludedPointsText.Text = $"Excluded Points: {excludedCount}";

            FilterApplied?.Invoke(this, new FilterAppliedEventArgs(finalList, excludedList));
        }
    }
}
