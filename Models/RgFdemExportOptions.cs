using System;

namespace DEMBuilder.Models
{
    /// <summary>
    /// Export options for RgF DEM format
    /// </summary>
    public class RgFdemExportOptions
    {
        /// <summary>
        /// Resolution in meters per pixel
        /// </summary>
        public double Resolution { get; set; } = 0.25;
        
        /// <summary>
        /// Whether to use ZIP compression for the RgF DEM file
        /// Default: false for maximum data integrity
        /// </summary>
        public bool UseCompression { get; set; } = false;
        
        /// <summary>
        /// Include farm name in metadata
        /// </summary>
        public bool IncludeFarmName { get; set; } = true;
        
        /// <summary>
        /// Include field name in metadata
        /// </summary>
        public bool IncludeFieldName { get; set; } = true;
        
        /// <summary>
        /// Include processing date in metadata
        /// </summary>
        public bool IncludeProcessingDate { get; set; } = true;
        
        /// <summary>
        /// Include GPS point count in metadata
        /// </summary>
        public bool IncludeGpsCount { get; set; } = true;
        
        /// <summary>
        /// Include elevation range in metadata
        /// </summary>
        public bool IncludeElevationRange { get; set; } = true;
    }
}
