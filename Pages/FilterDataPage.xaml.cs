using DEMBuilder.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DEMBuilder.Pages
{
    public partial class FilterDataPage : System.Windows.Controls.UserControl
    {
        // Event to notify the main window when the filter has been applied.
        public event EventHandler<List<GpsPoint>>? FilterApplied;

        // Property to hold the full list of points from the main window.
        public List<GpsPoint> AllGpsPoints { get; set; } = new List<GpsPoint>();

        public FilterDataPage()
        {
            InitializeComponent();
        }

        // This method will be called by the MainWindow when this page is navigated to.
        public void OnNavigatedTo()
        {
            if (AllGpsPoints.Any())
            {
                // Populate min/max with actual data range for user convenience
                var minAlt = AllGpsPoints.Min(p => p.Altitude);
                var maxAlt = AllGpsPoints.Max(p => p.Altitude);
                var maxHdop = AllGpsPoints.Max(p => p.Hdop);

                MinAltitudeTextBox.Text = Math.Round(minAlt, 2).ToString();
                MaxAltitudeTextBox.Text = Math.Round(maxAlt, 2).ToString();
                MaxHdopTextBox.Text = Math.Round(maxHdop, 2).ToString();
                // ReceiverIdsTextBox is blank by default to include all

                FilterSummaryTextBlock.Text = $"Loaded {AllGpsPoints.Count} points. Apply filters to refine the dataset.";
            }
            else
            {
                FilterSummaryTextBlock.Text = "No points loaded.";
            }
        }

        private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AllGpsPoints.Any()) return;

            var originalCount = AllGpsPoints.Count;
            IEnumerable<GpsPoint> filteredPoints = AllGpsPoints;

            // Altitude Filter
            if (double.TryParse(MinAltitudeTextBox.Text, out double minAlt) && double.TryParse(MaxAltitudeTextBox.Text, out double maxAlt))
            {
                filteredPoints = filteredPoints.Where(p => p.Altitude >= minAlt && p.Altitude <= maxAlt);
            }

            // HDOP Filter
            if (double.TryParse(MaxHdopTextBox.Text, out double maxHdop))
            {
                filteredPoints = filteredPoints.Where(p => p.Hdop <= maxHdop);
            }

            // Fix Quality Filter
            if (FixQualityComboBox.SelectedItem is ComboBoxItem selectedFixItem && int.TryParse(selectedFixItem.Tag.ToString(), out int fixQuality) && fixQuality != -1)
            {
                filteredPoints = filteredPoints.Where(p => p.FixQuality == fixQuality);
            }

            // Receiver ID Filter
            if (!string.IsNullOrWhiteSpace(ReceiverIdsTextBox.Text))
            {
                var includedIds = new HashSet<int>();
                var idStrings = ReceiverIdsTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var idStr in idStrings)
                {
                    if (int.TryParse(idStr, out int id))
                    {
                        includedIds.Add(id);
                    }
                }
                if (includedIds.Any())
                {
                    filteredPoints = filteredPoints.Where(p => includedIds.Contains(p.ReceiverId));
                }
            }

            var finalList = filteredPoints.ToList();
            var newCount = finalList.Count;

            FilterSummaryTextBlock.Text = $"Filtered from {originalCount} to {newCount} points.";

            // Raise the event to notify the main window of the change
            FilterApplied?.Invoke(this, finalList);
        }
    }
}
