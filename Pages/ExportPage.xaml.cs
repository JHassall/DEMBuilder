using System;
using System.Windows;
using System.Windows.Controls;
using TriangleNet.Geometry;

namespace DEMBuilder.Pages
{
    public enum ExportFormat
    {
        TextFile,
        GeoTiff,
        RgFdem
    }

    public class ExportFormatSelectedEventArgs : EventArgs
    {
        public ExportFormat SelectedFormat { get; set; }
        public double[,]? RasterData { get; set; }
        public TriangleNet.Geometry.Rectangle? Bounds { get; set; }
        public double ReferenceLatitude { get; set; }
        public double ReferenceLongitude { get; set; }
        public string FarmName { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
    }

    public partial class ExportPage : System.Windows.Controls.UserControl
    {
        public event EventHandler<ExportFormatSelectedEventArgs>? FormatSelected;
        public event EventHandler? BackRequested;

        private double[,]? _rasterData;
        private TriangleNet.Geometry.Rectangle? _bounds;
        private double _referenceLatitude;
        private double _referenceLongitude;
        private string _farmName = string.Empty;
        private string _fieldName = string.Empty;

        public ExportPage()
        {
            InitializeComponent();
        }

        public void SetExportData(double[,] rasterData, TriangleNet.Geometry.Rectangle bounds, double refLat, double refLon, string farmName, string fieldName)
        {
            _rasterData = rasterData;
            _bounds = bounds;
            _referenceLatitude = refLat;
            _referenceLongitude = refLon;
            _farmName = farmName;
            _fieldName = fieldName;

            var width = rasterData.GetLength(1);
            var height = rasterData.GetLength(0);
            var totalPixels = width * height;
            
            StatusTextBlock.Text = $"Ready to export DEM data ({width} x {height} pixels, {totalPixels:N0} data points).";
        }

        private ExportFormat GetSelectedFormat()
        {
            if (TextFileRadioButton.IsChecked == true)
                return ExportFormat.TextFile;
            else if (GeoTiffRadioButton.IsChecked == true)
                return ExportFormat.GeoTiff;
            else if (RgFdemRadioButton.IsChecked == true)
                return ExportFormat.RgFdem;
            else
                return ExportFormat.GeoTiff; // Default
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_rasterData == null || _bounds == null)
            {
                System.Windows.MessageBox.Show("No DEM data available for export.", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedFormat = GetSelectedFormat();
            var eventArgs = new ExportFormatSelectedEventArgs
            {
                SelectedFormat = selectedFormat,
                RasterData = _rasterData,
                Bounds = _bounds,
                ReferenceLatitude = _referenceLatitude,
                ReferenceLongitude = _referenceLongitude,
                FarmName = _farmName,
                FieldName = _fieldName
            };

            FormatSelected?.Invoke(this, eventArgs);
        }
    }
}
