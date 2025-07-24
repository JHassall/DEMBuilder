using DEMBuilder.Models;
using DEMBuilder.Services.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TriangleNet;
using TriangleNet.Geometry;
using TriangleNet.Meshing;
using TriangleNet.Meshing.Algorithm;
using TriangleNet.Tools;

namespace DEMBuilder.Services.Streaming
{
    /// <summary>
    /// High-performance streaming DEM generation service for massive datasets
    /// Processes data in chunks to avoid memory limitations
    /// </summary>
    public class StreamingDemService
    {
        private readonly int _maxPointsPerTile;
        private readonly int _maxParallelTasks;
        private readonly double _tileOverlapPercent;

        public StreamingDemService(int maxPointsPerTile = 25000, int maxParallelTasks = 0, double tileOverlapPercent = 0.1)
        {
            _maxPointsPerTile = maxPointsPerTile;
            _maxParallelTasks = maxParallelTasks <= 0 ? Environment.ProcessorCount : maxParallelTasks;
            _tileOverlapPercent = tileOverlapPercent;
        }

        /// <summary>
        /// Generate DEM using streaming approach for massive datasets
        /// </summary>
        public async Task<StreamingDemResult> GenerateStreamingDemAsync(
            StreamingDataResult dataResult,
            double resolution = 0.25,
            IProgress<StreamingDemProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (dataResult.SpatialIndex == null || dataResult.ProcessedChunks == null)
                throw new ArgumentException("Invalid data result - missing spatial index or chunks");

            var result = new StreamingDemResult { Resolution = resolution };
            
            // Calculate overall bounds from all chunks
            var overallBounds = CalculateOverallBounds(dataResult.ProcessedChunks);
            result.Bounds = overallBounds;
            
            // Determine optimal tile size based on data density and memory constraints
            var tileSize = CalculateOptimalTileSize(overallBounds, dataResult.PointsProcessed, resolution);
            result.TileSize = tileSize;
            
            // Create processing tiles
            var processingTiles = CreateProcessingTiles(overallBounds, tileSize);
            result.TotalTiles = processingTiles.Count;
            
            progress?.Report(new StreamingDemProgress
            {
                Phase = "Initializing",
                TotalTiles = processingTiles.Count,
                Message = $"Processing {processingTiles.Count} tiles at {resolution}m resolution"
            });

            // Process tiles in parallel batches
            var semaphore = new SemaphoreSlim(_maxParallelTasks);
            var processedTiles = new List<DemTile>();
            var tasks = new List<Task>();
            
            for (int i = 0; i < processingTiles.Count; i++)
            {
                var tileIndex = i;
                var tile = processingTiles[i];
                
                tasks.Add(ProcessTileAsync(tile, tileIndex, dataResult.SpatialIndex, resolution, 
                    processedTiles, progress, semaphore, cancellationToken));
            }
            
            await Task.WhenAll(tasks);
            
            result.ProcessedTiles = processedTiles.OrderBy(t => t.TileIndex).ToList();
            result.IsComplete = true;
            
            progress?.Report(new StreamingDemProgress
            {
                Phase = "Complete",
                TilesCompleted = processedTiles.Count,
                TotalTiles = processingTiles.Count,
                Message = $"DEM generation complete - {processedTiles.Count} tiles processed"
            });
            
            return result;
        }

        private async Task ProcessTileAsync(
            TileBounds tileBounds,
            int tileIndex,
            SpatialIndex spatialIndex,
            double resolution,
            List<DemTile> processedTiles,
            IProgress<StreamingDemProgress>? progress,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken);
            
