using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OSGeo.GDAL;

namespace DEMBuilder.Services
{
    /// <summary>
    /// Unified GDAL initialization and configuration manager.
    /// Ensures GDAL is initialized only once with proper PROJ database configuration.
    /// Thread-safe singleton pattern prevents conflicting initialization.
    /// </summary>
    public static class GdalManager
    {
        private static readonly object _lock = new object();
        private static bool _initialized = false;
        private static Exception? _initializationError = null;

        // P/Invoke declarations for Windows DLL loading
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AddDllDirectory(string NewDirectory);

        /// <summary>
        /// Ensures GDAL is properly initialized with correct PROJ database configuration.
        /// Thread-safe and idempotent - safe to call multiple times.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if GDAL initialization fails</exception>
        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                if (_initializationError != null)
                {
                    throw new InvalidOperationException("GDAL initialization previously failed", _initializationError);
                }
                return;
            }

            lock (_lock)
            {
                if (_initialized)
                {
                    if (_initializationError != null)
                    {
                        throw new InvalidOperationException("GDAL initialization previously failed", _initializationError);
                    }
                    return;
                }

                try
                {
                    LogDebug("=== STARTING UNIFIED GDAL INITIALIZATION ===");
                    
                    // Step 1: Configure native DLL paths FIRST
                    LogDebug("Step 1: Configuring native DLL paths");
                    ConfigureNativeDllPaths();
                    
                    // Step 2: Configure PROJ database paths BEFORE GDAL initialization
                    LogDebug("Step 2: Configuring PROJ database paths");
                    ConfigureProjPaths();
                    
                    // Step 3: Configure GDAL data paths
                    LogDebug("Step 3: Configuring GDAL data paths");
                    ConfigureGdalDataPaths();
                    
                    // Step 4: Register GDAL drivers
                    LogDebug("Step 4: Registering GDAL drivers");
                    Gdal.AllRegister();
                    
                    // Step 5: Validate initialization
                    LogDebug("Step 5: Validating GDAL initialization");
                    ValidateInitialization();
                    
                    _initialized = true;
                    LogDebug("=== GDAL INITIALIZATION COMPLETED SUCCESSFULLY ===");
                    LogGdalInfo();
                }
                catch (Exception ex)
                {
                    _initializationError = ex;
                    LogError("GDAL initialization failed", ex);
                    throw new InvalidOperationException($"Failed to initialize GDAL: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Gets the current GDAL initialization status.
        /// </summary>
        public static bool IsInitialized => _initialized;

        /// <summary>
        /// Gets any initialization error that occurred.
        /// </summary>
        public static Exception? InitializationError => _initializationError;

        private static void ConfigureNativeDllPaths()
        {
            try
            {
                var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var assemblyDir = Path.GetDirectoryName(assemblyPath) ?? "";
                
                // Search for GDAL native DLL directory
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
                                LogDebug($"Found GDAL native DLLs at: {normalizedPath}");
                                
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
                                
                                LogDebug($"Native DLL paths configured successfully");
                                return; // Use the first valid path found
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Failed to configure native path {path}: {ex.Message}");
                        continue;
                    }
                }
                
                LogDebug("WARNING: No GDAL native DLLs found in expected locations");
            }
            catch (Exception ex)
            {
                LogError("Native DLL configuration failed", ex);
                // Continue - GDAL initialization will handle the error if DLLs can't be found
            }
        }

        private static void ConfigureProjPaths()
        {
            LogDebug("Configuring PROJ database paths");
            
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            LogDebug($"Base directory: {baseDir}");
            
            // Search for PROJ database locations
            var possibleProjPaths = new[]
            {
                Path.Combine(baseDir, "gdal", "share"),        // Most common location
                Path.Combine(baseDir, "gdal", "projlib"),
                Path.Combine(baseDir, "gdal", "share", "proj"),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "projlib"),
                Path.Combine(baseDir, "proj"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GDAL", "projlib"),
                @"C:\OSGeo4W64\share\proj",
                @"C:\Program Files\GDAL\projlib"
            };

            foreach (var path in possibleProjPaths)
            {
                LogDebug($"Checking PROJ path: {path}");
                
                if (Directory.Exists(path))
                {
                    var projDbPath = Path.Combine(path, "proj.db");
                    LogDebug($"Looking for proj.db at: {projDbPath}");
                    
                    if (File.Exists(projDbPath))
                    {
                        LogDebug($"SUCCESS: proj.db found at: {projDbPath}");
                        
                        // Set PROJ_LIB configuration BEFORE any GDAL operations
                        Gdal.SetConfigOption("PROJ_LIB", path);
                        Environment.SetEnvironmentVariable("PROJ_LIB", path);
                        
                        // Verify the setting
                        var verifyPath = Gdal.GetConfigOption("PROJ_LIB", "NOT SET");
                        LogDebug($"PROJ_LIB configured: {verifyPath}");
                        return;
                    }
                    else
                    {
                        LogDebug($"proj.db not found at: {projDbPath}");
                    }
                }
                else
                {
                    LogDebug($"Directory does not exist: {path}");
                }
            }

            // If no proj.db found, configure fallback settings
            LogDebug("WARNING: proj.db not found, configuring fallback settings");
            Gdal.SetConfigOption("PROJ_NETWORK", "OFF");
            Gdal.SetConfigOption("PROJ_LIB", "");
        }

        private static void ConfigureGdalDataPaths()
        {
            LogDebug("Configuring GDAL data paths");
            
            // Check if GDAL_DATA is already set
            var existingGdalData = Environment.GetEnvironmentVariable("GDAL_DATA");
            if (!string.IsNullOrEmpty(existingGdalData) && Directory.Exists(existingGdalData))
            {
                LogDebug($"GDAL_DATA already configured: {existingGdalData}");
                return;
            }

            var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyPath) ?? "";
            
            var potentialGdalPaths = new[]
            {
                Path.Combine(assemblyDir, "gdal", "data"),
                Path.Combine(assemblyDir, "gdal-data"),
                Path.Combine(assemblyDir, "runtimes", "win-x64", "native", "gdal", "data"),
                Path.Combine(assemblyDir, "x64", "gdal", "data"),
                Path.Combine(assemblyDir, "..\\..\\..\\packages", "GDAL", "gdal", "data"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "gdal", "3.8.4", "gdal", "data")
            };

            foreach (var path in potentialGdalPaths)
            {
                try
                {
                    var normalizedPath = Path.GetFullPath(path);
                    LogDebug($"Checking GDAL data path: {normalizedPath}");
                    
                    if (Directory.Exists(normalizedPath))
                    {
                        // Verify the path contains expected GDAL data files
                        var testFiles = new[] { "epsg.wkt", "gdalicon.png", "default.rsc" };
                        var foundFiles = testFiles.Where(file => File.Exists(Path.Combine(normalizedPath, file))).ToArray();
                        
                        if (foundFiles.Length > 0)
                        {
                            LogDebug($"SUCCESS: GDAL data found at: {normalizedPath} (files: {string.Join(", ", foundFiles)})");
                            
                            Gdal.SetConfigOption("GDAL_DATA", normalizedPath);
                            Environment.SetEnvironmentVariable("GDAL_DATA", normalizedPath);
                            
                            var verifyPath = Gdal.GetConfigOption("GDAL_DATA", "NOT SET");
                            LogDebug($"GDAL_DATA configured: {verifyPath}");
                            return;
                        }
                        else
                        {
                            LogDebug($"Directory exists but no GDAL data files found: {normalizedPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Failed to check GDAL data path {path}: {ex.Message}");
                    continue;
                }
            }
            
            LogDebug("WARNING: No GDAL data directory found");
        }

        private static void ValidateInitialization()
        {
            // Test GDAL functionality by checking driver availability
            var driverCount = Gdal.GetDriverCount();
            LogDebug($"GDAL driver count: {driverCount}");
            
            if (driverCount == 0)
            {
                throw new InvalidOperationException("GDAL initialization failed: No drivers registered");
            }

            // Test specific drivers we need
            var geoTiffDriver = Gdal.GetDriverByName("GTiff");
            if (geoTiffDriver == null)
            {
                throw new InvalidOperationException("GeoTIFF driver not available after GDAL initialization");
            }
            
            LogDebug("GDAL validation completed successfully");
        }

        private static void LogGdalInfo()
        {
            try
            {
                LogDebug($"GDAL Version: {Gdal.VersionInfo("RELEASE_NAME")}");
                LogDebug($"GDAL Driver Count: {Gdal.GetDriverCount()}");
                LogDebug($"GDAL_DATA: {Gdal.GetConfigOption("GDAL_DATA", "NOT SET")}");
                LogDebug($"PROJ_LIB: {Gdal.GetConfigOption("PROJ_LIB", "NOT SET")}");
            }
            catch (Exception ex)
            {
                LogError("Failed to log GDAL info", ex);
            }
        }

        private static void LogDebug(string message)
        {
            try
            {
                var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gdal_manager.log");
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logEntry = $"[{timestamp}] {message}\n";
                File.AppendAllText(logFilePath, logEntry);
                System.Diagnostics.Debug.WriteLine($"GDAL MANAGER: {message}");
            }
            catch
            {
                // Ignore logging errors to prevent cascading failures
            }
        }

        private static void LogError(string operation, Exception ex)
        {
            var message = $"ERROR in {operation}: {ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
            {
                message += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            }
            LogDebug(message);
        }
    }
}
