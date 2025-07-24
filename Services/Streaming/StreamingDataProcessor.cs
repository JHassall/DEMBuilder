using DEMBuilder.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DEMBuilder.Services.Streaming
{
    /// <summary>
    /// High-performance streaming data processor for handling massive GPS datasets
    /// Supports up to 600,000+ GPS points with memory-efficient processing
    /// </summary>
    public class StreamingDataProcessor
    {
        private readonly int _maxPointsInMemory;
        private readonly int _processingChunkSize;
        private readonly int _maxParallelTasks;

        public StreamingDataProcessor(int maxPointsInMemory = 50000, int processingChunkSize = 10000, int maxParallelTasks = 0)
        {
            _maxPointsInMemory = maxPointsInMemory;
            _processingChunkSize = processingChunkSize;
            _maxParallelTasks = maxParallelTasks <= 0 ? Environment.ProcessorCount : maxParallelTasks;
        }

        /// <summary>
        /// Stream process GPS files with parallel processing and memory management
        /// </summary>
        public async Task<StreamingDataResult> ProcessGpsFilesAsync(
            string folderPath, 
            bool includeSubfolders,
            IProgress<StreamingProgress> progress,
            CancellationToken cancellationToken = default)
        {
            var result = new StreamingDataResult();
            var spatialIndex = new SpatialIndex();
            var processingQueue = new ConcurrentQueue<GpsPoint>();
            var processedChunks = new ConcurrentBag<ProcessedChunk>();
            
            // Get all files to process
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(folderPath, "*.txt", searchOption).ToList();
            
            result.TotalFiles = files.Count;
            result.TotalBytes = files.Sum(f => new FileInfo(f).Length);
            
            // Process files in parallel batches
            var semaphore = new SemaphoreSlim(_maxParallelTasks);
            var tasks = new List<Task>();
            
            foreach (var file in files)
            {
                tasks.Add(ProcessFileAsync(file, processingQueue, spatialIndex, result, progress, semaphore, cancellationToken));
            }
            
            // Start chunk processor task
            var chunkProcessorTask = ProcessChunksAsync(processingQueue, processedChunks, result, progress, cancellationToken);
            
            // Wait for all file processing to complete
            await Task.WhenAll(tasks);
            
            // Signal chunk processor to finish
            result.IsFileProcessingComplete = true;
            await chunkProcessorTask;
            
            // Finalize spatial index
            result.SpatialIndex = spatialIndex;
            result.ProcessedChunks = processedChunks.ToList();
            result.IsComplete = true;
            
            return result;
        }

        private async Task ProcessFileAsync(
            string filePath,
            ConcurrentQueue<GpsPoint> processingQueue,
            SpatialIndex spatialIndex,
            StreamingDataResult result,
            IProgress<StreamingProgress> progress,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken);
            
            try
            {
                var fileInfo = new FileInfo(filePath);
                var localPoints = new List<GpsPoint>();
                
                using var reader = new StreamReader(filePath);
                string line;
                long bytesRead = 0;
                
                while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
                {
                    bytesRead += line.Length + Environment.NewLine.Length;
                    
                    var point = ParseGpsLine(line);
                    if (point != null)
                    {
                        localPoints.Add(point);
                        
                        // Add to spatial index for fast lookups
                        spatialIndex.AddPoint(point);
                        
                        // Queue for processing when batch is full
                        if (localPoints.Count >= _processingChunkSize)
                        {
                            foreach (var p in localPoints)
                            {
                                processingQueue.Enqueue(p);
                            }
                            localPoints.Clear();
                        }
                    }
                    
                    // Update progress periodically
                    if (bytesRead % 1024000 == 0) // Every ~1MB
                    {
                        var currentBytes = Interlocked.Add(ref result._bytesProcessed, 1024000);
                        progress?.Report(new StreamingProgress 
                        { 
                            BytesProcessed = currentBytes,
                            TotalBytes = result.TotalBytes,
                            PointsProcessed = result.PointsProcessed,
                            CurrentFile = Path.GetFileName(filePath)
                        });
                    }
                }
                
                // Queue remaining points
                foreach (var p in localPoints)
                {
                    processingQueue.Enqueue(p);
                }
                
                Interlocked.Add(ref result._bytesProcessed, bytesRead);
                Interlocked.Increment(ref result._filesProcessed);
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task ProcessChunksAsync(
            ConcurrentQueue<GpsPoint> processingQueue,
            ConcurrentBag<ProcessedChunk> processedChunks,
            StreamingDataResult result,
            IProgress<StreamingProgress> progress,
            CancellationToken cancellationToken)
        {
            var currentChunk = new List<GpsPoint>();
            
            while (!result.IsFileProcessingComplete || !processingQueue.IsEmpty)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                // Collect points for processing
                while (processingQueue.TryDequeue(out var point) && currentChunk.Count < _processingChunkSize)
                {
                    currentChunk.Add(point);
                }
                
                // Process chunk if we have enough points or if file processing is complete
                if (currentChunk.Count >= _processingChunkSize || 
                    (result.IsFileProcessingComplete && currentChunk.Count > 0))
                {
                    var processedChunk = await ProcessPointChunkAsync(currentChunk);
                    processedChunks.Add(processedChunk);
                    
                    Interlocked.Add(ref result._pointsProcessed, currentChunk.Count);
                    currentChunk.Clear();
                    
                    // Update progress
                    progress?.Report(new StreamingProgress 
                    { 
                        BytesProcessed = result.BytesProcessed,
                        TotalBytes = result.TotalBytes,
                        PointsProcessed = result.PointsProcessed,
                        ChunksProcessed = processedChunks.Count
                    });
                }
                
                // Small delay to prevent busy waiting
                if (processingQueue.IsEmpty && !result.IsFileProcessingComplete)
                {
                    await Task.Delay(10, cancellationToken);
                }
            }
        }

        private async Task<ProcessedChunk> ProcessPointChunkAsync(List<GpsPoint> points)
        {
            return await Task.Run(() =>
            {
                var chunk = new ProcessedChunk
                {
                    Points = new List<GpsPoint>(points),
                    Bounds = CalculateBounds(points),
                    PointCount = points.Count,
                    ProcessedAt = DateTime.UtcNow
                };
                
                // Pre-calculate statistics for this chunk
                chunk.MinAltitude = points.Min(p => p.Altitude);
                chunk.MaxAltitude = points.Max(p => p.Altitude);
                chunk.AvgAltitude = points.Average(p => p.Altitude);
                
                return chunk;
            });
        }

        private GpsPoint? ParseGpsLine(string line)
        {
            try
            {
                // Extract Receiver ID from the '#<id>,' prefix
                var hashIndex = line.IndexOf('#');
                var commaAfterHashIndex = line.IndexOf(',');
                if (hashIndex == -1 || commaAfterHashIndex == -1 || commaAfterHashIndex < hashIndex)
                {
                    return null;
                }

                var receiverIdString = line.Substring(hashIndex + 1, commaAfterHashIndex - hashIndex - 1);
                if (!int.TryParse(receiverIdString, out int receiverId))
                {
                    return null;
                }

                // Find the NMEA sentence
                var nmeaStart = line.IndexOf("$G", commaAfterHashIndex);
                if (nmeaStart == -1) return null;

                var nmeaSentence = line.Substring(nmeaStart);
                
                // Parse NMEA sentence (simplified - can be expanded for more formats)
                if (nmeaSentence.StartsWith("$GPGGA") || nmeaSentence.StartsWith("$GNGGA"))
                {
                    return ParseGGA(nmeaSentence, receiverId);
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        private GpsPoint? ParseGGA(string ggaSentence, int receiverId)
        {
            var parts = ggaSentence.Split(',');
            if (parts.Length < 15) return null;

            try
            {
                // Parse latitude
                if (!double.TryParse(parts[2], out double latDegrees) || string.IsNullOrEmpty(parts[3]))
                    return null;
                
                double latitude = ConvertToDecimalDegrees(latDegrees, parts[3]);

                // Parse longitude  
                if (!double.TryParse(parts[4], out double lonDegrees) || string.IsNullOrEmpty(parts[5]))
                    return null;
                
                double longitude = ConvertToDecimalDegrees(lonDegrees, parts[5]);

                // Parse altitude
                if (!double.TryParse(parts[9], out double altitude))
                    return null;

                return new GpsPoint(receiverId, latitude, longitude, altitude, 0, 0, 0, 0);
            }
            catch
            {
                return null;
            }
        }

        private double ConvertToDecimalDegrees(double degrees, string direction)
        {
            double wholeDegrees = Math.Floor(degrees / 100);
            double minutes = degrees - (wholeDegrees * 100);
            double decimalDegrees = wholeDegrees + (minutes / 60);
            
            if (direction == "S" || direction == "W")
                decimalDegrees = -decimalDegrees;
                
            return decimalDegrees;
        }

        private TriangleNet.Geometry.Rectangle CalculateBounds(List<GpsPoint> points)
        {
            if (points.Count == 0) return new TriangleNet.Geometry.Rectangle();
            
            double minX = points.Min(p => p.Easting);
            double maxX = points.Max(p => p.Easting);
            double minY = points.Min(p => p.Northing);
            double maxY = points.Max(p => p.Northing);
            
            return new TriangleNet.Geometry.Rectangle(minX, minY, maxX, maxY);
        }
    }

    public class StreamingDataResult
    {
        public int TotalFiles { get; set; }
        public long TotalBytes { get; set; }
        
        internal long _bytesProcessed;
        internal int _filesProcessed;
        internal long _pointsProcessed;
        
        public long BytesProcessed => _bytesProcessed;
        public int FilesProcessed => _filesProcessed;
        public long PointsProcessed => _pointsProcessed;
        
        public bool IsFileProcessingComplete { get; set; }
        public bool IsComplete { get; set; }
        public SpatialIndex? SpatialIndex { get; set; }
        public List<ProcessedChunk>? ProcessedChunks { get; set; }
    }

    public class StreamingProgress
    {
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public long PointsProcessed { get; set; }
        public int ChunksProcessed { get; set; }
        public string CurrentFile { get; set; } = "";
        public double PercentComplete => TotalBytes > 0 ? (double)BytesProcessed / TotalBytes * 100 : 0;
    }

    public class ProcessedChunk
    {
        public List<GpsPoint> Points { get; set; } = new();
        public TriangleNet.Geometry.Rectangle Bounds { get; set; } = new TriangleNet.Geometry.Rectangle();
        public int PointCount { get; set; }
        public double MinAltitude { get; set; }
        public double MaxAltitude { get; set; }
        public double AvgAltitude { get; set; }
        public DateTime ProcessedAt { get; set; }
    }
}