            try
            {
                // Query points for this tile with overlap
                var expandedBounds = ExpandBounds(tileBounds, _tileOverlapPercent);
                var tilePoints = spatialIndex.QueryRegion(
                    expandedBounds.MinX, expandedBounds.MinY, 
                    expandedBounds.MaxX, expandedBounds.MaxY);
                
                if (tilePoints.Count == 0)
                {
                    // Create empty tile
                    var emptyTile = new DemTile
                    {
                        TileIndex = tileIndex,
                        Bounds = tileBounds,
                        RasterData = null,
                        PointCount = 0,
                        IsEmpty = true
                    };
                    
                    lock (processedTiles)
                    {
                        processedTiles.Add(emptyTile);
                    }
                    return;
                }
                
                // Generate DEM for this tile
                var demTile = await GenerateTileDemAsync(tileBounds, tilePoints, resolution, tileIndex);
                
                lock (processedTiles)
                {
                    processedTiles.Add(demTile);
                }
                
                progress?.Report(new StreamingDemProgress
                {
                    Phase = "Processing",
                    TilesCompleted = processedTiles.Count,
                    TotalTiles = -1, // Will be set by caller
                    CurrentTile = tileIndex,
                    Message = $"Tile {tileIndex}: {tilePoints.Count} points processed"
                });
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<DemTile> GenerateTileDemAsync(TileBounds bounds, List<GpsPoint> points, double resolution, int tileIndex)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Create TIN from points
                    var (mesh, altitudeData) = CreateTinFromPoints(points);
                    
                    // Rasterize the TIN for this tile
                    var rasterData = RasterizeTileArea(mesh, altitudeData, bounds, resolution);
                    
                    return new DemTile
                    {
                        TileIndex = tileIndex,
                        Bounds = bounds,
                        RasterData = rasterData,
                        PointCount = points.Count,
                        IsEmpty = false,
                        MinAltitude = points.Min(p => p.Altitude),
                        MaxAltitude = points.Max(p => p.Altitude),
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                catch (Exception ex)
                {
                    return new DemTile
                    {
                        TileIndex = tileIndex,
                        Bounds = bounds,
                        RasterData = null,
                        PointCount = points.Count,
                        IsEmpty = true,
                        ErrorMessage = ex.Message
                    };
                }
            });
        }

        private (IMesh mesh, double[] altitudeData) CreateTinFromPoints(List<GpsPoint> points)
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

