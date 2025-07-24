using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DEMBuilder.Models;
using GMap.NET;

namespace DEMBuilder.Services.Boundary
{
    /// <summary>
    /// High-performance boundary filtering service for massive GPS datasets
    /// Uses parallel processing and spatial optimization for point-in-polygon tests
    /// </summary>
    public class HighPerformanceBoundaryFilter
    {
        private readonly int _batchSize;
        private readonly int _progressUpdateInterval;

        public HighPerformanceBoundaryFilter(int batchSize = 10000, int progressUpdateInterval = 1000)
        {
            _batchSize = batchSize;
            _progressUpdateInterval = progressUpdateInterval;
        }

        /// <summary>
        /// Filter GPS points by boundary polygon with parallel processing and progress reporting
        /// </summary>
        public async Task<BoundaryFilterResult> FilterPointsByBoundaryAsync(
            List<GpsPoint> allPoints,
            List<PointLatLng> boundaryPoints,
            string farmName,
            string fieldName,
            IProgress<BoundaryFilterProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (allPoints == null || allPoints.Count == 0)
            {
                return new BoundaryFilterResult { Success = false, ErrorMessage = "No GPS points to filter" };
            }

            if (boundaryPoints == null || boundaryPoints.Count < 3)
            {
                return new BoundaryFilterResult { Success = false, ErrorMessage = "Invalid boundary - need at least 3 points" };
            }

            var result = new BoundaryFilterResult
            {
                TotalPoints = allPoints.Count,
                FarmName = farmName,
                FieldName = fieldName,
                StartTime = DateTime.UtcNow
            };

            try
            {
                progress?.Report(new BoundaryFilterProgress
                {
                    Phase = "Initializing",
                    PointsProcessed = 0,
                    TotalPoints = allPoints.Count,
                    Message = "Optimizing boundary for fast filtering..."
                });

                // Pre-calculate boundary optimization for faster point-in-polygon tests
                var optimizedBoundary = OptimizeBoundaryForFiltering(boundaryPoints);

                progress?.Report(new BoundaryFilterProgress
                {
                    Phase = "Filtering",
                    PointsProcessed = 0,
                    TotalPoints = allPoints.Count,
                    Message = "Starting parallel boundary filtering..."
                });

                // Process points in parallel batches
                var filteredPoints = new List<GpsPoint>();
                var processedCount = 0;
                var batches = CreateBatches(allPoints, _batchSize);
                var lockObject = new object();
                var lastProgressTime = DateTime.UtcNow;

                await Task.Run(() =>
                {
                    Parallel.ForEach(batches, new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    },
                    batch =>
                    {
                        // Process entire batch without UI updates
                        var batchFiltered = FilterBatch(batch, optimizedBoundary);

                        // Thread-safe collection update
                        lock (lockObject)
                        {
                            filteredPoints.AddRange(batchFiltered);
                        }

                        // Update progress more frequently for smoother progress bar
                        var currentCount = Interlocked.Add(ref processedCount, batch.Count);
                        var now = DateTime.UtcNow;
                        
                        // Report progress every 1000 points OR every 100ms OR at completion
                        if (currentCount % _progressUpdateInterval == 0 || 
                            (now - lastProgressTime).TotalMilliseconds >= 100 ||
                            currentCount == allPoints.Count)
                        {
                            lastProgressTime = now;
                            progress?.Report(new BoundaryFilterProgress
                            {
                                Phase = "Filtering",
                                PointsProcessed = currentCount,
                                TotalPoints = allPoints.Count,
                                Message = $"Filtered {currentCount:N0} of {allPoints.Count:N0} points..."
                            });
                        }
                    });
                }, cancellationToken);

                result.Success = true;
                result.FilteredPoints = filteredPoints;
                result.PointsInBoundary = filteredPoints.Count;
                result.PointsOutsideBoundary = allPoints.Count - filteredPoints.Count;
                result.EndTime = DateTime.UtcNow;

                progress?.Report(new BoundaryFilterProgress
                {
                    Phase = "Complete",
                    PointsProcessed = allPoints.Count,
                    TotalPoints = allPoints.Count,
                    Message = $"Boundary filtering complete! {filteredPoints.Count:N0} points selected for {farmName}/{fieldName} in {result.ProcessingTime.TotalSeconds:F1} seconds"
                });

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Boundary filtering cancelled by user";
                result.EndTime = DateTime.UtcNow;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        private OptimizedBoundary OptimizeBoundaryForFiltering(List<PointLatLng> boundaryPoints)
        {
            // Calculate bounding box for quick rejection tests
            var minLat = boundaryPoints.Min(p => p.Lat);
            var maxLat = boundaryPoints.Max(p => p.Lat);
            var minLng = boundaryPoints.Min(p => p.Lng);
            var maxLng = boundaryPoints.Max(p => p.Lng);

            return new OptimizedBoundary
            {
                BoundaryPoints = boundaryPoints,
                BoundingBox = new BoundingBox
                {
                    MinLat = minLat,
                    MaxLat = maxLat,
                    MinLng = minLng,
                    MaxLng = maxLng
                }
            };
        }

        private List<GpsPoint> FilterBatch(List<GpsPoint> batch, OptimizedBoundary optimizedBoundary)
        {
            var filtered = new List<GpsPoint>();

            foreach (var point in batch)
            {
                if (IsPointInOptimizedPolygon(point, optimizedBoundary))
                {
                    filtered.Add(point);
                }
            }

            return filtered;
        }

        private bool IsPointInOptimizedPolygon(GpsPoint point, OptimizedBoundary optimizedBoundary)
        {
            // Quick bounding box rejection test (very fast)
            if (point.Latitude < optimizedBoundary.BoundingBox.MinLat ||
                point.Latitude > optimizedBoundary.BoundingBox.MaxLat ||
                point.Longitude < optimizedBoundary.BoundingBox.MinLng ||
                point.Longitude > optimizedBoundary.BoundingBox.MaxLng)
            {
                return false; // Point is outside bounding box, definitely outside polygon
            }

            // Point is inside bounding box, do precise polygon test
            return IsPointInPolygonRayCasting(point.Latitude, point.Longitude, optimizedBoundary.BoundaryPoints);
        }

        private bool IsPointInPolygonRayCasting(double pointLat, double pointLng, List<PointLatLng> polygon)
        {
            bool isInside = false;
            int polygonCount = polygon.Count;

            for (int i = 0, j = polygonCount - 1; i < polygonCount; j = i++)
            {
                if (((polygon[i].Lat > pointLat) != (polygon[j].Lat > pointLat)) &&
                    (pointLng < (polygon[j].Lng - polygon[i].Lng) * (pointLat - polygon[i].Lat) / (polygon[j].Lat - polygon[i].Lat) + polygon[i].Lng))
                {
                    isInside = !isInside;
                }
            }

            return isInside;
        }

        private List<List<GpsPoint>> CreateBatches(List<GpsPoint> points, int batchSize)
        {
            var batches = new List<List<GpsPoint>>();

            for (int i = 0; i < points.Count; i += batchSize)
            {
                var batch = points.Skip(i).Take(batchSize).ToList();
                batches.Add(batch);
            }

            return batches;
        }

        /// <summary>
        /// Get performance recommendations for boundary filtering based on dataset size
        /// </summary>
        public BoundaryFilterRecommendation GetPerformanceRecommendation(int pointCount, int boundaryVertices)
        {
            var estimatedTimeSeconds = Math.Max(1, pointCount / 100000.0 * boundaryVertices / 4.0); // Rough estimate
            var recommendedBatchSize = pointCount switch
            {
                < 10000 => 5000,
                < 100000 => 15000,
                < 500000 => 25000,
                _ => 50000
            };

            return new BoundaryFilterRecommendation
            {
                PointCount = pointCount,
                BoundaryVertices = boundaryVertices,
                EstimatedTimeSeconds = estimatedTimeSeconds,
                RecommendedBatchSize = recommendedBatchSize,
                RecommendParallelProcessing = pointCount > 10000,
                MemoryUsageMB = pointCount * 64 / (1024 * 1024), // ~64 bytes per point
                PerformanceLevel = pointCount switch
                {
                    < 1000 => "Instant",
                    < 10000 => "Fast",
                    < 100000 => "Moderate",
                    < 500000 => "Slow",
                    _ => "Very Slow"
                }
            };
        }
    }

