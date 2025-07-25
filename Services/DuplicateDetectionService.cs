using System;
using System.Collections.Generic;
using System.Linq;
using DEMBuilder.Models;

namespace DEMBuilder.Services
{
    /// <summary>
    /// Service for detecting and filtering duplicate GPS points during import operations.
    /// Supports detection by GPS coordinates, timestamps, or both.
    /// </summary>
    public class DuplicateDetectionService
    {
        /// <summary>
        /// Result of duplicate detection operation
        /// </summary>
        public class DuplicateDetectionResult
        {
            public List<GpsPoint> UniquePoints { get; set; } = new();
            public List<GpsPoint> DuplicatePoints { get; set; } = new();
            public int TotalProcessed { get; set; }
            public int DuplicatesFound { get; set; }
            public TimeSpan ProcessingTime { get; set; }
        }

        /// <summary>
        /// Configuration for duplicate detection behavior
        /// </summary>
        public class DuplicateDetectionOptions
        {
            /// <summary>
            /// Tolerance for coordinate comparison (in decimal degrees)
            /// Default: 0.000001 degrees (~0.1 meters at equator)
            /// </summary>
            public double CoordinateTolerance { get; set; } = 0.000001;

            /// <summary>
            /// Tolerance for elevation comparison (in meters)
            /// Default: 0.01 meters (1 cm)
            /// </summary>
            public double ElevationTolerance { get; set; } = 0.01;
        }

        private readonly DuplicateDetectionOptions _options;

        public DuplicateDetectionService(DuplicateDetectionOptions? options = null)
        {
            _options = options ?? new DuplicateDetectionOptions();
        }

        /// <summary>
        /// Filters out duplicate GPS points from new data based on existing database points
        /// </summary>
        /// <param name="existingPoints">GPS points already in the database</param>
        /// <param name="newPoints">New GPS points to be imported</param>
        /// <param name="progress">Optional progress reporter for real-time duplicate count updates</param>
        /// <returns>Result containing unique points and duplicate statistics</returns>
        public DuplicateDetectionResult FilterDuplicates(
            IEnumerable<GpsPoint> existingPoints, 
            IEnumerable<GpsPoint> newPoints,
            IProgress<(int processed, int duplicatesFound)>? progress = null)
        {
            var startTime = DateTime.Now;
            var result = new DuplicateDetectionResult();

            // Convert to lists for efficient processing
            var existingList = existingPoints.ToList();
            var newList = newPoints.ToList();
            
            result.TotalProcessed = newList.Count;

            // Create lookup structures for fast duplicate detection
            var existingPointSet = new HashSet<string>();

            foreach (var point in existingList)
            {
                existingPointSet.Add(GetPointKey(point.Latitude, point.Longitude, point.Altitude));
            }

            // Process new points and identify duplicates
            int processedCount = 0;
            foreach (var newPoint in newList)
            {
                var pointKey = GetPointKey(newPoint.Latitude, newPoint.Longitude, newPoint.Altitude);
                bool isDuplicate = false;

                // Check for exact duplicate (same position and elevation)
                if (existingPointSet.Contains(pointKey))
                {
                    isDuplicate = true;
                }
                else
                {
                    // Check for near-duplicate within tolerance
                    if (HasNearDuplicatePoint(existingList, newPoint))
                    {
                        isDuplicate = true;
                    }
                }

                if (isDuplicate)
                {
                    result.DuplicatePoints.Add(newPoint);
                    result.DuplicatesFound++;
                }
                else
                {
                    result.UniquePoints.Add(newPoint);
                    
                    // Add to lookup set for subsequent duplicate checks within the new batch
                    existingPointSet.Add(pointKey);
                }
                
                processedCount++;
                
                // Report progress every 1000 points or at completion
                if (processedCount % 1000 == 0 || processedCount == newList.Count)
                {
                    progress?.Report((processedCount, result.DuplicatesFound));
                }
            }

            result.ProcessingTime = DateTime.Now - startTime;
            return result;
        }

        /// <summary>
        /// Creates a point key for exact duplicate detection based on position and elevation
        /// </summary>
        private string GetPointKey(double latitude, double longitude, double altitude)
        {
            // Round to tolerance precision to create consistent keys
            var roundedLat = Math.Round(latitude / _options.CoordinateTolerance) * _options.CoordinateTolerance;
            var roundedLon = Math.Round(longitude / _options.CoordinateTolerance) * _options.CoordinateTolerance;
            var roundedAlt = Math.Round(altitude / _options.ElevationTolerance) * _options.ElevationTolerance;
            return $"{roundedLat:F8},{roundedLon:F8},{roundedAlt:F3}";
        }

        /// <summary>
        /// Checks if a new point has near-duplicate position and elevation in existing points
        /// </summary>
        private bool HasNearDuplicatePoint(List<GpsPoint> existingPoints, GpsPoint newPoint)
        {
            return existingPoints.Any(existing =>
                Math.Abs(existing.Latitude - newPoint.Latitude) <= _options.CoordinateTolerance &&
                Math.Abs(existing.Longitude - newPoint.Longitude) <= _options.CoordinateTolerance &&
                Math.Abs(existing.Altitude - newPoint.Altitude) <= _options.ElevationTolerance);
        }

        /// <summary>
        /// Removes duplicates within a single collection of GPS points
        /// </summary>
        /// <param name="points">GPS points to deduplicate</param>
        /// <returns>Result containing unique points from the input collection</returns>
        public DuplicateDetectionResult RemoveInternalDuplicates(IEnumerable<GpsPoint> points)
        {
            var pointsList = points.ToList();
            return FilterDuplicates(new List<GpsPoint>(), pointsList);
        }

        /// <summary>
        /// Gets summary statistics for duplicate detection operation
        /// </summary>
        /// <param name="result">Duplicate detection result</param>
        /// <returns>Human-readable summary string</returns>
        public static string GetSummary(DuplicateDetectionResult result)
        {
            var uniqueCount = result.UniquePoints.Count;
            var duplicateCount = result.DuplicatesFound;
            var totalCount = result.TotalProcessed;
            var duplicatePercent = totalCount > 0 ? (duplicateCount * 100.0 / totalCount) : 0;

            return $"Processed {totalCount:N0} points: {uniqueCount:N0} unique, {duplicateCount:N0} duplicates ({duplicatePercent:F1}%) in {result.ProcessingTime.TotalMilliseconds:F0}ms";
        }
    }
}
