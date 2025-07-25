using DEMBuilder.Models;
using DEMBuilder.Pages;
using DEMBuilder.Services;
using DEMBuilder.Services.Boundary;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DEMBuilder
{
    public partial class MainWindow : Window
    {
        private readonly NmeaParserService _nmeaParserService;
        private readonly HighPerformanceBoundaryFilter _boundaryFilterService;
        private CancellationTokenSource? _boundaryFilterCancellation;
        private List<GpsPoint>? _allGpsPoints;
        private List<GpsPoint>? _displayedGpsPoints;
        private List<GpsPoint>? _pointsBeforeBoundaryFilter;
        private List<GpsPoint>? _projectedGpsPoints;
        private List<GpsPoint>? _currentFilteredGpsPoints;
        private List<GpsPoint>? _excludedGpsPoints;

        private double _referenceLatitude;
        private double _referenceLongitude;
        private TriangleNet.Geometry.Rectangle _bounds = new TriangleNet.Geometry.Rectangle();
        private double[,]? _rasterData;
        private string _farmName = string.Empty;
        private string _fieldName = string.Empty;
        private System.Windows.Media.Imaging.BitmapSource? _demPreviewBitmap;

        private readonly LoadDataPage _loadDataPage;
        private readonly FilterDataPage _filterDataPage;
        private readonly BoundaryPage _boundaryPage;
        private readonly ProjectionPage _projectionPage;
        private readonly DemGenerationPage _demGenerationPage;
        private readonly DemPreviewPage _demPreviewPage;
        private readonly ExportPage _exportPage;
        private readonly TextFileOptionsPage _textFileOptionsPage;
        private readonly GeoTiffOptionsPage _geoTiffOptionsPage;
        private readonly RgFdemOptionsPage _rgFdemOptionsPage;
        private readonly List<System.Windows.Controls.UserControl> _wizardPages;
        private int _currentPageIndex = 0;

        private bool _isDrawingBoundary = false;
        private bool _isBoundaryApplied = false;
        private CustomBoundaryPolygon? _boundaryPolygon;

        public MainWindow()
        {
            InitializeComponent();
            InitializeMap();

            _nmeaParserService = new NmeaParserService();
            _boundaryFilterService = new HighPerformanceBoundaryFilter();

            _loadDataPage = new LoadDataPage();
            _filterDataPage = new FilterDataPage();
            _boundaryPage = new BoundaryPage();
            _projectionPage = new ProjectionPage();
            _demGenerationPage = new DemGenerationPage();
            _demPreviewPage = new DemPreviewPage();
            _exportPage = new ExportPage();
            _textFileOptionsPage = new TextFileOptionsPage();
            _geoTiffOptionsPage = new GeoTiffOptionsPage();
            _rgFdemOptionsPage = new RgFdemOptionsPage();

            _wizardPages = new List<System.Windows.Controls.UserControl>
            {
                _loadDataPage,
                _boundaryPage,
                _filterDataPage,
                _projectionPage,
                _demGenerationPage,
                _demPreviewPage,
                _exportPage
            };

            _loadDataPage.GpsDataLoaded += loadDataPage_GpsDataLoaded;
            
            // Set up duplicate detection by passing existing GPS points to LoadDataPage
            UpdateLoadDataPageWithExistingPoints();

            _boundaryPage.StartDrawing += BoundaryPage_StartDrawing;
            _boundaryPage.FinishDrawing += BoundaryPage_FinishDrawing;
            _boundaryPage.ClearBoundary += BoundaryPage_ClearBoundary;
            _boundaryPage.BoundaryApplied += BoundaryPage_BoundaryApplied;
            _boundaryPage.DeletePointsRequested += BoundaryPage_DeletePointsRequested;

            _filterDataPage.FilterApplied += FilterDataPage_FilterApplied;

            _projectionPage.ProjectionCompleted += ProjectionPage_ProjectionCompleted;

            _demGenerationPage.DemGenerationCompleted += DemGenerationPage_DemGenerationCompleted;

            _demPreviewPage.GoBackRequested += GoToPreviousPage;
            _demPreviewPage.GoToNextPage += GoToNextPage;

            _exportPage.FormatSelected += ExportPage_FormatSelected;
            _exportPage.BackRequested += ExportPage_BackRequested;
            
            // Format-specific options pages event handlers
            _textFileOptionsPage.BackRequested += FormatOptionsPage_BackRequested;
            _geoTiffOptionsPage.BackRequested += FormatOptionsPage_BackRequested;
            _rgFdemOptionsPage.BackRequested += FormatOptionsPage_BackRequested;
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
            WizardFrame.Content = _wizardPages[_currentPageIndex];
            UpdateNavigationButtons();
        }

        private void loadDataPage_GpsDataLoaded(object? sender, Pages.GpsDataLoadedEventArgs e)
        {
            if (e.GpsPoints.Count == 0)
            {
                System.Windows.MessageBox.Show("No valid GPS data points were found in the selected folder.", "No Data Found", MessageBoxButton.OK, MessageBoxImage.Information);
                _allGpsPoints = null;
            }
            else
            {
                // Update GPS data collections
                if (_allGpsPoints == null)
                {
                    // First import - no existing data
                    _allGpsPoints = e.GpsPoints;
                }
                else
                {
                    // Subsequent import - add new points to existing data
                    _allGpsPoints.AddRange(e.GpsPoints);
                }
                
                _displayedGpsPoints = new List<GpsPoint>(_allGpsPoints);
                _pointsBeforeBoundaryFilter = new List<GpsPoint>(_allGpsPoints);
                _currentFilteredGpsPoints = new List<GpsPoint>(_allGpsPoints);
                _excludedGpsPoints = null;
                UpdateMapWithPoints(true);
                
                // Update LoadDataPage with current GPS points for future duplicate detection
                UpdateLoadDataPageWithExistingPoints();
                
                // Show import summary with duplicate detection statistics
                ShowImportSummary(e);
            }
            UpdateNavigationButtons();
        }
        
        /// <summary>
        /// Updates the LoadDataPage with current GPS points for duplicate detection
        /// </summary>
        private void UpdateLoadDataPageWithExistingPoints()
        {
            _loadDataPage.ExistingGpsPoints = _allGpsPoints;
        }
        
        /// <summary>
        /// Shows import summary with duplicate detection statistics
        /// </summary>
        private void ShowImportSummary(Pages.GpsDataLoadedEventArgs e)
        {
            var message = $"GPS Data Import Complete\n\n";
            message += $"Total points processed: {e.TotalPointsProcessed:N0}\n";
            message += $"Unique points imported: {e.GpsPoints.Count:N0}\n";
            
            if (e.DuplicateDetectionEnabled)
            {
                message += $"Duplicate points skipped: {e.DuplicatesSkipped:N0}\n";
                var duplicatePercent = e.TotalPointsProcessed > 0 ? (e.DuplicatesSkipped * 100.0 / e.TotalPointsProcessed) : 0;
                message += $"Duplicate rate: {duplicatePercent:F1}%\n";
            }
            else
            {
                message += "Duplicate detection: Disabled\n";
            }
            
            message += $"\nTotal GPS points in database: {_allGpsPoints?.Count ?? 0:N0}";
            
            System.Windows.MessageBox.Show(message, "Import Summary", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void GoToNextPage(object? sender, EventArgs e)
        {
            GoToNextPage();
        }

        private void GoToPreviousPage(object? sender, EventArgs e)
        {
            GoToPreviousPage();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            GoToNextPage();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            GoToPreviousPage();
        }

        private void GoToNextPage()
        {
            if (_currentPageIndex < _wizardPages.Count - 1)
            {
                _currentPageIndex++;
                WizardFrame.Content = _wizardPages[_currentPageIndex];

                if (_wizardPages[_currentPageIndex] is FilterDataPage)
                {
                    _filterDataPage.SetGpsPoints(_currentFilteredGpsPoints);
                }
                else if (_wizardPages[_currentPageIndex] is ProjectionPage)
                {
                    _projectionPage.SetFilteredPoints(_currentFilteredGpsPoints);
                }
                else if (_wizardPages[_currentPageIndex] is DemGenerationPage)
                {
                    _demGenerationPage.SetData(_projectedGpsPoints);
                }
                else if (_wizardPages[_currentPageIndex] is DemPreviewPage)
                {
                    if (_demPreviewBitmap != null)
                    {
                        _demPreviewPage.SetDemImage(_demPreviewBitmap);
                    }
                }
                else if (_wizardPages[_currentPageIndex] is ExportPage)
                {
                    if (_rasterData != null)
                    {
                        _exportPage.SetExportData(_rasterData, _bounds, _referenceLatitude, _referenceLongitude, _farmName, _fieldName);
                    }
                }

                // Hide main navigation when on the last page
                bool isLastPage = _currentPageIndex == _wizardPages.Count - 1;
                NextButton.Visibility = isLastPage ? Visibility.Collapsed : Visibility.Visible;
                BackButton.Visibility = isLastPage ? Visibility.Collapsed : Visibility.Visible;

                UpdateNavigationButtons();
            }
        }

        private void GoToPreviousPage()
        {
            if (_currentPageIndex > 0)
            {
                _currentPageIndex--;
                WizardFrame.Content = _wizardPages[_currentPageIndex];

                // Ensure main navigation is visible when moving away from the last page
                NextButton.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;

                UpdateNavigationButtons();
            }
        }

        private void UpdateNavigationButtons()
        {
            BackButton.IsEnabled = _currentPageIndex > 0;

            bool isNextEnabled = _currentPageIndex < _wizardPages.Count - 1;
            if (_currentPageIndex == 0) // Load Data
            {
                isNextEnabled &= (_allGpsPoints != null && _allGpsPoints.Any());
            }
            else if (_currentPageIndex == 1) // Boundary
            {
                isNextEnabled &= _isBoundaryApplied;
            }
            NextButton.IsEnabled = isNextEnabled;
        }

        private void FilterDataPage_FilterApplied(object? sender, FilterAppliedEventArgs e)
        {
            _currentFilteredGpsPoints = e.FilteredPoints;
            _excludedGpsPoints = e.ExcludedPoints;
            _displayedGpsPoints = e.FilteredPoints.Concat(e.ExcludedPoints).ToList();
            UpdateMapWithPoints(false); // Don't zoom, just refresh colors
        }

        private void ProjectionPage_ProjectionCompleted(object? sender, ProjectionCompletedEventArgs e)
        {
            _projectedGpsPoints = e.ProjectedPoints;
            _referenceLatitude = e.ReferenceLatitude;
            _referenceLongitude = e.ReferenceLongitude;
        }

        private void DemGenerationPage_DemGenerationCompleted(object? sender, DemGenerationCompletedEventArgs e)
        {
            // Store the DEM generation results for export
            _rasterData = e.RasterData;
            _bounds = e.Bounds;
            
            // Create preview bitmap for display
            _demPreviewBitmap = new Services.Dem.DemGenerationService().CreateDemBitmap(e.RasterData);
            GoToNextPage();
        }

        private void ExportPage_FormatSelected(object? sender, ExportFormatSelectedEventArgs e)
        {
            // Set export data for the selected format-specific options page
            if (_rasterData != null)
            {
                switch (e.SelectedFormat)
                {
                    case ExportFormat.TextFile:
                        _textFileOptionsPage.SetExportData(_rasterData, _bounds, _referenceLatitude, _referenceLongitude, _farmName, _fieldName);
                        WizardFrame.Content = _textFileOptionsPage;
                        break;
                    case ExportFormat.GeoTiff:
                        _geoTiffOptionsPage.SetExportData(_currentFilteredGpsPoints ?? _projectedGpsPoints ?? _allGpsPoints ?? new List<GpsPoint>(), _referenceLatitude, _referenceLongitude, _farmName, _fieldName);
                        WizardFrame.Content = _geoTiffOptionsPage;
                        break;
                    case ExportFormat.RgFdem:
                        _rgFdemOptionsPage.SetExportData(_rasterData, _bounds, _referenceLatitude, _referenceLongitude, _farmName, _fieldName);
                        WizardFrame.Content = _rgFdemOptionsPage;
                        break;
                }
                
                // Hide main navigation buttons since format-specific pages have their own
                NextButton.Visibility = Visibility.Collapsed;
                BackButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ExportPage_BackRequested(object? sender, EventArgs e)
        {
            GoToPreviousPage();
        }

        private void FormatOptionsPage_BackRequested(object? sender, EventArgs e)
        {
            // Navigate back to the Export Page (format selection)
            WizardFrame.Content = _exportPage;
            
            // Restore main navigation buttons
            NextButton.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Visible;
            UpdateNavigationButtons();
        }

        private void UpdateMapWithPoints(bool zoomToFit)
        {
            MainMap.Markers.Clear();

            if (_displayedGpsPoints == null || !_displayedGpsPoints.Any())
                return;

            var pointsToDisplay = _displayedGpsPoints;
            // Only sample points for performance on the initial load and boundary pages.
            // On the filter page (index 2) and beyond, show all points.
            if (_displayedGpsPoints.Count > 10000 && _currentPageIndex < 2)
            {
                // Shuffle before taking a sample to get a representative view
                var random = new Random();
                pointsToDisplay = _displayedGpsPoints.OrderBy(p => random.Next()).Take(10000).ToList();
            }

            var excludedSet = _excludedGpsPoints != null ? new HashSet<GpsPoint>(_excludedGpsPoints) : null;

            foreach (var point in pointsToDisplay)
            {
                SolidColorBrush brush;
                if (excludedSet != null)
                {
                    brush = new SolidColorBrush(excludedSet.Contains(point) ? Colors.Red : Colors.Green);
                }
                else
                {
                    // Before filtering, color by GPS Fix Quality
                    switch (point.FixQuality)
                    {
                        case 4: // RTK Fixed
                        case 5: // RTK Float
                            brush = new SolidColorBrush(Colors.Green);
                            break;
                        default:
                            brush = new SolidColorBrush(Colors.Yellow);
                            break;
                    }
                }

                var marker = new GMapMarker(point.AsPointLatLng())
                {
                    Shape = new Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Fill = brush,
                        Stroke = brush
                    }
                };
                MainMap.Markers.Add(marker);
            }

            if (zoomToFit && MainMap.Markers.Any())
            {
                MainMap.ZoomAndCenterMarkers(null);
            }
        }

        private void BoundaryPage_StartDrawing(object? sender, EventArgs e)
        {
            _isDrawingBoundary = true;
            if (_boundaryPolygon != null)
            {
                MainMap.Markers.Remove(_boundaryPolygon);
            }
            _boundaryPolygon = new CustomBoundaryPolygon(new List<PointLatLng>());
            MainMap.Markers.Add(_boundaryPolygon);
        }

        private void BoundaryPage_FinishDrawing(object? sender, EventArgs e)
        {
            _isDrawingBoundary = false;
        }

        private void BoundaryPage_ClearBoundary(object? sender, EventArgs e)
        {
            _isDrawingBoundary = false;
            _isBoundaryApplied = false;
            MainMap.Markers.Clear();
            _boundaryPolygon = null;
            _displayedGpsPoints = _pointsBeforeBoundaryFilter;
            _currentFilteredGpsPoints = _pointsBeforeBoundaryFilter;
            _excludedGpsPoints = null;
            UpdateMapWithPoints(true);
            UpdateNavigationButtons();
        }

        private void BoundaryPage_DeletePointsRequested(object? sender, BoundaryAppliedEventArgs e)
        {
            if (_allGpsPoints == null || !_allGpsPoints.Any())
            {
                System.Windows.MessageBox.Show("No GPS data available to delete.", "No Data", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var pointsToDelete = _allGpsPoints.Where(p => IsPointInPolygon(p.AsPointLatLng(), e.BoundaryPoints)).ToList();

            if (pointsToDelete.Count == 0)
            {
                System.Windows.MessageBox.Show("No points found within the selected boundary to delete.", "No Points Found", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var result = System.Windows.MessageBox.Show($"Are you sure you want to permanently delete {pointsToDelete.Count} points? This action cannot be undone.",
                "Confirm Deletion", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);

            if (result == MessageBoxResult.OK)
            {
                int deletedCount = pointsToDelete.Count;
                _allGpsPoints.RemoveAll(p => pointsToDelete.Contains(p));

                // After deleting, we are back to a state with no boundary applied.
                _displayedGpsPoints = new List<GpsPoint>(_allGpsPoints);
                _pointsBeforeBoundaryFilter = null; 
                _isBoundaryApplied = false;

                // Now, update the map with the new set of points
                UpdateMapWithPoints(true);

                // Reset the boundary page UI and clear the boundary polygon from the map
                _boundaryPage.Reset();
                if (_boundaryPolygon != null)
                {
                    MainMap.Markers.Remove(_boundaryPolygon);
                    _boundaryPolygon = null;
                }
                
                _boundaryPage.ShowStatusMessage($"{deletedCount} points were permanently deleted.");
            }
        }

        private async void BoundaryPage_BoundaryApplied(object? sender, BoundaryAppliedEventArgs e)
        {
            try
            {
                if (_allGpsPoints == null || !_allGpsPoints.Any())
                {
                    System.Windows.MessageBox.Show("No GPS data loaded to define a boundary.", "No Data", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                if (e.BoundaryPoints.Count < 3)
                {
                    System.Windows.MessageBox.Show("Please define a valid boundary with at least 3 points.", "Invalid Boundary", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

            // Show progress bar and start filtering
            _boundaryPage.ShowProgressBar();
            _boundaryPage.ShowStatusMessage("Filtering GPS points by boundary...");

            // Setup cancellation
            _boundaryFilterCancellation = new CancellationTokenSource();

            try
            {
                // Use high-performance boundary filtering service
                var result = await _boundaryFilterService.FilterPointsByBoundaryAsync(
                    _allGpsPoints, e.BoundaryPoints, e.FarmName, e.FieldName,
                    new Progress<BoundaryFilterProgress>(progress =>
                    {
                        // Update progress bar and status on UI thread
                        Dispatcher.Invoke(() =>
                        {
                            _boundaryPage.UpdateProgress(progress.PercentComplete);
                            _boundaryPage.ShowStatusMessage($"Processing: {progress.PercentComplete:F0}% complete...");
                        });
                    }),
                    _boundaryFilterCancellation.Token);

                if (result.Success)
                {
                    _farmName = e.FarmName;
                    _fieldName = e.FieldName;
                    _pointsBeforeBoundaryFilter = new List<GpsPoint>(_allGpsPoints);
                    _displayedGpsPoints = result.FilteredPoints;
                    _currentFilteredGpsPoints = new List<GpsPoint>(_displayedGpsPoints);
                    _excludedGpsPoints = null;
                    
                    UpdateMapWithPoints(true);
                    
                    _boundaryPage.HideProgressBar();
                    var successMessage = $"✅ {result.PointsInBoundary:N0} points selected for Farm: '{_farmName}', Field: '{_fieldName}'";
                    _boundaryPage.ShowStatusMessage(successMessage);
                    
                    _isBoundaryApplied = true;
                    UpdateNavigationButtons();
                }
                else
                {
                    _boundaryPage.HideProgressBar();
                    _boundaryPage.ShowStatusMessage($"❌ Boundary filtering failed: {result.ErrorMessage}");
                }
            }
            catch (OperationCanceledException)
            {
                _boundaryPage.HideProgressBar();
                _boundaryPage.ShowStatusMessage("⏹️ Boundary filtering cancelled by user.");
            }
            catch (Exception ex)
            {
                _boundaryPage.HideProgressBar();
                _boundaryPage.ShowStatusMessage($"⚠️ Boundary filtering error: {ex.Message}");
            }
            finally
            {
                _boundaryFilterCancellation?.Dispose();
                _boundaryFilterCancellation = null;
            }
            }
            catch (Exception globalEx)
            {
                // Catch any exception that might prevent the method from running
                _boundaryPage.HideProgressBar();
                _boundaryPage.ShowStatusMessage($"🚨 Critical error in boundary assignment: {globalEx.Message}");
                System.Windows.MessageBox.Show($"Boundary assignment failed with error: {globalEx.Message}\n\nStack trace: {globalEx.StackTrace}", "Critical Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
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
        public CustomBoundaryPolygon(IEnumerable<PointLatLng> points) : base(points) { }

        public override Path CreatePath(List<System.Windows.Point> localPath, bool add)
        {
            var path = base.CreatePath(localPath, add);
            path.Stroke = System.Windows.Media.Brushes.Blue;
            path.StrokeThickness = 3;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue) { Opacity = 0.2 };
            return path;
        }
    }
}