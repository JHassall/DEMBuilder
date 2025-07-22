using System;

namespace DEMBuilder.Models
{
    public class DemGenerationCompletedEventArgs : EventArgs
    {
        public double[,] RasterData { get; }
        public TriangleNet.Geometry.Rectangle Bounds { get; }

        public DemGenerationCompletedEventArgs(double[,] rasterData, TriangleNet.Geometry.Rectangle bounds)
        {
            RasterData = rasterData;
            Bounds = bounds;
        }
    }
}
