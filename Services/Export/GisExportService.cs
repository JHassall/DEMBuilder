using DEMBuilder.Models;
using DEMBuilder.Services;
using DEMBuilder.Services.Dem;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TriangleNet.Geometry;

namespace DEMBuilder.Services.Export
{
    public static class GdalDebugLogger
    {
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gdal_debug.log");
        
        public static void Log(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logEntry = $"[{timestamp}] {message}\n";
                File.AppendAllText(LogFilePath, logEntry);
                Debug.WriteLine($"GDAL DEBUG: {message}");
            }
            catch
            {
                // Ignore logging errors to prevent cascading failures
            }
        }
        
        public static void LogError(string operation, Exception ex)
        {
            var message = $"ERROR in {operation}: {ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
            {
                message += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            }
            message += $" | StackTrace: {ex.StackTrace}";
            Log(message);
        }
        
        public static void LogGdalInfo()
        {
            try
            {
                Log($"GDAL Version: {Gdal.VersionInfo("RELEASE_NAME")}");
                Log($"GDAL Driver Count: {Gdal.GetDriverCount()}");
                Log($"GDAL Data Path: {Gdal.GetConfigOption("GDAL_DATA", "NOT SET")}");
                Log($"PROJ Lib Path: {Gdal.GetConfigOption("PROJ_LIB", "NOT SET")}");
                
                // List available drivers
                var drivers = new StringBuilder("Available GDAL Drivers: ");
                for (int i = 0; i < Math.Min(10, Gdal.GetDriverCount()); i++)
                {
                    var driver = Gdal.GetDriver(i);
                    drivers.Append($"{driver.ShortName}, ");
                }
                Log(drivers.ToString());
            }
            catch (Exception ex)
            {
                LogError("LogGdalInfo", ex);
            }
        }
        