    public class OptimizedBoundary
    {
        public List<PointLatLng> BoundaryPoints { get; set; } = new();
        public BoundingBox BoundingBox { get; set; } = new();
    }

    public class BoundingBox
    {
        public double MinLat { get; set; }
        public double MaxLat { get; set; }
        public double MinLng { get; set; }
        public double MaxLng { get; set; }
    }

    public class BoundaryFilterResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int TotalPoints { get; set; }
        public int PointsInBoundary { get; set; }
        public int PointsOutsideBoundary { get; set; }
        public List<GpsPoint> FilteredPoints { get; set; } = new();
        public string FarmName { get; set; } = "";
        public string FieldName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public TimeSpan ProcessingTime => EndTime?.Subtract(StartTime) ?? DateTime.UtcNow.Subtract(StartTime);
        public double PointsPerSecond => ProcessingTime.TotalSeconds > 0 ? TotalPoints / ProcessingTime.TotalSeconds : 0;
        public double FilterEfficiency => TotalPoints > 0 ? (double)PointsInBoundary / TotalPoints * 100 : 0;
    }

    public class BoundaryFilterProgress
    {
        public string Phase { get; set; } = "";
        public int PointsProcessed { get; set; }
        public int TotalPoints { get; set; }
        public string Message { get; set; } = "";
        public double PercentComplete => TotalPoints > 0 ? (double)PointsProcessed / TotalPoints * 100 : 0;
    }

    public class BoundaryFilterRecommendation
    {
        public int PointCount { get; set; }
        public int BoundaryVertices { get; set; }
        public double EstimatedTimeSeconds { get; set; }
        public int RecommendedBatchSize { get; set; }
        public bool RecommendParallelProcessing { get; set; }
        public double MemoryUsageMB { get; set; }
        public string PerformanceLevel { get; set; } = "";

        public string GetRecommendationText()
        {
            return $"Dataset: {PointCount:N0} points, {BoundaryVertices} boundary vertices ({PerformanceLevel}) - " +
                   $"Estimated time: {EstimatedTimeSeconds:F1}s, Memory: {MemoryUsageMB:F0}MB";
        }
    }
}
