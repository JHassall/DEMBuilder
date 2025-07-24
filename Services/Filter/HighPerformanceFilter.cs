using DEMBuilder.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DEMBuilder.Services.Filter
{
    public class FilterProgress
    {
        public string Phase { get; set; } = "";
        public int PointsProcessed { get; set; }
        public int TotalPoints { get; set; }
        public double PercentComplete => TotalPoints > 0 ? (double)PointsProcessed / TotalPoints * 100 : 0;
        public string Message { get; set; } = "";
    }

    public class FilterResult
    {
        public bool Success { get; set; }
        public List<GpsPoint> FilteredPoints { get; set; } = new List<GpsPoint>();
        public List<GpsPoint> ExcludedPoints { get; set; } = new List<GpsPoint>();
        public int PointsIncluded { get; set; }
        public int PointsExcluded { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan ProcessingTime => EndTime - StartTime;
        public double PointsPerSecond => ProcessingTime.TotalSeconds > 0 ? (PointsIncluded + PointsExcluded) / ProcessingTime.TotalSeconds : 0;
        public string ErrorMessage { get; set; } = "";
    }

    public class FilterCriteria
    {
        public double? MaxHdop { get; set; }
        public HashSet<int> AllowedFixQualities { get; set; } = new HashSet<int>();
        public double? MaxAgeOfDiff { get; set; }
    }

    public class HighPerformanceFilter
    {
        private readonly int _batchSize;
        private readonly int _progressUpdateInterval;

        public HighPerformanceFilter(int batchSize = 10000, int progressUpdateInterval = 1000)
        {
            _batchSize = batchSize;
            _progressUpdateInterval = progressUpdateInterval;
        }

        /// <summary>
        /// Filter GPS points with parallel processing and progress reporting
        /// </summary>
        public async Task<FilterResult> FilterPointsAsync(
            List<GpsPoint> allPoints,
            FilterCriteria criteria,
            IProgress<FilterProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new FilterResult
            {
                StartTime = DateTime.UtcNow
            };

            try
            {
                if (!allPoints.Any())
                {
                    result.Success = true;
                    result.EndTime = DateTime.UtcNow;
                    return result;
                }

                progress?.Report(new FilterProgress
                {
                    Phase = "Starting",
                    PointsProcessed = 0,
                    TotalPoints = allPoints.Count,
                    Message = "Initializing filter process..."
                });

                var processedCount = 0;
                var filteredPoints = new List<GpsPoint>();
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
                        // Process entire batch
                        var batchFiltered = FilterBatch(batch, criteria);

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
                            progress?.Report(new FilterProgress
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
                result.ExcludedPoints = allPoints.Except(filteredPoints).ToList();
                result.PointsIncluded = filteredPoints.Count;
                result.PointsExcluded = result.ExcludedPoints.Count;
                result.EndTime = DateTime.UtcNow;

                progress?.Report(new FilterProgress
                {
                    Phase = "Complete",
                    PointsProcessed = allPoints.Count,
                    TotalPoints = allPoints.Count,
                    Message = $"Filtering complete: {result.PointsIncluded:N0} included, {result.PointsExcluded:N0} excluded"
                });

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

        private List<GpsPoint> FilterBatch(List<GpsPoint> batch, FilterCriteria criteria)
        {
            var filtered = batch.AsEnumerable();

            // Apply HDOP filter
            if (criteria.MaxHdop.HasValue)
            {
                filtered = filtered.Where(p => p.Hdop <= criteria.MaxHdop.Value);
            }

            // Apply RTK Status filter
            if (criteria.AllowedFixQualities.Any())
            {
                filtered = filtered.Where(p => criteria.AllowedFixQualities.Contains(p.FixQuality));
            }

            // Apply Age of Differential filter
            if (criteria.MaxAgeOfDiff.HasValue)
            {
                filtered = filtered.Where(p => p.AgeOfDiff <= criteria.MaxAgeOfDiff.Value);
            }

            return filtered.ToList();
        }
    }
}