        public static void ClearLog()
        {
            try
            {
                if (File.Exists(LogFilePath))
                    File.Delete(LogFilePath);
                Log("=== GDAL DEBUG LOG STARTED ===");
            }
            catch
            {
                // Ignore
            }
        }
    }

    public class GisExportService
    {
        public async Task<GeoTiffExportResult> ExportGeoTiffAsync(
            List<GpsPoint> gpsPoints,
            string outputPath,
            GeoTiffExportOptions options,
            double referenceLatitude,
            double referenceLongitude,
            string farmName,
            string fieldName,
            double resolution = 0.25,
            IProgress<GeoTiffExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Clear and start debug logging
            GdalDebugLogger.ClearLog();
            GdalDebugLogger.Log($"=== STARTING GEOTIFF EXPORT ===");
            GdalDebugLogger.Log($"Output Path: {outputPath}");
            GdalDebugLogger.Log($"Coordinate System: {options.CoordinateSystem}");
            GdalDebugLogger.Log($"Export Type: {options.ExportType}");
            GdalDebugLogger.Log($"GPS Points Count: {gpsPoints.Count}");
            GdalDebugLogger.Log($"Resolution: {resolution}");
            GdalDebugLogger.Log($"Reference: Lat={referenceLatitude}, Lon={referenceLongitude}");
            
            try
            {
                GdalDebugLogger.Log("Step 1: Initializing GDAL using unified manager");
                // Use unified GDAL manager for proper initialization
                GdalManager.EnsureInitialized();
                GdalDebugLogger.LogGdalInfo();

                // Step 2: Create TIN mesh and rasterize data (same as ASCII export)
                GdalDebugLogger.Log("Step 2: Creating TIN mesh from GPS points");
                var demService = new DemGenerationService();
                var (tinMesh, altitudeData) = demService.CreateTin(gpsPoints);
                GdalDebugLogger.Log($"TIN mesh created with {tinMesh.Triangles.Count} triangles");
                
                GdalDebugLogger.Log("Step 3: Rasterizing TIN mesh");
                var (rasterData, bounds) = demService.RasterizeTin(tinMesh, altitudeData, resolution);
                GdalDebugLogger.Log($"Rasterization complete: {rasterData.GetLength(0)} x {rasterData.GetLength(1)} pixels");
                GdalDebugLogger.Log($"Bounds: Left={bounds.Left}, Right={bounds.Right}, Top={bounds.Top}, Bottom={bounds.Bottom}");

                progress?.Report(new GeoTiffExportProgress 
                { 
                    Message = "TIN mesh created, rasterizing data", 
                    PercentComplete = 20 
                });

                GdalDebugLogger.Log("Step 4: Validating rasterized data");
                // Validate rasterized data
                if (rasterData == null)
                {
                    GdalDebugLogger.Log("ERROR: Rasterized data is null");
                    throw new ArgumentException("Rasterized data cannot be null");
                }

                var height = rasterData.GetLength(0);
                var width = rasterData.GetLength(1);
                GdalDebugLogger.Log($"Raster dimensions: {width} x {height}");

                if (height == 0 || width == 0)
                {
                    GdalDebugLogger.Log("ERROR: Raster data is empty");
                    throw new ArgumentException("Raster data cannot be empty");
                }

                // Create coordinate system
                progress?.Report(new GeoTiffExportProgress 
                { 
                    Message = "Setting up coordinate system", 
                    PercentComplete = 30 
                });

                GdalDebugLogger.Log("Step 5: Creating spatial reference system");
                var spatialRef = CreateSpatialReference(options.CoordinateSystem, referenceLatitude, referenceLongitude);
                GdalDebugLogger.Log("Spatial reference created successfully");
                
                GdalDebugLogger.Log("Step 6: Calculating geotransform");
                // Calculate geotransform based on coordinate system
                var geotransform = CalculateGeoTransform(bounds, width, height, options.CoordinateSystem, 
                    referenceLatitude, referenceLongitude);
                GdalDebugLogger.Log($"Geotransform: [{string.Join(", ", geotransform)}]");

                cancellationToken.ThrowIfCancellationRequested();

                GdalDebugLogger.Log($"Step 5: Selecting export method - {options.ExportType}");
                if (options.ExportType == GeoTiffExportType.SingleFile)
                {
                    GdalDebugLogger.Log("Calling ExportSingleFileAsync");
                    return await ExportSingleFileAsync(rasterData, outputPath, options, spatialRef, 
                        geotransform, farmName, fieldName, progress, cancellationToken);
                }
                else
                {
                    GdalDebugLogger.Log("Calling ExportTiledAsync");
                    return await ExportTiledAsync(rasterData, outputPath, options, spatialRef, 
                        geotransform, farmName, fieldName, progress, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                GdalDebugLogger.Log("=== EXPORT CANCELLED ===");
                return new GeoTiffExportResult
                {
                    Success = false,
                    Message = "Export was cancelled",
                    ExportTime = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                GdalDebugLogger.LogError("ExportGeoTiffAsync - Main Method", ex);
                GdalDebugLogger.Log("=== EXPORT FAILED ===");
                return new GeoTiffExportResult
                {
                    Success = false,
                    Message = $"Export failed: {ex.Message}",
                    ExportTime = stopwatch.Elapsed
                };
            }
        }

        private async Task<GeoTiffExportResult> ExportSingleFileAsync(
            double[,] rasterData,
            string outputPath,
            GeoTiffExportOptions options,
            SpatialReference spatialRef,
            double[] geotransform,
            string farmName,
            string fieldName,
            IProgress<GeoTiffExportProgress>? progress,
            CancellationToken cancellationToken)
        {
            GdalDebugLogger.Log("=== ENTERING ExportSingleFileAsync ===");
            var stopwatch = Stopwatch.StartNew();
            var height = rasterData.GetLength(0);
            var width = rasterData.GetLength(1);
            GdalDebugLogger.Log($"Single file export dimensions: {width} x {height}");

            progress?.Report(new GeoTiffExportProgress 
            { 
                Message = "Creating GeoTIFF file", 
                PercentComplete = 20 
            });

            try
            {
                GdalDebugLogger.Log("Step A: Getting GTiff driver");
                // Create GDAL driver
                var driver = Gdal.GetDriverByName("GTiff");
                if (driver == null)
                {
                    GdalDebugLogger.Log("ERROR: GTiff driver not available");
                    throw new InvalidOperationException("GTiff driver not available");
                }
                GdalDebugLogger.Log("GTiff driver obtained successfully");

                GdalDebugLogger.Log("Step B: Setting creation options");
                // Set creation options
                var creationOptions = new string[]
                {
                    options.UseCompression ? "COMPRESS=LZW" : "COMPRESS=NONE",
                    "TILED=YES",
                    "BLOCKXSIZE=256",
                    "BLOCKYSIZE=256"
                };
                GdalDebugLogger.Log($"Creation options: [{string.Join(", ", creationOptions)}]");

                GdalDebugLogger.Log("Step C: Creating GDAL dataset");
                // Create dataset
                using var dataset = driver.Create(outputPath, width, height, 1, DataType.GDT_Float32, creationOptions);
                if (dataset == null)
                {
                    GdalDebugLogger.Log($"ERROR: Failed to create dataset at {outputPath}");
                    throw new InvalidOperationException($"Failed to create GeoTIFF file: {outputPath}");
                }
                GdalDebugLogger.Log("Dataset created successfully");

                GdalDebugLogger.Log("Step D: Setting geotransform and projection");
                // Set geotransform and projection
                dataset.SetGeoTransform(geotransform);
                GdalDebugLogger.Log("Geotransform set successfully");
                
                string wktString;
                spatialRef.ExportToWkt(out wktString, null);
                GdalDebugLogger.Log($"WKT String: {wktString?.Substring(0, Math.Min(100, wktString?.Length ?? 0))}...");
                dataset.SetProjection(wktString);
                GdalDebugLogger.Log("Projection set successfully");

                progress?.Report(new GeoTiffExportProgress 
                { 
                    Message = "Writing elevation data", 
                    PercentComplete = 40 
                });

                GdalDebugLogger.Log("Step E: Getting raster band and setting NoData value");
                // Get the raster band
                using var band = dataset.GetRasterBand(1);
                if (band == null)
                {
                    GdalDebugLogger.Log("ERROR: Failed to get raster band 1");
                    throw new InvalidOperationException("Failed to get raster band 1");
                }
                band.SetNoDataValue(-9999.0);
                GdalDebugLogger.Log("Raster band obtained and NoData value set");

                GdalDebugLogger.Log("Step F: Converting 2D array to 1D for GDAL");
                // Convert 2D array to 1D for GDAL
                var flatData = new float[width * height];
                var minElevation = double.MaxValue;
                var maxElevation = double.MinValue;
                GdalDebugLogger.Log($"Allocated flat data array of size {flatData.Length}");

                GdalDebugLogger.Log("Step G: Converting raster data to flat array");
                var validCount = 0;
                var nanCount = 0;
                await Task.Run(() =>
                {
                    try
                    {
                        for (int row = 0; row < height; row++)
                        {
                            for (int col = 0; col < width; col++)
                            {
                                var value = rasterData[row, col];
                                
                                // Handle NoData values: convert NaN to -9999 for GDAL, but exclude from statistics
                                if (double.IsNaN(value))
                                {
                                    flatData[row * width + col] = -9999f; // NoData value for GDAL
                                    nanCount++;
                                }
                                else
                                {
                                    flatData[row * width + col] = (float)value;
                                    // Only include valid values in statistics (exclude NoData)
                                    minElevation = Math.Min(minElevation, value);
                                    maxElevation = Math.Max(maxElevation, value);
                                    validCount++;
                                }
                            }

                            // Report progress every 10% of rows
                            if (row % (height / 10) == 0)
                            {
                                var percent = 40 + (int)((row / (double)height) * 40);
                                progress?.Report(new GeoTiffExportProgress 
                                { 
                                    Message = "Writing elevation data", 
                                    PercentComplete = percent 
                                });
                            }

                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        
                        // Handle case where no valid values were found
                        if (validCount == 0)
                        {
                            GdalDebugLogger.Log($"WARNING: No valid elevation values found! Valid: {validCount}, NaN: {nanCount}");
                            minElevation = 0;
                            maxElevation = 0;
                        }
                        else
                        {
                            GdalDebugLogger.Log($"Data conversion complete. Valid values: {validCount}, NaN values: {nanCount}");
                            GdalDebugLogger.Log($"Elevation range: Min: {minElevation}, Max: {maxElevation}");
                        }
                    }
                    catch (Exception ex)
                    {
                        GdalDebugLogger.LogError("Data conversion", ex);
                        throw;
                    }
                }, cancellationToken);

                GdalDebugLogger.Log("Step H: Writing data to GDAL raster band");
                // Write data to band
                band.WriteRaster(0, 0, width, height, flatData, width, height, 0, 0);
                GdalDebugLogger.Log("Data written to raster band successfully");

                progress?.Report(new GeoTiffExportProgress 
                { 
                    Message = "Adding metadata", 
                    PercentComplete = 85 
                });

                GdalDebugLogger.Log("Step I: Adding metadata to dataset");
                // Add metadata
                AddMetadata(dataset, options, farmName, fieldName, minElevation, maxElevation, 
                    width * height, DateTime.Now);
                GdalDebugLogger.Log("Metadata added successfully");

                // Add color palette if requested
                if (options.IncludeColorPalette)
                {
                    GdalDebugLogger.Log("Step J: Adding color palette");
                    AddElevationColorPalette(band, minElevation, maxElevation);
                    GdalDebugLogger.Log("Color palette added successfully");
                }
                else
                {
                    GdalDebugLogger.Log("Step J: Skipping color palette (not requested)");
                }

                progress?.Report(new GeoTiffExportProgress 
                { 
                    Message = "Finalizing file", 
                    PercentComplete = 95 
                });

                GdalDebugLogger.Log("Step K: Flushing caches and finalizing file");
                // Force write and close
                dataset.FlushCache();
                band.FlushCache();
                GdalDebugLogger.Log("Caches flushed successfully");

                stopwatch.Stop();

                GdalDebugLogger.Log("Step L: Checking file creation and getting file size");
                // Get file size
                var fileInfo = new FileInfo(outputPath);
                var fileSize = fileInfo.Exists ? fileInfo.Length : 0;
                GdalDebugLogger.Log($"File created: {fileInfo.Exists}, Size: {fileSize} bytes");

                progress?.Report(new GeoTiffExportProgress 
                { 
                    Message = "Export complete", 
                    PercentComplete = 100 
                });

                GdalDebugLogger.Log($"=== EXPORT COMPLETED SUCCESSFULLY in {stopwatch.Elapsed.TotalSeconds:F1}s ===");
                return new GeoTiffExportResult
                {
                    Success = true,
                    FilePath = outputPath,
                    FileSize = fileSize,
                    ExportTime = stopwatch.Elapsed,
                    Message = $"GeoTIFF exported successfully in {stopwatch.Elapsed.TotalSeconds:F1} seconds"
                };
            }
            catch (Exception ex)
            {
                GdalDebugLogger.LogError("ExportSingleFileAsync", ex);
                throw;
            }
        }

        private async Task<GeoTiffExportResult> ExportTiledAsync(
            double[,] rasterData,
            string outputPath,
            GeoTiffExportOptions options,
            SpatialReference spatialRef,
            double[] geotransform,
            string farmName,
            string fieldName,
            IProgress<GeoTiffExportProgress>? progress,
            CancellationToken cancellationToken)
        {
            // For now, delegate to single file export
            // TODO: Implement proper tiled export with multiple files
            progress?.Report(new GeoTiffExportProgress 
            { 
                Message = "Tiled export using single file method", 
                PercentComplete = 5 
            });

            return await ExportSingleFileAsync(rasterData, outputPath, options, spatialRef, 
                geotransform, farmName, fieldName, progress, cancellationToken);
        }



        private SpatialReference CreateSpatialReference(CoordinateSystemType coordSystem, 
            double refLat, double refLon)
        {
            var spatialRef = new SpatialReference("");

            try
            {
                switch (coordSystem)
                {
                    case CoordinateSystemType.UTM:
                        // Determine UTM zone from longitude
                        var utmZone = (int)Math.Floor((refLon + 180) / 6) + 1;
                        var isNorthern = refLat >= 0;
                        
                        try
                        {
                            spatialRef.SetUTM(utmZone, isNorthern ? 1 : 0);
                            spatialRef.SetWellKnownGeogCS("WGS84");
                        }
                        catch
                        {
                            // Fallback: Create UTM using WKT string if PROJ database fails
                            var wkt = CreateUTMWktString(utmZone, isNorthern);
                            spatialRef.ImportFromWkt(ref wkt);
                        }
                        break;

                    case CoordinateSystemType.WGS84:
                        try
                        {
                            spatialRef.SetWellKnownGeogCS("WGS84");
                        }
                        catch
                        {
                            // Fallback: Create WGS84 using WKT string if PROJ database fails
                            var wkt = CreateWGS84WktString();
                            spatialRef.ImportFromWkt(ref wkt);
                        }
                        break;

                    case CoordinateSystemType.LocalTangentPlane:
                        // Create local coordinate system (doesn't rely on PROJ database)
                        spatialRef.SetLocalCS("Local Tangent Plane");
                        break;
                }
            }
            catch (Exception)
            {
                // If all else fails, create a minimal spatial reference
                spatialRef = new SpatialReference("");
                spatialRef.SetLocalCS("Unknown");
            }

            return spatialRef;
        }

        private double[] CalculateGeoTransform(TriangleNet.Geometry.Rectangle bounds, 
            int width, int height, CoordinateSystemType coordSystem, 
            double refLat, double refLon)
        {
            double originX, originY, pixelWidth, pixelHeight;

            switch (coordSystem)
            {
                case CoordinateSystemType.UTM:
                    // Convert bounds to UTM coordinates
                    var utmBounds = ConvertToUTM(bounds, refLat, refLon);
                    originX = utmBounds.Left;
                    originY = utmBounds.Top;
                    pixelWidth = (utmBounds.Right - utmBounds.Left) / width;
                    pixelHeight = -(utmBounds.Top - utmBounds.Bottom) / height; // Negative for north-up
                    break;

                case CoordinateSystemType.WGS84:
                    // Use geographic coordinates directly
                    originX = bounds.Left;
                    originY = bounds.Top;
                    pixelWidth = (bounds.Right - bounds.Left) / width;
                    pixelHeight = -(bounds.Top - bounds.Bottom) / height; // Negative for north-up
                    break;

                case CoordinateSystemType.LocalTangentPlane:
                default:
                    // Use local coordinates as-is
                    originX = bounds.Left;
                    originY = bounds.Top;
                    pixelWidth = (bounds.Right - bounds.Left) / width;
                    pixelHeight = -(bounds.Top - bounds.Bottom) / height; // Negative for north-up
                    break;
            }

            return new double[] { originX, pixelWidth, 0, originY, 0, pixelHeight };
        }

        private TriangleNet.Geometry.Rectangle ConvertToUTM(TriangleNet.Geometry.Rectangle bounds, 
            double refLat, double refLon)
        {
            try
            {
                // Create source and target spatial references
                var sourceSrs = new SpatialReference("");
                var targetSrs = new SpatialReference("");
                
                // Set up WGS84 geographic coordinate system
                sourceSrs.SetWellKnownGeogCS("WGS84");
                
                // Set up UTM coordinate system
                var utmZone = (int)Math.Floor((refLon + 180) / 6) + 1;
                var isNorthern = refLat >= 0;
                targetSrs.SetUTM(utmZone, isNorthern ? 1 : 0);
                targetSrs.SetWellKnownGeogCS("WGS84");
                
                // Create coordinate transformation
                var transform = new CoordinateTransformation(sourceSrs, targetSrs);
                
                // Transform corner points
                var corners = new[]
                {
                    new[] { bounds.Left, bounds.Bottom, 0.0 },
                    new[] { bounds.Right, bounds.Bottom, 0.0 },
                    new[] { bounds.Right, bounds.Top, 0.0 },
                    new[] { bounds.Left, bounds.Top, 0.0 }
                };
                
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                
                foreach (var corner in corners)
                {
                    transform.TransformPoint(corner);
                    minX = Math.Min(minX, corner[0]);
                    maxX = Math.Max(maxX, corner[0]);
                    minY = Math.Min(minY, corner[1]);
                    maxY = Math.Max(maxY, corner[1]);
                }
                
                return new TriangleNet.Geometry.Rectangle(minX, minY, maxX, maxY);
            }
            catch (Exception)
            {
                // Fallback: Use approximate UTM conversion if PROJ transformation fails
                return ConvertToUTMApproximate(bounds, refLat, refLon);
            }
        }

        private void AddMetadata(Dataset dataset, GeoTiffExportOptions options, 
            string farmName, string fieldName, double minElevation, double maxElevation, 
            int pointCount, DateTime processingDate)
        {
            if (options.IncludeFarmName && !string.IsNullOrEmpty(farmName))
                dataset.SetMetadataItem("FARM_NAME", farmName, "");

            if (options.IncludeFieldName && !string.IsNullOrEmpty(fieldName))
                dataset.SetMetadataItem("FIELD_NAME", fieldName, "");

            if (options.IncludeProcessingDate)
                dataset.SetMetadataItem("PROCESSING_DATE", processingDate.ToString("yyyy-MM-dd HH:mm:ss"), "");

            if (options.IncludeGpsCount)
                dataset.SetMetadataItem("GPS_POINT_COUNT", pointCount.ToString(), "");

            if (options.IncludeElevationRange)
            {
                dataset.SetMetadataItem("ELEVATION_MIN", minElevation.ToString("F3"), "");
                dataset.SetMetadataItem("ELEVATION_MAX", maxElevation.ToString("F3"), "");
            }

            // Add software info
            dataset.SetMetadataItem("SOFTWARE", "DEMBuilder", "");
            dataset.SetMetadataItem("COORDINATE_SYSTEM", options.CoordinateSystem.ToString(), "");
            dataset.SetMetadataItem("RESOLUTION", options.Resolution.ToString("F3"), "");
        }

        private void AddElevationColorPalette(Band band, double minElevation, double maxElevation)
        {
            // Check if the band data type supports color tables
            // Color tables are only supported for Byte and UInt16 bands in GeoTIFF format
            var dataType = band.DataType;
            if (dataType != DataType.GDT_Byte && dataType != DataType.GDT_UInt16)
            {
                GdalDebugLogger.Log($"Skipping color palette: Band data type {dataType} does not support color tables in GeoTIFF format");
                return;
            }
            
            // Create a simple elevation color palette
            var colorTable = new ColorTable(PaletteInterp.GPI_RGB);
            
            var elevationRange = maxElevation - minElevation;
            if (elevationRange <= 0) return;

            // Create color ramp from blue (low) to red (high)
            for (int i = 0; i <= 255; i++)
            {
                var ratio = i / 255.0;
                var colorEntry = new ColorEntry();
                
                // Blue to green to red color ramp
                if (ratio < 0.5)
                {
                    // Blue to green
                    colorEntry.c1 = (short)(ratio * 2 * 255); // Red
                    colorEntry.c2 = (short)(255); // Green
                    colorEntry.c3 = (short)((1 - ratio * 2) * 255); // Blue
                }
                else
                {
                    // Green to red
                    colorEntry.c1 = (short)(255); // Red
                    colorEntry.c2 = (short)((2 - ratio * 2) * 255); // Green
                    colorEntry.c3 = (short)(0); // Blue
                }
                colorEntry.c4 = 255; // Alpha
                
                colorTable.SetColorEntry(i, colorEntry);
            }
            
            band.SetRasterColorTable(colorTable);
            band.SetRasterColorInterpretation(ColorInterp.GCI_PaletteIndex);
        }



        private string CreateWGS84WktString()
        {
            // Create WGS84 geographic WKT string without relying on PROJ database
            return @"GEOGCS[""WGS 84"",
                DATUM[""WGS_1984"",
                    SPHEROID[""WGS 84"",6378137,298.257223563]],
                PRIMEM[""Greenwich"",0],
                UNIT[""degree"",0.0174532925199433]]";
        }

        private string CreateUTMWktString(int zone, bool isNorthern)
        {
            // Create UTM WKT string without relying on PROJ database
            var hemisphere = isNorthern ? "N" : "S";
            return $@"PROJCS[""WGS 84 / UTM zone {zone}{hemisphere}"",
                GEOGCS[""WGS 84"",
                    DATUM[""WGS_1984"",
                        SPHEROID[""WGS 84"",6378137,298.257223563]],
                    PRIMEM[""Greenwich"",0],
                    UNIT[""degree"",0.0174532925199433]],
                PROJECTION[""Transverse_Mercator""],
                PARAMETER[""latitude_of_origin"",0],
                PARAMETER[""central_meridian"",{-183 + 6 * zone}],
                PARAMETER[""scale_factor"",0.9996],
                PARAMETER[""false_easting"",500000],
                PARAMETER[""false_northing"",{(isNorthern ? 0 : 10000000)}],
                UNIT[""metre"",1]]";
        }

        private TriangleNet.Geometry.Rectangle ConvertToUTMApproximate(TriangleNet.Geometry.Rectangle bounds, 
            double refLat, double refLon)
        {
            // Approximate UTM conversion using mathematical formulas when PROJ fails
            // This provides reasonable coordinate transformation without PROJ database
            
            var utmZone = (int)Math.Floor((refLon + 180) / 6) + 1;
            var centralMeridian = -183 + 6 * utmZone;
            
            // Convert corner points using approximate UTM formulas
            var corners = new[]
            {
                ConvertLatLonToUTMApproximate(bounds.Bottom, bounds.Left, centralMeridian, refLat >= 0),
                ConvertLatLonToUTMApproximate(bounds.Bottom, bounds.Right, centralMeridian, refLat >= 0),
                ConvertLatLonToUTMApproximate(bounds.Top, bounds.Right, centralMeridian, refLat >= 0),
                ConvertLatLonToUTMApproximate(bounds.Top, bounds.Left, centralMeridian, refLat >= 0)
            };
            
            double minX = corners.Min(c => c.Item1);
            double maxX = corners.Max(c => c.Item1);
            double minY = corners.Min(c => c.Item2);
            double maxY = corners.Max(c => c.Item2);
            
            return new TriangleNet.Geometry.Rectangle(minX, minY, maxX, maxY);
        }
        
        private (double X, double Y) ConvertLatLonToUTMApproximate(double lat, double lon, double centralMeridian, bool isNorthern)
        {
            // Simplified UTM conversion using mathematical approximation
            // Based on standard UTM projection formulas
            
            const double a = 6378137.0; // WGS84 semi-major axis
            const double f = 1.0 / 298.257223563; // WGS84 flattening
            const double k0 = 0.9996; // UTM scale factor
            
            var latRad = lat * Math.PI / 180.0;
            var lonRad = lon * Math.PI / 180.0;
            var centralMeridianRad = centralMeridian * Math.PI / 180.0;
            
            var deltaLon = lonRad - centralMeridianRad;
            
            // Simplified UTM calculation (good enough for agricultural applications)
            var N = a / Math.Sqrt(1 - (2 * f - f * f) * Math.Sin(latRad) * Math.Sin(latRad));
            var T = Math.Tan(latRad) * Math.Tan(latRad);
            var C = (2 * f - f * f) / (1 - (2 * f - f * f)) * Math.Cos(latRad) * Math.Cos(latRad);
            var A = Math.Cos(latRad) * deltaLon;
            
            var M = a * ((1 - (2 * f - f * f) / 4 - 3 * (2 * f - f * f) * (2 * f - f * f) / 64) * latRad
                       - (3 * (2 * f - f * f) / 8 + 3 * (2 * f - f * f) * (2 * f - f * f) / 32) * Math.Sin(2 * latRad)
                       + (15 * (2 * f - f * f) * (2 * f - f * f) / 256) * Math.Sin(4 * latRad));
            
            var x = k0 * N * (A + (1 - T + C) * A * A * A / 6) + 500000; // False easting
            var y = k0 * (M + N * Math.Tan(latRad) * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * A * A * A * A / 24));
            
            if (!isNorthern)
                y += 10000000; // False northing for southern hemisphere
                
            return (x, y);
        }
    }
}
