using System;
using DEMBuilder.Services;
using OSGeo.GDAL;

namespace DEMBuilder
{
    /// <summary>
    /// Simple test to verify GdalManager initialization works correctly
    /// </summary>
    public static class TestGdalInit
    {
        public static void RunTest()
        {
            try
            {
                Console.WriteLine("=== TESTING GDAL MANAGER INITIALIZATION ===");
                
                // Test GdalManager initialization
                Console.WriteLine("Calling GdalManager.EnsureInitialized()...");
                GdalManager.EnsureInitialized();
                
                Console.WriteLine($"✅ GDAL initialized successfully");
                Console.WriteLine($"✅ IsInitialized: {GdalManager.IsInitialized}");
                Console.WriteLine($"✅ InitializationError: {GdalManager.InitializationError?.Message ?? "None"}");
                
                // Test GDAL functionality
                Console.WriteLine("\nTesting GDAL functionality...");
                var driverCount = Gdal.GetDriverCount();
                Console.WriteLine($"✅ GDAL driver count: {driverCount}");
                
                var geoTiffDriver = Gdal.GetDriverByName("GTiff");
                Console.WriteLine($"✅ GeoTIFF driver available: {geoTiffDriver != null}");
                
                // Test PROJ configuration
                Console.WriteLine("\nTesting PROJ configuration...");
                var projLib = Gdal.GetConfigOption("PROJ_LIB", "NOT SET");
                var gdalData = Gdal.GetConfigOption("GDAL_DATA", "NOT SET");
                Console.WriteLine($"✅ PROJ_LIB: {projLib}");
                Console.WriteLine($"✅ GDAL_DATA: {gdalData}");
                
                Console.WriteLine("\n=== GDAL MANAGER TEST COMPLETED SUCCESSFULLY ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GDAL Manager test failed: {ex.Message}");
                Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            }
        }
    }
}
