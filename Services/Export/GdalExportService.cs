using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OSGeo.GDAL;
using OSGeo.OSR;
using DEMBuilder.Models;
using DEMBuilder.Services.Projection;

namespace DEMBuilder.Services.Export
{
    public class GdalExportService
    {
        private bool _gdalInitialized = false;

        // P/Invoke declarations for Windows DLL loading
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AddDllDirectory(string NewDirectory);

        public GdalExportService()
        {
            ConfigureNativeDllPaths();
            InitializeGdal();
        }

        private void ConfigureNativeDllPaths()
        {
            try
            {
                var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var assemblyDir = Path.GetDirectoryName(assemblyPath) ?? "";
                
                // Try to find the GDAL native DLL directory
                var potentialNativePaths = new[]
                {
                    Path.Combine(assemblyDir, "gdal", "x64"),
                    Path.Combine(assemblyDir, "gdal", "x86"),
                    Path.Combine(assemblyDir, "runtimes", "win-x64", "native"),
                    Path.Combine(assemblyDir, "x64"),
                    assemblyDir // Current directory as fallback
                };
                
                foreach (var path in potentialNativePaths)
                {
                    try
                    {
                        var normalizedPath = Path.GetFullPath(path);
                        
                        if (Directory.Exists(normalizedPath))
                        {
                            // Check if gdal.dll exists in this directory
                            var gdalDllPath = Path.Combine(normalizedPath, "gdal.dll");
                            if (File.Exists(gdalDllPath))
                            {
                                // Add this directory to the DLL search path
                                AddDllDirectory(normalizedPath);
                                
                                // Also add to PATH environment variable
                                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                                if (!currentPath.Contains(normalizedPath))
                                {
                                    Environment.SetEnvironmentVariable("PATH", $"{normalizedPath};{currentPath}");
                                }
                                
                                // Set as the primary DLL directory
                                SetDllDirectory(normalizedPath);
                                
                                return; // Use the first valid path found
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Continue to next path if this one fails
                        continue;
                    }
                }
            }
            catch (Exception)
            {
                // If native DLL configuration fails, GDAL initialization will handle the error
            }
        }

        private void InitializeGdal()
        {
            if (_gdalInitialized)
            {
                return;
            }

            try
            {
                // Configure GDAL data paths
                ConfigureGdalPaths();
                
                // Register all GDAL drivers
                Gdal.AllRegister();
                
                // Test GDAL functionality by trying to get a driver
                var geoTiffDriver = Gdal.GetDriverByName("GTiff");
                if (geoTiffDriver == null)
                {
                    throw new InvalidOperationException("GeoTIFF driver not available after GDAL initialization");
                }

                _gdalInitialized = true;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to initialize GDAL: {ex.Message}. " +
                    $"GDAL_DATA: {Environment.GetEnvironmentVariable("GDAL_DATA") ?? "not set"}, " +
                    $"Assembly location: {System.Reflection.Assembly.GetExecutingAssembly().Location}";
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        private void ConfigureGdalPaths()
        {
            // Check if GDAL_DATA is already set
            var gdalData = Environment.GetEnvironmentVariable("GDAL_DATA");
            
            if (!string.IsNullOrEmpty(gdalData) && Directory.Exists(gdalData))
            {
                return; // Already configured
            }

            // Try multiple potential GDAL data locations
            var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyPath) ?? "";
            
            var potentialPaths = new[]
            {
                Path.Combine(assemblyDir, "gdal", "data"),
                Path.Combine(assemblyDir, "gdal-data"),
                Path.Combine(assemblyDir, "runtimes", "win-x64", "native", "gdal", "data"),
                Path.Combine(assemblyDir, "x64", "gdal", "data"),
                Path.Combine(assemblyDir, "..\\..\\..\\packages", "GDAL", "gdal", "data"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "gdal", "3.8.4", "gdal", "data")
            };

            foreach (var path in potentialPaths)
            {
                try
                {
                    var normalizedPath = Path.GetFullPath(path);
                    
                    if (Directory.Exists(normalizedPath))
                    {
                        // Verify the path contains expected GDAL data files
                        var testFiles = new[] { "epsg.wkt", "gdalicon.png", "default.rsc" };
                        var foundFiles = testFiles.Where(file => File.Exists(Path.Combine(normalizedPath, file))).ToArray();
                        
                        if (foundFiles.Length > 0)
                        {
                            try
                            {
                                Gdal.SetConfigOption("GDAL_DATA", normalizedPath);
                                Environment.SetEnvironmentVariable("GDAL_DATA", normalizedPath);
                                return; // Success - exit the method
                            }
                            catch (Exception)
                            {
                                // Continue to next path if this one fails
                                continue;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        public async Task<bool> ExportDemToGeoTiffAsync(
            double[,] rasterData, 
            TriangleNet.Geometry.Rectangle bounds, 
            string outputPath, 
            double resolution = 0.25, 
            double tileSize = 50.0,
            double referenceLatitude = 0.0,
            double referenceLongitude = 0.0,
            IProgress<string>? progress = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    progress?.Report("Initializing GeoTIFF export...");
                    
                    // Calculate dimensions
                    int width = rasterData.GetLength(0);
                    int height = rasterData.GetLength(1);
                    
                    // Create output directory if it doesn't exist
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // Determine if we need tiling
                    double dataWidth = bounds.Right - bounds.Left;
                    double dataHeight = bounds.Top - bounds.Bottom;
                    
                    if (dataWidth <= tileSize && dataHeight <= tileSize)
                    {
                        // Single tile export
                        progress?.Report("Exporting single GeoTIFF tile...");
                        return ExportSingleTile(rasterData, bounds, outputPath, resolution, referenceLatitude, referenceLongitude);
                    }
                    else
                    {
                        // Multi-tile export
                        progress?.Report("Exporting tiled GeoTIFF...");
                        return ExportTiledGeoTiff(rasterData, bounds, outputPath, resolution, tileSize, referenceLatitude, referenceLongitude, progress);
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report($"Export failed: {ex.Message}");
                    return false;
                }
            });
        }

        private bool ExportSingleTile(
            double[,] rasterData, 
            TriangleNet.Geometry.Rectangle bounds, 
            string outputPath, 
            double resolution,
            double referenceLatitude,
            double referenceLongitude)
        {
            try
            {
                int width = rasterData.GetLength(0);
                int height = rasterData.GetLength(1);

                // Create GeoTIFF driver
                using var driver = Gdal.GetDriverByName("GTiff");
                if (driver == null)
                {
                    throw new InvalidOperationException("GeoTIFF driver not available");
                }

                // Create dataset
                using var dataset = driver.Create(outputPath, width, height, 1, DataType.GDT_Float32, new string[] 
                { 
                    "COMPRESS=LZW",
                    "TILED=YES",
                    "BLOCKXSIZE=256",
                    "BLOCKYSIZE=256"
                });

                if (dataset == null)
                {
                    throw new InvalidOperationException($"Failed to create dataset: {outputPath}");
                }

                // Set geotransform (defines pixel-to-world coordinate mapping)
                double[] geoTransform = new double[6];
                geoTransform[0] = bounds.Left;     // Top-left X
                geoTransform[1] = resolution;      // Pixel width
                geoTransform[2] = 0;               // Rotation (0 for north-up)
                geoTransform[3] = bounds.Top;      // Top-left Y
                geoTransform[4] = 0;               // Rotation (0 for north-up)
                geoTransform[5] = -resolution;     // Pixel height (negative for north-up)

                dataset.SetGeoTransform(geoTransform);

                // Set spatial reference system (local coordinate system compatible with AgOpenGPS)
                using var srs = new SpatialReference("");
                srs.SetLocalCS("AgOpenGPS Local Coordinate System");
                srs.SetLinearUnits("metre", 1.0);
                
                // Add metadata about the reference point
                dataset.SetMetadataItem("REFERENCE_LATITUDE", referenceLatitude.ToString("F8"), "");
                dataset.SetMetadataItem("REFERENCE_LONGITUDE", referenceLongitude.ToString("F8"), "");
                dataset.SetMetadataItem("COORDINATE_SYSTEM", "AgOpenGPS_Local", "");
                dataset.SetMetadataItem("RESOLUTION", resolution.ToString("F3"), "");

                string wkt;
                srs.ExportToWkt(out wkt, null);
                dataset.SetProjection(wkt);

                // Get the raster band
                using var band = dataset.GetRasterBand(1);
                band.SetNoDataValue(-9999);

                // Convert 2D array to 1D for GDAL
                float[] pixelData = new float[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        double value = rasterData[x, y];
                        pixelData[y * width + x] = double.IsNaN(value) ? -9999f : (float)value;
                    }
                }

                // Write data to band
                band.WriteRaster(0, 0, width, height, pixelData, width, height, 0, 0);
                band.FlushCache();
                dataset.FlushCache();

                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to export single tile: {ex.Message}", ex);
            }
        }

        private bool ExportTiledGeoTiff(
            double[,] rasterData, 
            TriangleNet.Geometry.Rectangle bounds, 
            string outputPath, 
            double resolution, 
            double tileSize,
            double referenceLatitude,
            double referenceLongitude,
            IProgress<string>? progress)
        {
            try
            {
                // Calculate tile grid using safe long arithmetic
                double dataWidth = bounds.Right - bounds.Left;
                double dataHeight = bounds.Top - bounds.Bottom;
                
                // Use long for large datasets (up to 500ha)
                long tilesX = (long)Math.Ceiling(dataWidth / tileSize);
                long tilesY = (long)Math.Ceiling(dataHeight / tileSize);
                long totalTiles = tilesX * tilesY;
                
                // Sanity check for reasonable tile counts
                if (totalTiles > 10000)
                {
                    throw new InvalidOperationException($"Too many tiles ({totalTiles}). Consider increasing tile size or reducing area.");
                }
                
                progress?.Report($"Creating {tilesX}×{tilesY} = {totalTiles} tiles...");

                string baseFileName = Path.GetFileNameWithoutExtension(outputPath);
                string outputDir = Path.GetDirectoryName(outputPath) ?? "";
                string extension = Path.GetExtension(outputPath);

                long currentTile = 0;

                // Process tiles with safe long indices
                for (long tileY = 0; tileY < tilesY; tileY++)
                {
                    for (long tileX = 0; tileX < tilesX; tileX++)
                    {
                        currentTile++;
                        progress?.Report($"Processing tile {currentTile}/{totalTiles} ({tileX},{tileY})...");

                        // Calculate tile bounds with safe arithmetic
                        double tileLeft = bounds.Left + (tileX * tileSize);
                        double tileRight = Math.Min(bounds.Right, tileLeft + tileSize);
                        double tileTop = bounds.Top - (tileY * tileSize);
                        double tileBottom = Math.Max(bounds.Bottom, tileTop - tileSize);

                        var tileBounds = new TriangleNet.Geometry.Rectangle(tileLeft, tileBottom, tileRight, tileTop);

                        // Extract tile data using improved method
                        var tileData = ExtractTileDataSafe(rasterData, bounds, tileBounds, resolution);
                        
                        if (tileData == null)
                        {
                            continue;
                        }

                        // Generate tile filename with safe conversion
                        string tileFileName = $"{baseFileName}_tile_{tileX:D4}_{tileY:D4}{extension}";
                        string tileFilePath = Path.Combine(outputDir, tileFileName);

                        // Export tile
                        if (!ExportSingleTile(tileData, tileBounds, tileFilePath, resolution, referenceLatitude, referenceLongitude))
                        {
                            return false;
                        }
                    }
                }

                progress?.Report($"Successfully exported {totalTiles} tiles");
                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to export tiled GeoTIFF: {ex.Message}", ex);
            }
        }

        private double[,]? ExtractTileDataSafe(
            double[,] sourceData, 
            TriangleNet.Geometry.Rectangle sourceBounds, 
            TriangleNet.Geometry.Rectangle tileBounds, 
            double resolution)
        {
            try
            {
                // Calculate source data dimensions
                long sourceWidth = sourceData.GetLength(0);
                long sourceHeight = sourceData.GetLength(1);

                // Calculate tile dimensions using safe arithmetic
                double tileWidthMeters = tileBounds.Right - tileBounds.Left;
                double tileHeightMeters = tileBounds.Top - tileBounds.Bottom;
                
                // Validate tile bounds
                if (tileWidthMeters <= 0 || tileHeightMeters <= 0)
                {
                    return null;
                }
                
                // Calculate pixel dimensions with safety checks
                double tileWidthPixelsDouble = tileWidthMeters / resolution;
                double tileHeightPixelsDouble = tileHeightMeters / resolution;
                
                // Ensure reasonable tile dimensions (max 2000x2000 pixels per tile)
                if (tileWidthPixelsDouble > 2000 || tileHeightPixelsDouble > 2000)
                {
                    return null;
                }
                
                int tileWidth = Math.Max(1, (int)Math.Ceiling(tileWidthPixelsDouble));
                int tileHeight = Math.Max(1, (int)Math.Ceiling(tileHeightPixelsDouble));

                var tileData = new double[tileWidth, tileHeight];

                // Initialize with NaN
                for (int x = 0; x < tileWidth; x++)
                {
                    for (int y = 0; y < tileHeight; y++)
                    {
                        tileData[x, y] = double.NaN;
                    }
                }

                // Calculate the overlap between source and tile bounds
                double overlapLeft = Math.Max(sourceBounds.Left, tileBounds.Left);
                double overlapRight = Math.Min(sourceBounds.Right, tileBounds.Right);
                double overlapBottom = Math.Max(sourceBounds.Bottom, tileBounds.Bottom);
                double overlapTop = Math.Min(sourceBounds.Top, tileBounds.Top);
                
                if (overlapLeft >= overlapRight || overlapBottom >= overlapTop)
                {
                    return tileData; // Return empty tile
                }

                // Copy data within the overlap region using safe coordinate conversion
                for (int tileX = 0; tileX < tileWidth; tileX++)
                {
                    for (int tileY = 0; tileY < tileHeight; tileY++)
                    {
                        // Convert tile pixel to world coordinates
                        double worldX = tileBounds.Left + (tileX * resolution);
                        double worldY = tileBounds.Top - (tileY * resolution);

                        // Check if this world coordinate is within source bounds
                        if (worldX < sourceBounds.Left || worldX >= sourceBounds.Right ||
                            worldY <= sourceBounds.Bottom || worldY > sourceBounds.Top)
                        {
                            continue; // Outside source bounds, leave as NaN
                        }

                        // Convert world coordinates to source pixel coordinates using safe arithmetic
                        double sourceXDouble = (worldX - sourceBounds.Left) / resolution;
                        double sourceYDouble = (sourceBounds.Top - worldY) / resolution;
                        
                        // Use safe rounding and bounds checking
                        long sourceXLong = (long)Math.Round(sourceXDouble);
                        long sourceYLong = (long)Math.Round(sourceYDouble);

                        // Final bounds check with safe conversion
                        if (sourceXLong >= 0 && sourceXLong < sourceWidth && 
                            sourceYLong >= 0 && sourceYLong < sourceHeight)
                        {
                            int sourceX = (int)sourceXLong;
                            int sourceY = (int)sourceYLong;
                            tileData[tileX, tileY] = sourceData[sourceX, sourceY];
                        }
                    }
                }

                return tileData;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Dispose()
        {
            // GDAL cleanup is handled automatically
            _gdalInitialized = false;
        }
    }
}
