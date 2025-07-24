using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TriangleNet.Geometry;

namespace DEMBuilder.Services.Export
{
    public class RgFdemMetadata
    {
        public string Version { get; set; } = "1.0";
        public string CreatedBy { get; set; } = "DEMBuilder";
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public string FarmName { get; set; } = "";
        public string FieldName { get; set; } = "";
        public double ReferenceLatitude { get; set; }
        public double ReferenceLongitude { get; set; }
        public double Resolution { get; set; }
        public int PixelsX { get; set; }
        public int PixelsY { get; set; }
        public double MinElevation { get; set; }
        public double MaxElevation { get; set; }
        public TriangleNet.Geometry.Rectangle Bounds { get; set; } = new TriangleNet.Geometry.Rectangle();
        public int TotalPoints { get; set; }
        public string ProjectionInfo { get; set; } = "AgOpenGPS Compatible Local Coordinate System";
        public Dictionary<string, object> CustomProperties { get; set; } = new Dictionary<string, object>();
    }

    public class RgFdemProgress
    {
        public string Phase { get; set; } = "";
        public double PercentComplete { get; set; }
        public string Message { get; set; } = "";
    }

    public class RgFdemExportResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public string ErrorMessage { get; set; } = "";
        public TimeSpan ExportTime { get; set; }
        public RgFdemMetadata? Metadata { get; set; }
    }

    /// <summary>
    /// RgF DEM Export Service - Creates single-file agricultural DEM packages
    /// Optimized for thumb drive storage and wireless transfer to ABLS software
    /// </summary>
    public class RgFdemExportService
    {
        public async Task<RgFdemExportResult> ExportRgFdemAsync(
            double[,] rasterData,
            TriangleNet.Geometry.Rectangle bounds,
            string outputPath,
            double resolution,
            double referenceLatitude,
            double referenceLongitude,
            string farmName = "",
            string fieldName = "",
            IProgress<RgFdemProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new RgFdemExportResult();
            var startTime = DateTime.UtcNow;

            try
            {
                progress?.Report(new RgFdemProgress
                {
                    Phase = "Initializing",
                    PercentComplete = 0,
                    Message = "Preparing RgF DEM export..."
                });

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Ensure .RgFdem extension
                if (!outputPath.EndsWith(".RgFdem", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath = Path.ChangeExtension(outputPath, ".RgFdem");
                }

                progress?.Report(new RgFdemProgress
                {
                    Phase = "Metadata",
                    PercentComplete = 10,
                    Message = "Creating metadata..."
                });

                // Create metadata
                var metadata = CreateMetadata(rasterData, bounds, resolution, referenceLatitude, referenceLongitude, farmName, fieldName);

                progress?.Report(new RgFdemProgress
                {
                    Phase = "Compression",
                    PercentComplete = 20,
                    Message = "Compressing DEM data..."
                });

                // Create RgF DEM file (ZIP-based container)
                using (var fileStream = new FileStream(outputPath, FileMode.Create))
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
                {
                    // Add metadata file
                    var metadataEntry = archive.CreateEntry("metadata.json");
                    using (var metadataStream = metadataEntry.Open())
                    {
                        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                        using (var writer = new StreamWriter(metadataStream))
                        {
                            await writer.WriteAsync(metadataJson);
                        }
                    }

                    progress?.Report(new RgFdemProgress
                    {
                        Phase = "DEM Data",
                        PercentComplete = 30,
                        Message = "Writing elevation data..."
                    });

                    // Add DEM data as binary file (more compact than text)
                    var demEntry = archive.CreateEntry("elevation.dem");
                    using (var demStream = demEntry.Open())
                    using (var writer = new BinaryWriter(demStream))
                    {
                        // Write dimensions
                        writer.Write(rasterData.GetLength(0)); // rows
                        writer.Write(rasterData.GetLength(1)); // cols

                        // Write elevation data
                        int totalCells = rasterData.GetLength(0) * rasterData.GetLength(1);
                        int processedCells = 0;

                        for (int row = 0; row < rasterData.GetLength(0); row++)
                        {
                            for (int col = 0; col < rasterData.GetLength(1); col++)
                            {
                                writer.Write((float)rasterData[row, col]);
                                processedCells++;

                                // Update progress periodically
                                if (processedCells % 10000 == 0 || processedCells == totalCells)
                                {
                                    var demProgress = 40 + (processedCells * 40.0 / totalCells);
                                    progress?.Report(new RgFdemProgress
                                    {
                                        Phase = "DEM Data",
                                        PercentComplete = demProgress,
                                        Message = $"Writing elevation data: {processedCells:N0}/{totalCells:N0} cells..."
                                    });
                                }

                                cancellationToken.ThrowIfCancellationRequested();
                            }
                        }
                    }

                    progress?.Report(new RgFdemProgress
                    {
                        Phase = "Coordinate System",
                        PercentComplete = 85,
                        Message = "Adding coordinate system information..."
                    });

                    // Add coordinate system information for ABLS compatibility
                    var coordEntry = archive.CreateEntry("coordinate_system.txt");
                    using (var coordStream = coordEntry.Open())
                    using (var writer = new StreamWriter(coordStream))
                    {
                        await writer.WriteLineAsync("# AgOpenGPS Compatible Coordinate System");
                        await writer.WriteLineAsync($"Reference_Latitude={referenceLatitude:F8}");
                        await writer.WriteLineAsync($"Reference_Longitude={referenceLongitude:F8}");
                        await writer.WriteLineAsync($"Resolution_Meters={resolution:F3}");
                        await writer.WriteLineAsync($"Bounds_Left={bounds.Left:F3}");
                        await writer.WriteLineAsync($"Bounds_Right={bounds.Right:F3}");
                        await writer.WriteLineAsync($"Bounds_Bottom={bounds.Bottom:F3}");
                        await writer.WriteLineAsync($"Bounds_Top={bounds.Top:F3}");
                        await writer.WriteLineAsync($"Projection=Local_Tangent_Plane");
                        await writer.WriteLineAsync($"Units=Meters");
                    }

                    progress?.Report(new RgFdemProgress
                    {
                        Phase = "README",
                        PercentComplete = 90,
                        Message = "Adding documentation..."
                    });

                    // Add README for users
                    var readmeEntry = archive.CreateEntry("README.txt");
                    using (var readmeStream = readmeEntry.Open())
                    using (var writer = new StreamWriter(readmeStream))
                    {
                        await writer.WriteLineAsync("RgF DEM File");
                        await writer.WriteLineAsync("============");
                        await writer.WriteLineAsync();
                        await writer.WriteLineAsync($"Farm: {farmName}");
                        await writer.WriteLineAsync($"Field: {fieldName}");
                        await writer.WriteLineAsync($"Created: {metadata.CreatedDate:yyyy-MM-dd HH:mm:ss} UTC");
                        await writer.WriteLineAsync($"Resolution: {resolution:F3} meters/pixel");
                        await writer.WriteLineAsync($"Size: {metadata.PixelsX} x {metadata.PixelsY} pixels");
                        await writer.WriteLineAsync($"Elevation Range: {metadata.MinElevation:F2}m to {metadata.MaxElevation:F2}m");
                        await writer.WriteLineAsync();
                        await writer.WriteLineAsync("This file contains DEM data in AgOpenGPS-compatible format.");
                        await writer.WriteLineAsync("It can be transferred via USB drive or wireless network to ABLS software.");
                        await writer.WriteLineAsync();
                        await writer.WriteLineAsync("File Contents:");
                        await writer.WriteLineAsync("- metadata.json: Complete metadata and parameters");
                        await writer.WriteLineAsync("- elevation.dem: Binary elevation data");
                        await writer.WriteLineAsync("- coordinate_system.txt: Projection and coordinate information");
                        await writer.WriteLineAsync("- README.txt: This documentation file");
                    }
                }

                progress?.Report(new RgFdemProgress
                {
                    Phase = "Complete",
                    PercentComplete = 100,
                    Message = "RgF DEM export completed successfully!"
                });

                // Set result properties
                result.Success = true;
                result.FilePath = outputPath;
                result.FileSizeBytes = new FileInfo(outputPath).Length;
                result.ExportTime = DateTime.UtcNow - startTime;
                result.Metadata = metadata;

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Export was cancelled by user";
                result.ExportTime = DateTime.UtcNow - startTime;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.ExportTime = DateTime.UtcNow - startTime;
                return result;
            }
        }

        private RgFdemMetadata CreateMetadata(
            double[,] rasterData,
            TriangleNet.Geometry.Rectangle bounds,
            double resolution,
            double referenceLatitude,
            double referenceLongitude,
            string farmName,
            string fieldName)
        {
            var metadata = new RgFdemMetadata
            {
                FarmName = farmName,
                FieldName = fieldName,
                ReferenceLatitude = referenceLatitude,
                ReferenceLongitude = referenceLongitude,
                Resolution = resolution,
                PixelsX = rasterData.GetLength(1),
                PixelsY = rasterData.GetLength(0),
                Bounds = bounds,
                TotalPoints = rasterData.GetLength(0) * rasterData.GetLength(1)
            };

            // Calculate elevation range
            double minElev = double.MaxValue;
            double maxElev = double.MinValue;

            for (int row = 0; row < rasterData.GetLength(0); row++)
            {
                for (int col = 0; col < rasterData.GetLength(1); col++)
                {
                    var elevation = rasterData[row, col];
                    if (!double.IsNaN(elevation))
                    {
                        minElev = Math.Min(minElev, elevation);
                        maxElev = Math.Max(maxElev, elevation);
                    }
                }
            }

            metadata.MinElevation = minElev == double.MaxValue ? 0 : minElev;
            metadata.MaxElevation = maxElev == double.MinValue ? 0 : maxElev;

            // Add custom properties for ABLS compatibility
            metadata.CustomProperties["format_version"] = "1.0";
            metadata.CustomProperties["compatible_software"] = new[] { "ABLS", "AgOpenGPS" };
            metadata.CustomProperties["transfer_optimized"] = true;
            metadata.CustomProperties["coordinate_system"] = "local_tangent_plane";

            return metadata;
        }

        public static string GetRecommendedFileName(string farmName, string fieldName)
        {
            var dateStamp = DateTime.Now.ToString("ddMMyy");
            var safeFarmName = SanitizeFileName(farmName);
            var safeFieldName = SanitizeFileName(fieldName);
            
            if (!string.IsNullOrEmpty(safeFarmName) && !string.IsNullOrEmpty(safeFieldName))
            {
                return $"{safeFarmName}_{safeFieldName}_{dateStamp}.RgFdem";
            }
            else if (!string.IsNullOrEmpty(safeFarmName))
            {
                return $"{safeFarmName}_{dateStamp}.RgFdem";
            }
            else
            {
                return $"DEM_{dateStamp}.RgFdem";
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = fileName;

            foreach (var invalidChar in invalidChars)
            {
                sanitized = sanitized.Replace(invalidChar, '_');
            }

            // Remove multiple consecutive underscores and trim
            while (sanitized.Contains("__"))
            {
                sanitized = sanitized.Replace("__", "_");
            }

            return sanitized.Trim('_');
        }
    }
}
