using DEMBuilder.Services.Export;
using DEMBuilder.Services.Streaming;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TriangleNet.Geometry;

namespace DEMBuilder.Services.Streaming
{
    /// <summary>
    /// High-performance streaming export service for massive DEM datasets
    /// Exports data in chunks to avoid memory limitations
    /// </summary>
    public class StreamingExportService
    {
        private readonly GdalExportService _gdalExportService;
        private readonly int _maxParallelExports;

        public StreamingExportService(int maxParallelExports = 0)
        {
            _gdalExportService = new GdalExportService();
            _maxParallelExports = maxParallelExports <= 0 ? Math.Max(1, Environment.ProcessorCount / 2) : maxParallelExports;
        }

        /// <summary>
        /// Export streaming DEM result as tiled GeoTIFF files
        /// </summary>
        public async Task<StreamingExportResult> ExportStreamingDemAsync(
            StreamingDemResult demResult,
            string outputPath,
            double referenceLatitude,
            double referenceLongitude,
            IProgress<StreamingExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new StreamingExportResult
            {
                OutputPath = outputPath,
                TotalTiles = demResult.ProcessedTiles.Count(t => !t.IsEmpty),
                StartTime = DateTime.UtcNow
            };

            // Create output directory
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Filter out empty tiles
            var tilesToExport = demResult.ProcessedTiles.Where(t => !t.IsEmpty).ToList();
            
            progress?.Report(new StreamingExportProgress
            {
                Phase = "Initializing",
                TotalTiles = tilesToExport.Count,
                Message = $"Exporting {tilesToExport.Count} tiles to GeoTIFF format"
            });

            // Export tiles in parallel batches
            var semaphore = new SemaphoreSlim(_maxParallelExports);
            var exportTasks = new List<Task>();
            var exportedFiles = new List<string>();
            var failedExports = new List<FailedExport>();

            string baseFileName = Path.GetFileNameWithoutExtension(outputPath);
            string extension = Path.GetExtension(outputPath);

            for (int i = 0; i < tilesToExport.Count; i++)
            {
                var tile = tilesToExport[i];
                var tileFileName = $"{baseFileName}_tile_{tile.Bounds.TileX:D4}_{tile.Bounds.TileY:D4}{extension}";
                var tileFilePath = Path.Combine(outputDir!, tileFileName);

                exportTasks.Add(ExportTileAsync(tile, tileFilePath, demResult.Resolution, 
                    referenceLatitude, referenceLongitude, exportedFiles, failedExports, 
                    progress, semaphore, cancellationToken));
            }

            await Task.WhenAll(exportTasks);

            result.ExportedFiles = exportedFiles.ToList();
            result.FailedExports = failedExports.ToList();
            result.SuccessfulTiles = exportedFiles.Count;
            result.FailedTiles = failedExports.Count;
            result.EndTime = DateTime.UtcNow;
            result.IsComplete = true;

            progress?.Report(new StreamingExportProgress
            {
                Phase = "Complete",
                TilesCompleted = result.SuccessfulTiles,
                TotalTiles = tilesToExport.Count,
                Message = $"Export complete - {result.SuccessfulTiles} tiles exported, {result.FailedTiles} failed"
            });

            return result;
        }

