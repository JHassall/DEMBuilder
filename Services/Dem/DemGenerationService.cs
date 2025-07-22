using DEMBuilder.Models;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TriangleNet;
using TriangleNet.Geometry;
using TriangleNet.Meshing;
using TriangleNet.Meshing.Algorithm;
using TriangleNet.Tools;

namespace DEMBuilder.Services.Dem
{
    public class DemGenerationService
    {
        public Task<(double[,] rasterData, TriangleNet.Geometry.Rectangle bounds)> GenerateDemPreviewDataAsync(List<GpsPoint> projectedPoints, double resolution = 0.25)
        {
            return Task.Run(() =>
            {
                var (tinMesh, altitudeData) = CreateTin(projectedPoints);
                var (rasterData, bounds) = RasterizeTin(tinMesh, altitudeData, resolution);
                return (rasterData, bounds);
            });
        }

        public Task ExportDemAsTxtAsync(List<GpsPoint> points, string outputFilePath, double resolution = 0.25)
        {
            return Task.Run(() =>
            {
                var (tinMesh, altitudeData) = CreateTin(points);
                var (rasterData, bounds) = RasterizeTin(tinMesh, altitudeData, resolution);

                using (var writer = new StreamWriter(outputFilePath))
                {
                    int gridWidth = rasterData.GetLength(0);
                    int gridHeight = rasterData.GetLength(1);

                    for (int y = 0; y < gridHeight; y++)
                    {
                        for (int x = 0; x < gridWidth; x++)
                        {
                            double altitude = rasterData[x, y];
                            if (altitude > -9998) // Don't write NODATA values
                            {
                                double easting = bounds.Left + (x * resolution);
                                double northing = bounds.Top - (y * resolution); // Y is inverted in raster
                                writer.WriteLine($"{easting.ToString("F4", CultureInfo.InvariantCulture)},{northing.ToString("F4", CultureInfo.InvariantCulture)},{altitude.ToString("F3", CultureInfo.InvariantCulture)}");
                            }
                        }
                    }
                }
            });
        }

        public Task ExportDemAsAscAsync(List<GpsPoint> points, string outputFilePath, double resolution = 0.25)
        {
            return Task.Run(() =>
            {
                var (tinMesh, altitudeData) = CreateTin(points);
                var (rasterData, bounds) = RasterizeTin(tinMesh, altitudeData, resolution);

                using (var writer = new StreamWriter(outputFilePath))
                {
                    int gridWidth = rasterData.GetLength(0);
                    int gridHeight = rasterData.GetLength(1);

                    // Write ASCII Grid Header
                    writer.WriteLine($"ncols        {gridWidth}");
                    writer.WriteLine($"nrows        {gridHeight}");
                    writer.WriteLine($"xllcorner    {bounds.Left.ToString("F4", CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"yllcorner    {bounds.Bottom.ToString("F4", CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"cellsize     {resolution.ToString("F4", CultureInfo.InvariantCulture)}");
                    writer.WriteLine("NODATA_value -9999");

                    // Write Data
                    for (int y = gridHeight - 1; y >= 0; y--)
                    {
                        for (int x = 0; x < gridWidth; x++)
                        {
                            writer.Write(rasterData[x, y].ToString("F3", CultureInfo.InvariantCulture) + " ");
                        }
                        writer.WriteLine();
                    }
                }
            });
        }

        public Task<(bool Success, string ErrorMessage)> ConvertAscToGeoTiffAsync(string ascFilePath, double referenceLatitude, double referenceLongitude)
        {
            return Task.Run(() =>
            {
                string geoTiffFilePath = Path.ChangeExtension(ascFilePath, ".tif");
                // WGS 84
                int epsg = 4326; 

                string arguments = $"-of GTiff -a_srs EPSG:{epsg} -a_ullr {referenceLongitude} {referenceLatitude} {referenceLongitude + 1} {referenceLatitude - 1} \"{ascFilePath}\" \"{geoTiffFilePath}\"";

                string exePath = Path.Combine(AppContext.BaseDirectory, "gdal_runtime", "gdal_translate.exe");

                var process = new System.Diagnostics.Process
                {
                    StartInfo =
                    {
                        FileName = exePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string errorMessage = string.IsNullOrWhiteSpace(error) ? "An unknown error occurred with GDAL." : error;
                    Console.WriteLine("GDAL Error: " + errorMessage);
                    return (false, errorMessage);
                }

                return (true, string.Empty);
            });
        }

        private (IMesh mesh, double[] altitudeData) CreateTin(List<GpsPoint> points)
        {
            var polygon = new Polygon();
            var altitudes = new Dictionary<int, double>();
            int id = 0;

            foreach (var point in points)
            {
                var vertex = new Vertex(point.Easting, point.Northing) { ID = id };
                polygon.Add(vertex);
                altitudes.Add(id, point.Altitude);
                id++;
            }

            var mesher = new GenericMesher(new Dwyer());
            var mesh = mesher.Triangulate(polygon.Points);

            var altitudeArray = new double[altitudes.Count];
            foreach (var vertex in mesh.Vertices)
            {
                if (altitudes.TryGetValue(vertex.ID, out double alt))
                {
                    altitudeArray[vertex.ID] = alt;
                }
            }

            return (mesh, altitudeArray);
        }

        public BitmapSource CreateDemBitmap(double[,] rasterData)
        {
            int width = rasterData.GetLength(0);
            int height = rasterData.GetLength(1);

            double minAlt = double.MaxValue;
            double maxAlt = double.MinValue;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double alt = rasterData[x, y];
                    if (alt > -9998)
                    {
                        if (alt < minAlt) minAlt = alt;
                        if (alt > maxAlt) maxAlt = alt;
                    }
                }
            }

            double altRange = maxAlt - minAlt;
            if (altRange < 1) altRange = 1; // Avoid division by zero

            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            uint[] pixels = new uint[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double alt = rasterData[x, y];
                    byte gray;

                    if (alt > -9998)
                    {
                        gray = (byte)(((alt - minAlt) / altRange) * 255);
                    }
                    else
                    {
                        gray = 0; // Or some other color for NODATA
                    }
                    // Invert Y for correct image orientation
                    int invertedY = height - 1 - y;
                    pixels[invertedY * width + x] = (uint)((255 << 24) | (gray << 16) | (gray << 8) | gray);
                }
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }

        private (double[,] rasterData, TriangleNet.Geometry.Rectangle bounds) RasterizeTin(IMesh mesh, double[] altitudeData, double resolution)
        {
            var bounds = mesh.Bounds;
            int gridWidth = (int)Math.Ceiling(bounds.Width / resolution);
            int gridHeight = (int)Math.Ceiling(bounds.Height / resolution);
            double[,] rasterData = new double[gridWidth, gridHeight];

            var qtree = new TriangleQuadTree((Mesh)mesh);

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    var queryPoint = new TriangleNet.Geometry.Point(
                        bounds.Left + (x + 0.5) * resolution,
                        bounds.Bottom + (y + 0.5) * resolution
                    );

                    var triangle = qtree.Query(queryPoint.X, queryPoint.Y);

                    if (triangle != null)
                    {
                        rasterData[x, y] = Interpolation.InterpolatePoint(triangle, queryPoint, altitudeData);
                    }
                    else
                    {                        rasterData[x, y] = -9999.0; // No data value
                    }
                }
            }

            return (rasterData, bounds);
        }
    }
}
