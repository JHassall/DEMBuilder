namespace DEMBuilder.Models
{
    public enum CoordinateSystemType
    {
        UTM,
        WGS84,
        LocalTangentPlane
    }

    public enum GeoTiffExportType
    {
        SingleFile,
        Tiled
    }

    public class GeoTiffExportOptions
    {
        public CoordinateSystemType CoordinateSystem { get; set; } = CoordinateSystemType.UTM;
        public GeoTiffExportType ExportType { get; set; } = GeoTiffExportType.SingleFile;
        public bool UseCompression { get; set; } = true;
        public bool IncludeColorPalette { get; set; } = true;
        public double Resolution { get; set; } = 0.25;
        
        // Metadata options
        public bool IncludeFarmName { get; set; } = true;
        public bool IncludeFieldName { get; set; } = true;
        public bool IncludeProcessingDate { get; set; } = true;
        public bool IncludeGpsCount { get; set; } = true;
        public bool IncludeElevationRange { get; set; } = true;
    }

    public class GeoTiffExportProgress
    {
        public string Message { get; set; } = string.Empty;
        public int PercentComplete { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
    }

    public class GeoTiffExportResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public long FileSize { get; set; }
        public TimeSpan ExportTime { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    public class GeoTiffExportCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public long FileSize { get; set; }
        public TimeSpan ExportTime { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
