using DEMBuilder.Services.Streaming;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DEMBuilder.Services.Streaming
{
    /// <summary>
    /// Central performance manager for coordinating massive dataset processing
    /// Orchestrates streaming data processing, DEM generation, and export
    /// </summary>
    public class PerformanceManager
    {
        private readonly StreamingDataProcessor _dataProcessor;
        private readonly StreamingDemService _demService;
        private readonly StreamingExportService _exportService;
        private readonly PerformanceMonitor _monitor;

        public PerformanceManager()
        {
            _dataProcessor = new StreamingDataProcessor();
            _demService = new StreamingDemService();
            _exportService = new StreamingExportService();
            _monitor = new PerformanceMonitor();
        }

        /// <summary>
        /// Process massive GPS dataset end-to-end with performance optimization
        /// </summary>
        public async Task<MassiveDatasetResult> ProcessMassiveDatasetAsync(
            string inputFolderPath,
            string outputPath,
            double resolution,
            double referenceLatitude,
            double referenceLongitude,
            bool includeSubfolders = true,
            IProgress<MassiveDatasetProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new MassiveDatasetResult
            {
                InputPath = inputFolderPath,
                OutputPath = outputPath,
                Resolution = resolution,
                StartTime = DateTime.UtcNow
            };

            _monitor.StartMonitoring();

            try
            {
                // Phase 1: Data Loading and Processing
                progress?.Report(new MassiveDatasetProgress
                {
                    Phase = "Loading Data",
                    OverallProgress = 0,
                    Message = "Starting massive dataset processing..."
                });

                var dataResult = await _dataProcessor.ProcessGpsFilesAsync(
                    inputFolderPath, includeSubfolders,
                    new Progress<StreamingProgress>(sp => 
                    {
                        progress?.Report(new MassiveDatasetProgress
                        {
                            Phase = "Loading Data",
                            OverallProgress = sp.PercentComplete * 0.3, // 30% of total
                            Message = $"Processing {sp.CurrentFile}: {sp.PointsProcessed:N0} points loaded",
                            DataProgress = sp
                        });
                    }), cancellationToken);

                result.DataResult = dataResult;
                result.TotalPointsProcessed = dataResult.PointsProcessed;

                // Phase 2: DEM Generation
                progress?.Report(new MassiveDatasetProgress
                {
                    Phase = "Generating DEM",
                    OverallProgress = 30,
                    Message = $"Generating DEM from {dataResult.PointsProcessed:N0} GPS points..."
                });

                var demResult = await _demService.GenerateStreamingDemAsync(
                    dataResult, resolution,
                    new Progress<StreamingDemProgress>(dp =>
                    {
                        progress?.Report(new MassiveDatasetProgress
                        {
                            Phase = "Generating DEM",
                            OverallProgress = 30 + (dp.PercentComplete * 0.4), // 40% of total
                            Message = dp.Message,
                            DemProgress = dp
                        });
                    }), cancellationToken);

                result.DemResult = demResult;

                // Phase 3: Export Decision
                var exportRecommendation = _exportService.GetExportRecommendation(demResult);
                result.ExportRecommendation = exportRecommendation;

                progress?.Report(new MassiveDatasetProgress
                {
                    Phase = "Exporting",
                    OverallProgress = 70,
                    Message = exportRecommendation.GetRecommendationText()
                });

                // Phase 4: Export
                if (exportRecommendation.RecommendTiledExport)
                {
                    // Tiled export for large datasets
                    var exportResult = await _exportService.ExportStreamingDemAsync(
                        demResult, outputPath, referenceLatitude, referenceLongitude,
                        new Progress<StreamingExportProgress>(ep =>
                        {
                            progress?.Report(new MassiveDatasetProgress
                            {
                                Phase = "Exporting (Tiled)",
                                OverallProgress = 70 + (ep.PercentComplete * 0.3), // 30% of total
                                Message = ep.Message,
                                ExportProgress = ep
                            });
                        }), cancellationToken);

                    result.ExportResult = exportResult;
                    result.ExportSuccess = exportResult.SuccessfulTiles > 0;
                }
                else
                {
                    // Merged export for smaller datasets
                    var exportSuccess = await _exportService.ExportMergedGeoTiffAsync(
                        demResult, outputPath, referenceLatitude, referenceLongitude,
                        new Progress<string>(message =>
                        {
                            progress?.Report(new MassiveDatasetProgress
                            {
                                Phase = "Exporting (Merged)",
                                OverallProgress = 85, // Estimate
                                Message = message
                            });
                        }), cancellationToken);

                    result.ExportSuccess = exportSuccess;
                }

                result.EndTime = DateTime.UtcNow;
                result.IsComplete = true;

                // Final performance report
                var perfStats = _monitor.GetCurrentStats();
                result.PerformanceStats = perfStats;

                progress?.Report(new MassiveDatasetProgress
                {
                    Phase = "Complete",
                    OverallProgress = 100,
                    Message = $"Processing complete! {result.TotalPointsProcessed:N0} points processed in {result.ProcessingTime.TotalMinutes:F1} minutes"
                });

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.UtcNow;
                
                progress?.Report(new MassiveDatasetProgress
                {
                    Phase = "Error",
                    OverallProgress = 0,
                    Message = $"Processing failed: {ex.Message}"
                });

                return result;
            }
            finally
            {
                _monitor.StopMonitoring();
            }
        }

        /// <summary>
        /// Get system performance recommendations for dataset size
        /// </summary>
        public SystemRecommendation GetSystemRecommendation(long estimatedPoints, double resolution, double areaHectares)
        {
            var availableMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
            var processorCount = Environment.ProcessorCount;
            
            // Estimate memory requirements
            double pointsMemoryMB = estimatedPoints * 32 / (1024.0 * 1024.0); // ~32 bytes per GPS point
            double rasterMemoryMB = (areaHectares * 10000 / (resolution * resolution)) * 8 / (1024.0 * 1024.0); // 8 bytes per pixel
            double totalEstimatedMB = pointsMemoryMB + rasterMemoryMB;

            return new SystemRecommendation
            {
                EstimatedPoints = estimatedPoints,
                EstimatedMemoryMB = totalEstimatedMB,
                AvailableMemoryMB = availableMemoryMB,
                ProcessorCount = processorCount,
                CanProcessInMemory = totalEstimatedMB < (availableMemoryMB * 0.5), // Use max 50% of available memory
                RecommendedChunkSize = CalculateOptimalChunkSize(estimatedPoints, availableMemoryMB),
                RecommendedParallelTasks = Math.Min(processorCount, Math.Max(2, processorCount - 1)),
                EstimatedProcessingTimeMinutes = CalculateEstimatedTime(estimatedPoints, areaHectares, processorCount),
                Warnings = GenerateWarnings(totalEstimatedMB, availableMemoryMB, estimatedPoints)
            };
        }

        private int CalculateOptimalChunkSize(long totalPoints, long availableMemoryMB)
        {
            // Target using ~25% of available memory for point chunks
            long targetMemoryMB = availableMemoryMB / 4;
            long maxPointsInMemory = targetMemoryMB * 1024 * 1024 / 32; // 32 bytes per point
            
            return (int)Math.Min(50000, Math.Max(5000, maxPointsInMemory));
        }

        private double CalculateEstimatedTime(long points, double areaHectares, int processorCount)
        {
            // Rough estimates based on performance testing
            double dataLoadingMinutes = points / 100000.0; // ~100k points per minute
            double demGenerationMinutes = (areaHectares / 100.0) * (points / 50000.0) / processorCount; // Scales with area and points
            double exportMinutes = areaHectares / 50.0; // ~50 hectares per minute for export
            
            return Math.Max(1, dataLoadingMinutes + demGenerationMinutes + exportMinutes);
        }

        private List<string> GenerateWarnings(double estimatedMemoryMB, long availableMemoryMB, long points)
        {
            var warnings = new List<string>();
            
            if (estimatedMemoryMB > availableMemoryMB * 0.8)
            {
                warnings.Add($"High memory usage warning: Estimated {estimatedMemoryMB:F0}MB vs {availableMemoryMB}MB available");
            }
            
            if (points > 1000000)
            {
                warnings.Add($"Very large dataset: {points:N0} points may require extended processing time");
            }
            
            if (Environment.ProcessorCount < 4)
            {
                warnings.Add("Limited CPU cores detected - processing may be slower on this system");
            }
            
            return warnings;
        }
    }

    public class MassiveDatasetResult
    {
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public double Resolution { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool IsComplete { get; set; }
        public bool ExportSuccess { get; set; }
        public long TotalPointsProcessed { get; set; }
        public string? ErrorMessage { get; set; }
        
        public StreamingDataResult? DataResult { get; set; }
        public StreamingDemResult? DemResult { get; set; }
        public StreamingExportResult? ExportResult { get; set; }
        public ExportRecommendation? ExportRecommendation { get; set; }
        public PerformanceStats? PerformanceStats { get; set; }
        
        public TimeSpan ProcessingTime => EndTime?.Subtract(StartTime) ?? DateTime.UtcNow.Subtract(StartTime);
        public double PointsPerSecond => ProcessingTime.TotalSeconds > 0 ? TotalPointsProcessed / ProcessingTime.TotalSeconds : 0;
    }

    public class MassiveDatasetProgress
    {
        public string Phase { get; set; } = "";
        public double OverallProgress { get; set; }
        public string Message { get; set; } = "";
        
        public StreamingProgress? DataProgress { get; set; }
        public StreamingDemProgress? DemProgress { get; set; }
        public StreamingExportProgress? ExportProgress { get; set; }
    }

    public class SystemRecommendation
    {
        public long EstimatedPoints { get; set; }
        public double EstimatedMemoryMB { get; set; }
        public long AvailableMemoryMB { get; set; }
        public int ProcessorCount { get; set; }
        public bool CanProcessInMemory { get; set; }
        public int RecommendedChunkSize { get; set; }
        public int RecommendedParallelTasks { get; set; }
        public double EstimatedProcessingTimeMinutes { get; set; }
        public List<string> Warnings { get; set; } = new();
        
        public string GetRecommendationSummary()
        {
            var summary = $"System can handle ~{EstimatedPoints:N0} points using {EstimatedMemoryMB:F0}MB memory. ";
            summary += $"Estimated processing time: {EstimatedProcessingTimeMinutes:F1} minutes. ";
            
            if (CanProcessInMemory)
            {
                summary += "Dataset can be processed efficiently in available memory.";
            }
            else
            {
                summary += "Large dataset - will use streaming processing to manage memory usage.";
            }
            
            return summary;
        }
    }

    /// <summary>
    /// Performance monitoring for system resource usage
    /// </summary>
    public class PerformanceMonitor
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _memoryCounter;
        private System.Threading.Timer? _monitoringTimer;
        private readonly List<PerformanceSample> _samples = new();
        private bool _isMonitoring;

        public PerformanceMonitor()
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            
            _isMonitoring = true;
            _samples.Clear();
            
            _monitoringTimer = new System.Threading.Timer(CollectSample, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            _monitoringTimer?.Dispose();
            _monitoringTimer = null;
        }

        private void CollectSample(object? state)
        {
            if (!_isMonitoring) return;
            
            try
            {
                var sample = new PerformanceSample
                {
                    Timestamp = DateTime.UtcNow,
                    CpuUsagePercent = _cpuCounter.NextValue(),
                    AvailableMemoryMB = _memoryCounter.NextValue(),
                    UsedMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024)
                };
                
                lock (_samples)
                {
                    _samples.Add(sample);
                    
                    // Keep only last 100 samples
                    if (_samples.Count > 100)
                    {
                        _samples.RemoveAt(0);
                    }
                }
            }
            catch
            {
                // Ignore performance counter errors
            }
        }

        public PerformanceStats GetCurrentStats()
        {
            lock (_samples)
            {
                if (_samples.Count == 0)
                {
                    return new PerformanceStats();
                }
                
                return new PerformanceStats
                {
                    SampleCount = _samples.Count,
                    AverageCpuUsage = _samples.Average(s => s.CpuUsagePercent),
                    MaxCpuUsage = _samples.Max(s => s.CpuUsagePercent),
                    AverageMemoryUsageMB = _samples.Average(s => s.UsedMemoryMB),
                    MaxMemoryUsageMB = _samples.Max(s => s.UsedMemoryMB),
                    MinAvailableMemoryMB = _samples.Min(s => s.AvailableMemoryMB)
                };
            }
        }
    }

    public class PerformanceSample
    {
        public DateTime Timestamp { get; set; }
        public float CpuUsagePercent { get; set; }
        public float AvailableMemoryMB { get; set; }
        public long UsedMemoryMB { get; set; }
    }

    public class PerformanceStats
    {
        public int SampleCount { get; set; }
        public double AverageCpuUsage { get; set; }
        public double MaxCpuUsage { get; set; }
        public double AverageMemoryUsageMB { get; set; }
        public double MaxMemoryUsageMB { get; set; }
        public double MinAvailableMemoryMB { get; set; }
        
        public string GetSummary()
        {
            return $"CPU: {AverageCpuUsage:F1}% avg, {MaxCpuUsage:F1}% peak | " +
                   $"Memory: {AverageMemoryUsageMB:F0}MB avg, {MaxMemoryUsageMB:F0}MB peak | " +
                   $"Available: {MinAvailableMemoryMB:F0}MB min";
        }
    }
}