        /// <summary>
        /// Export streaming DEM result as a single merged GeoTIFF (memory intensive)
        /// </summary>
        public async Task<bool> ExportMergedGeoTiffAsync(
            StreamingDemResult demResult,
            string outputPath,
            double referenceLatitude,
            double referenceLongitude,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                progress?.Report("Merging tiles into single raster...");

                // Calculate merged raster dimensions
                var bounds = demResult.Bounds;
                if (bounds == null)
                {
                    throw new InvalidOperationException("DEM result bounds are not available for export.");
                }
                
                int totalWidth = (int)Math.Ceiling(bounds.Width / demResult.Resolution);
                int totalHeight = (int)Math.Ceiling(bounds.Height / demResult.Resolution);

                // Check if merged raster would be too large
                long totalPixels = (long)totalWidth * totalHeight;
                const long maxPixels = 500_000_000; // 500M pixels (~4GB for double array)
                
                if (totalPixels > maxPixels)
                {
                    progress?.Report($"Error: Merged raster too large ({totalPixels:N0} pixels). Use tiled export instead.");
                    return false;
                }

                // Create merged raster array
                var mergedRaster = new double[totalWidth, totalHeight];
                
                // Initialize with NODATA values
                progress?.Report("Initializing merged raster...");
                Parallel.For(0, totalHeight, y =>
                {
                    for (int x = 0; x < totalWidth; x++)
                    {
                        mergedRaster[x, y] = -9999.0;
                    }
                });

                // Merge tiles into the main raster
                progress?.Report("Merging tile data...");
                var nonEmptyTiles = demResult.ProcessedTiles.Where(t => !t.IsEmpty && t.RasterData != null).ToList();
                
                Parallel.ForEach(nonEmptyTiles, tile =>
                {
                    MergeTileIntoRaster(tile, mergedRaster, bounds, demResult.Resolution);
                });

                // Export merged raster using GDAL
                progress?.Report("Exporting merged GeoTIFF...");
                return await _gdalExportService.ExportDemToGeoTiffAsync(
                    mergedRaster, bounds, outputPath, demResult.Resolution, 0, // 0 = single file
                    referenceLatitude, referenceLongitude, progress);
            }
            catch (Exception ex)
            {
                progress?.Report($"Merged export failed: {ex.Message}");
                return false;
            }
        }

        private async Task ExportTileAsync(
            DemTile tile,
            string outputPath,
            double resolution,
            double referenceLatitude,
            double referenceLongitude,
            List<string> exportedFiles,
            List<FailedExport> failedExports,
            IProgress<StreamingExportProgress>? progress,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken);