        private double[,] RasterizeTileArea(IMesh mesh, double[] altitudeData, TileBounds bounds, double resolution)
        {
            int gridWidth = (int)Math.Ceiling((bounds.MaxX - bounds.MinX) / resolution);
            int gridHeight = (int)Math.Ceiling((bounds.MaxY - bounds.MinY) / resolution);
            
            var rasterData = new double[gridWidth, gridHeight];
            var qtree = new TriangleQuadTree((Mesh)mesh);

            // Parallel rasterization for better performance
            Parallel.For(0, gridHeight, y =>
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    var queryPoint = new TriangleNet.Geometry.Point(
                        bounds.MinX + (x + 0.5) * resolution,
                        bounds.MinY + (y + 0.5) * resolution
                    );

                    var triangle = qtree.Query(queryPoint.X, queryPoint.Y);

                    if (triangle != null)
                    {
                        rasterData[x, y] = Interpolation.InterpolatePoint(triangle, queryPoint, altitudeData);
                    }
                    else
                    {
                        rasterData[x, y] = -9999.0; // No data value
                    }
                }
            });

            return rasterData;
        }

        private TriangleNet.Geometry.Rectangle CalculateOverallBounds(List<ProcessedChunk> chunks)
        {
            if (chunks.Count == 0) return new TriangleNet.Geometry.Rectangle();
            
            double minX = chunks.Min(c => c.Bounds.Left);
            double maxX = chunks.Max(c => c.Bounds.Right);
            double minY = chunks.Min(c => c.Bounds.Bottom);
            double maxY = chunks.Max(c => c.Bounds.Top);
            
            return new TriangleNet.Geometry.Rectangle(minX, minY, maxX, maxY);
        }

        private double CalculateOptimalTileSize(TriangleNet.Geometry.Rectangle bounds, long totalPoints, double resolution)
        {
            // Calculate data density (points per square meter)
            double area = bounds.Width * bounds.Height;
            double density = totalPoints / area;
            
            // Target points per tile based on memory constraints
            double targetPointsPerTile = Math.Min(_maxPointsPerTile, totalPoints / 10); // At least 10 tiles
            
            // Calculate tile size to achieve target point density
            double tileArea = targetPointsPerTile / density;
            double tileSize = Math.Sqrt(tileArea);
            
            // Ensure tile size is reasonable (between 50m and 1000m)
            tileSize = Math.Max(50, Math.Min(1000, tileSize));
            
            // Round to nice numbers
            if (tileSize < 100) tileSize = Math.Round(tileSize / 10) * 10;
            else if (tileSize < 500) tileSize = Math.Round(tileSize / 50) * 50;
            else tileSize = Math.Round(tileSize / 100) * 100;
            
            return tileSize;
        }

        private List<TileBounds> CreateProcessingTiles(TriangleNet.Geometry.Rectangle bounds, double tileSize)
        {
            var tiles = new List<TileBounds>();
            
            int tilesX = (int)Math.Ceiling(bounds.Width / tileSize);
            int tilesY = (int)Math.Ceiling(bounds.Height / tileSize);
            
            for (int y = 0; y < tilesY; y++)
            {
                for (int x = 0; x < tilesX; x++)
                {
                    double minX = bounds.Left + (x * tileSize);
                    double maxX = Math.Min(bounds.Right, minX + tileSize);
                    double minY = bounds.Bottom + (y * tileSize);
                    double maxY = Math.Min(bounds.Top, minY + tileSize);
                    
                    tiles.Add(new TileBounds
                    {
                        MinX = minX,
                        MaxX = maxX,
                        MinY = minY,
                        MaxY = maxY,
                        TileX = x,
                        TileY = y
                    });
                }
            }
            
            return tiles;
        }

        private TileBounds ExpandBounds(TileBounds bounds, double overlapPercent)
        {
            double overlapX = (bounds.MaxX - bounds.MinX) * overlapPercent;
            double overlapY = (bounds.MaxY - bounds.MinY) * overlapPercent;
            
            return new TileBounds
            {
                MinX = bounds.MinX - overlapX,
                MaxX = bounds.MaxX + overlapX,
                MinY = bounds.MinY - overlapY,
                MaxY = bounds.MaxY + overlapY,
                TileX = bounds.TileX,
                TileY = bounds.TileY
            };
        }
    }

    public class StreamingDemResult
    {
        public double Resolution { get; set; }
        public double TileSize { get; set; }
        public TriangleNet.Geometry.Rectangle? Bounds { get; set; }
        public int TotalTiles { get; set; }
        public List<DemTile> ProcessedTiles { get; set; } = new();
        public bool IsComplete { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        
        public TimeSpan ProcessingTime => EndTime?.Subtract(StartTime) ?? DateTime.UtcNow.Subtract(StartTime);
        public int CompletedTiles => ProcessedTiles.Count(t => !t.IsEmpty);
        public int EmptyTiles => ProcessedTiles.Count(t => t.IsEmpty);
    }

    public class StreamingDemProgress
    {
        public string Phase { get; set; } = "";
        public int TilesCompleted { get; set; }
        public int TotalTiles { get; set; }
        public int CurrentTile { get; set; }
        public string Message { get; set; } = "";
        public double PercentComplete => TotalTiles > 0 ? (double)TilesCompleted / TotalTiles * 100 : 0;
    }

    public class DemTile
    {
        public int TileIndex { get; set; }
        public TileBounds Bounds { get; set; } = new TileBounds();
        public double[,]? RasterData { get; set; }
        public int PointCount { get; set; }
        public bool IsEmpty { get; set; }
        public double MinAltitude { get; set; }
        public double MaxAltitude { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string? ErrorMessage { get; set; }
        
        public int RasterWidth => RasterData?.GetLength(0) ?? 0;
        public int RasterHeight => RasterData?.GetLength(1) ?? 0;
        public long RasterPixels => (long)RasterWidth * RasterHeight;
    }

    public class TileBounds
    {
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public int TileX { get; set; }
        public int TileY { get; set; }
        
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double Area => Width * Height;
    }
}
