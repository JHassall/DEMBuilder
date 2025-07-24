using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DEMBuilder.Models;

namespace DEMBuilder.Services.Projection
{
    /// <summary>
    /// High-performance parallel projection service for massive GPS datasets
    /// Optimized for 600,000+ points with minimal UI blocking
    /// </summary>
    public class HighPerformanceProjection
    {
        private readonly int _batchSize;
        private readonly int _progressUpdateInterval;

        public HighPerformanceProjection(int batchSize = 10000, int progressUpdateInterval = 1000)
        {
            _batchSize = batchSize;
            _progressUpdateInterval = progressUpdateInterval;
        }

        /// <summary>
        /// Project GPS points to local coordinate system with parallel processing and progress reporting
        /// </summary>
        public async Task<ProjectionResult> ProjectPointsAsync(
            List<GpsPoint> points,
            double referenceLatitude,
            double referenceLongitude,
            IProgress<ProjectionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (points == null || points.Count == 0)
            {
                return new ProjectionResult { Success = false, ErrorMessage = "No points to project" };
            }

            var result = new ProjectionResult
            {
                TotalPoints = points.Count,
                ReferenceLatitude = referenceLatitude,
                ReferenceLongitude = referenceLongitude,
                StartTime = DateTime.UtcNow
            };

            try
            {
                progress?.Report(new ProjectionProgress
                {
                    Phase = "Initializing",
                    PointsProcessed = 0,
                    TotalPoints = points.Count,
                    Message = "Setting up projection parameters..."
                });

                // Pre-calculate projection constants (expensive operations done once)
                var projectionConstants = CalculateProjectionConstants(referenceLatitude, referenceLongitude);

                progress?.Report(new ProjectionProgress
                {
                    Phase = "Projecting",
                    PointsProcessed = 0,
                    TotalPoints = points.Count,
                    Message = "Starting parallel projection..."
                });

                // Process points in parallel batches
                var processedCount = 0;
                var batches = CreateBatches(points, _batchSize);

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
                        ProcessBatch(batch, projectionConstants);

                        // Update progress periodically (not on every point)
                        var currentCount = Interlocked.Add(ref processedCount, batch.Count);
                        if (currentCount % _progressUpdateInterval == 0 || currentCount == points.Count)
                        {
                            progress?.Report(new ProjectionProgress
                            {
                                Phase = "Projecting",
                                PointsProcessed = currentCount,
                                TotalPoints = points.Count,
                                Message = $"Projected {currentCount:N0} of {points.Count:N0} points..."
                            });
                        }
                    });
                }, cancellationToken);

                result.Success = true;
                result.PointsProcessed = points.Count;
                result.EndTime = DateTime.UtcNow;

                progress?.Report(new ProjectionProgress
                {
                    Phase = "Complete",
                    PointsProcessed = points.Count,
                    TotalPoints = points.Count,
                    Message = $"Projection complete! {points.Count:N0} points processed in {result.ProcessingTime.TotalSeconds:F1} seconds"
                });

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Projection cancelled by user";
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

        private ProjectionConstants CalculateProjectionConstants(double referenceLatitude, double referenceLongitude)
        {
            // Pre-calculate expensive trigonometric operations once
            var latRad = referenceLatitude * Math.PI / 180.0;
            
            // From AgOpenGPS CNMEA.SetLocalMetersPerDegree
            var mPerDegreeLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad) 
                              + 1.175 * Math.Cos(4.0 * latRad) 
                              - 0.0023 * Math.Cos(6.0 * latRad);

            return new ProjectionConstants
            {
                ReferenceLatitude = referenceLatitude,
                ReferenceLongitude = referenceLongitude,
                MetersPerDegreeLat = mPerDegreeLat,
                LatitudeRadians = latRad
            };
        }

        private void ProcessBatch(List<GpsPoint> batch, ProjectionConstants constants)
        {
            // Process entire batch without any UI calls or expensive operations
            foreach (var point in batch)
            {
                ProjectSinglePoint(point, constants);
            }
        }

        private void ProjectSinglePoint(GpsPoint point, ProjectionConstants constants)
        {
            // Optimized projection calculation
            var pointLatRad = point.Latitude * Math.PI / 180.0;
            
            // Calculate meters per degree longitude for this specific latitude
            var mPerDegreeLon = 111412.84 * Math.Cos(pointLatRad) 
                              - 93.5 * Math.Cos(3.0 * pointLatRad) 
                              + 0.118 * Math.Cos(5.0 * pointLatRad);

            // Convert to local coordinates
            point.Northing = (point.Latitude - constants.ReferenceLatitude) * constants.MetersPerDegreeLat;
            point.Easting = (point.Longitude - constants.ReferenceLongitude) * mPerDegreeLon;
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
        /// Get performance recommendations for projection based on dataset size
        /// </summary>
        public ProjectionRecommendation GetPerformanceRecommendation(int pointCount)
        {
            var estimatedTimeSeconds = Math.Max(1, pointCount / 50000.0); // ~50k points per second
            var recommendedBatchSize = pointCount switch
            {
                < 10000 => 1000,
                < 100000 => 5000,
                < 500000 => 10000,
                _ => 25000
            };

            return new ProjectionRecommendation
            {
                PointCount = pointCount,
                EstimatedTimeSeconds = estimatedTimeSeconds,
                RecommendedBatchSize = recommendedBatchSize,
                RecommendParallelProcessing = pointCount > 5000,
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

    public class ProjectionConstants
    {
        public double ReferenceLatitude { get; set; }
        public double ReferenceLongitude { get; set; }
        public double MetersPerDegreeLat { get; set; }
        public double LatitudeRadians { get; set; }
    }

    public class ProjectionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int TotalPoints { get; set; }
        public int PointsProcessed { get; set; }
        public double ReferenceLatitude { get; set; }
        public double ReferenceLongitude { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        
        public TimeSpan ProcessingTime => EndTime?.Subtract(StartTime) ?? DateTime.UtcNow.Subtract(StartTime);
        public double PointsPerSecond => ProcessingTime.TotalSeconds > 0 ? PointsProcessed / ProcessingTime.TotalSeconds : 0;
    }

    public class ProjectionProgress
    {
        public string Phase { get; set; } = "";
        public int PointsProcessed { get; set; }
        public int TotalPoints { get; set; }
        public string Message { get; set; } = "";
        public double PercentComplete => TotalPoints > 0 ? (double)PointsProcessed / TotalPoints * 100 : 0;
    }

    public class ProjectionRecommendation
    {
        public int PointCount { get; set; }
        public double EstimatedTimeSeconds { get; set; }
        public int RecommendedBatchSize { get; set; }
        public bool RecommendParallelProcessing { get; set; }
        public double MemoryUsageMB { get; set; }
        public string PerformanceLevel { get; set; } = "";
        
        public string GetRecommendationText()
        {
            return $"Dataset: {PointCount:N0} points ({PerformanceLevel}) - " +
                   $"Estimated time: {EstimatedTimeSeconds:F1}s, Memory: {MemoryUsageMB:F0}MB";
        }
    }
}