            try
            {
                if (tile.RasterData == null)
                {
                    lock (failedExports)
                    {
                        failedExports.Add(new FailedExport
                        {
                            TileIndex = tile.TileIndex,
                            OutputPath = outputPath,
                            ErrorMessage = "Tile has no raster data"
                        });
                    }
                    return;
                }

                // Convert tile bounds to TriangleNet Rectangle for GDAL export
                var tileBounds = new TriangleNet.Geometry.Rectangle(
                    tile.Bounds.MinX, tile.Bounds.MinY, 
                    tile.Bounds.MaxX, tile.Bounds.MaxY);

                // Export this tile
                bool success = await _gdalExportService.ExportDemToGeoTiffAsync(
                    tile.RasterData, tileBounds, outputPath, resolution, 0, // 0 = single file per tile
                    referenceLatitude, referenceLongitude);

                if (success)
                {
                    lock (exportedFiles)
                    {
                        exportedFiles.Add(outputPath);
                    }
                }
                else
                {
                    lock (failedExports)
                    {
                        failedExports.Add(new FailedExport
                        {
                            TileIndex = tile.TileIndex,
                            OutputPath = outputPath,
                            ErrorMessage = "GDAL export failed"
                        });
                    }
                }

                // Update progress
                int completedCount;
                lock (exportedFiles)
                {
                    completedCount = exportedFiles.Count;
                }

                progress?.Report(new StreamingExportProgress
                {
                    Phase = "Exporting",
                    TilesCompleted = completedCount,
                    TotalTiles = -1, // Will be set by caller
                    CurrentTile = tile.TileIndex,
                    Message = $"Exported tile {tile.TileIndex} ({tile.PointCount} points)"
                });
            }
            catch (Exception ex)
            {
                lock (failedExports)
                {
                    failedExports.Add(new FailedExport
                    {
                        TileIndex = tile.TileIndex,
                        OutputPath = outputPath,
                        ErrorMessage = ex.Message
                    });
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        private void MergeTileIntoRaster(DemTile tile, double[,] mergedRaster, TriangleNet.Geometry.Rectangle bounds, double resolution)
        {
            if (tile.RasterData == null) return;

            int tileWidth = tile.RasterData.GetLength(0);
            int tileHeight = tile.RasterData.GetLength(1);

            // Calculate offset of this tile in the merged raster
            int offsetX = (int)Math.Round((tile.Bounds.MinX - bounds.Left) / resolution);
            int offsetY = (int)Math.Round((bounds.Top - tile.Bounds.MaxY) / resolution);

            // Copy tile data to merged raster
            for (int tileY = 0; tileY < tileHeight; tileY++)
            {
                for (int tileX = 0; tileX < tileWidth; tileX++)
                {
                    int mergedX = offsetX + tileX;
                    int mergedY = offsetY + tileY;

                    // Bounds check
                    if (mergedX >= 0 && mergedX < mergedRaster.GetLength(0) &&
                        mergedY >= 0 && mergedY < mergedRaster.GetLength(1))
                    {
                        double value = tile.RasterData[tileX, tileY];
                        if (value > -9998) // Only copy valid data
                        {
                            mergedRaster[mergedX, mergedY] = value;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get export statistics and recommendations
        /// </summary>
        public ExportRecommendation GetExportRecommendation(StreamingDemResult demResult)
        {
            var nonEmptyTiles = demResult.ProcessedTiles.Where(t => !t.IsEmpty).ToList();
            long totalPixels = nonEmptyTiles.Sum(t => t.RasterPixels);
            double estimatedMemoryMB = totalPixels * sizeof(double) / (1024.0 * 1024.0);

            return new ExportRecommendation
            {
                TotalTiles = nonEmptyTiles.Count,
                TotalPixels = totalPixels,
                EstimatedMemoryMB = estimatedMemoryMB,
                RecommendTiledExport = estimatedMemoryMB > 1000, // > 1GB
                RecommendMergedExport = estimatedMemoryMB <= 1000,
                MaxRecommendedTileSize = estimatedMemoryMB > 2000 ? 100 : 200,
                EstimatedExportTimeMinutes = Math.Max(1, nonEmptyTiles.Count * 0.1) // ~6 seconds per tile
            };
        }
    }

    public class StreamingExportResult
    {
        public string OutputPath { get; set; } = "";
        public int TotalTiles { get; set; }
        public int SuccessfulTiles { get; set; }
        public int FailedTiles { get; set; }
        public List<string> ExportedFiles { get; set; } = new();
        public List<FailedExport> FailedExports { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool IsComplete { get; set; }
        
        public TimeSpan ExportTime => EndTime?.Subtract(StartTime) ?? DateTime.UtcNow.Subtract(StartTime);
        public double SuccessRate => TotalTiles > 0 ? (double)SuccessfulTiles / TotalTiles * 100 : 0;
    }

    public class StreamingExportProgress
    {
        public string Phase { get; set; } = "";
        public int TilesCompleted { get; set; }
        public int TotalTiles { get; set; }
        public int CurrentTile { get; set; }
        public string Message { get; set; } = "";
        public double PercentComplete => TotalTiles > 0 ? (double)TilesCompleted / TotalTiles * 100 : 0;
    }

    public class FailedExport
    {
        public int TileIndex { get; set; }
        public string OutputPath { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public class ExportRecommendation
    {
        public int TotalTiles { get; set; }
        public long TotalPixels { get; set; }
        public double EstimatedMemoryMB { get; set; }
        public bool RecommendTiledExport { get; set; }
        public bool RecommendMergedExport { get; set; }
        public double MaxRecommendedTileSize { get; set; }
        public double EstimatedExportTimeMinutes { get; set; }
        
        public string GetRecommendationText()
        {
            if (RecommendTiledExport)
            {
                return $"Recommend tiled export: {TotalTiles} tiles, ~{EstimatedMemoryMB:F0}MB memory required. " +
                       $"Estimated time: {EstimatedExportTimeMinutes:F1} minutes.";
            }
            else
            {
                return $"Can use merged export: {TotalPixels:N0} pixels, ~{EstimatedMemoryMB:F0}MB memory required. " +
                       $"Estimated time: {EstimatedExportTimeMinutes:F1} minutes.";
            }
        }
    }
}
