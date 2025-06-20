using DEMBuilder.Models;
using DEMBuilder.Pages;
using DEMBuilder.Services;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DEMBuilder
{
    public partial class MainWindow : Window
    {
        private readonly NmeaParserService _nmeaParserService;
        private List<GpsPoint>? _allGpsPoints; // The original, full dataset
        private List<GpsPoint>? _displayedGpsPoints; // The dataset currently shown on the map
        private List<GpsPoint>? _pointsBeforeBoundaryFilter; // Backup of points before boundary filter
        private List<GpsPoint>? _currentFilteredGpsPoints; // Points after filtering (for FilterDataPage)

        private readonly LoadDataPage _loadDataPage;
        private readonly FilterDataPage _filterDataPage;
        private readonly BoundaryPage _boundaryPage;
        private readonly List<System.Windows.Controls.UserControl> _wizardPages;
        private int _currentPageIndex = 0;

        private bool _isDrawingBoundary = false;
        private CustomBoundaryPolygon? _boundaryPolygon;

        public MainWindow()
        {
            InitializeComponent();
            InitializeMap();

            _nmeaParserService = new NmeaParserService();

            _loadDataPage = new LoadDataPage();
            _filterDataPage = new FilterDataPage();
            _boundaryPage = new BoundaryPage();

            _wizardPages = new List<System.Windows.Controls.UserControl>
            {
                _loadDataPage,
                _boundaryPage,
                _filterDataPage
            };

            _loadDataPage.FolderSelected += LoadDataPage_FolderSelected;
            _filterDataPage.FilterApplied += FilterDataPage_FilterApplied;
            _boundaryPage.StartDrawing += BoundaryPage_StartDrawing;
            _boundaryPage.FinishDrawing += BoundaryPage_FinishDrawing;
            _boundaryPage.ClearBoundary += BoundaryPage_ClearBoundary;
            _boundaryPage.BoundaryApplied += BoundaryPage_BoundaryApplied;
        }

        private void InitializeMap()
        {
            MainMap.MapProvider = GMapProviders.GoogleSatelliteMap;
            MainMap.Position = new PointLatLng(-25.2744, 133.7751);
            MainMap.MinZoom = 1;
            MainMap.MaxZoom = 20;
            MainMap.Zoom = 4;
            MainMap.MouseWheelZoomType = MouseWheelZoomType.MousePositionAndCenter;
            MainMap.CanDragMap = true;
            MainMap.DragButton = MouseButton.Left;
            MainMap.MouseLeftButtonDown += MainMap_MouseLeftButtonDown;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WizardFrame.Navigate(_wizardPages[_currentPageIndex]);
        }

        private async void LoadDataPage_FolderSelected(object? sender, FolderSelectedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.FolderPath))
                return;

            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                NextButton.IsEnabled = false;

                _allGpsPoints = await _nmeaParserService.ParseFolderAsync(e.FolderPath, e.IncludeSubfolders);
                _currentFilteredGpsPoints = null;
                _pointsBeforeBoundaryFilter = null;

                if (_allGpsPoints == null || !_allGpsPoints.Any())
                {
                    System.Windows.MessageBox.Show("No valid GPS data found in the selected folder.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    _displayedGpsPoints = new List<GpsPoint>();
                    UpdateMapWithPoints();
                    UpdateNavigationButtons();
                    return;
                }

                _displayedGpsPoints = new List<GpsPoint>(_allGpsPoints);
                SampleAndDisplayPoints();
                UpdateNavigationButtons();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading GPS data: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _allGpsPoints = null;
                _displayedGpsPoints = new List<GpsPoint>();
                UpdateMapWithPoints();
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex < _wizardPages.Count - 1)
            {
                _currentPageIndex++;

                if (_wizardPages[_currentPageIndex] is FilterDataPage filterPage)
                {
                    filterPage.AllGpsPoints = _displayedGpsPoints ?? new List<GpsPoint>();
                    filterPage.OnNavigatedTo();
                }

                WizardFrame.Navigate(_wizardPages[_currentPageIndex]);
                UpdateNavigationButtons();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex > 0)
            {
                _currentPageIndex--;
                WizardFrame.Navigate(_wizardPages[_currentPageIndex]);
                UpdateNavigationButtons();
            }
        }

        private void UpdateNavigationButtons()
        {
            BackButton.IsEnabled = _currentPageIndex > 0;

            if (_currentPageIndex >= _wizardPages.Count - 1)
            {
                NextButton.IsEnabled = false;
            }
            else if (_currentPageIndex == 0)
            {
                NextButton.IsEnabled = _allGpsPoints != null && _allGpsPoints.Any();
            }
            else
            {
                NextButton.IsEnabled = true;
            }
        }

        private void FilterDataPage_FilterApplied(object? sender, List<GpsPoint> filteredPoints)
        {
            _currentFilteredGpsPoints = filteredPoints;
            _displayedGpsPoints = new List<GpsPoint>(_currentFilteredGpsPoints);
            _pointsBeforeBoundaryFilter = null;
            UpdateMapWithPoints();
        }

        private void UpdateMapWithPoints()
        {
            MainMap.Markers.Clear();
            if (_displayedGpsPoints == null || !_displayedGpsPoints.Any()) return;

            const int maxPointsToShow = 5000;
            var pointsToShow = _displayedGpsPoints;
            if (_displayedGpsPoints.Count > maxPointsToShow)
            {
                int step = _displayedGpsPoints.Count / maxPointsToShow;
                pointsToShow = _displayedGpsPoints.Where((p, i) => i % step == 0).ToList();
            }

            foreach (var point in pointsToShow)
            {
                var marker = new GMapMarker(point.AsPointLatLng())
                {
                    Shape = new Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Fill = System.Windows.Media.Brushes.Red,
                        Stroke = System.Windows.Media.Brushes.DarkRed,
                        StrokeThickness = 1
                    }
                };
                MainMap.Markers.Add(marker);
            }

            MainMap.ZoomAndCenterMarkers(null);
        }

        private void BoundaryPage_StartDrawing(object? sender, System.EventArgs e)
        {
            _isDrawingBoundary = true;
            MainMap.CanDragMap = false;

            if (_boundaryPolygon != null)
            {
                MainMap.Markers.Remove(_boundaryPolygon);
            }

            _boundaryPolygon = new CustomBoundaryPolygon(new List<PointLatLng>());
            MainMap.Markers.Add(_boundaryPolygon);
        }

        private void BoundaryPage_FinishDrawing(object? sender, System.EventArgs e)
        {
            _isDrawingBoundary = false;
            MainMap.CanDragMap = true;
        }

        private void BoundaryPage_ClearBoundary(object? sender, System.EventArgs e)
        {
            if (_boundaryPolygon != null)
            {
                MainMap.Markers.Remove(_boundaryPolygon);
                _boundaryPolygon = null;
            }
            _isDrawingBoundary = false;
            MainMap.CanDragMap = true;

            if (_allGpsPoints != null)
            {
                _displayedGpsPoints = new List<GpsPoint>(_allGpsPoints);
            }
            else
            {
                _displayedGpsPoints = new List<GpsPoint>();
            }
            _pointsBeforeBoundaryFilter = null;
            _currentFilteredGpsPoints = null;
            SampleAndDisplayPoints();
        }

        private void BoundaryPage_BoundaryApplied(object? sender, List<PointLatLng> boundaryPoints)
        {
            if (_allGpsPoints == null || !_allGpsPoints.Any())
            {
                System.Windows.MessageBox.Show("No GPS data loaded to define a boundary.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (boundaryPoints.Count < 3)
            {
                System.Windows.MessageBox.Show("Please define a valid boundary with at least 3 points.", "Invalid Boundary", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _pointsBeforeBoundaryFilter = new List<GpsPoint>(_allGpsPoints);

            var filteredByBoundaryPoints = _allGpsPoints.Where(p => IsPointInPolygon(p.AsPointLatLng(), boundaryPoints)).ToList();

            _displayedGpsPoints = filteredByBoundaryPoints;
            _currentFilteredGpsPoints = new List<GpsPoint>(_displayedGpsPoints);
            UpdateMapWithPoints();

            System.Windows.MessageBox.Show($"{_displayedGpsPoints.Count} points selected within the boundary.", "Boundary Applied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MainMap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDrawingBoundary && _boundaryPolygon != null)
            {
                var point = e.GetPosition(MainMap);
                var latLng = MainMap.FromLocalToLatLng((int)point.X, (int)point.Y);

                var currentPoints = new List<PointLatLng>(_boundaryPolygon.Points);
                currentPoints.Add(latLng);
                MainMap.Markers.Remove(_boundaryPolygon);
                _boundaryPolygon = new CustomBoundaryPolygon(currentPoints);
                MainMap.Markers.Add(_boundaryPolygon);

                _boundaryPage.SetCurrentBoundaryPoints(_boundaryPolygon.Points.ToList());
                e.Handled = true;
            }
        }

        private bool IsPointInPolygon(PointLatLng point, List<PointLatLng> polygon)
        {
            bool isInside = false;
            if (polygon.Count < 3) return false;

            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if (((polygon[i].Lat > point.Lat) != (polygon[j].Lat > point.Lat)) &&
                    (point.Lng < (polygon[j].Lng - polygon[i].Lng) * (point.Lat - polygon[i].Lat) / (polygon[j].Lat - polygon[i].Lat) + polygon[i].Lng))
                {
                    isInside = !isInside;
                }
            }
            return isInside;
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            MainMap.Zoom += 1;
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            MainMap.Zoom -= 1;
        }

        private void SampleAndDisplayPoints()
        {
            const int maxPointsToShow = 5000;
            var pointsToShow = _displayedGpsPoints;
            if (_displayedGpsPoints.Count > maxPointsToShow)
            {
                int step = _displayedGpsPoints.Count / maxPointsToShow;
                pointsToShow = _displayedGpsPoints.Where((p, i) => i % step == 0).ToList();
            }

            UpdateMapWithPoints();
        }
    }

    public static class GpsPointExtensions
    {
        public static PointLatLng AsPointLatLng(this GpsPoint point)
        {
            return new PointLatLng(point.Latitude, point.Longitude);
        }
    }

    public class CustomBoundaryPolygon : GMapPolygon
    {
        public CustomBoundaryPolygon(IEnumerable<PointLatLng> points) : base(points)
        {
        }

        public override Path CreatePath(List<System.Windows.Point> localPath, bool add)
        {
            var path = base.CreatePath(localPath, add);
            path.Stroke = System.Windows.Media.Brushes.Blue;
            path.StrokeThickness = 3;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.Fill = new SolidColorBrush(System.Windows.Media.Colors.Blue) { Opacity = 0.2 };
            return path;
        }
    }
}