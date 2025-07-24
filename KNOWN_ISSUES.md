# Known Issues - DEMBuilder

## Critical Issue: GDAL Export Failure (PROJ Database Error)

### Status: ACTIVE BUG
**Affects:** GeoTIFF and ASCII Grid exports  
**Severity:** High - Export functionality partially broken  
**Date Identified:** 2025-07-24  

### Symptoms
- Export generates partial files (~1,567KB) then fails with error
- Error message: `PROJ: proj_create_from_database: Cannot find proj.db`
- Occurs during `Band.WriteRaster()` operation in GDAL export process
- All export formats affected (GeoTIFF, ASCII Grid)
- RgF DEM export works correctly (doesn't use GDAL)

### Root Cause Analysis
**Conflicting GDAL Initialization Architecture:**

1. **Two separate GDAL services** with conflicting initialization logic:
   - `GdalExportService.cs` - Has its own GDAL initialization with `_gdalInitialized` flag
   - `GisExportService.cs` - Has separate GDAL initialization logic

2. **Problem sequence:**
   ```
   GisExportService.ExportGeoTiffAsync()
   ├── new GdalExportService()           // ← GDAL initialized HERE (no PROJ config)
   │   ├── ConfigureNativeDllPaths()
   │   └── InitializeGdal()             // ← Sets _gdalInitialized = true
   └── InitializeGdal()                 // ← Sees GetDriverCount() > 0, skips PROJ config
   ```

3. **Result:** GDAL gets initialized by `GdalExportService` without PROJ configuration, then `GisExportService` can't reconfigure it, causing "Cannot find proj.db" errors during WriteRaster operations.

### Technical Details
- **PROJ database location:** `bin\Debug\net8.0-windows\gdal\share\proj.db` (file exists)
- **GDAL version:** 3.8.4 with 204 drivers available
- **Error occurs at:** Step H (Writing data to GDAL raster band) in export process
- **Debug logging:** Comprehensive logging implemented in `GisExportService.cs`

### Attempted Fixes (Unsuccessful)
1. ✅ Added comprehensive debug logging to track exact failure point
2. ❌ Fixed PROJ_LIB path configuration (still not called due to initialization order)
3. ❌ Enhanced coordinate transformation with fallbacks
4. ❌ Multiple bandaid fixes to initialization sequence

### Planned Solution
**Unified GDAL Initialization Service:**
- Create singleton `GdalManager` class
- Ensure GDAL is initialized only once, properly
- Proper initialization order: PROJ paths → GDAL data paths → Driver registration
- Thread-safe with comprehensive error handling

### Workaround
- Use RgF DEM export format (works correctly)
- Text File export may work for some coordinate systems

### Files Affected
- `Services/Export/GdalExportService.cs` - Primary GDAL service
- `Services/Export/GisExportService.cs` - Secondary GDAL service (conflicting)
- `Pages/GeoTiffOptionsPage.xaml.cs` - GeoTIFF export UI
- `Pages/TextFileOptionsPage.xaml.cs` - ASCII Grid export UI

### Debug Information
Debug log location: `bin\Debug\net8.0-windows\gdal_debug.log`

The debug log shows successful completion through Step G (data conversion) but failure at Step H (WriteRaster), confirming the PROJ database issue occurs during actual GDAL write operations.

---

## Other Known Issues
None currently documented.

---

**Last Updated:** 2025-07-24  
**Next Review:** After GDAL architecture redesign
