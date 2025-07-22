using DEMBuilder.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DEMBuilder.Services
{
    public class ImportProgress
    {
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public int PointsFound { get; set; }
        public string CurrentFile { get; set; } = "";
        public int FilesProcessed { get; set; }
        public int TotalFiles { get; set; }
    }

    public class NmeaParserService
    {
        public async Task<List<GpsPoint>> ParseFolderAsync(string folderPath, bool includeSubfolders, IProgress<ImportProgress> progress)
        {
            return await Task.Run(() =>
            {
                var points = new List<GpsPoint>();
                var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                var files = Directory.GetFiles(folderPath, "*.txt", searchOption)
                                     .Select(f => new FileInfo(f))
                                     .ToList();

                long totalBytes = files.Sum(f => f.Length);
                int totalFiles = files.Count;
                long totalBytesProcessed = 0;
                int pointsFound = 0;
                int filesProcessed = 0;

                var report = new ImportProgress { TotalBytes = totalBytes, TotalFiles = totalFiles };
                progress.Report(report); // Initial report with total size

                foreach (var fileInfo in files)
                {
                    report.CurrentFile = fileInfo.Name;
                    int pointsInThisFile = 0;
                    long fileBytesProcessed = 0;
                    try
                    {
                        using (var reader = fileInfo.OpenText())
                        {
                            while (!reader.EndOfStream)
                            {
                                string? line = reader.ReadLine();
                                if (line == null) continue;

                                var point = ParseLine(line);
                                if (point != null)
                                {
                                    points.Add(point);
                                    pointsFound++;
                                    pointsInThisFile++;
                                }

                                // Report progress every 1000 points
                                if (pointsFound > 0 && pointsFound % 1000 == 0)
                                {
                                    report.BytesProcessed = totalBytesProcessed + reader.BaseStream.Position;
                                    report.PointsFound = pointsFound;
                                    progress.Report(report);
                                }
                            }
                            fileBytesProcessed = reader.BaseStream.Position;
                        }
                    }
                    catch (Exception)
                    {
                        // A file caused an error. Assume the whole file was 'processed' for progress purposes.
                        fileBytesProcessed = fileInfo.Length;
                    }
                    totalBytesProcessed += fileBytesProcessed;
                    if (pointsInThisFile > 0) filesProcessed++;

                    // Report progress after each file
                    report.BytesProcessed = totalBytesProcessed;
                    report.PointsFound = pointsFound;
                    report.FilesProcessed = filesProcessed;
                    progress.Report(report);
                }

                // Final report
                report.BytesProcessed = totalBytes;
                report.PointsFound = pointsFound;
                report.FilesProcessed = filesProcessed;
                report.CurrentFile = "Import Complete!";
                progress.Report(report);

                return points;
            });
        }

        private GpsPoint? ParseLine(string line)
        {
            try
            {
                // Extract Receiver ID from the '#<id>,' prefix
                var hashIndex = line.IndexOf('#');
                var commaAfterHashIndex = line.IndexOf(',');
                if (hashIndex == -1 || commaAfterHashIndex == -1 || commaAfterHashIndex < hashIndex)
                {
                    return null; // Does not have the expected '#id,' prefix
                }

                var receiverIdString = line.Substring(hashIndex + 1, commaAfterHashIndex - hashIndex - 1);
                if (!int.TryParse(receiverIdString, out int receiverId))
                {
                    return null; // Could not parse receiver ID
                }

                // Find the NMEA sentence and validate its checksum
                var dollarIndex = line.IndexOf('$');
                if (dollarIndex == -1) return null;

                var starIndex = line.LastIndexOf('*');
                if (starIndex == -1 || starIndex < dollarIndex) return null; // No checksum

                var sentenceBody = line.Substring(dollarIndex + 1, starIndex - dollarIndex - 1);
                var checksumString = line.Substring(starIndex + 1).Trim();

                byte calculatedChecksum = 0;
                foreach (char c in sentenceBody) calculatedChecksum ^= (byte)c;

                if (!int.TryParse(checksumString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int receivedChecksum) || calculatedChecksum != receivedChecksum)
                {
                    return null; // Checksum mismatch
                }

                // Now parse the validated sentence body
                var parts = sentenceBody.Split(',');

                // Validate GGA sentence structure
                if (parts.Length < 10 || !parts[0].EndsWith("GGA") || string.IsNullOrEmpty(parts[6]) || string.IsNullOrEmpty(parts[7]) || string.IsNullOrEmpty(parts[8]))
                {
                    return null;
                }

                // Parse all required fields
                var lat = ParseLatitude(parts[2], parts[3]);
                var lon = ParseLongitude(parts[4], parts[5]);
                var alt = double.Parse(parts[9], CultureInfo.InvariantCulture);
                var fixQuality = int.Parse(parts[6], CultureInfo.InvariantCulture);
                var numSats = int.Parse(parts[7], CultureInfo.InvariantCulture);
                var hdop = double.Parse(parts[8], CultureInfo.InvariantCulture);

                var ageOfDiff = 0.0;
                if (parts.Length > 13 && !string.IsNullOrEmpty(parts[13]))
                {
                    double.TryParse(parts[13], NumberStyles.Any, CultureInfo.InvariantCulture, out ageOfDiff);
                }

                return new GpsPoint(receiverId, lat, lon, alt, fixQuality, numSats, hdop, ageOfDiff);
            }
            catch
            {
                // Ignore any line that fails to parse completely
                return null;
            }
        }

        private double ParseLatitude(string value, string hemisphere)
        {
            var degrees = double.Parse(value.Substring(0, 2), CultureInfo.InvariantCulture);
            var minutes = double.Parse(value.Substring(2), CultureInfo.InvariantCulture);
            var decimalDegrees = degrees + (minutes / 60.0);
            return hemisphere == "S" ? -decimalDegrees : decimalDegrees;
        }

        private double ParseLongitude(string value, string hemisphere)
        {
            var degrees = double.Parse(value.Substring(0, 3), CultureInfo.InvariantCulture);
            var minutes = double.Parse(value.Substring(3), CultureInfo.InvariantCulture);
            var decimalDegrees = degrees + (minutes / 60.0);
            return hemisphere == "W" ? -decimalDegrees : decimalDegrees;
        }

        public async Task<int> CountValidFilesAsync(string folderPath, bool includeSubfolders)
        {
            return await Task.Run(() =>
            {
                var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.EnumerateFiles(folderPath, "*.txt", searchOption);

                int validFileCount = 0;
                foreach (var file in files)
                {
                    try
                    {
                        // A file is valid if it contains at least one line that can be parsed into a point.
                        if (File.ReadLines(file).Any(line => ParseLine(line) != null))
                        {
                            validFileCount++;
                        }
                    }
                    catch
                    {
                        // Ignore files that can't be read or cause other errors.
                        continue;
                    }
                }
                return validFileCount;
            });
        }
    }
}
